using UnityEngine;

namespace Bismuth.UI.Pages
{
    internal static class PageHideUi
    {
        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, "Hide UI");
            UIBuilder.Collapsible(content, "Enable", s.HideUiEnabled,
                v => { s.HideUiEnabled = v; notify?.Invoke(); }, null);

            // Forward-declare the sub-container so the Hide All toggle handler can flip its
            // visibility. When Hide All is on, the individual toggles are no-ops, so we hide
            // them entirely — matches the IMGUI version's conditional render.
            GameObject subContainer = null;

            UIBuilder.Collapsible(content, "Hide all UI", s.HideAllUI,
                v =>
                {
                    s.HideAllUI = v;
                    if (subContainer != null) subContainer.SetActive(!v);
                    notify?.Invoke();
                }, null);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Individual");

            subContainer = UIBuilder.Rect("HideSubs", content);
            var vlg = subContainer.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;

            UIBuilder.Collapsible(subContainer.transform, "Hitmeter", s.HideHitmeter,
                v => { s.HideHitmeter = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(subContainer.transform, "Autoplay text", s.HideAutoplayText,
                v => { s.HideAutoplayText = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(subContainer.transform, "Autoplay icon", s.HideAutoplayIcon,
                v => { s.HideAutoplayIcon = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(subContainer.transform, "No-Fail", s.HideNoFail,
                v => { s.HideNoFail = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(subContainer.transform, "Difficulty", s.HideDifficulty,
                v => { s.HideDifficulty = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(subContainer.transform, "Perfect judgements", s.HidePerfectJudgements,
                v => { s.HidePerfectJudgements = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(subContainer.transform, "Song title", s.HideLevelName,
                v => { s.HideLevelName = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(subContainer.transform, "Alpha/beta build text", s.HideBetaBuild,
                v => { s.HideBetaBuild = v; notify?.Invoke(); }, null);

            subContainer.SetActive(!s.HideAllUI);
        }
    }
}
