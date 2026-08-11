using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Bismuth
{
    /* Ported from Quartz's optimizer module (optimizer.qmod, HitSoundRenderer).

       The game schedules every hit sound as its own AudioSource.PlayScheduled. On dense charts
       that is thousands of voices, and the scheduling work lands on the main thread during play.
       This replaces it: the whole hit-sound track is captured once, mixed down on a BACKGROUND
       thread into half-second PCM segments a few seconds ahead of the playhead, and played back
       through a small pool of pre-allocated AudioSources.

       TIMING IS THE ENTIRE POINT OF THIS FILE. Every constant, rounding, and clamp below is
       kept exactly as Quartz has it — the segment length, the 12s lookahead, the 50ms late
       margin, the linear resample in MixEvent, the `timeSamples` offset when a segment is
       already partly in the past. Changing any of them desyncs hit sounds, which is both the
       worst bug class in a rhythm game and invisible to any static check. VERIFY BY EAR.

       Off by default (OptRenderAllHitSounds). Differences from Quartz, all plumbing:
       Quartz's config/logging swapped for Bismuth's, and the clip loader calls
       AudioManager.FindOrLoadAudioClip by reflection instead of Quartz's compat layer. */
    internal static class HitSoundRenderer
    {
        private sealed class HitSoundEvent { public double Time; public float Volume; public ClipData Data; }
        private sealed class ClipData { public float[] Data; public int Samples; public int ClipChannels; public int Frequency; }
        private sealed class SegmentJob { public double Start; public double End; public readonly List<HitSoundEvent> Events = new List<HitSoundEvent>(); }
        private sealed class RenderResult { public int Generation; public SegmentJob Job; public float[] Buffer; }
        private sealed class Voice { public GameObject Go; public AudioSource Source; public AudioClip Clip; public double BusyUntil; }

        private const int Channels = 2;
        private const double SegmentSeconds = 0.5;
        private const double AheadSeconds = 12.0;
        private const double ClipTailSeconds = 0.03;
        private const double LateMarginSeconds = 0.05;
        private const int MaxVoices = 32;
        private const int MaxQueuePerFrame = 2;
        private const int MaxApplyPerFrame = 2;
        private const int MaxPooledBuffers = 12;

        private static int SampleRate = 48000;
        private static int SegmentSamples = (int)Math.Ceiling(SegmentSeconds * 48000);
        private static int SegmentFloats = SegmentSamples * Channels;

        private static readonly FieldInfo HitSoundsDataField = AccessTools.Field(typeof(scrConductor), "hitSoundsData");
        private static readonly FieldInfo NextHitSoundField = AccessTools.Field(typeof(scrConductor), "nextHitSoundToSchedule");
        private static readonly Type HitSoundsDataType = AccessTools.Inner(typeof(scrConductor), "HitSoundsData");
        private static readonly FieldInfo HitSoundField = HitSoundsDataType != null ? AccessTools.Field(HitSoundsDataType, "hitSound") : null;
        private static readonly FieldInfo TimeField = HitSoundsDataType != null ? AccessTools.Field(HitSoundsDataType, "time") : null;
        private static readonly FieldInfo VolumeField = HitSoundsDataType != null ? AccessTools.Field(HitSoundsDataType, "volume") : null;
        private static readonly MethodInfo FindOrLoadClip = AccessTools.Method(typeof(AudioManager), "FindOrLoadAudioClip");
        /* Unity 6 added ReadOnlySpan<float>/Span<float> overloads of AudioClip.SetData/GetData.
           This assembly targets .NETFramework 4.8, whose compiler can't resolve Span at all
           (CS7069), so the float[] overloads are bound explicitly instead. */
        private static readonly MethodInfo ClipSetData =
            AccessTools.Method(typeof(AudioClip), "SetData", new[] { typeof(float[]), typeof(int) });
        private static readonly MethodInfo ClipGetData =
            AccessTools.Method(typeof(AudioClip), "GetData", new[] { typeof(float[]), typeof(int) });

        private static readonly Dictionary<int, ClipData> ClipCache = new Dictionary<int, ClipData>();
        private static readonly Dictionary<int, ClipData> HitSoundClipCache = new Dictionary<int, ClipData>();

        // Boxed struct field reads run per hit-sound on capture; compiled getters avoid the
        // reflection cost. Falls back to plain FieldInfo.GetValue if Expression.Compile fails.
        private static Func<object, int> _hitSoundIdGetter;
        private static Func<object, double> _timeGetter;
        private static Func<object, float> _volumeGetter;
        private static bool _captureGettersBuilt;

        private static readonly List<SegmentJob> Segments = new List<SegmentJob>();
        private static int _nextSegmentIndex;
        private static AudioMixerGroup _mixerGroup;
        private static bool _sceneHookInstalled;

        // Bumped by StopAll. Results carrying an older generation are discarded, which is how
        // a restart/recapture invalidates work already queued on the render thread.
        private static int _generation;

        private static readonly ConcurrentQueue<RenderResult> _pendingJobs = new ConcurrentQueue<RenderResult>();
        private static readonly ConcurrentQueue<RenderResult> _completedJobs = new ConcurrentQueue<RenderResult>();
        private static readonly AutoResetEvent _jobSignal = new AutoResetEvent(false);
        private static Thread _renderThread;
        private static readonly object _bufferPoolLock = new object();
        private static readonly Stack<float[]> _bufferPool = new Stack<float[]>();
        private static bool _loggedRenderError;
        private static GameObject _poolRoot;
        private static readonly List<Voice> _voices = new List<Voice>();

        internal static bool Active =>
            MainClass.Settings != null && MainClass.Settings.OptimizationsEnabled
            && MainClass.Settings.OptRenderAllHitSounds;

        private static bool ReflectionReady =>
            HitSoundsDataField != null && NextHitSoundField != null
            && HitSoundField != null && TimeField != null && VolumeField != null;

        private static bool EnsureCaptureGetters()
        {
            if (_captureGettersBuilt) return _hitSoundIdGetter != null;
            _captureGettersBuilt = true;
            try
            {
                var item = Expression.Parameter(typeof(object), "item");
                var typed = Expression.Convert(item, HitSoundsDataType);
                Func<object, T> Build<T>(FieldInfo f) =>
                    Expression.Lambda<Func<object, T>>(Expression.Convert(Expression.Field(typed, f), typeof(T)), item).Compile();
                _hitSoundIdGetter = Build<int>(HitSoundField);
                _timeGetter = Build<double>(TimeField);
                _volumeGetter = Build<float>(VolumeField);
                return true;
            }
            catch (Exception e)
            {
                BismuthLog.Log("HitSoundRenderer: capture getters unavailable, using reflection: " + e.Message);
                _hitSoundIdGetter = null; _timeGetter = null; _volumeGetter = null;
                return false;
            }
        }

        internal static void EnsureSceneHook()
        {
            if (_sceneHookInstalled) return;
            _sceneHookInstalled = true;
            SceneManager.sceneUnloaded += _ => StopAll("scene unloaded", destroyPool: true);
        }

        // The device rate can differ from 48k and can change; segment sizing and the whole
        // voice pool depend on it, so both are rebuilt when it moves.
        private static void EnsureAudioFormat()
        {
            int rate = AudioSettings.outputSampleRate;
            if (rate <= 0) rate = 48000;
            if (rate == SampleRate && SegmentSamples > 0) return;
            SampleRate = rate;
            SegmentSamples = (int)Math.Ceiling(SegmentSeconds * SampleRate);
            SegmentFloats = SegmentSamples * Channels;
            lock (_bufferPoolLock) _bufferPool.Clear();
            DestroyPool();
        }

        /* Takes the conductor's whole hit-sound list, resolves each entry's clip, then CLEARS
           the list and resets nextHitSoundToSchedule — that's what stops the game scheduling
           them itself. Any failure path calls StopAll and leaves the game's own scheduling
           intact, so a bad capture degrades to vanilla behaviour rather than silence. */
        internal static void Capture(scrConductor conductor)
        {
            if (conductor == null || !ReflectionReady) return;
            try
            {
                EnsureAudioFormat();
                if (!(HitSoundsDataField.GetValue(conductor) is IList list)) return;
                if (list.Count == 0) { StopAll("no hit sounds"); return; }

                bool fast = EnsureCaptureGetters();
                var events = new List<HitSoundEvent>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    object item = list[i];
                    if (item == null) continue;
                    ClipData data;
                    if (fast)
                    {
                        int id = _hitSoundIdGetter(item);
                        if (!HitSoundClipCache.TryGetValue(id, out data))
                        {
                            if (!ResolveClipData(item, out data)) { StopAll("clip unreadable"); return; }
                            HitSoundClipCache[id] = data;
                        }
                        if (data != null)
                            events.Add(new HitSoundEvent { Time = _timeGetter(item), Volume = _volumeGetter(item), Data = data });
                    }
                    else
                    {
                        if (!ResolveClipData(item, out data)) { StopAll("clip unreadable"); return; }
                        if (data != null)
                            events.Add(new HitSoundEvent
                            {
                                Time = Convert.ToDouble(TimeField.GetValue(item)),
                                Volume = Convert.ToSingle(VolumeField.GetValue(item)),
                                Data = data
                            });
                    }
                }
                if (events.Count == 0) { StopAll("no readable hit sounds"); return; }

                events.Sort((a, b) => a.Time.CompareTo(b.Time));
                StopAll("recapture");
                _mixerGroup = conductor.hitSoundGroup;
                BuildSegments(events);
                list.Clear();
                NextHitSoundField.SetValue(conductor, 0);
            }
            catch (Exception e)
            {
                BismuthLog.Log("HitSoundRenderer: capture failed: " + e.Message);
                StopAll("capture error");
            }
        }

        // Called once per frame: queues upcoming segments to the render thread and schedules
        // whatever came back. Both sides are capped per frame to bound main-thread cost.
        internal static void Pump()
        {
            if (!Active) return;
            try
            {
                double dsp = AudioSettings.dspTime;
                bool queued = false;
                int n = 0;
                while (n < MaxQueuePerFrame && _nextSegmentIndex < Segments.Count
                       && Segments[_nextSegmentIndex].Start <= dsp + AheadSeconds)
                {
                    var job = Segments[_nextSegmentIndex];
                    _nextSegmentIndex++;
                    // Already fully in the past (incl. clip tail) — nothing to render.
                    if (job.End + ClipTailSeconds < dsp - LateMarginSeconds) continue;
                    _pendingJobs.Enqueue(new RenderResult { Generation = _generation, Job = job });
                    queued = true;
                    n++;
                }
                if (queued) { EnsureRenderThread(); _jobSignal.Set(); }

                int applied = 0;
                while (applied < MaxApplyPerFrame && _completedJobs.TryDequeue(out var done))
                {
                    var buf = done.Buffer;
                    if (done.Generation == _generation)
                    {
                        ScheduleSegment(done.Job, buf, AudioSettings.dspTime);
                        applied++;
                    }
                    ReturnBuffer(buf);
                }
            }
            catch (Exception e)
            {
                BismuthLog.Log("HitSoundRenderer: pump failed: " + e.Message);
                StopAll("pump error");
            }
        }

        internal static void StopAll(string reason) => StopAll(reason, false);

        private static void StopAll(string reason, bool destroyPool)
        {
            _generation++;
            while (_completedJobs.TryDequeue(out var stale)) ReturnBuffer(stale.Buffer);
            Segments.Clear();
            _nextSegmentIndex = 0;
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Source != null) _voices[i].Source.Stop();
                _voices[i].BusyUntil = 0.0;
            }
            if (destroyPool) DestroyPool();
        }

        private static void DestroyPool()
        {
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Clip != null) UnityEngine.Object.Destroy(_voices[i].Clip);
                if (_voices[i].Go != null) UnityEngine.Object.Destroy(_voices[i].Go);
            }
            _voices.Clear();
            if (_poolRoot != null) UnityEngine.Object.Destroy(_poolRoot);
            _poolRoot = null;
        }

        // One segment per half second from the first hit. An event is added to every segment
        // its clip overlaps, so a sound crossing a boundary is mixed into both.
        private static void BuildSegments(List<HitSoundEvent> events)
        {
            Segments.Clear();
            _nextSegmentIndex = 0;
            double t0 = events[0].Time;
            long span = (long)Math.Floor((events[events.Count - 1].Time - t0) / SegmentSeconds) + 1;
            int count = (int)Math.Min(200000L, Math.Max(1L, span));
            for (int i = 0; i < count; i++)
            {
                double start = t0 + i * SegmentSeconds;
                Segments.Add(new SegmentJob { Start = start, End = start + SegmentSeconds });
            }
            for (int j = 0; j < events.Count; j++)
            {
                var ev = events[j];
                int first = (int)Math.Floor((ev.Time - t0) / SegmentSeconds);
                if (first < 0) first = 0;
                if (first >= count) continue;
                double dur = (ev.Data != null && ev.Data.Frequency > 0)
                    ? (double)ev.Data.Samples / ev.Data.Frequency : 0.0;
                int last = (int)Math.Floor((ev.Time + dur - t0) / SegmentSeconds);
                if (last >= count) last = count - 1;
                if (last < first) last = first;
                for (int k = first; k <= last; k++) Segments[k].Events.Add(ev);
            }
        }

        private static void EnsureRenderThread()
        {
            if (_renderThread != null && _renderThread.IsAlive) return;
            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "Bismuth HitSoundMixer",
                Priority = System.Threading.ThreadPriority.BelowNormal,
            };
            _renderThread.Start();
        }

        // Background thread. Touches no Unity API — only float[] math over ClipData captured
        // on the main thread, which is what makes it safe to run off-thread.
        private static void RenderLoop()
        {
            while (true)
            {
                _jobSignal.WaitOne();
                while (_pendingJobs.TryDequeue(out var job))
                {
                    if (job.Generation != Volatile.Read(ref _generation)) continue;
                    try
                    {
                        float[] buf = RentBuffer();
                        Array.Clear(buf, 0, buf.Length);
                        var evs = job.Job.Events;
                        for (int i = 0; i < evs.Count; i++) MixEvent(buf, job.Job.Start, evs[i]);
                        job.Buffer = buf;
                        _completedJobs.Enqueue(job);
                    }
                    catch (Exception e)
                    {
                        if (!_loggedRenderError)
                        {
                            _loggedRenderError = true;
                            BismuthLog.Log("HitSoundRenderer: mix failed: " + e.Message);
                        }
                    }
                }
            }
        }

        private static float[] RentBuffer()
        {
            lock (_bufferPoolLock)
                if (_bufferPool.Count > 0) return _bufferPool.Pop();
            return new float[SegmentFloats];
        }

        private static void ReturnBuffer(float[] buffer)
        {
            if (buffer == null || buffer.Length != SegmentFloats) return;
            lock (_bufferPoolLock)
                if (_bufferPool.Count < MaxPooledBuffers) _bufferPool.Push(buffer);
        }

        /* A segment can come back after its start time has passed; rather than drop it, play
           from partway in via timeSamples so the remainder still lands on the right beat.
           Skipped entirely only when the whole segment would already be over. */
        private static void ScheduleSegment(SegmentJob job, float[] buffer, double now)
        {
            var voice = AcquireVoice(now);
            if (voice == null) return;

            double when = job.Start;
            double skip = 0.0;
            double earliest = now + LateMarginSeconds;
            if (when < earliest) { skip = earliest - when; when = earliest; }

            float segLen = (float)((double)SegmentSamples / SampleRate);
            if (skip >= segLen - 0.001) return;

            ClipSetData.Invoke(voice.Clip, new object[] { buffer, 0 });
            var src = voice.Source;
            src.clip = voice.Clip;
            if (_mixerGroup != null) src.outputAudioMixerGroup = _mixerGroup;

            int offset = skip > 0.0 ? (int)Math.Round(skip * SampleRate) : 0;
            if (offset < 0) offset = 0;
            if (offset >= SegmentSamples) offset = SegmentSamples - 1;
            src.timeSamples = offset;
            src.PlayScheduled(when);
            voice.BusyUntil = when + segLen - skip + 0.1;
        }

        // Free voice if there is one, else grow to MaxVoices, else steal the one finishing soonest.
        private static Voice AcquireVoice(double now)
        {
            EnsurePoolRoot();
            if (_poolRoot == null) return null;

            Voice soonest = null;
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (v.Source == null || v.Clip == null) continue;
                if (v.BusyUntil <= now) return v;
                if (soonest == null || v.BusyUntil < soonest.BusyUntil) soonest = v;
            }
            if (_voices.Count < MaxVoices)
            {
                var made = CreateVoice();
                if (made != null) { _voices.Add(made); return made; }
            }
            return soonest;
        }

        private static void EnsurePoolRoot()
        {
            if (_poolRoot != null) return;
            _poolRoot = new GameObject("Bismuth HitSound Pool");
            UnityEngine.Object.DontDestroyOnLoad(_poolRoot);
        }

        private static Voice CreateVoice()
        {
            if (_poolRoot == null) return null;
            var go = new GameObject("Voice");
            go.transform.SetParent(_poolRoot.transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.volume = 1f;
            src.pitch = 1f;
            src.priority = 128;
            var clip = AudioClip.Create("BismuthHitSoundSegment", SegmentSamples, Channels, SampleRate, false);
            return new Voice { Go = go, Source = src, Clip = clip };
        }

        // Linear resample from the clip's rate to the device rate, summed into the segment.
        // Mono clips feed both output channels (channel index is clamped to the clip's count).
        private static void MixEvent(float[] output, double segmentStart, HitSoundEvent hit)
        {
            var data = hit.Data;
            if (data == null) return;

            int frames = output.Length / Channels;
            int startFrame = (int)Math.Round((hit.Time - segmentStart) * SampleRate);
            if (startFrame >= frames) return;

            int len = Math.Min(frames - startFrame, (int)Math.Ceiling((double)data.Samples * SampleRate / data.Frequency));
            if (len <= 0) return;

            for (int i = 0; i < len; i++)
            {
                int outFrame = startFrame + i;
                if (outFrame < 0) continue;
                double srcPos = (double)i * data.Frequency / SampleRate;
                int s0 = (int)srcPos;
                if (s0 < 0 || s0 >= data.Samples) continue;
                int s1 = Math.Min(data.Samples - 1, s0 + 1);
                float frac = (float)(srcPos - s0);
                for (int ch = 0; ch < Channels; ch++)
                {
                    int srcCh = Math.Min(ch, data.ClipChannels - 1);
                    float a = data.Data[s0 * data.ClipChannels + srcCh];
                    float b = data.Data[s1 * data.ClipChannels + srcCh];
                    output[outFrame * Channels + ch] += (a + (b - a) * frac) * hit.Volume;
                }
            }
        }

        // Returns false only on a genuine read failure (caller aborts to vanilla scheduling).
        // "None"/absent hit sounds return true with data == null — nothing to mix, not an error.
        private static bool ResolveClipData(object item, out ClipData data)
        {
            data = null;
            object hs = HitSoundField.GetValue(item);
            if (hs == null) return true;
            string name = hs.ToString();
            if (string.Equals(name, "None", StringComparison.OrdinalIgnoreCase)) return true;
            var clip = LoadClip("snd" + name);
            if (clip == null) return true;
            return TryGetClipData(clip, out data);
        }

        private static AudioClip LoadClip(string clipName)
        {
            try
            {
                if (AudioManager.Instance == null || FindOrLoadClip == null) return null;
                // (clipName, internalLevelName, fromBundle) — both trailing args are optional.
                return FindOrLoadClip.Invoke(AudioManager.Instance, new object[] { clipName, null, false }) as AudioClip;
            }
            catch (Exception e)
            {
                BismuthLog.Log("HitSoundRenderer: could not load " + clipName + ": " + e.Message);
                return null;
            }
        }

        private static bool TryGetClipData(AudioClip clip, out ClipData data)
        {
            data = null;
            if (clip == null) return false;
            int id = clip.GetInstanceID();
            if (ClipCache.TryGetValue(id, out data)) return true;
            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                int ch = Math.Max(1, clip.channels);
                int samples = Math.Max(0, clip.samples);
                if (samples <= 0) return false;
                var buf = new float[samples * ch];
                if (!(bool)ClipGetData.Invoke(clip, new object[] { buf, 0 })) return false;
                data = new ClipData { Data = buf, Samples = samples, ClipChannels = ch, Frequency = Math.Max(1, clip.frequency) };
                ClipCache[id] = data;
                return true;
            }
            catch (Exception e)
            {
                BismuthLog.Log("HitSoundRenderer: could not read clip " + clip.name + ": " + e.Message);
                data = null;
                return false;
            }
        }

        // ── Patches ────────────────────────────────────────────────────────
        // Capture replaces the game's own per-hit scheduling; the rest are stop points.

        [HarmonyPatch(typeof(scrConductor), "PlayHitTimes")]
        internal static class PlayHitTimesPatch
        {
            public static void Postfix(scrConductor __instance)
            {
                if (!Active) return;
                EnsureSceneHook();
                Capture(__instance);
            }
        }

        [HarmonyPatch(typeof(AudioManager), "StopAllSounds")]
        internal static class StopAllSoundsPatch
        {
            public static void Postfix() { if (Active) StopAll("StopAllSounds"); }
        }

        [HarmonyPatch(typeof(scrConductor), "KillAllSounds")]
        internal static class KillAllSoundsPatch
        {
            public static void Postfix() { if (Active) StopAll("KillAllSounds"); }
        }

        [HarmonyPatch(typeof(scrController), "Restart")]
        internal static class RestartPatch
        {
            public static void Prefix() { if (Active) StopAll("restart"); }
        }

        [HarmonyPatch(typeof(scrController), "FailAction")]
        internal static class FailActionPatch
        {
            public static void Postfix() { if (Active) StopAll("fail"); }
        }
    }
}
