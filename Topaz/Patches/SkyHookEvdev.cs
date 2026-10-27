using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace Topaz
{
    /* libskyhook on Linux picks its backend at start: X11 XRecord when an X display opens,
       /dev/input (evdev) otherwise. XRecord only sees keys delivered to X clients — none at
       all once the game is a native Wayland window — and carries X server millisecond stamps.
       evdev reads the kernel's per-report timestamps (an 8 kHz keyboard keeps its precision),
       so Topaz starts every hook on evdev, with no launch options needed, and falls back to
       X11 when /dev/input can't be read (user not in the `input` group).

       SkyHook's evdev events need two corrections, applied where the game turns an event
       into a hit time (GetTimeInTicks):
         - TimeSubsecNano holds input_event's tv_usec unscaled, so every press read as the
           first millisecond of its second;
         - the stamp is UTC, while the game measures hits against DateTime.Now (local time),
           so with any non-zero UTC offset every hit judged TooEarly. */
    internal static class SkyHookEvdev
    {
        // SkyHook is reading /dev/input — arms the timestamp correction.
        private static volatile bool _active;

        // DISPLAY the game started with; null = launched without one (SkyHook can only pick evdev).
        private static readonly string _display = Environment.GetEnvironmentVariable("DISPLAY");

        // Set while restarting on X11, so the start patch leaves DISPLAY alone.
        private static bool _forceX11;

        private static readonly long EpochTicks = new DateTime(1970, 1, 1).Ticks;

        [DllImport("libc")] private static extern int setenv(string name, string value, int overwrite);
        [DllImport("libc")] private static extern int unsetenv(string name);
        [DllImport("libc")] private static extern IntPtr readlink(string path, byte[] buf, IntPtr size);

        private static bool IsLinux => Application.platform == RuntimePlatform.LinuxPlayer;

        private static int CountInputFds()
        {
            int n = 0;
            var buf = new byte[256];
            try
            {
                foreach (var fd in Directory.GetFiles("/proc/self/fd"))
                {
                    long len = (long)readlink(fd, buf, (IntPtr)buf.Length);
                    if (len > 0 && Encoding.ASCII.GetString(buf, 0, (int)len).StartsWith("/dev/input/event"))
                        n++;
                }
            }
            catch { }
            return n;
        }

        // The native hook opens its devices on its own thread after start_hook returns.
        private static bool WaitForEvdev(int fdsBefore)
        {
            for (int i = 0; i < 100; i++)
            {
                if (CountInputFds() > fdsBefore) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        private static object Manager()
        {
            var t = AccessTools.TypeByName("SkyHook.SkyHookManager");
            return t != null ? AccessTools.PropertyGetter(t, "Instance")?.Invoke(null, null) : null;
        }

        private static bool HookRunning(object mgr)
        {
            var getter = mgr != null ? AccessTools.PropertyGetter(mgr.GetType(), "isHookActive") : null;
            return getter != null && (bool)getter.Invoke(mgr, null);
        }

        private static void Restart(object mgr)
        {
            AccessTools.Method(mgr.GetType(), "_StopHook")?.Invoke(mgr, null);
            AccessTools.Method(mgr.GetType(), "_StartHook")?.Invoke(mgr, null);
        }

        /* After patching: a hook the game started before our patches went in is on whichever
           backend DISPLAY gave it — restart it through the patched start. */
        internal static void Rearm()
        {
            if (!IsLinux || _active) return;
            try
            {
                var mgr = Manager();
                if (HookRunning(mgr)) Restart(mgr);
            }
            catch (Exception e)
            {
                TopazLog.Log("SkyHook: restart onto evdev failed: " + (e.InnerException ?? e).Message);
            }
        }

        /* Master switch off: the timestamp correction is about to be unpatched, so hand the
           hook back to the backend the game would have picked itself. */
        internal static void Release()
        {
            if (!IsLinux || !_active || _display == null) return;
            _active = false;
            try
            {
                var mgr = Manager();
                if (!HookRunning(mgr)) return;
                _forceX11 = true;
                try { Restart(mgr); }
                finally { _forceX11 = false; }
            }
            catch (Exception e)
            {
                TopazLog.Log("SkyHook: restart onto X11 failed: " + (e.InnerException ?? e).Message);
            }
        }

        [HarmonyPatch]
        private static class StartHookPatch
        {
            static bool Prepare() => IsLinux;

            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("SkyHook.SkyHookManager");
                return t != null ? AccessTools.Method(t, "_StartHook") : null;
            }

            // __state = /dev/input fds already open before this start.
            static void Prefix(out int __state)
            {
                __state = CountInputFds();
                _active = false;
                if (_display != null && !_forceX11) unsetenv("DISPLAY");
            }

            static Exception Finalizer(object __instance, Exception __exception, int __state)
            {
                if (_forceX11) return __exception;
                bool evdev = __exception == null && WaitForEvdev(__state);
                if (_display != null) setenv("DISPLAY", _display, 1);
                if (evdev)
                {
                    _active = true;
                    TopazLog.Log("SkyHook: reading /dev/input (kernel timestamps)");
                    return null;
                }
                if (_display == null) return __exception;   // no X display to fall back to

                TopazLog.Log("SkyHook: /dev/input unavailable" +
                    (__exception != null ? " (" + __exception.Message + ")" : "") +
                    " — staying on X11. Add yourself to the 'input' group for kernel-timed input.");
                _forceX11 = true;
                try
                {
                    if (__exception == null) AccessTools.Method(__instance.GetType(), "_StopHook")?.Invoke(__instance, null);
                    AccessTools.Method(__instance.GetType(), "_StartHook")?.Invoke(__instance, null);
                    return null;
                }
                catch (Exception e) { return e.InnerException ?? e; }
                finally { _forceX11 = false; }
            }
        }

        [HarmonyPatch]
        private static class EventTimePatch
        {
            static bool Prepare() => IsLinux;

            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("SkyHook.SkyHookEvent");
                return t != null ? AccessTools.Method(t, "GetTimeInTicks") : null;
            }

            static bool Prefix(long ___TimeSec, uint ___TimeSubsecNano, ref long __result)
            {
                if (!_active) return true;
                long utc = ___TimeSec * TimeSpan.TicksPerSecond + ___TimeSubsecNano * 10L + EpochTicks;
                __result = new DateTime(utc, DateTimeKind.Utc).ToLocalTime().Ticks;
                return false;
            }
        }
    }
}
