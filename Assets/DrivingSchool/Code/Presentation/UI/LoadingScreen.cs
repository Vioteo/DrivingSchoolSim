using System;
using TMPro;
using UnityEngine;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Экран загрузки (T70): закрывает всё, пока грузится фон главного меню или сцена задания. Название, строка состояния,
    /// полоса прогресса (ширина <see cref="bar"/> — доля загрузки) и совет. Скрывается плавно; <see cref="Hidden"/> —
    /// когда погас совсем.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        public CanvasGroup group;
        [Tooltip("Заполнение полосы: anchorMax.x = доля загрузки")] public RectTransform bar;
        public TMP_Text status, tip;
        public float fadeSeconds = 0.5f;

        public static readonly string[] Tips =
        {
            "Перед началом движения пристегните ремень, запустите двигатель и включите указатель поворота.",
            "С рулём меню листается крестовиной, разделы переключаются лепестками, крестик — выбрать, кружок — назад.",
            "На площадке каждое упражнение можно пройти отдельно — прогресс виден в «Заданиях».",
            "Касание конуса на площадке — 3 игровых балла; с 5 баллов упражнение не зачтено.",
            "На подъёме держите машину тормозом, пока не наберёте обороты и не поймаете сцепление.",
            "Машину, коробку передач и цвет кузова можно сменить в меню «Автомобиль».",
            "В паузе (Esc) — клавиши управления (F4), настройки и досрочный разбор поездки.",
        };

        public bool Showing { get; private set; }
        public float Progress { get; private set; }
        public event Action Hidden;

        /// <summary>Показать сразу, без затухания; прогресс — с нуля, совет — случайный.</summary>
        public void Show(string text)
        {
            gameObject.SetActive(true);
            Showing = true;
            if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; }
            if (status != null) status.text = text;
            if (tip != null && Tips.Length > 0) tip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            SetProgress(0f);
        }

        public void SetProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
            if (bar != null) bar.anchorMax = new Vector2(Progress, bar.anchorMax.y);
        }

        /// <summary>Погасить плавно (за <see cref="fadeSeconds"/>); ввод проходит сразу.</summary>
        public void Hide()
        {
            if (!Showing) return;
            Showing = false;
            SetProgress(1f);
            if (group != null) group.blocksRaycasts = false;
            if (group == null || fadeSeconds <= 0f) Finish();
        }

        void Update()
        {
            if (Showing || group == null) return;
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / fadeSeconds);
            if (group.alpha <= 0f) Finish();
        }

        void Finish()
        {
            if (group != null) group.alpha = 0f;
            gameObject.SetActive(false);
            Hidden?.Invoke();
        }
    }
}
