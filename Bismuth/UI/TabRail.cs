using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bismuth.UI
{
    internal class TabRail
    {
        public const float Width = 130f;

        private readonly RectTransform _rail;
        private readonly RectTransform _pageHost;
        private readonly List<Tab> _tabs = new List<Tab>();
        private int _active = -1;
        private float _y = 0f;

        public RectTransform PageHost { get { return _pageHost; } }

        private class Tab
        {
            public string Name;
            public GameObject Item;
            public Image Bg;
            public Image AccentStrip;
            public TextMeshProUGUI Label;
            public RectTransform Page;
            public PageStack Stack;
        }

        public TabRail(RectTransform railParent, RectTransform pageParent)
        {
            _rail = railParent;
            _pageHost = pageParent;
            SettingsSearch.Clear(); // fresh rail = fresh search index
            var bg = _rail.gameObject.AddComponent<Image>();
            bg.sprite = Theme.White;
            bg.color = Theme.TabRail;
            bg.raycastTarget = true;
        }

        public int AddTab(string name, Action<PageStack> buildPage)
        {
            int idx = _tabs.Count;
            var item = UIBuilder.Rect("Tab_" + name, _rail);
            var r = (RectTransform)item.transform;
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1);
            r.sizeDelta = new Vector2(0, 36f);
            r.anchoredPosition = new Vector2(0, -_y);
            _y += 36f;

            var rowBg = item.AddComponent<Image>();
            rowBg.sprite = Theme.White;
            rowBg.color = new Color(0, 0, 0, 0);
            rowBg.raycastTarget = true;

            // Accent strip on the left edge (visible when active)
            var stripGo = UIBuilder.Rect("Strip", item.transform);
            var sr = (RectTransform)stripGo.transform;
            sr.anchorMin = new Vector2(0, 0);
            sr.anchorMax = new Vector2(0, 1);
            sr.pivot = new Vector2(0, 0.5f);
            sr.sizeDelta = new Vector2(2f, 0f);
            var stripImg = stripGo.AddComponent<Image>();
            stripImg.sprite = Theme.White;
            stripImg.color = Theme.Accent;
            stripImg.raycastTarget = false;
            stripGo.SetActive(false);

            var labelGo = UIBuilder.Rect("Label", item.transform);
            var lr = (RectTransform)labelGo.transform;
            lr.anchorMin = new Vector2(0, 0);
            lr.anchorMax = new Vector2(1, 1);
            lr.offsetMin = new Vector2(12f, 0f);
            lr.offsetMax = Vector2.zero;
            /* The guard pins the LOCALIZED name. The game's source-text pass is what made the
               guard necessary (it matched our English "Misc" and wrote 기타); once the label is
               already Korean there's nothing for that pass to match, and the guard defends the
               translated string rather than reverting to English. */
            string shown = Loc.T(name);
            var lbl = UIBuilder.Tmp(labelGo, shown, 15, TextAnchor.MiddleLeft, Theme.TextMuted);
            var guard = labelGo.AddComponent<TabLabelGuard>();
            guard.Label = lbl; guard.Expected = shown;

            var pageGo = UIBuilder.Rect("Page_" + name, _pageHost);
            var pr = (RectTransform)pageGo.transform;
            pr.anchorMin = Vector2.zero;
            pr.anchorMax = Vector2.one;
            pr.offsetMin = Vector2.zero;
            pr.offsetMax = Vector2.zero;
            pageGo.SetActive(false);

            SettingsSearch.BeginTab(name, idx);
            var stack = BuildScrollableContent(pr, buildPage);
            SettingsSearch.EndTab();

            var tab = new Tab { Name = name, Item = item, Bg = rowBg, AccentStrip = stripImg, Label = lbl, Page = pr, Stack = stack };
            _tabs.Add(tab);

            var hover = item.AddComponent<HoverHandler>();
            hover.OnEnter = () => { if (_active != idx) rowBg.color = Theme.TabHover; };
            hover.OnExit = () => { if (_active != idx) rowBg.color = new Color(0, 0, 0, 0); };
            ClickHandler.Attach(item, () => Select(idx));

            if (_active < 0) Select(idx);
            return idx;
        }

        // Wraps the page builder's target Transform in a scrollable VerticalLayoutGroup
        // container, with a PageStack for drill-in subpages.
        private PageStack BuildScrollableContent(RectTransform page, Action<PageStack> buildPage)
        {
            // Scroll viewport
            var viewport = UIBuilder.Rect("Viewport", page);
            var vr = (RectTransform)viewport.transform;
            vr.anchorMin = Vector2.zero;
            vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(12f, 12f);
            vr.offsetMax = new Vector2(-12f, -12f);
            // Transparent raycast target — gives the scroll wheel something to land on
            // when the cursor is between rows (otherwise the event has no handler to bubble from).
            var vpImg = viewport.AddComponent<Image>();
            vpImg.sprite = Theme.White;
            vpImg.color = new Color(0, 0, 0, 0);
            vpImg.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            var content = UIBuilder.Rect("Content", viewport.transform);
            var cr = (RectTransform)content.transform;
            cr.anchorMin = new Vector2(0, 1);
            cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta = Vector2.zero;

            // Transparent raycast target spanning Content, so every cursor position (incl.
            // row gaps) has a hit candidate — without it, scrolling between rows hits
            // nothing and ScrollRect never gets the event, making scroll feel intermittent.
            var contentBg = content.AddComponent<Image>();
            contentBg.sprite = Theme.White;
            contentBg.color = new Color(0, 0, 0, 0);
            contentBg.raycastTarget = true;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            // childControlHeight=true so VLG honors LayoutElement.preferredHeight on rows.
            // With it off, VLG reads the RectTransform's sizeDelta.y (which is 0) and the
            // content collapses to zero height — nothing to scroll.
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = page.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = vr;
            scroll.content = cr;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var stack = new PageStack(scroll, cr);
            buildPage(stack);
            return stack;
        }

        /* Master switch pinned to the rail's bottom edge (below a divider) — mirrors the
           UMM enable checkbox so the whole mod can be flipped without leaving the game.
           Same ring+dot radio as UIBuilder.Toggle; the label dims while the mod is off. */
        public void AddMasterSwitch(string label, bool initial, Action<bool> onChange)
        {
            label = Loc.T(label);
            const float rowH = 34f;
            var item = UIBuilder.Rect("MasterSwitch", _rail);
            var r = (RectTransform)item.transform;
            r.anchorMin = new Vector2(0, 0);
            r.anchorMax = new Vector2(1, 0);
            r.pivot = new Vector2(0.5f, 0);
            r.sizeDelta = new Vector2(0, rowH);
            r.anchoredPosition = Vector2.zero;

            var rowBg = item.AddComponent<Image>();
            rowBg.sprite = Theme.White;
            rowBg.color = new Color(0, 0, 0, 0);
            rowBg.raycastTarget = true;

            var div = UIBuilder.Rect("Divider", item.transform);
            var dr = (RectTransform)div.transform;
            dr.anchorMin = new Vector2(0, 1);
            dr.anchorMax = new Vector2(1, 1);
            dr.offsetMin = Vector2.zero;
            dr.offsetMax = new Vector2(0, 1f);
            var dImg = div.AddComponent<Image>();
            dImg.sprite = Theme.White;
            dImg.color = Theme.PanelBorder;
            dImg.raycastTarget = false;

            var labelGo = UIBuilder.Rect("Label", item.transform);
            var lr = (RectTransform)labelGo.transform;
            lr.anchorMin = new Vector2(0, 0);
            lr.anchorMax = new Vector2(1, 1);
            lr.offsetMin = new Vector2(12f, 0f);
            lr.offsetMax = new Vector2(-30f, 0f);
            var lbl = UIBuilder.Tmp(labelGo, label, 13, TextAnchor.MiddleLeft,
                initial ? Theme.Text : Theme.TextMuted);
            var guard = labelGo.AddComponent<TabLabelGuard>();
            guard.Label = lbl; guard.Expected = label;

            const float ringSize = 15f;
            const float dotSize = 6.5f;
            var ringGo = UIBuilder.Rect("Ring", item.transform);
            var ringRect = (RectTransform)ringGo.transform;
            ringRect.anchorMin = new Vector2(1f, 0.5f);
            ringRect.anchorMax = new Vector2(1f, 0.5f);
            ringRect.pivot = new Vector2(1f, 0.5f);
            ringRect.anchoredPosition = new Vector2(-9f, 0f);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = ringSize * 0.5f;
            ring.BorderWidth = 1.25f;
            ring.BorderColor = initial ? Theme.ToggleOn : Theme.ToggleOff;
            ring.color = new Color(0, 0, 0, 0);
            ring.raycastTarget = false;

            var dotGo = UIBuilder.Rect("Dot", ringGo.transform);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);
            var dot = dotGo.AddComponent<RoundedRectGraphic>();
            dot.Radius = dotSize * 0.5f;
            dot.color = Theme.ToggleOn;
            dot.raycastTarget = false;
            dotGo.SetActive(initial);

            bool value = initial;
            var hover = item.AddComponent<HoverHandler>();
            hover.OnEnter = () => rowBg.color = Theme.TabHover;
            hover.OnExit = () => rowBg.color = new Color(0, 0, 0, 0);
            ClickHandler.Attach(item, () =>
            {
                value = !value;
                ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
                dotGo.SetActive(value);
                lbl.color = value ? Theme.Text : Theme.TextMuted;
                onChange?.Invoke(value);
            });
        }

        // Search navigation: land on the tab's root, then run the result's opener (a
        // NavRow/NavCard onOpen closure that pushes its subpage).
        public void OpenSearchResult(int idx, Action open)
        {
            if (idx < 0 || idx >= _tabs.Count) return;
            Select(idx);
            _tabs[idx].Stack?.PopToRoot();
            open?.Invoke();
        }

        public void Select(int idx)
        {
            if (idx < 0 || idx >= _tabs.Count) return;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                bool sel = i == idx;
                t.Bg.color = sel ? Theme.TabActive : new Color(0, 0, 0, 0);
                t.AccentStrip.gameObject.SetActive(sel);
                t.Label.color = sel ? Theme.Text : Theme.TextMuted;
                t.Page.gameObject.SetActive(sel);
            }
            _active = idx;
        }
    }

    /* A guarded label always reads its fixed text. The game runs a source-text
       localization pass shortly after the panel builds that rewrites Bismuth's text by
       matching the English value ("Misc" → "기타", "Difficulty" → "난이도") — no component
       is attached (verified: the label's whole parent chain is pure Bismuth), it just
       calls `.text =`. Bismuth owns these labels, so restore the text whenever something
       changes it. The pass fires once, so this is a one-time correction in practice, not
       a per-frame fight. Used by tab labels, cards, and NavRows (the feature-name
       surfaces that collide with the game's localization keys). */
    internal class TabLabelGuard : MonoBehaviour
    {
        internal TextMeshProUGUI Label;
        internal string Expected;
        private bool _logged;

        private void LateUpdate()
        {
            if (Label == null || Label.text == Expected) return;
            if (!_logged)
            {
                _logged = true;
                BismuthLog.Debug($"[dbg] label '{Expected}' was rewritten to '{Label.text}' — restoring");
            }
            Label.text = Expected;
        }
    }
}
