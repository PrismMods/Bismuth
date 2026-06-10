using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageLocations
    {
        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, "Locations");
            Desc(content,
                "Drag elements directly on screen: the status panels, combo display and its " +
                "label (vertical only), judgements, timing scale, attempts and key viewer panels. " +
                "Elements snap to the screen edges, a 10px inset margin, and the center lines. " +
                "Hidden elements have no handle — enable them first. " +
                "Move or close this panel if it covers something; finish with the Done " +
                "button at the top of the screen.");

            UIBuilder.Button(content, "Edit positions on screen", LocationEditor.Open);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Reset");
            UIBuilder.DangerButton(content, "Reset all positions", () =>
            {
                s.StatusLeftX  = 0.0052f; s.StatusLeftY  = 0.9907f;
                s.StatusRightX = 0.9948f; s.StatusRightY = 0.9907f;
                s.ComboDisplayX = 0.5f;   s.ComboDisplayAnchorY = 0.85f;
                s.ComboDisplayY = 0f;
                s.JudgementsX = 0.5f;     s.JudgementsAnchorY = 0f;
                s.JudgementsY = 0f;
                s.TimingScaleX = 0.5f;    s.TimingScaleAnchorY = 0.12f;
                s.TimingScaleY = 0f;
                s.AttemptsX = 0.77f;      s.AttemptsY = 0.05f;
                if (s.Hand != null) { s.Hand.X = 0.01f; s.Hand.Y = 0.01f; }
                if (s.Foot != null) { s.Foot.X = 0.01f; s.Foot.Y = 0.01f; }
                notify?.Invoke();
            });
        }

        private static void Desc(Transform parent, string text)
        {
            var wrap = UIBuilder.Rect("Desc", parent);
            var vlg = wrap.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(10, 4, 0, 6);

            var t = UIBuilder.Label(wrap.transform, text, (int)UIBuilder.LabelFontSize - 2, TextAnchor.UpperLeft, Theme.TextMuted);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
