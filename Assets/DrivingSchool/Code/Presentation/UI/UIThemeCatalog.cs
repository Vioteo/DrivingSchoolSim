using UnityEngine;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Темы по порядку пункта gameplay.uiTheme. Лежит в Resources, создаёт UIBuilder — руками не править.</summary>
    public sealed class UIThemeCatalog : ScriptableObject
    {
        public const string ResourcePath = "UIThemeCatalog";
        public UITheme[] themes = new UITheme[0];

        static UIThemeCatalog cached;
        public static UIThemeCatalog Instance => cached != null ? cached : cached = Resources.Load<UIThemeCatalog>(ResourcePath);

        public UITheme Get(int index) => themes.Length == 0 ? null : themes[Mathf.Clamp(index, 0, themes.Length - 1)];
    }
}
