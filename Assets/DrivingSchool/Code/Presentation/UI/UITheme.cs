using System;
using UnityEngine;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Роли цвета из docs/ui-settings.md §8. Элементы UI ссылаются на роль, а не на литерал Color.</summary>
    public enum ThemeRole { BgDark, BgPanel, BgCard, BgRowHover, Accent, AccentSoft, OnAccent, Text, Text2, Muted, Line, Red, Green, Info }

    /// <summary>Цветовая тема интерфейса. Ассеты создаёт UIBuilder (Driving School/Build UI Prefabs), руками не править.</summary>
    [CreateAssetMenu(menuName = "Driving School/UI Theme")]
    public sealed class UITheme : ScriptableObject
    {
        public string displayName;
        public Color bgDark, bgPanel, bgCard, bgRowHover, accent, accentSoft, onAccent, text, text2, muted, line, red, green, info;

        public Color Get(ThemeRole role)
        {
            switch (role)
            {
                case ThemeRole.BgDark: return bgDark;
                case ThemeRole.BgPanel: return bgPanel;
                case ThemeRole.BgCard: return bgCard;
                case ThemeRole.BgRowHover: return bgRowHover;
                case ThemeRole.Accent: return accent;
                case ThemeRole.AccentSoft: return accentSoft;
                case ThemeRole.OnAccent: return onAccent;
                case ThemeRole.Text: return text;
                case ThemeRole.Text2: return text2;
                case ThemeRole.Muted: return muted;
                case ThemeRole.Line: return line;
                case ThemeRole.Red: return red;
                case ThemeRole.Green: return green;
                case ThemeRole.Info: return info;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
            }
        }
    }

    /// <summary>Текущая тема. Меняется из настроек (gameplay.uiTheme); подписчики перекрашиваются сразу.</summary>
    public static class UIThemeState
    {
        public static UITheme Current { get; private set; }
        public static event Action<UITheme> Changed;

        public static void Set(UITheme theme)
        {
            if (theme == null || theme == Current) return;
            Current = theme;
            Changed?.Invoke(theme);
        }
    }
}
