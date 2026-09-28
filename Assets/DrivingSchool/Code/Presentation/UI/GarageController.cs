using System;
using DrivingSchool.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Экран «Автомобиль» (T65): выбор учебной машины и её настройки — коробка передач, ABS, цвет кузова. Всё сразу
    /// сохраняется в профиль ученика (ProfileService, profile.json) и действует со следующей поездки.
    /// Навигация: стрелки вверх/вниз и Tab — пункты, влево/вправо и Enter — значение настройки, Esc — назад.
    /// Список машин и их описания собирает генератор (UIBuilder) из каталога Data/Vehicles/vehicles.json.
    /// </summary>
    public sealed class GarageController : MonoBehaviour
    {
        [Serializable] public sealed class Car { public string id, title, description; public int defaultTransmission; }

        public Car[] cars = Array.Empty<Car>();
        public Button[] carButtons = Array.Empty<Button>();
        public Button transmissionButton, absButton, paintButton, backButton;
        public TMP_Text title, description, profile;

        public static readonly string[] Transmissions = { "МКПП", "АКПП" };
        public static bool ClosedThisFrame => closedFrame == Time.frameCount;
        static int closedFrame = -1;

        public string SelectedId { get; private set; }
        Action onBack;
        bool initialized;
        Button[] chain = Array.Empty<Button>();

        void Awake() { Initialize(); }

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            for (int i = 0; i < carButtons.Length && i < cars.Length; i++)
            {
                int k = i;
                carButtons[i].onClick.AddListener(() => Choose(cars[k].id));
                var view = carButtons[i].GetComponent<MenuItemView>();
                if (view != null) view.Focused += _ => Choose(cars[k].id);
            }
            transmissionButton.onClick.AddListener(() => Step(transmissionButton, 1));
            absButton.onClick.AddListener(() => Step(absButton, 1));
            paintButton.onClick.AddListener(() => Step(paintButton, 1));
            backButton.onClick.AddListener(Back);
            var list = new System.Collections.Generic.List<Button>(carButtons) { transmissionButton, absButton, paintButton, backButton };
            chain = list.ToArray();
            for (int i = 0; i < chain.Length; i++)
                chain[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = chain[(i + chain.Length - 1) % chain.Length], selectOnDown = chain[(i + 1) % chain.Length] };
            string id = ProfileService.Current.carId;
            Choose(Array.Exists(cars, c => c.id == id) ? id : cars.Length > 0 ? cars[0].id : "", save: false);
        }

        public void Open(Action back)
        {
            Initialize();
            onBack = back;
            gameObject.SetActive(true);
            Refresh();
            int i = Array.FindIndex(cars, c => c.id == SelectedId);
            Focus(i >= 0 && i < carButtons.Length ? carButtons[i] : backButton);
        }

        /// <summary>Выбрать машину: она становится машиной профиля (сохраняется сразу).</summary>
        public void Choose(string id, bool save = true)
        {
            var car = Array.Find(cars, c => c.id == id);
            if (car == null) return;
            SelectedId = id;
            if (!ProfileService.Current.Has(id)) ProfileService.Current.For(id, SettingsService.IsLoaded ? SettingsService.Current.gameplay.transmission : car.defaultTransmission);
            if (save || ProfileService.Current.carId != id) ProfileService.SelectCar(id, save);
            Refresh();
        }

        /// <summary>Сменить значение настройки на <paramref name="delta"/> шагов (по кругу).</summary>
        public void Step(Button option, int delta)
        {
            if (string.IsNullOrEmpty(SelectedId)) return;
            ProfileService.UpdateSetup(SelectedId, s =>
            {
                if (option == transmissionButton) s.transmission = Wrap(s.transmission + delta, Transmissions.Length);
                else if (option == absButton) s.abs = !s.abs;
                else if (option == paintButton) s.paint = Wrap(s.paint + delta, CarPaints.Count);
            });
            Refresh();
        }

        public CarSetup Setup => string.IsNullOrEmpty(SelectedId) ? null : ProfileService.SetupOf(SelectedId);

        void Refresh()
        {
            var car = Array.Find(cars, c => c.id == SelectedId);
            if (title != null) title.text = car?.title ?? "";
            if (description != null) description.text = car?.description ?? "";
            if (profile != null) profile.text = "Профиль: " + ProfileService.Current.name;
            var s = Setup;
            if (s == null) return;
            Label(transmissionButton, "Коробка передач: " + Transmissions[Wrap(s.transmission, Transmissions.Length)]);
            Label(absButton, "ABS: " + (s.abs ? "включена" : "выключена"));
            Label(paintButton, "Цвет кузова: " + CarPaints.Names[Wrap(s.paint, CarPaints.Count)]);
            for (int i = 0; i < carButtons.Length && i < cars.Length; i++)
                Label(carButtons[i], (cars[i].id == SelectedId ? "● " : "") + cars[i].title);
        }

        static void Label(Button b, string text)
        {
            var t = b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
            if (t != null) t.text = text;
        }

        static int Wrap(int v, int n) => n <= 0 ? 0 : (v % n + n) % n;

        public void Back()
        {
            closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            onBack?.Invoke();
        }

        static void Focus(Button b)
        {
            if (b != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        static bool MenuWheelLeft() => DrivingSchool.Input.WheelDevice.Current != null && DrivingSchool.Input.WheelDevice.WasPressed(DrivingSchool.Input.WheelDevice.Control("hat/left"));
        static bool MenuWheelRight() => DrivingSchool.Input.WheelDevice.Current != null && DrivingSchool.Input.WheelDevice.WasPressed(DrivingSchool.Input.WheelDevice.Control("hat/right"));

        void Update()
        {
            var es = EventSystem.current;
            var module = es != null ? es.currentInputModule as InputSystemUIInputModule : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            var kb = Keyboard.current;
            if (cancel != null ? cancel.WasPerformedThisFrame() : kb != null && kb.escapeKey.wasPressedThisFrame) { Back(); return; }
            var current = es != null ? es.currentSelectedGameObject : null;
            int i = Array.FindIndex(chain, b => b.gameObject == current);
            if (kb != null && kb.tabKey.wasPressedThisFrame) { Focus(chain[(i < 0 ? 0 : i + (kb.shiftKey.isPressed ? chain.Length - 1 : 1)) % chain.Length]); return; }
            if (i < 0) { int k = Array.FindIndex(cars, c => c.id == SelectedId); Focus(k >= 0 && k < carButtons.Length ? carButtons[k] : backButton); return; }
            var focused = chain[i];
            if (focused == transmissionButton || focused == absButton || focused == paintButton)
            {
                if ((kb != null && kb.leftArrowKey.wasPressedThisFrame) || MenuWheelLeft()) Step(focused, -1);
                else if ((kb != null && kb.rightArrowKey.wasPressedThisFrame) || MenuWheelRight()) Step(focused, 1);
            }
        }
    }
}
