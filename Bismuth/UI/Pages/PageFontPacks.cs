using System;
using UnityEngine;

namespace Bismuth.UI.Pages
{
    /* Appearance → Font packs. The build ships no font files; this is where they come from.
       One row per pack: name, what's in it, and Install / Remove. The whole body rebuilds on
       any state change (FontPacks.OnChanged) rather than mutating rows — the list is under a
       dozen entries and a rebuild can't leave a row showing a stale state. */
    internal static class PageFontPacks
    {
        public static void Open(PageStack stack)
        {
            // rebuildOnReveal: an install triggers a force reload, which disposes the panel.
            stack.Push("Font packs", body => Build(body, stack), rebuildOnReveal: true);
            if (FontPacks.Status == FontPacks.State.Idle) FontPacks.Refresh();
        }

        private static void Build(Transform body, PageStack stack)
        {
            // Re-entrant: the handler fires from FontPacks.Tick, outside this build.
            FontPacks.OnChanged = () => stack.RefreshTop();

            UIBuilder.SectionHeaderWithHelp(body, "Font packs",
                "Bismuth ships without fonts to keep the download small.\n"
                + "Install a pack here, or drop your own .ttf/.otf files\n"
                + "into the mod's Fonts folder.");

            if (FontPacks.Status == FontPacks.State.Loading)
            {
                Note(body, "Loading…");
            }
            else if (FontPacks.Status == FontPacks.State.Failed)
            {
                Note(body, Loc.T("Couldn't reach the font list: ") + FontPacks.StatusMessage);
                UIBuilder.Button(body, "Retry", FontPacks.Refresh);
            }
            else if (FontPacks.Status == FontPacks.State.Ready)
            {
                if (FontPacks.Available.Count == 0) Note(body, "No packs listed.");
                foreach (var pack in FontPacks.Available) Row(body, pack);
                UIBuilder.Spacer(body);
                UIBuilder.Button(body, "Refresh list", FontPacks.Refresh);
            }
            else
            {
                UIBuilder.Button(body, "Load font list", FontPacks.Refresh);
            }

            UIBuilder.Spacer(body);
            UIBuilder.Button(body, "Open Fonts folder", () => OsShell.OpenFolder(FontPacks.FontsDir));
        }

        private static void Row(Transform body, FontPacks.Pack pack)
        {
            bool installed = FontPacks.IsInstalled(pack.Id);
            bool busy = FontPacks.Busy == pack.Id;
            bool blocked = FontPacks.Busy != null && !busy;

            var row = UIBuilder.Row(body, 34f);
            var name = UIBuilder.Label(row.transform, pack.Name,
                (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
            name.rectTransform.offsetMin = new Vector2(8f, 8f);
            name.rectTransform.offsetMax = new Vector2(-90f, 0f);

            // Second line: pack contents, or whatever went wrong with it last.
            FontPacks.Errors.TryGetValue(pack.Id, out string err);
            string sub = err != null
                ? Loc.T("Failed: ") + err
                : string.IsNullOrEmpty(pack.Size) ? pack.Note : pack.Note + "  ·  " + pack.Size;
            var note = UIBuilder.Label(row.transform, sub,
                (int)UIBuilder.LabelFontSize - 3, TextAnchor.MiddleLeft,
                err != null ? Theme.DangerText : Theme.TextMuted);
            note.rectTransform.offsetMin = new Vector2(8f, -8f);
            note.rectTransform.offsetMax = new Vector2(-90f, -14f);

            if (busy) { MiniLabel(row.transform, "Installing…"); return; }
            if (blocked) { MiniLabel(row.transform, installed ? "Already installed" : ""); return; }

            if (installed)
                MiniButton(row.transform, "Remove", () => FontPacks.Remove(pack.Id, out _));
            else
                MiniButton(row.transform, "Install", () => FontPacks.Install(pack));
        }

        private static void Note(Transform body, string text)
        {
            var row = UIBuilder.Row(body, 24f);
            var t = UIBuilder.Label(row.transform, Loc.T(text),
                (int)UIBuilder.LabelFontSize - 2, TextAnchor.MiddleLeft, Theme.TextMuted);
            t.rectTransform.offsetMin = new Vector2(8f, 0);
        }

        private static void MiniLabel(Transform row, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var t = UIBuilder.Label(row, Loc.T(text),
                (int)UIBuilder.LabelFontSize - 2, TextAnchor.MiddleRight, Theme.TextMuted);
            t.rectTransform.offsetMin = new Vector2(-140f, 0);
            t.rectTransform.offsetMax = new Vector2(-8f, 0);
        }

        private static void MiniButton(Transform parent, string label, Action onClick)
        {
            label = Loc.T(label);
            var btn = UIBuilder.Rect(label, parent);
            var rect = (RectTransform)btn.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(-8f, 0);
            rect.sizeDelta = new Vector2(74f, 22f);

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = Theme.ButtonBg;
            bg.raycastTarget = true;

            var lblGo = UIBuilder.Rect("L", btn.transform);
            var lblRect = (RectTransform)lblGo.transform;
            lblRect.anchorMin = Vector2.zero;
            lblRect.anchorMax = Vector2.one;
            lblRect.offsetMin = Vector2.zero;
            lblRect.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lblGo, label, (int)UIBuilder.LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

            ClickHandler.Attach(btn, onClick);
        }
    }
}
