using DrivingSchool.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Масштаб интерфейса (gameplay.uiScale): опорное разрешение CanvasScaler делится на множитель. Ставит генератор UI.</summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class UIScaleFollower : MonoBehaviour
    {
        Vector2 baseReference;
        CanvasScaler scaler;

        void Awake()
        {
            scaler = GetComponent<CanvasScaler>();
            baseReference = scaler.referenceResolution;
        }

        void OnEnable() { SettingsService.Applied += Apply; Apply(SettingsService.Current); }
        void OnDisable() { SettingsService.Applied -= Apply; }

        public void Apply(GameSettings s)
        {
            if (scaler == null || s == null) return;
            float k = Mathf.Clamp(s.gameplay.uiScale, 50, 200) / 100f;
            scaler.referenceResolution = baseReference / k;
        }
    }
}
