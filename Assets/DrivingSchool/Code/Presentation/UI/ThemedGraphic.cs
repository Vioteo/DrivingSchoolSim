using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Красит Graphic (Image, TMP-текст) цветом роли текущей темы. Без темы остаётся цвет, запечённый генератором.</summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class ThemedGraphic : MonoBehaviour
    {
        public ThemeRole role;
        [Range(0f, 1f)] public float alpha = 1f;

        void OnEnable() { UIThemeState.Changed += Apply; Apply(UIThemeState.Current); }
        void OnDisable() { UIThemeState.Changed -= Apply; }

        public void Apply(UITheme theme)
        {
            if (theme == null) return;
            var c = theme.Get(role);
            c.a *= alpha;
            GetComponent<Graphic>().color = c;
        }
    }
}
