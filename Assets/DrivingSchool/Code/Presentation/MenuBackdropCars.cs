using System;
using System.Collections.Generic;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Settings;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Фон главного меню «Гараж» (T70): в лёгкой сцене стоят копии всех машин игрока (только модели, без физики), видна
    /// машина из профиля ученика и в его цвете. Меняется сразу, как только ученик выбрал другую машину или цвет на экране
    /// «Автомобиль» (<see cref="ProfileService.Changed"/>).
    /// </summary>
    public sealed class MenuBackdropCars : MonoBehaviour
    {
        public string[] ids = Array.Empty<string>();
        public GameObject[] cars = Array.Empty<GameObject>();

        public string ShownId { get; private set; }
        public int ShownPaint { get; private set; } = -1;

        readonly Dictionary<Renderer, Material[]> factory = new Dictionary<Renderer, Material[]>();

        void Awake() { Show(); ProfileService.Changed += OnProfileChanged; }
        void OnDestroy() { ProfileService.Changed -= OnProfileChanged; }
        void OnProfileChanged(PlayerProfile _) { if (this != null) Show(); }

        /// <summary>Показать машину профиля (нет её в сцене — первую) с её цветом кузова.</summary>
        public void Show()
        {
            if (cars.Length == 0) return;
            string wanted = ProfileService.Current.carId;
            int index = Math.Max(0, Array.IndexOf(ids, wanted));
            for (int i = 0; i < cars.Length; i++) if (cars[i] != null) cars[i].SetActive(i == index);
            string id = index < ids.Length ? ids[index] : "";
            int paint = string.IsNullOrEmpty(id) ? 0 : ProfileService.SetupOf(id).paint;
            if (id == ShownId && paint == ShownPaint) return;
            ShownId = id; ShownPaint = paint;
            Paint(cars[index], paint);
        }

        /// <summary>Цвет кузова: материалы «Paint*» заменяются копиями нужного цвета; 0 — заводские материалы.</summary>
        void Paint(GameObject car, int paint)
        {
            if (car == null) return;
            bool custom = CarPaints.TryGet(paint, out float r, out float g, out float b);
            var colour = new Color(r, g, b);
            foreach (var ren in car.GetComponentsInChildren<Renderer>(true))
            {
                if (!factory.TryGetValue(ren, out var original)) factory[ren] = original = ren.sharedMaterials;
                var mats = (Material[])original.Clone();
                bool changed = false;
                for (int i = 0; i < mats.Length && custom; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith("Paint", StringComparison.Ordinal)) continue;
                    var m = new Material(mats[i]) { name = mats[i].name + " (профиль)" };
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour); else m.color = colour;
                    mats[i] = m; changed = true;
                }
                if (changed || ren.sharedMaterials != original) ren.sharedMaterials = mats;
            }
        }
    }
}
