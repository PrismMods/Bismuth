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
        }
    }
}
