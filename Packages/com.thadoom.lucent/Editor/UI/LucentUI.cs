using UnityEditor;
using UnityEngine;

namespace Lucent
{
    public static class LucentUI
    {
        static GUIStyle _h1, _h2, _pill, _mono, _card, _subtle;

        public static GUIStyle H1 => _h1 ??= new GUIStyle(EditorStyles.boldLabel)
        { fontSize = 16, margin = new RectOffset(0, 0, 6, 6) };

        public static GUIStyle H2 => _h2 ??= new GUIStyle(EditorStyles.boldLabel)
        { fontSize = 12, margin = new RectOffset(0, 0, 8, 2) };

        public static GUIStyle Pill => _pill ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(8, 8, 2, 2),
            normal = { textColor = Color.white }
        };

        public static GUIStyle Mono => _mono ??= new GUIStyle(EditorStyles.label)
        { font = EditorStyles.miniFont, fontSize = 10 };

        public static GUIStyle Card => _card ??= new GUIStyle("HelpBox")
        { padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(0, 0, 4, 4) };

        public static GUIStyle Subtle => _subtle ??= new GUIStyle(EditorStyles.miniLabel)
        { wordWrap = true, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };

        public static void Badge(string text, Color color, float width = 90f)
        {
            var r = GUILayoutUtility.GetRect(new GUIContent(text), Pill, GUILayout.Width(width), GUILayout.Height(18));
            EditorGUI.DrawRect(r, color * new Color(1, 1, 1, 0.85f));
            GUI.Label(r, text, Pill);
        }

        /// <summary>Horizontal VRAM bar with the four VRChat rank thresholds marked.</summary>
        public static void VramBar(Rect r, long bytes, Color fill)
        {
            const float max = 200f * 1024f * 1024f;
            EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.17f));

            DrawTick(r, Vram.RankExcellent / max, new Color(1, 1, 1, 0.18f));
            DrawTick(r, Vram.RankGood / max, new Color(1, 1, 1, 0.18f));
            DrawTick(r, Vram.RankMedium / max, new Color(1, 1, 1, 0.18f));
            DrawTick(r, Vram.RankPoor / max, new Color(1, 1, 1, 0.28f));

            float t = Mathf.Clamp01(bytes / max);
            var fillRect = new Rect(r.x, r.y, r.width * t, r.height);
            EditorGUI.DrawRect(fillRect, fill);

            DrawTick(r, Vram.RankExcellent / max, new Color(1, 1, 1, 0.18f));
            DrawTick(r, Vram.RankGood / max, new Color(1, 1, 1, 0.18f));
            DrawTick(r, Vram.RankMedium / max, new Color(1, 1, 1, 0.18f));
            DrawTick(r, Vram.RankPoor / max, new Color(1, 1, 1, 0.28f));
        }

        static void DrawTick(Rect r, float t, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x + r.width * Mathf.Clamp01(t), r.y, 1f, r.height), c);
        }

        public static Color SeverityColor(Severity s)
        {
            switch (s)
            {
                case Severity.Critical: return new Color(0.92f, 0.32f, 0.30f);
                case Severity.Warning: return new Color(0.95f, 0.72f, 0.25f);
                case Severity.Note: return new Color(0.45f, 0.68f, 0.95f);
                default: return new Color(0.35f, 0.80f, 0.55f);
            }
        }

        public static MessageType ToMessageType(Severity s)
        {
            switch (s)
            {
                case Severity.Critical: return MessageType.Error;
                case Severity.Warning: return MessageType.Warning;
                case Severity.Note: return MessageType.Info;
                default: return MessageType.None;
            }
        }

        public static void StatRow(string label, string value, bool ok)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(230));
            var c = GUI.color;
            GUI.color = ok ? new Color(0.5f, 0.9f, 0.6f) : new Color(0.95f, 0.6f, 0.35f);
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            GUI.color = c;
            EditorGUILayout.EndHorizontal();
        }

        public static void LimitRow(string label, int value, int excellent, int good, int medium, int poor)
        {
            string rank = value <= excellent ? "Excellent"
                        : value <= good ? "Good"
                        : value <= medium ? "Medium"
                        : value <= poor ? "Poor" : "Very Poor";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(180));
            EditorGUILayout.LabelField(value.ToString("N0"), EditorStyles.boldLabel, GUILayout.Width(80));
            Badge(rank, Vram.RankColor(rank), 80);
            EditorGUILayout.LabelField($"limit {excellent:N0} / {good:N0} / {medium:N0} / {poor:N0}", Subtle);
            EditorGUILayout.EndHorizontal();
        }
    }
}
