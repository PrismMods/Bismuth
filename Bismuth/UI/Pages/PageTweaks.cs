using UnityEngine;

namespace Bismuth.UI.Pages
{
    // Custom-level tweaks, shown at the top of the Misc tab (editor helpers live in Sapphire).
    internal static class PageTweaks
    {
        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, "Custom levels");
            UIBuilder.Slider(content, "Preview volume %", s.ClsPreviewVolume * 100f, 0f, 100f,
                v =>
                {
                    s.ClsPreviewVolume = v / 100f;
                    Tweaks.ApplyClsPreviewVolume();
                    notify?.Invoke();
                }, "0", 1f);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Gameplay");
            UIBuilder.Toggle(content, "Hide planet orbit rings", s.HidePlanetRings,
                v => { s.HidePlanetRings = v; notify?.Invoke(); });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Custom countdown",
                "Restarting from a checkpoint replays the tiles before it\n"
                + "at the level's own tempo, which past ~600 BPM is too fast\n"
                + "to read. This speeds those tiles up by a power of two until\n"
                + "the ticks land in the range below, then pulls the audio back\n"
                + "by the same amount — the song is never re-pitched and the\n"
                + "first real hit still falls on its beat.");

            GameObject bpmHost = null;
            UIBuilder.Toggle(content, "Slow the countdown", s.CountdownSlowdown,
                v =>
                {
                    s.CountdownSlowdown = v;
                    if (bpmHost != null) bpmHost.SetActive(v);
                    // Turning it off has to put any stretched tiles back right now.
                    Bismuth.Countdown.OnSettingChanged();
                    notify?.Invoke();
                });
            bpmHost = UIBuilder.VGroup(content, "CountdownBpm");
            UIBuilder.Slider(bpmHost.transform, "Minimum tempo", s.CountdownMinBpm, 100f, 1000f,
                v =>
                {
                    s.CountdownMinBpm = v;
                    if (s.CountdownMaxBpm < v) s.CountdownMaxBpm = v;
                    notify?.Invoke();
                }, "0", 10f);
            UIBuilder.Slider(bpmHost.transform, "Maximum tempo", s.CountdownMaxBpm, 100f, 1000f,
                v =>
                {
                    s.CountdownMaxBpm = Mathf.Max(v, s.CountdownMinBpm);
                    notify?.Invoke();
                }, "0", 10f);
            bpmHost.SetActive(s.CountdownSlowdown);
        }
    }
}
