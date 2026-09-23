using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static partial class PageKeyViewer
    {
        // Page-lifetime navigation state. The page is built once per session by TabRail;
        // subpage bodies are (re)built by the PageStack on push/reveal.
        private static PageStack _stack;
        private static RectTransform _editorBody;   // current preset-editor body — drag-ghost host
        private static Action _listRebuildAll;

        // Rebind state: when a cell is right-clicked, we capture the next keydown into
        // this cell. The KeyListener lives on the editor view's GameObject.
        private static KeyViewerCell _rebindCell;
        private static KeyListener _rebindListener;
        private static Action _rebindRebuild;

        public static void Build(PageStack stack)
        {
            _stack = stack;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            Action rebuild = () => UICore.OnKeyViewerRebuild?.Invoke();

            // Drop rebuild hooks accumulated by a previous panel build (force reload).
            _listRebuildAll = null;
            // Preset names can change while an editor subpage is open.
            stack.OnRootRevealed += () => _listRebuildAll?.Invoke();

            BuildListView(stack.Root, s, notify, rebuild);
        }

        private static void BuildListView(Transform parent, Settings s, Action notify, Action rebuild)
        {
            UIBuilder.SectionHeader(parent, "Key Viewer");
            UIBuilder.Collapsible(parent, "Enable", s.ShowKeyViewer,
                v => { s.ShowKeyViewer = v; notify?.Invoke(); rebuild(); }, null);
            UIBuilder.Collapsible(parent, "Hide in level editor", s.HideKeyViewerInEditor,
                v => { s.HideKeyViewerInEditor = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(parent, "Hide in main menu", s.HideKeyViewerInMainMenu,
                v => { s.HideKeyViewerInMainMenu = v; notify?.Invoke(); }, null);
            PageUI.BuildFontSelector(parent, "Font", UICore.AvailableFonts, s.KeyViewerFontName,
                entry =>
                {
                    s.KeyViewerFontName = entry.Name;
                    MainClass.ApplySelectedFont();
                    notify?.Invoke();
                    PageOverlay.RefreshFontWeightRows?.Invoke();
                }, showWeightRow: false);
            // Weight rows only show when the KV font's family has multiple weights.
            // NOTE: relies on the Overlay tab building first (it resets
            // PageOverlay.RefreshFontWeightRows at the top of its Build).
            PageOverlay.AddWeightRow(parent, "Label weight",
                () => s.KeyViewerLabelWeight, v => s.KeyViewerLabelWeight = v,
                fontName: () => s.EffectiveKeyViewerFont);
            PageOverlay.AddWeightRow(parent, "Count weight",
                () => s.KeyViewerCountWeight, v => s.KeyViewerCountWeight = v,
                fontName: () => s.EffectiveKeyViewerFont);

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

            UIBuilder.Spacer(parent);
            UIBuilder.NavRow(parent, "DM Note presets",
                () => _stack.Push("DM Note", body => BuildDmNotePage(body, s, notify, rebuild)),
                keywords: "dmnote,import,export,json");
        }

        // <mod>/DmNote/*.json ↔ presets. Import appends a new preset and makes it active;
        // export sits in the preset editor.
        private static void BuildDmNotePage(Transform body, Settings s, Action notify, Action rebuild)
        {
            UIBuilder.SectionHeaderWithHelp(body, "DM Note presets",
                "Drop a DM Note preset.json into the DmNote folder,\n"
                + "then import it as a hand or foot preset.\n"
                + "Export (in a preset's editor) writes a preset.json\n"
                + "DM Note can open. Layout, binds, and colors carry\n"
                + "over; rain and ghost keys as far as DM Note allows.");
            var listHost = UIBuilder.VGroup(body, "DmNoteList");
            Action rebuildList = null;
            rebuildList = () =>
            {
                for (int i = listHost.transform.childCount - 1; i >= 0; i--)
                {
                    var c = listHost.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                var names = DmNotePreset.ListFiles();
                if (names.Count == 0)
                    UIBuilder.Label(listHost.transform, Loc.T("No .json files in the DmNote folder yet."),
                        (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
                foreach (var name in names)
                {
                    var row = UIBuilder.Row(listHost.transform);
                    UIBuilder.SolidImage(row, Theme.RowBg);
                    var label = UIBuilder.Label(row.transform, name, (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
                    label.rectTransform.offsetMin = new Vector2(8f, 0);
                    label.rectTransform.offsetMax = new Vector2(-160f, 0);
                    MakeMiniButton(row.transform, Loc.T("→ Hand"), 70f, -84f, () => ImportDmNote(name, false, s, notify, rebuild));
                    MakeMiniButton(row.transform, Loc.T("→ Foot"), 70f, -8f, () => ImportDmNote(name, true, s, notify, rebuild));
                }
            };
            rebuildList();
            UIBuilder.Button(body, "Rescan folder", rebuildList);
            UIBuilder.Button(body, "Open DM Note folder", () => OsShell.OpenFolder(DmNotePreset.DirPath()));
        }

        private static void ImportDmNote(string name, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            if (!DmNotePreset.Import(name, out var preset, out string err)) { BismuthLog.Log("DmNote: " + err); return; }
            var presets = isFoot ? s.KvFootPresets : s.KvHandPresets;
            if (presets == null) return;
            presets.Add(preset);
            if (isFoot) s.KvActiveFoot = presets.Count - 1; else s.KvActiveHand = presets.Count - 1;
            notify?.Invoke();
            rebuild();
            _stack.Pop();   // back to the lists, where the new preset now shows as active
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
            // Combine all preset-list rebuilds so returning to the root refreshes both hand and foot.
            _listRebuildAll = (_listRebuildAll ?? (Action)delegate { }) + listRebuild;

            string label = isFoot ? Loc.T("+ Add Foot Preset") : Loc.T("+ Add Hand Preset");
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
            var nameTxt = UIBuilder.Tmp(nameTxtGo, "", (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
            nameTxt.richText = false;

            var nameInput = UIBuilder.BuildInputField(nameGo, nameTxt);
            nameInput.contentType = TMP_InputField.ContentType.Standard;
            nameInput.lineType = TMP_InputField.LineType.SingleLine;
            nameInput.text = preset.Name ?? "";
            nameInput.onEndEdit.AddListener(v =>
            {
                preset.Name = v;
                notify?.Invoke();
            });

            // Edit — opens the editor view
            Action openEditor = () => OpenEditor(preset, isFoot, s, notify, rebuild);
            var editBtn = MakeMiniButton(row.transform, "Edit", editW,
                anchoredX: -(delW + buttonGap * 2 + 8f),
                onClick: openEditor);

            /* This row is a bespoke widget, not a NavRow, so nothing registered it — which
               left EVERY key viewer setting below it unsearchable (a user hunting "rain
               color" had to know it lives under a row). Register the preset as the search
               entry point with its subpages' contents as keywords, same as NavRow does. */
            SettingsSearch.Register((isFoot ? Loc.T("Foot preset: ") : Loc.T("Hand preset: ")) + (preset.Name ?? "?"),
                openEditor, EditorKeywords);

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
                delBtn.GetComponentInChildren<TextMeshProUGUI>().color = Theme.TextMuted;
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
            var txt = UIBuilder.Tmp(lblGo, label, (int)UIBuilder.LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

            if (onClick != null) ClickHandler.Attach(btn, onClick);
            return btn;
        }

        /* Contents of the preset editor and its row/cell subpages, so search can reach
           settings that only exist behind a drill-in. Keep in step with the labels those
           pages build — `grep -oE '(Slider|Collapsible|ColorPicker|BindKv)\(body[^,]*, "[^"]+"'`
           over this file lists them. */
        private const string EditorKeywords =
            "key width,radius,border,gap,scale,position,x,y,"
            + "rain,rain color,key rain,fade start,track length,speed,width step,"
            + "shadow size,shadow color,background,label text,count text,font size,"
            + "persist counts,reset counters,ghost keys,custom rain color,style,"
            + "glow size,glow tint,corner radius,released,pressed,"
            + "row,row height,show rain,add row,cell,key,width,visible,"
            + "hide in main menu,hide in level editor";

        // ── Editor view ────────────────────────────────────────────────────

        private static void OpenEditor(KeyViewerPreset preset, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            // rebuildOnReveal: the row/cell submenus mutate the grid underneath, so the
            // editor re-reads the preset when they pop back to it.
            _stack.Push((isFoot ? Loc.T("Foot / ") : Loc.T("Hand / ")) + preset.Name,
                body => BuildEditorContent(body, preset, isFoot, s, notify, rebuild),
                rebuildOnReveal: true);
        }

        private static void BuildEditorContent(Transform parent, KeyViewerPreset preset, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            _editorBody = (RectTransform)parent;
            // Stale hook from a previously-opened editor would target destroyed objects;
            // clear before the rows section's initial rebuild fires it.
            _ghostRefresh = null;

            // Combined callback for structural fields. Cosmetic-only fields use notify.
            Action structural = () => { notify?.Invoke(); rebuild(); };

            // Name + Reset Counters
            UIBuilder.TextInput(parent, "Name", preset.Name ?? "",
                v => { preset.Name = v; _stack.RetitleTop((isFoot ? Loc.T("Foot / ") : Loc.T("Hand / ")) + v); notify?.Invoke(); });
            UIBuilder.DangerButton(parent, "Reset counters for this preset", () =>
            {
                if (KeyViewer.Instance != null)
                {
                    KeyViewer.Instance.ResetCounts();
                    notify?.Invoke();
                }
            });
            GameObject exportBtn = null;
            exportBtn = UIBuilder.Button(parent, "Export to DM Note", () =>
            {
                if (!DmNotePreset.Export(preset, out string path, out string _)) return;
                var t = exportBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (t != null) t.text = Loc.T("Exported to DmNote/") + Path.GetFileName(path);
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
                "Rebind mode: click keys and press their new binds.\n" +
                "Click: cell settings (bind, display text, width)\n" +
                "Right Click: change key bind\n" +
                "Drag: change key position\n" +
                "Click Settings on a row for height + rain options.");
            BuildRebindModeButton(parent);
            BuildRowsSection(parent, preset, isFoot, s, notify, rebuild);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeaderWithHelp(parent, "Style",
                "Click a card for its settings, Enabled included.\n"
                + "(highlighted = on).");

            var grid = UIBuilder.CardGrid(parent).transform;

            UIBuilder.NavCard(grid, "Background", preset.ShowBackground,
                v => { preset.ShowBackground = v; structural(); },
                () => _stack.Push("Background", body =>
            {
                EnsureKv(ref preset.BgIdle, 0, 0, 0, 0.7f);
                EnsureKv(ref preset.BgHeld, 1, 1, 1, 1);
                BindKv(body, "Released", preset.BgIdle, notify);
                BindKv(body, "Pressed",  preset.BgHeld, notify);
            }));

            UIBuilder.NavCard(grid, "Border", preset.ShowBorder,
                v => { preset.ShowBorder = v; structural(); },
                () => _stack.Push("Border", body =>
            {
                UIBuilder.IntSlider(body, "Radius", preset.Radius, 0, 64,
                    v => { preset.Radius = v; structural(); });
                UIBuilder.Slider(body, "Width", preset.BorderWidth, 0f, 16f,
                    v => { preset.BorderWidth = v; structural(); }, "0.0", 0.5f);
                EnsureKv(ref preset.BorderIdle, 1, 1, 1, 1);
                EnsureKv(ref preset.BorderHeld, 1, 1, 1, 1);
                BindKv(body, "Released", preset.BorderIdle, notify);
                BindKv(body, "Pressed",  preset.BorderHeld, notify);
            }));

            UIBuilder.NavCard(grid, "Label Text", preset.ShowLabel,
                v => { preset.ShowLabel = v; structural(); },
                () => _stack.Push("Label Text", body =>
                {
                    UIBuilder.IntSlider(body, "Font size", preset.LabelSize, 6, 48,
                        v => { preset.LabelSize = v; notify?.Invoke(); });
                    EnsureKv(ref preset.TxtIdle, 1, 1, 1, 1);
                    EnsureKv(ref preset.TxtHeld, 0, 0, 0, 1);
                    BindKv(body, "Released", preset.TxtIdle, notify);
                    BindKv(body, "Pressed",  preset.TxtHeld, notify);
                }));

            UIBuilder.NavCard(grid, "Count Text", preset.ShowCount,
                v => { preset.ShowCount = v; structural(); },
                () => _stack.Push("Count Text", body =>
                {
                    UIBuilder.IntSlider(body, "Font size", preset.CountSize, 6, 48,
                        v => { preset.CountSize = v; notify?.Invoke(); });
                    EnsureKv(ref preset.CountIdle, 0.7f, 0.7f, 0.7f, 1);
                    EnsureKv(ref preset.CountHeld, 0, 0, 0, 1);
                    BindKv(body, "Released", preset.CountIdle, notify);
                    BindKv(body, "Pressed",  preset.CountHeld, notify);
                }));

            UIBuilder.NavCard(grid, "Key Rain", preset.ShowRain,
                v => { preset.ShowRain = v; structural(); },
                () => _stack.Push("Key Rain", body =>
            {
                UIBuilder.SectionHeader(body, "Track");
                UIBuilder.Slider(body, "Track length", preset.RainTrackLength, 50f, 1000f,
                    v => { preset.RainTrackLength = v; notify?.Invoke(); }, "0", 1f);
                UIBuilder.Slider(body, "Fade start", preset.RainDistance, 0f, 1000f,
                    v => { preset.RainDistance = v; notify?.Invoke(); }, "0", 1f);
                UIBuilder.Slider(body, "Speed (px/sec)", preset.RainSpeed, 50f, 2000f,
                    v => { preset.RainSpeed = v; notify?.Invoke(); }, "0", 10f);

                UIBuilder.Spacer(body);
                UIBuilder.SectionHeader(body, "Shape");
                UIBuilder.Slider(body, "Width step", preset.RainWidthStep, 0f, 30f,
                    v => { preset.RainWidthStep = v; notify?.Invoke(); }, "0.0", 0.5f);
                UIBuilder.IntSlider(body, "Corner radius", preset.RainRadius, 0, 20,
                    v => { preset.RainRadius = v; notify?.Invoke(); });

                UIBuilder.Spacer(body);
                UIBuilder.SectionHeader(body, "Shadow");
                UIBuilder.Slider(body, "Size", preset.RainShadowSize, 0f, 40f,
                    v => { preset.RainShadowSize = v; notify?.Invoke(); }, "0.0", 0.5f);
                EnsureKv(ref preset.RainShadowColor, 0, 0, 0, 0.05f);
                BindKv(body, "Color", preset.RainShadowColor, notify);

                UIBuilder.Spacer(body);
                UIBuilder.SectionHeaderWithHelp(body, "Glow",
                    "The tint multiplies the rain color, so white glows\n"
                    + "in each key's own color.");
                UIBuilder.Slider(body, "Size", preset.RainGlowSize, 0f, 60f,
                    v => { preset.RainGlowSize = v; notify?.Invoke(); }, "0.0", 0.5f);
                EnsureKv(ref preset.RainGlowColor, 1, 1, 1, 0.5f);
                BindKv(body, "Tint", preset.RainGlowColor, notify);
            }));

            // Ghost Keys — hand presets only. Foot doesn't use them. The subpage carries the
            // Enabled switch, so the rest of it is just the slots and their rain color.
            if (!isFoot)
            {
                UIBuilder.NavCard(grid, "Ghost Keys", preset.GhostKeysEnabled,
                    v => { preset.GhostKeysEnabled = v; structural(); },
                    () => _stack.Push("Ghost Keys", body =>
                    {
                        UIBuilder.SectionHeaderWithHelp(body, "Slots",
                            "Ghost keys spawn rain at the matching top-row position\n"
                            + "but don't count as input.\n"
                            + "Withholding them from the game needs the key limiter\n"
                            + "on (Input tab) — with it off, they still spawn rain\n"
                            + "but also hit tiles.");
                        BuildGhostSection(body, preset, notify, rebuild);
                    }));
            }
        }
    }
}
