using UnityEngine;

namespace Bismuth.UI.Pages
{
    // A page of independent on/off flags → toggle cards in a wrapping grid (tinted =
    // hidden) instead of a long radio-row column.
    internal static class PageHideUi
    {
        public static void Build(PageStack stack)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            var content = stack.Root;

            UIBuilder.SectionHeader(content, "Hide UI");
            UIBuilder.Collapsible(content, "Enable", s.HideUiEnabled,
                v => { s.HideUiEnabled = v; notify?.Invoke(); }, null);

            // Forward-declare the sub-container so the Hide All toggle handler can flip its
            // visibility. When Hide All is on, the individual toggles are no-ops, so we hide
            // them entirely.
            GameObject subContainer = null;

            UIBuilder.Collapsible(content, "Hide all UI", s.HideAllUI,
                v =>
                {
                    s.HideAllUI = v;
                    if (subContainer != null) subContainer.SetActive(!v);
                    notify?.Invoke();
                }, null);

            subContainer = UIBuilder.VGroup(content, "HideSubs");
            var sub = subContainer.transform;

            UIBuilder.Spacer(sub);
            UIBuilder.SectionHeaderWithHelp(sub, "Individual",
                "Click a card to hide that game element\n(highlighted = hidden).");

            var grid = UIBuilder.CardGrid(sub).transform;
            UIBuilder.ToggleCard(grid, "Hit error meter", s.HideHitmeter,
                v => { s.HideHitmeter = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, "Autoplay text", s.HideAutoplayText,
                v => { s.HideAutoplayText = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, "Autoplay icon", s.HideAutoplayIcon,
                v => { s.HideAutoplayIcon = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, "No-Fail", s.HideNoFail,
                v => { s.HideNoFail = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, "Difficulty", s.HideDifficulty,
                v => { s.HideDifficulty = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, "Song title", s.HideLevelName,
                v => { s.HideLevelName = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, "Beta build text", s.HideBetaBuild,
                v => { s.HideBetaBuild = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, "Autoplay controls", s.HideControlsTip,
                v => { s.HideControlsTip = v; notify?.Invoke(); });

            UIBuilder.Spacer(sub);
            UIBuilder.SectionHeaderWithHelp(sub, "Judgements",
                "Click a card to hide those judgement popups\n(highlighted = hidden).\n\"All judgements\" hides every type at once.");
            UIBuilder.Collapsible(sub, "Hide judgements", s.HideJudgementsEnabled,
                v => { s.HideJudgementsEnabled = v; notify?.Invoke(); }, null);

            // One grid: "All" card first; while it's on the per-category cards are moot,
            // so they deactivate and the grid re-flows around them.
            var jGrid = UIBuilder.CardGrid(sub).transform;
            var cats = new GameObject[5];
            void SetCats(bool all) { foreach (var c in cats) c.SetActive(!all); }

            UIBuilder.ToggleCard(jGrid, "All judgements", s.HideJudgementsAll,
                v => { s.HideJudgementsAll = v; SetCats(v); notify?.Invoke(); });
            cats[0] = UIBuilder.ToggleCard(jGrid, "Perfects", s.HideJudgementsPerfect,
                v => { s.HideJudgementsPerfect = v; notify?.Invoke(); });
            cats[1] = UIBuilder.ToggleCard(jGrid, "E/LPerfects", s.HideJudgementsELPerfect,
                v => { s.HideJudgementsELPerfect = v; notify?.Invoke(); });
            cats[2] = UIBuilder.ToggleCard(jGrid, "Early/Late", s.HideJudgementsEarlyLate,
                v => { s.HideJudgementsEarlyLate = v; notify?.Invoke(); });
            cats[3] = UIBuilder.ToggleCard(jGrid, "Misses", s.HideJudgementsMiss,
                v => { s.HideJudgementsMiss = v; notify?.Invoke(); });
            cats[4] = UIBuilder.ToggleCard(jGrid, "Deaths", s.HideJudgementsDeath,
                v => { s.HideJudgementsDeath = v; notify?.Invoke(); });
            SetCats(s.HideJudgementsAll);

            subContainer.SetActive(!s.HideAllUI);
        }
    }
}
