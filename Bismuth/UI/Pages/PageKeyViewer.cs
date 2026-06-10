using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageKeyViewer
    {
        // Static state for the list/editor view swap. Page is built once per session
        // by TabRail; these refs persist for the page lifetime.
        private static GameObject _listView;
        private static GameObject _editorView;
        private static Action _listRebuildAll;

        // Rebind state: when a cell is right-clicked, we capture the next keydown into
        // this cell. The KeyListener lives on the editor view's GameObject.
        private static KeyViewerCell _rebindCell;
        private static KeyListener _rebindListener;
        private static Action _rebindRebuild;

        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            Action rebuild = () => UICore.OnKeyViewerRebuild?.Invoke();

            _listView = UIBuilder.Rect("ListView", content);
            var lvlg = _listView.AddComponent<VerticalLayoutGroup>();
            lvlg.childControlWidth = true;
            lvlg.childControlHeight = true;
            lvlg.childForceExpandWidth = true;
            lvlg.childForceExpandHeight = false;
            lvlg.spacing = 2f;

            _editorView = UIBuilder.Rect("EditorView", content);
            var evlg = _editorView.AddComponent<VerticalLayoutGroup>();
            evlg.childControlWidth = true;
            evlg.childControlHeight = true;
            evlg.childForceExpandWidth = true;
            evlg.childForceExpandHeight = false;
            evlg.spacing = 2f;
            _editorView.SetActive(false);

            BuildListView(_listView.transform, s, notify, rebuild);
        }

        private static void BuildListView(Transform parent, Settings s, Action notify, Action rebuild)
        {
            UIBuilder.SectionHeader(parent, "Key Viewer");
            UIBuilder.Collapsible(parent, "Enable", s.ShowKeyViewer,
                v => { s.ShowKeyViewer = v; notify?.Invoke(); rebuild(); }, null);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Hand");
            UIBuilder.Collapsible(parent, "Enabled", s.ShowHandViewer,
                v => { s.ShowHandViewer = v; notify?.Invoke(); rebuild(); }, null);
            BuildPresetList(parent, isFoot: false, s, notify, rebuild);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Foot");
            UIBuilder.Collapsible(parent, "Enabled", s.ShowFootViewer,
                v => { s.ShowFootViewer = v; notify?.Invoke(); rebuild(); }, null);
            BuildPresetList(parent, isFoot: true, s, notify, rebuild);
        }

        private static void BuildPresetList(Transform parent, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            var listGo = UIBuilder.Rect(isFoot ? "FootPresets" : "HandPresets", parent);
            var lvlg = listGo.AddComponent<VerticalLayoutGroup>();
            lvlg.childControlWidth = true;
            lvlg.childControlHeight = true;
            lvlg.childForceExpandWidth = true;
            lvlg.childForceExpandHeight = false;
            lvlg.spacing = 2f;

            Action listRebuild = null;
            listRebuild = () =>
            {
                for (int i = listGo.transform.childCount - 1; i >= 0; i--)
                {
                    var c = listGo.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                var presets = isFoot ? s.KvFootPresets : s.KvHandPresets;
                if (presets == null) return;
                for (int i = 0; i < presets.Count; i++)
                    BuildPresetRow(listGo.transform, isFoot, i, presets[i], s, notify, rebuild, listRebuild);
            };
            listRebuild();
            // Combine all preset-list rebuilds so CloseEditor refreshes both hand and foot.
            _listRebuildAll = (_listRebuildAll ?? (Action)delegate { }) + listRebuild;

            string label = isFoot ? "+ Add Foot Preset" : "+ Add Hand Preset";
            UIBuilder.Button(parent, label, () =>
            {
                var presets = isFoot ? s.KvFootPresets : s.KvHandPresets;
                if (presets == null) return;
                string nm = (isFoot ? "Foot" : "Hand") + " " + (presets.Count + 1);
                var np = new KeyViewerPreset { Name = nm };
                np.EnsureDefaults();
                presets.Add(np);
                listRebuild();
                notify?.Invoke();
                rebuild();
            });
        }

        private static void BuildPresetRow(
            Transform parent, bool isFoot, int idx, KeyViewerPreset preset,
            Settings s, Action notify, Action rebuild, Action listRebuild)
        {
            int active = isFoot ? s.KvActiveFoot : s.KvActiveHand;
            bool isActive = idx == active;

            var row = UIBuilder.Rect("Preset_" + idx, parent);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = UIBuilder.RowHeight;
            rowLe.minHeight = UIBuilder.RowHeight;
            var rowBg = UIBuilder.SolidImage(row, new Color(0, 0, 0, 0));
            rowBg.raycastTarget = true;

            const float ringSize = 14f;
            const float dotSize = 6f;
            const float editW = 50f;
            const float delW = 32f;
            const float buttonGap = 4f;

            var ringGo = UIBuilder.Rect("Ring", row.transform);
            var ringRect = (RectTransform)ringGo.transform;
            ringRect.anchorMin = new Vector2(0, 0.5f);
            ringRect.anchorMax = new Vector2(0, 0.5f);
            ringRect.pivot = new Vector2(0, 0.5f);
            ringRect.anchoredPosition = new Vector2(8f, 0);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = ringSize * 0.5f;
            ring.BorderWidth = 1.25f;
            ring.BorderColor = isActive ? Theme.ToggleOn : Theme.ToggleOff;
            ring.color = new Color(0, 0, 0, 0);
            ring.raycastTarget = true;
            var ringAccent = ringGo.AddComponent<AccentBorder>();
            ringAccent.Active = isActive;

            var dotGo = UIBuilder.Rect("Dot", ringGo.transform);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);
            var dot = dotGo.AddComponent<RoundedRectGraphic>();
            dot.Radius = dotSize * 0.5f;
            dot.color = Theme.ToggleOn;
            dot.raycastTarget = false;
            dotGo.AddComponent<AccentFill>();
            dotGo.SetActive(isActive);

            float rightCluster = editW + delW + buttonGap * 3 + 8f;
            var nameGo = UIBuilder.Rect("Name", row.transform);
            var nameRect = (RectTransform)nameGo.transform;
            nameRect.anchorMin = new Vector2(0, 0);
            nameRect.anchorMax = new Vector2(1, 1);
            nameRect.offsetMin = new Vector2(8f + ringSize + 8f, 4f);
            nameRect.offsetMax = new Vector2(-rightCluster, -4f);
            var nameBg = UIBuilder.SolidImage(nameGo, new Color(1, 1, 1, 0.04f));
            nameBg.raycastTarget = true;

            var nameTxtGo = UIBuilder.Rect("T", nameGo.transform);
            var nameTxtRect = (RectTransform)nameTxtGo.transform;
            nameTxtRect.anchorMin = Vector2.zero;
            nameTxtRect.anchorMax = Vector2.one;
            nameTxtRect.offsetMin = new Vector2(8f, 0);
            nameTxtRect.offsetMax = new Vector2(-8f, 0);
            var nameTxt = nameTxtGo.AddComponent<Text>();
            nameTxt.font = Theme.Font;
            nameTxt.fontSize = (int)UIBuilder.LabelFontSize;
            nameTxt.color = Theme.Text;
            nameTxt.alignment = TextAnchor.MiddleLeft;
            nameTxt.supportRichText = false;
            nameTxt.raycastTarget = false;

            var nameInput = nameGo.AddComponent<InputField>();
            nameInput.textComponent = nameTxt;
            nameInput.contentType = InputField.ContentType.Standard;
            nameInput.lineType = InputField.LineType.SingleLine;
            nameInput.caretWidth = 1;
            nameInput.customCaretColor = true;
            nameInput.caretColor = Theme.Text;
            nameInput.selectionColor = new Color(Theme.ToggleOn.r, Theme.ToggleOn.g, Theme.ToggleOn.b, 0.45f);
            nameInput.text = preset.Name ?? "";
            nameInput.onEndEdit.AddListener(v =>
            {
                preset.Name = v;
                notify?.Invoke();
            });

            // Edit — opens the editor view
            var editBtn = MakeMiniButton(row.transform, "Edit", editW,
                anchoredX: -(delW + buttonGap * 2 + 8f),
                onClick: () => OpenEditor(preset, isFoot, s, notify, rebuild));

            // Delete (disabled when only 1 preset)
            var presets = isFoot ? s.KvFootPresets : s.KvHandPresets;
            bool canDelete = presets != null && presets.Count > 1;
            var delBtn = MakeMiniButton(row.transform, "×", delW,
                anchoredX: -8f,
                onClick: canDelete ? new Action(() =>
                {
                    presets.RemoveAt(idx);
                    int newActive = active >= presets.Count ? presets.Count - 1 : active;
                    if (isFoot) s.KvActiveFoot = newActive;
                    else        s.KvActiveHand = newActive;
                    listRebuild();
                    notify?.Invoke();
                    rebuild();
                }) : null);
            if (!canDelete)
            {
                var delBg = delBtn.GetComponent<RoundedRectGraphic>();
                delBg.color = new Color(Theme.ButtonBg.r, Theme.ButtonBg.g, Theme.ButtonBg.b, 0.04f);
                delBg.raycastTarget = false;
                delBtn.GetComponentInChildren<Text>().color = Theme.TextMuted;
            }

            Action select = () =>
            {
                if (isFoot) s.KvActiveFoot = idx;
                else        s.KvActiveHand = idx;
                listRebuild();
                notify?.Invoke();
                rebuild();
            };
            ClickHandler.Attach(ringGo, select);
            ClickHandler.Attach(row, select);
        }

        // Mini button used for Edit / Delete / Back. Compact 22-tall pill with a label.
        private static GameObject MakeMiniButton(Transform parent, string label, float width, float anchoredX, Action onClick)
        {
            var btn = UIBuilder.Rect(label, parent);
            var rect = (RectTransform)btn.transform;
            rect.anchorMin = new Vector2(1, 0.5f);
            rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(anchoredX, 0);
            rect.sizeDelta = new Vector2(width, 22f);

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
            var txt = lblGo.AddComponent<Text>();
            txt.text = label;
            txt.font = Theme.Font;
            txt.fontSize = (int)UIBuilder.LabelFontSize;
            txt.color = Theme.Text;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            if (onClick != null) ClickHandler.Attach(btn, onClick);
            return btn;
        }

        // ── Editor view ──────────────────────────────────────────────────────

        private static void OpenEditor(KeyViewerPreset preset, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            // Tear down any prior editor contents (re-using the same container across
            // presets keeps the page's parent layout stable).
            for (int i = _editorView.transform.childCount - 1; i >= 0; i--)
            {
                var c = _editorView.transform.GetChild(i);
                c.SetParent(null);
                UnityEngine.Object.Destroy(c.gameObject);
            }

            BuildEditorContent(_editorView.transform, preset, isFoot, s, notify, rebuild);

            _listView.SetActive(false);
            _editorView.SetActive(true);
        }

        private static void CloseEditor()
        {
            _editorView.SetActive(false);
            _listView.SetActive(true);
            // Re-build preset lists in case names changed during editing.
            _listRebuildAll?.Invoke();
        }

        private static void BuildEditorContent(Transform parent, KeyViewerPreset preset, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            // Stale hook from a previously-opened editor would target destroyed objects;
            // clear before the rows section's initial rebuild fires it.
            _ghostRefresh = null;

            // Combined callback for structural fields. Cosmetic-only fields use notify.
            Action structural = () => { notify?.Invoke(); rebuild(); };

            // Top bar: Back + title
            var topRow = UIBuilder.Rect("TopRow", parent);
            var topLe = topRow.AddComponent<LayoutElement>();
            topLe.preferredHeight = 32f;
            topLe.minHeight = 32f;
            // Back button on the left
            var backBtn = UIBuilder.Rect("Back", topRow.transform);
            var backRect = (RectTransform)backBtn.transform;
            backRect.anchorMin = new Vector2(0, 0.5f);
            backRect.anchorMax = new Vector2(0, 0.5f);
            backRect.pivot = new Vector2(0, 0.5f);
            backRect.anchoredPosition = new Vector2(8f, 0);
            backRect.sizeDelta = new Vector2(90f, 24f);
            var backBg = backBtn.AddComponent<RoundedRectGraphic>();
            backBg.Radius = 3f;
            backBg.AAFringe = 0.5f;
            backBg.color = Theme.ButtonBg;
            backBg.raycastTarget = true;
            var backLbl = UIBuilder.Rect("L", backBtn.transform);
            var backLblRect = (RectTransform)backLbl.transform;
            backLblRect.anchorMin = Vector2.zero;
            backLblRect.anchorMax = Vector2.one;
            backLblRect.offsetMin = Vector2.zero;
            backLblRect.offsetMax = Vector2.zero;
            var backTxt = backLbl.AddComponent<Text>();
            backTxt.text = "← Back";
            backTxt.font = Theme.Font;
            backTxt.fontSize = (int)UIBuilder.LabelFontSize;
            backTxt.color = Theme.Text;
            backTxt.alignment = TextAnchor.MiddleCenter;
            backTxt.raycastTarget = false;
            ClickHandler.Attach(backBtn, CloseEditor);

            // Title text on the right of Back
            var title = UIBuilder.Rect("Title", topRow.transform);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(110f, 0);
            titleRect.offsetMax = new Vector2(-8f, 0);
            var titleTxt = title.AddComponent<Text>();
            titleTxt.text = "Editing " + (isFoot ? "Foot / " : "Hand / ") + preset.Name;
            titleTxt.font = Theme.Font;
            titleTxt.fontSize = (int)UIBuilder.LabelFontSize;
            titleTxt.color = Theme.TextMuted;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.raycastTarget = false;

            UIBuilder.Spacer(parent, 4f);

            // Name + Reset Counters
            UIBuilder.TextInput(parent, "Name", preset.Name ?? "",
                v => { preset.Name = v; titleTxt.text = "Editing " + (isFoot ? "Foot / " : "Hand / ") + v; notify?.Invoke(); });
            UIBuilder.DangerButton(parent, "Reset counters for this preset", () =>
            {
                if (KeyViewer.Instance != null)
                {
                    KeyViewer.Instance.ResetCounts();
                    notify?.Invoke();
                }
            });

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Main");
            UIBuilder.Slider(parent, "Key width", preset.KeyWidth, 20f, 200f,
                v => { preset.KeyWidth = v; structural(); }, "0", 1f);
            UIBuilder.Slider(parent, "Gap", preset.Gap, 0f, 30f,
                v => { preset.Gap = v; structural(); }, "0", 1f);
            UIBuilder.Slider(parent, "X", preset.X, 0f, 1f,
                v => { preset.X = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Slider(parent, "Y", preset.Y, 0f, 1f,
                v => { preset.Y = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Slider(parent, "Scale", preset.Scale, 0.25f, 3f,
                v => { preset.Scale = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Collapsible(parent, "Persist counts", preset.PersistCounts,
                v => { preset.PersistCounts = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeaderWithHelp(parent, "Rows",
                "Click: edit label/width\n" +
                "Right Click: change key bind\n" +
                "Drag: change key position\n" +
                "Click ⚙ Settings on a row for height + rain options.");
            BuildRowsSection(parent, preset, isFoot, s, notify, rebuild);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Background");
            EnsureKv(ref preset.BgIdle, 0, 0, 0, 0.7f);
            EnsureKv(ref preset.BgHeld, 1, 1, 1, 1);
            BindKv(parent, "Released", preset.BgIdle, notify);
            BindKv(parent, "Pressed",  preset.BgHeld, notify);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Border");
            UIBuilder.IntSlider(parent, "Radius", preset.Radius, 0, 64,
                v => { preset.Radius = v; structural(); });
            UIBuilder.Slider(parent, "Width", preset.BorderWidth, 0f, 16f,
                v => { preset.BorderWidth = v; structural(); }, "0.0", 0.5f);
            EnsureKv(ref preset.BorderIdle, 1, 1, 1, 1);
            EnsureKv(ref preset.BorderHeld, 1, 1, 1, 1);
            BindKv(parent, "Released", preset.BorderIdle, notify);
            BindKv(parent, "Pressed",  preset.BorderHeld, notify);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Label Text");
            UIBuilder.Collapsible(parent, "Visible", preset.ShowLabel,
                v => { preset.ShowLabel = v; structural(); }, null);
            UIBuilder.IntSlider(parent, "Font size", preset.LabelSize, 6, 48,
                v => { preset.LabelSize = v; notify?.Invoke(); });
            EnsureKv(ref preset.TxtIdle, 1, 1, 1, 1);
            EnsureKv(ref preset.TxtHeld, 0, 0, 0, 1);
            BindKv(parent, "Released", preset.TxtIdle, notify);
            BindKv(parent, "Pressed",  preset.TxtHeld, notify);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Count Text");
            UIBuilder.Collapsible(parent, "Visible", preset.ShowCount,
                v => { preset.ShowCount = v; structural(); }, null);
            UIBuilder.IntSlider(parent, "Font size", preset.CountSize, 6, 48,
                v => { preset.CountSize = v; notify?.Invoke(); });
            EnsureKv(ref preset.CountIdle, 0.7f, 0.7f, 0.7f, 1);
            EnsureKv(ref preset.CountHeld, 0, 0, 0, 1);
            BindKv(parent, "Released", preset.CountIdle, notify);
            BindKv(parent, "Pressed",  preset.CountHeld, notify);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Key Rain");
            UIBuilder.Slider(parent, "Track length", preset.RainTrackLength, 50f, 1000f,
                v => { preset.RainTrackLength = v; notify?.Invoke(); }, "0", 1f);
            UIBuilder.Slider(parent, "Fade start", preset.RainDistance, 0f, 1000f,
                v => { preset.RainDistance = v; notify?.Invoke(); }, "0", 1f);
            UIBuilder.Slider(parent, "Speed (px/sec)", preset.RainSpeed, 50f, 2000f,
                v => { preset.RainSpeed = v; notify?.Invoke(); }, "0", 10f);
            UIBuilder.Slider(parent, "Width step", preset.RainWidthStep, 0f, 30f,
                v => { preset.RainWidthStep = v; notify?.Invoke(); }, "0.0", 0.5f);
            UIBuilder.Slider(parent, "Shadow size", preset.RainShadowSize, 0f, 40f,
                v => { preset.RainShadowSize = v; notify?.Invoke(); }, "0.0", 0.5f);
            EnsureKv(ref preset.RainShadowColor, 0, 0, 0, 0.05f);
            BindKv(parent, "Shadow color", preset.RainShadowColor, notify);

            // Ghost Keys — hand presets only. Foot doesn't use them.
            if (!isFoot)
            {
                UIBuilder.Spacer(parent);
                UIBuilder.SectionHeaderWithHelp(parent, "Ghost Keys",
                    "Ghost keys spawn rain at the matching top-row position but don't count as input.");
                BuildGhostSection(parent, preset, notify, rebuild);
            }
        }

        // ── Ghost keys ───────────────────────────────────────────────────────

        // Re-syncs the ghost slot chips when the top row's cells change (slot count is
        // derived from the top row). Set while a hand preset's editor is open.
        private static Action _ghostRefresh;

        private static int TopRowKeySlots(KeyViewerPreset preset)
        {
            if (preset?.Rows == null || preset.Rows.Count == 0) return 0;
            var row = preset.Rows[0];
            if (row?.Cells == null) return 0;
            int n = 0;
            foreach (var c in row.Cells)
                if (c.Token != "KPS" && c.Token != "Total") n++;
            return n;
        }

        private static void BuildGhostSection(Transform parent, KeyViewerPreset preset, Action notify, Action rebuild)
        {
            Action structural = () => { notify?.Invoke(); rebuild(); };

            GameObject body = null;
            UIBuilder.Collapsible(parent, "Enable", preset.GhostKeysEnabled,
                v =>
                {
                    preset.GhostKeysEnabled = v;
                    if (body != null) body.SetActive(v);
                    structural();
                }, null);

            body = UIBuilder.Rect("GhostBody", parent);
            var vlg = body.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;

            // One chip per non-stat top-row cell. Click empty → listen; click assigned → clear.
            var stripGo = UIBuilder.Rect("Slots", body.transform);
            var stripLe = stripGo.AddComponent<LayoutElement>();
            stripLe.preferredHeight = 32f;
            stripLe.minHeight = 32f;
            var hlg = stripGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(8, 0, 4, 4);

            var listenerGo = UIBuilder.Rect("GhostListener", body.transform);
            var listener = listenerGo.AddComponent<KeyListener>();

            int listenIdx = -1;
            Action rebuildSlots = null;
            rebuildSlots = () =>
            {
                for (int i = stripGo.transform.childCount - 1; i >= 0; i--)
                {
                    var c = stripGo.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }

                int slots = TopRowKeySlots(preset);
                if (preset.GhostKeys == null) preset.GhostKeys = new List<string>();
                while (preset.GhostKeys.Count < slots) preset.GhostKeys.Add("None");
                while (preset.GhostKeys.Count > slots) preset.GhostKeys.RemoveAt(preset.GhostKeys.Count - 1);
                if (listenIdx >= slots) { listenIdx = -1; listener.Active = false; }

                if (slots == 0)
                {
                    MakeGhostChip(stripGo.transform, "(top row has no key cells)", false, null);
                    return;
                }

                for (int i = 0; i < slots; i++)
                {
                    int si = i;
                    string tok = preset.GhostKeys[si] ?? "None";
                    bool assigned = tok != "None" && !string.IsNullOrEmpty(tok);
                    bool listeningThis = listenIdx == si;
                    string label = listeningThis ? "…" : (assigned ? KeyTokens.PrettyTokenLabel(tok) : "None");
                    MakeGhostChip(stripGo.transform, label, listeningThis, () =>
                    {
                        if (listeningThis) { listenIdx = -1; listener.Active = false; }
                        else if (assigned)
                        {
                            preset.GhostKeys[si] = "None";
                            structural();
                        }
                        else { listenIdx = si; listener.Active = true; }
                        rebuildSlots();
                    });
                }
            };
            listener.OnKey = kc =>
            {
                if (listenIdx < 0) return;
                if (kc != KeyCode.Escape && listenIdx < preset.GhostKeys.Count)
                {
                    preset.GhostKeys[listenIdx] = KeyTokens.TokenFromKeyCode(kc);
                    structural();
                }
                listenIdx = -1;
                listener.Active = false;
                rebuildSlots();
            };
            rebuildSlots();
            _ghostRefresh = rebuildSlots;

            // Rain color defaults to yellow when unset (null). The picker binds one persistent
            // KvColor instance; the toggle just points GhostRainColor at it or back to null,
            // so edits survive toggling custom off and on again.
            var ghostCol = preset.GhostRainColor ?? new KvColor { R = 1f, G = 0.9f, B = 0f, A = 1f };
            GameObject pickerGo = null;
            UIBuilder.Collapsible(body.transform, "Custom rain color", preset.GhostRainColor != null,
                v =>
                {
                    preset.GhostRainColor = v ? ghostCol : null;
                    if (pickerGo != null) pickerGo.SetActive(v);
                    structural();
                }, null);
            pickerGo = UIBuilder.ColorPicker(body.transform, "Rain color",
                new Color(ghostCol.R, ghostCol.G, ghostCol.B, ghostCol.A), true,
                c =>
                {
                    ghostCol.R = c.r; ghostCol.G = c.g; ghostCol.B = c.b; ghostCol.A = c.a;
                    notify?.Invoke();
                });
            pickerGo.SetActive(preset.GhostRainColor != null);

            body.SetActive(preset.GhostKeysEnabled);
        }

        private static void MakeGhostChip(Transform parent, string text, bool active, Action onClick)
        {
            var go = UIBuilder.Rect("Chip", parent);
            float width = Mathf.Max(36f, text.Length * 8f + 14f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 24f;
            le.minWidth = width;
            le.minHeight = 24f;

            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = active ? Theme.ToggleOn : Theme.ButtonBg;
            bg.raycastTarget = onClick != null;
            if (active) go.AddComponent<AccentFill>();

            var txtGo = UIBuilder.Rect("T", go.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(6f, 0f);
            txtRect.offsetMax = new Vector2(-6f, 0f);
            var txt = txtGo.AddComponent<Text>();
            txt.text = text;
            txt.font = Theme.Font;
            txt.fontSize = (int)UIBuilder.LabelFontSize;
            txt.color = onClick != null ? Theme.Text : Theme.TextMuted;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            if (onClick != null) ClickHandler.Attach(go, onClick);
        }

        // ── Rows section + cell grid ─────────────────────────────────────────

        // Visual row grid: each row is a horizontal strip of cell buttons + a Row Settings
        // button on the right. Click cell → key submenu. Right-click cell → listen-rebind.
        // Click row settings → row submenu. Drag-reorder is deferred.
        private static void BuildRowsSection(
            Transform parent, KeyViewerPreset preset, bool isFoot,
            Settings s, Action notify, Action rebuild)
        {
            // KeyListener for rebind, attached once per editor build. Its OnKey is wired
            // by the right-click handlers and rewires the rebind state on each capture.
            var listenerGo = UIBuilder.Rect("RebindListener", parent);
            _rebindListener = listenerGo.AddComponent<KeyListener>();
            _rebindCell = null;
            _rebindRebuild = null;
            _rebindListener.OnKey = kc =>
            {
                if (_rebindCell == null) return;
                if (kc == KeyCode.Escape)
                {
                    // Cancel rebind without changing anything
                    _rebindCell = null;
                    _rebindListener.Active = false;
                    _rebindRebuild?.Invoke();
                    return;
                }
                // Swap the token first so TransferKeyCount's "is oldKey still in use" scan
                // sees the new binding; otherwise it spots this very cell still holding the
                // old token and leaves the old count behind.
                bool hadOld = KeyViewer.TryParseKey(_rebindCell.Token, out KeyCode oldKey);
                _rebindCell.Token = KeyTokens.TokenFromKeyCode(kc);
                _rebindCell.Label = null; // clear stale override
                if (hadOld && KeyViewer.Instance != null)
                    KeyViewer.Instance.TransferKeyCount(preset, oldKey, kc);
                _rebindCell = null;
                _rebindListener.Active = false;
                _rebindRebuild?.Invoke();
                notify?.Invoke();
                rebuild();
            };

            var rowsContainer = UIBuilder.Rect("RowsContainer", parent);
            var rvlg = rowsContainer.AddComponent<VerticalLayoutGroup>();
            rvlg.childControlWidth = true;
            rvlg.childControlHeight = true;
            rvlg.childForceExpandWidth = true;
            rvlg.childForceExpandHeight = false;
            rvlg.spacing = 4f;

            Action rebuildRows = null;
            rebuildRows = () =>
            {
                for (int i = rowsContainer.transform.childCount - 1; i >= 0; i--)
                {
                    var c = rowsContainer.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                if (preset.Rows == null) return;
                for (int i = 0; i < preset.Rows.Count; i++)
                {
                    BuildRowStrip(rowsContainer.transform, preset, isFoot, i, s, notify, rebuild, rebuildRows);
                }
                // Ghost slot count derives from the top row's cells — keep the chips in sync.
                _ghostRefresh?.Invoke();
            };
            _rebindRebuild = rebuildRows;
            rebuildRows();

            UIBuilder.Spacer(parent, 8f);
            UIBuilder.Button(parent, "+ Add Row", () =>
            {
                if (preset.Rows == null) preset.Rows = new List<KeyViewerRow>();
                var newRow = new KeyViewerRow { Cells = new List<KeyViewerCell>(), Height = 60f, ShowRain = true };
                preset.Rows.Add(newRow);
                rebuildRows();
                notify?.Invoke();
                rebuild();
            });
        }

        private static void BuildRowStrip(
            Transform parent, KeyViewerPreset preset, bool isFoot, int rowIdx,
            Settings s, Action notify, Action rebuild, Action rebuildRows)
        {
            const float cellH = 32f;
            // Reserved horizontal space for the right cluster (+ KPS Total Settings + gaps).
            const float rightClusterReserve = 280f;

            var row = preset.Rows[rowIdx];

            var stripGo = UIBuilder.Rect("Row_" + rowIdx, parent);
            var stripLe = stripGo.AddComponent<LayoutElement>();
            stripLe.preferredHeight = cellH + 4f;
            stripLe.minHeight = cellH + 4f;

            // Cells container — left-anchored, leaves room for the right cluster.
            // Only data cells live here; the + / KPS / Total / Settings buttons sit in the
            // right cluster so drag-reorder doesn't have to skip non-data children.
            var cellsGo = UIBuilder.Rect("Cells", stripGo.transform);
            var cellsRect = (RectTransform)cellsGo.transform;
            cellsRect.anchorMin = new Vector2(0, 0);
            cellsRect.anchorMax = new Vector2(1, 1);
            cellsRect.offsetMin = new Vector2(8f, 2f);
            cellsRect.offsetMax = new Vector2(-rightClusterReserve, -2f);
            var cellsHlg = cellsGo.AddComponent<HorizontalLayoutGroup>();
            cellsHlg.childControlWidth = true;
            cellsHlg.childControlHeight = true;
            cellsHlg.childForceExpandWidth = false;
            cellsHlg.childForceExpandHeight = false;
            cellsHlg.childAlignment = TextAnchor.MiddleLeft;
            cellsHlg.spacing = 2f;

            if (row.Cells != null)
            {
                for (int j = 0; j < row.Cells.Count; j++)
                {
                    BuildCellButton(cellsGo.transform, preset, isFoot, rowIdx, j, row.Cells[j], s, notify, rebuild, rebuildRows);
                }
            }

            // Right cluster: + / + KPS / + Total / ⚙ Settings, packed against the right edge.
            var rightCluster = UIBuilder.Rect("RightCluster", stripGo.transform);
            var rcRect = (RectTransform)rightCluster.transform;
            rcRect.anchorMin = new Vector2(1, 0);
            rcRect.anchorMax = new Vector2(1, 1);
            rcRect.pivot = new Vector2(1, 0.5f);
            rcRect.anchoredPosition = new Vector2(-8f, 0);
            rcRect.sizeDelta = new Vector2(0, 0);
            var rcHlg = rightCluster.AddComponent<HorizontalLayoutGroup>();
            rcHlg.childControlWidth = true;
            rcHlg.childControlHeight = true;
            rcHlg.childForceExpandWidth = false;
            rcHlg.childForceExpandHeight = false;
            rcHlg.childAlignment = TextAnchor.MiddleRight;
            rcHlg.spacing = 4f;
            // Auto-size width to fit children; right-anchored placement keeps it pinned.
            var rcCsf = rightCluster.AddComponent<ContentSizeFitter>();
            rcCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildAddCellButton(rightCluster.transform, row, rebuildRows);
            BuildAddSpecialButton(rightCluster.transform, row, "KPS", rebuildRows);
            BuildAddSpecialButton(rightCluster.transform, row, "Total", rebuildRows);
            BuildRowSettingsButton(rightCluster.transform, preset, isFoot, rowIdx, s, notify, rebuild);
        }

        // Row Settings button (HLG-positioned, no manual anchoredPosition).
        private static void BuildRowSettingsButton(Transform parent, KeyViewerPreset preset, bool isFoot, int rowIdx, Settings s, Action notify, Action rebuild)
        {
            const float w = 84f;
            var btn = UIBuilder.Rect("Settings", parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

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
            var txt = lblGo.AddComponent<Text>();
            txt.text = "⚙ Settings";
            txt.font = Theme.Font;
            txt.fontSize = (int)UIBuilder.LabelFontSize - 1;
            txt.color = Theme.Text;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            ClickHandler.Attach(btn, () => OpenRowSubmenu(preset, isFoot, rowIdx, s, notify, rebuild));
        }

        // Per-cell button. Label = override if set, else PrettyTokenLabel(Token). Click →
        // key submenu, right-click → enter rebind state for this cell (visual feedback via
        // accent tint until next keypress or Esc).
        private static void BuildCellButton(
            Transform parent, KeyViewerPreset preset, bool isFoot,
            int rowIdx, int cellIdx, KeyViewerCell cell,
            Settings s, Action notify, Action rebuild, Action rebuildRows)
        {
            float w = Mathf.Max(28f, cell.WidthMul * 40f);
            var btn = UIBuilder.Rect("Cell_" + cellIdx, parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

            bool isRebinding = (cell == _rebindCell);
            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = isRebinding ? Theme.ToggleOn : Theme.ButtonBg;
            bg.raycastTarget = true;
            if (isRebinding) btn.AddComponent<AccentFill>();

            var txtGo = UIBuilder.Rect("L", btn.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(2f, 0);
            txtRect.offsetMax = new Vector2(-2f, 0);
            var txt = txtGo.AddComponent<Text>();
            txt.font = Theme.Font;
            txt.color = Theme.Text;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            // Best-fit so long labels (Space, Total, RAlt…) auto-shrink instead of wrapping
            // into two lines and clipping. Short labels still render at the normal size.
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize = 8;
            txt.resizeTextMaxSize = (int)UIBuilder.LabelFontSize - 1;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.text = isRebinding ? "…"
                : (!string.IsNullOrEmpty(cell.Label) ? cell.Label : KeyTokens.PrettyTokenLabel(cell.Token));

            var ch = ClickHandler.Attach(btn, () => OpenKeySubmenu(preset, isFoot, rowIdx, cellIdx, s, notify, rebuild));
            ch.OnRightClick = () =>
            {
                // Cancel any prior pending rebind, then enter rebind state for this cell.
                _rebindCell = cell;
                _rebindListener.Active = true;
                rebuildRows();
            };

            // Drag-reorder. Cross-row drops route through Preset.Rows lookup in the handler.
            var dr = btn.AddComponent<CellDragReorder>();
            dr.Cell = cell;
            dr.Row = preset.Rows[rowIdx];
            dr.Preset = preset;
            dr.CellsContainer = (RectTransform)parent;
            dr.GhostHost = (RectTransform)_editorView.transform;
            // After reorder: rebuild the editor's grid AND fire the live KeyViewer rebuild
            // so the overlay reflects the new cell order immediately.
            dr.OnReorder = () =>
            {
                rebuildRows();
                notify?.Invoke();
                rebuild();
            };
        }

        // Inline "+ add cell" button at the end of each row strip. Adds an empty-token cell
        // and immediately enters rebind state for it, so the next keypress sets the token.
        private static void BuildAddCellButton(Transform parent, KeyViewerRow row, Action rebuildRows)
        {
            const float w = 28f;
            var btn = UIBuilder.Rect("AddCell", parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            // Half-alpha button bg — visually distinct from regular cells so it reads as
            // an affordance rather than another key.
            var c = Theme.ButtonBg; c.a *= 0.5f;
            bg.color = c;
            bg.raycastTarget = true;

            var txtGo = UIBuilder.Rect("L", btn.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
            var txt = txtGo.AddComponent<Text>();
            txt.text = "+";
            txt.font = Theme.Font;
            txt.fontSize = (int)UIBuilder.LabelFontSize + 2;
            txt.color = Theme.TextMuted;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            ClickHandler.Attach(btn, () =>
            {
                if (row.Cells == null) row.Cells = new List<KeyViewerCell>();
                var newCell = new KeyViewerCell { Token = "", WidthMul = 1f };
                row.Cells.Add(newCell);
                // Immediately listen-rebind the new cell — next keypress sets its token.
                _rebindCell = newCell;
                if (_rebindListener != null) _rebindListener.Active = true;
                rebuildRows();
            });
        }

        // Inline button that inserts a special-token cell (KPS or Total). No rebind step
        // since these aren't keyboard keys — they're computed by the runtime.
        private static void BuildAddSpecialButton(Transform parent, KeyViewerRow row, string token, Action rebuildRows)
        {
            float w = token.Length * 9f + 18f;
            var btn = UIBuilder.Rect("Add" + token, parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            var c = Theme.ButtonBg; c.a *= 0.5f;
            bg.color = c;
            bg.raycastTarget = true;

            var txtGo = UIBuilder.Rect("L", btn.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
            var txt = txtGo.AddComponent<Text>();
            txt.text = "+ " + token;
            txt.font = Theme.Font;
            txt.fontSize = (int)UIBuilder.LabelFontSize - 1;
            txt.color = Theme.TextMuted;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            ClickHandler.Attach(btn, () =>
            {
                if (row.Cells == null) row.Cells = new List<KeyViewerCell>();
                row.Cells.Add(new KeyViewerCell { Token = token, WidthMul = 1f });
                rebuildRows();
            });
        }

        // ── Submenus ─────────────────────────────────────────────────────────

        // Each submenu rebuilds the editor view in-place. "Back" returns to the preset editor.
        private static void OpenRowSubmenu(
            KeyViewerPreset preset, bool isFoot, int rowIdx,
            Settings s, Action notify, Action rebuild)
        {
            for (int i = _editorView.transform.childCount - 1; i >= 0; i--)
            {
                var c = _editorView.transform.GetChild(i);
                c.SetParent(null);
                UnityEngine.Object.Destroy(c.gameObject);
            }
            BuildSubmenuTopBar(_editorView.transform,
                title: "Editing " + (isFoot ? "Foot / " : "Hand / ") + preset.Name + " / Row " + (rowIdx + 1),
                onBack: () => OpenEditor(preset, isFoot, s, notify, rebuild));

            var row = preset.Rows[rowIdx];

            UIBuilder.Spacer(_editorView.transform);
            UIBuilder.SectionHeader(_editorView.transform, "Row");
            UIBuilder.Slider(_editorView.transform, "Height", row.Height, 30f, 200f,
                v => { row.Height = v; notify?.Invoke(); rebuild(); }, "0", 1f);
            UIBuilder.Collapsible(_editorView.transform, "Show rain", row.ShowRain,
                v => { row.ShowRain = v; notify?.Invoke(); rebuild(); }, null);

            EnsureKv(ref row.RainColor, 1, 1, 1, 1);
            BindKv(_editorView.transform, "Rain color", row.RainColor, notify);

            UIBuilder.Spacer(_editorView.transform);
            bool canDelete = preset.Rows.Count > 1;
            UIBuilder.Button(_editorView.transform, canDelete ? "Delete this row" : "Delete this row (last row — disabled)", () =>
            {
                if (!canDelete) return;
                preset.Rows.RemoveAt(rowIdx);
                notify?.Invoke();
                rebuild();
                OpenEditor(preset, isFoot, s, notify, rebuild);
            });
        }

        private static void OpenKeySubmenu(
            KeyViewerPreset preset, bool isFoot, int rowIdx, int cellIdx,
            Settings s, Action notify, Action rebuild)
        {
            for (int i = _editorView.transform.childCount - 1; i >= 0; i--)
            {
                var c = _editorView.transform.GetChild(i);
                c.SetParent(null);
                UnityEngine.Object.Destroy(c.gameObject);
            }
            var cell = preset.Rows[rowIdx].Cells[cellIdx];

            BuildSubmenuTopBar(_editorView.transform,
                title: "Editing " + (isFoot ? "Foot / " : "Hand / ") + preset.Name
                    + " / Row " + (rowIdx + 1) + " / Cell " + (cellIdx + 1),
                onBack: () => OpenEditor(preset, isFoot, s, notify, rebuild));

            UIBuilder.Spacer(_editorView.transform);
            UIBuilder.SectionHeader(_editorView.transform, "Key");

            // Token display (read-only — rebind via right-click in the row grid)
            var tokenRow = UIBuilder.Rect("Token", _editorView.transform);
            var tokenLe = tokenRow.AddComponent<LayoutElement>();
            tokenLe.preferredHeight = UIBuilder.RowHeight;
            tokenLe.minHeight = UIBuilder.RowHeight;
            var tokenLblGo = UIBuilder.Rect("Lbl", tokenRow.transform);
            var tokenLblRect = (RectTransform)tokenLblGo.transform;
            tokenLblRect.anchorMin = new Vector2(0, 0);
            tokenLblRect.anchorMax = new Vector2(0, 1);
            tokenLblRect.pivot = new Vector2(0, 0.5f);
            tokenLblRect.sizeDelta = new Vector2(140f, 0);
            tokenLblRect.anchoredPosition = new Vector2(8f, 0);
            var tokenLbl = tokenLblGo.AddComponent<Text>();
            tokenLbl.text = "Token";
            tokenLbl.font = Theme.Font;
            tokenLbl.fontSize = (int)UIBuilder.LabelFontSize;
            tokenLbl.color = Theme.Text;
            tokenLbl.alignment = TextAnchor.MiddleLeft;
            tokenLbl.raycastTarget = false;
            var tokenValGo = UIBuilder.Rect("Val", tokenRow.transform);
            var tokenValRect = (RectTransform)tokenValGo.transform;
            tokenValRect.anchorMin = new Vector2(1, 0);
            tokenValRect.anchorMax = new Vector2(1, 1);
            tokenValRect.pivot = new Vector2(1, 0.5f);
            tokenValRect.sizeDelta = new Vector2(220f, 0);
            tokenValRect.anchoredPosition = new Vector2(-8f, 0);
            var tokenVal = tokenValGo.AddComponent<Text>();
            tokenVal.text = KeyTokens.PrettyTokenLabel(cell.Token);
            tokenVal.font = Theme.Font;
            tokenVal.fontSize = (int)UIBuilder.LabelFontSize;
            tokenVal.color = Theme.TextMuted;
            tokenVal.alignment = TextAnchor.MiddleRight;
            tokenVal.raycastTarget = false;

            UIBuilder.TextInput(_editorView.transform, "Label", cell.Label ?? "",
                v => { cell.Label = string.IsNullOrEmpty(v) ? null : v; notify?.Invoke(); rebuild(); });

            UIBuilder.Slider(_editorView.transform, "Width", cell.WidthMul, 0.25f, 4f,
                v => { cell.WidthMul = v; notify?.Invoke(); rebuild(); }, "0.00");

            UIBuilder.Spacer(_editorView.transform);
            UIBuilder.Button(_editorView.transform, "Delete this cell", () =>
            {
                preset.Rows[rowIdx].Cells.RemoveAt(cellIdx);
                notify?.Invoke();
                rebuild();
                OpenEditor(preset, isFoot, s, notify, rebuild);
            });
        }

        // Shared top bar for sub-menus: Back button + title text.
        private static void BuildSubmenuTopBar(Transform parent, string title, Action onBack)
        {
            var topRow = UIBuilder.Rect("TopRow", parent);
            var topLe = topRow.AddComponent<LayoutElement>();
            topLe.preferredHeight = 32f;
            topLe.minHeight = 32f;

            var backBtn = UIBuilder.Rect("Back", topRow.transform);
            var backRect = (RectTransform)backBtn.transform;
            backRect.anchorMin = new Vector2(0, 0.5f);
            backRect.anchorMax = new Vector2(0, 0.5f);
            backRect.pivot = new Vector2(0, 0.5f);
            backRect.anchoredPosition = new Vector2(8f, 0);
            backRect.sizeDelta = new Vector2(90f, 24f);
            var backBg = backBtn.AddComponent<RoundedRectGraphic>();
            backBg.Radius = 3f;
            backBg.AAFringe = 0.5f;
            backBg.color = Theme.ButtonBg;
            backBg.raycastTarget = true;
            var backLbl = UIBuilder.Rect("L", backBtn.transform);
            var backLblRect = (RectTransform)backLbl.transform;
            backLblRect.anchorMin = Vector2.zero;
            backLblRect.anchorMax = Vector2.one;
            backLblRect.offsetMin = Vector2.zero;
            backLblRect.offsetMax = Vector2.zero;
            var backTxt = backLbl.AddComponent<Text>();
            backTxt.text = "← Back";
            backTxt.font = Theme.Font;
            backTxt.fontSize = (int)UIBuilder.LabelFontSize;
            backTxt.color = Theme.Text;
            backTxt.alignment = TextAnchor.MiddleCenter;
            backTxt.raycastTarget = false;
            ClickHandler.Attach(backBtn, onBack);

            var titleGo = UIBuilder.Rect("Title", topRow.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(110f, 0);
            titleRect.offsetMax = new Vector2(-8f, 0);
            var titleTxt = titleGo.AddComponent<Text>();
            titleTxt.text = title;
            titleTxt.font = Theme.Font;
            titleTxt.fontSize = (int)UIBuilder.LabelFontSize;
            titleTxt.color = Theme.TextMuted;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.raycastTarget = false;
        }

        // ────────────────────────────────────────────────────────────────────

        // KvColor binding helpers — convert KvColor ↔ Color for the ColorPicker.
        private static void EnsureKv(ref KvColor c, float r, float g, float b, float a)
        {
            if (c == null) c = new KvColor { R = r, G = g, B = b, A = a };
        }

        private static GameObject BindKv(Transform parent, string label, KvColor col, Action notify)
        {
            var initial = new Color(col.R, col.G, col.B, col.A);
            return UIBuilder.ColorPicker(parent, label, initial, true, c =>
            {
                col.R = c.r; col.G = c.g; col.B = c.b; col.A = c.a;
                notify?.Invoke();
            });
        }
    }

    // Drag-reorder for cell buttons. A ghost clone follows the cursor (preserving grab
    // offset so the cursor stays exactly where the user grabbed) while the original cell
    // fades. On drop, finds the row strip under the cursor — same row → reorder; different
    // row → splice across rows. Drop outside any row is a no-op.
    internal class CellDragReorder : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public KeyViewerCell Cell;
        public KeyViewerRow Row;
        public KeyViewerPreset Preset;
        public RectTransform CellsContainer;
        public RectTransform GhostHost;
        public Action OnReorder;

        private GameObject _ghost;
        private RectTransform _ghostRt;
        private CanvasGroup _selfCg;
        private bool _dragging;
        // World-space vector from cursor to cell pivot at grab time. Stored in WORLD coords
        // (not screen) so we don't have to keep converting; ScreenPointToWorldPointInRectangle
        // is used to convert the cursor each frame, which transparently handles CanvasScaler.
        private Vector3 _grabOffsetWorld;

        public void OnBeginDrag(PointerEventData e)
        {
            // Only respond to left-click drags. Right-click is rebind; middle is unused.
            if (e.button != PointerEventData.InputButton.Left) return;
            if (Row == null || Cell == null || CellsContainer == null || GhostHost == null) return;
            _dragging = true;

            // Compute grab offset in WORLD space — vector from cursor (converted to world
            // via ScreenPointToWorldPointInRectangle) to cell pivot.
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    (RectTransform)transform.parent, e.position, e.pressEventCamera, out Vector3 cursorWorld))
            {
                _grabOffsetWorld = transform.position - cursorWorld;
            }

            _selfCg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _selfCg.alpha = 0.25f;
            _selfCg.blocksRaycasts = false;

            _ghost = UnityEngine.Object.Instantiate(gameObject, GhostHost);
            var ghostDr = _ghost.GetComponent<CellDragReorder>();
            if (ghostDr != null) UnityEngine.Object.Destroy(ghostDr);
            var ghostCg = _ghost.GetComponent<CanvasGroup>() ?? _ghost.AddComponent<CanvasGroup>();
            ghostCg.alpha = 0.9f;
            ghostCg.blocksRaycasts = false;
            var ghostLe = _ghost.GetComponent<LayoutElement>() ?? _ghost.AddComponent<LayoutElement>();
            ghostLe.ignoreLayout = true;

            _ghostRt = (RectTransform)_ghost.transform;
            UpdateGhostPosition(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging || _ghost == null) return;
            UpdateGhostPosition(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;

            if (_selfCg != null)
            {
                _selfCg.alpha = 1f;
                _selfCg.blocksRaycasts = true;
            }
            if (_ghost != null)
            {
                UnityEngine.Object.Destroy(_ghost);
                _ghost = null;
            }

            if (Preset == null || Row == null || Cell == null || CellsContainer == null) return;

            var rowsContainer = CellsContainer.parent != null ? CellsContainer.parent.parent : null;
            if (rowsContainer == null) return;

            // Find which row's cells container the cursor is over. Sibling indices in
            // rowsContainer line up with Preset.Rows since they're built in order.
            RectTransform targetCells = null;
            int targetRowIdx = -1;
            for (int i = 0; i < rowsContainer.childCount && i < Preset.Rows.Count; i++)
            {
                var strip = rowsContainer.GetChild(i);
                var cellsT = strip.Find("Cells") as RectTransform;
                if (cellsT == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(cellsT, e.position, e.pressEventCamera))
                {
                    targetCells = cellsT;
                    targetRowIdx = i;
                    break;
                }
            }
            if (targetCells == null || targetRowIdx < 0) return;

            var targetRow = Preset.Rows[targetRowIdx];

            // Convert cursor to world space against targetCells, then compare to each child's
            // world center via GetWorldCorners. Doing the comparison in world space avoids
            // the CanvasScaler mismatch that was causing every drop to land at index 0.
            Vector3 cursorWorld;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    targetCells, e.position, e.pressEventCamera, out cursorWorld))
                return;

            Vector3[] corners = new Vector3[4];
            int targetIdx = 0;
            for (int i = 0; i < targetCells.childCount; i++)
            {
                var child = (RectTransform)targetCells.GetChild(i);
                if (child == transform) continue;
                child.GetWorldCorners(corners);
                float midX = (corners[0].x + corners[2].x) * 0.5f;
                if (cursorWorld.x > midX) targetIdx++;
            }

            int fromIdx = Row.Cells.IndexOf(Cell);
            if (fromIdx < 0) return;

            if (targetRow == Row)
            {
                if (targetIdx == fromIdx) return;
                Row.Cells.RemoveAt(fromIdx);
                // targetIdx already excludes the dragged cell (loop skips `child == transform`),
                // so it's the correct post-removal insertion index. No decrement.
                Row.Cells.Insert(Mathf.Clamp(targetIdx, 0, Row.Cells.Count), Cell);
            }
            else
            {
                Row.Cells.RemoveAt(fromIdx);
                if (targetRow.Cells == null) targetRow.Cells = new List<KeyViewerCell>();
                targetRow.Cells.Insert(Mathf.Clamp(targetIdx, 0, targetRow.Cells.Count), Cell);
            }
            OnReorder?.Invoke();
        }

        private void UpdateGhostPosition(PointerEventData e)
        {
            if (_ghost == null) return;
            var hostRt = (RectTransform)_ghost.transform.parent;
            // Convert cursor to world space against the ghost's parent rect. World coords
            // handle CanvasScaler correctly — screen pixels alone misalign under scaling.
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    hostRt, e.position, e.pressEventCamera, out Vector3 cursorWorld))
            {
                _ghost.transform.position = cursorWorld + _grabOffsetWorld;
            }
        }
    }
}
