using System.Collections.Generic;
using DrivingSchool.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Управление меню с клавиатуры, геймпада и руля G29 (docs/ui-settings.md §6): крестовина — пункт/значение,
    /// ✕ и красная кнопка — выбрать, ○ — назад, лепестки — вкладки, △ — сброс пункта, колесо-селектор — значение.
    /// Кнопки руля в меню — по физическим именам HID (кнопка 1 = «trigger», дальше buttonN, крестовина — hat),
    /// они не зависят от назначений в поездке.
    /// </summary>
    public static class MenuInput
    {
        public const string WheelSubmit = "trigger|button1", WheelSubmitAlt = "button24", WheelCancel = "button3",
                            WheelTabPrev = "button6", WheelTabNext = "button5", WheelReset = "button4";

        static bool Down(ButtonControl b) => b != null && b.wasPressedThisFrame;
        static bool Held(ButtonControl b) => b != null && b.isPressed;
        static bool WheelDown(string path) => WheelDevice.Current != null && WheelDevice.WasPressed(WheelDevice.Control(path));
        static bool WheelHeld(string path) => WheelDevice.Current != null && WheelDevice.IsPressed(WheelDevice.Control(path));

        public static bool Submit
        {
            get
            {
                var kb = Keyboard.current; var pad = Gamepad.current;
                return Down(kb?.enterKey) || Down(kb?.numpadEnterKey) || Down(kb?.spaceKey) || Down(pad?.buttonSouth)
                       || WheelDown(WheelSubmit) || WheelDown(WheelSubmitAlt);
            }
        }

        public static bool Cancel
        {
            get
            {
                var kb = Keyboard.current; var pad = Gamepad.current;
                return Down(kb?.escapeKey) || Down(pad?.buttonEast) || WheelDown(WheelCancel);
            }
        }

        /// <summary>Отмена только с руля (для экранов, где Esc уже читается через модуль UI).</summary>
        public static bool WheelCancelPressed => WheelDown(WheelCancel);
        public static bool WheelSubmitPressed => WheelDown(WheelSubmit) || WheelDown(WheelSubmitAlt);

        public static bool TabPrev { get { var kb = Keyboard.current; var pad = Gamepad.current; return Down(kb?.qKey) || Down(pad?.leftShoulder) || WheelDown(WheelTabPrev); } }
        public static bool TabNext { get { var kb = Keyboard.current; var pad = Gamepad.current; return Down(kb?.eKey) || Down(pad?.rightShoulder) || WheelDown(WheelTabNext); } }
        public static bool ResetItem { get { var kb = Keyboard.current; var pad = Gamepad.current; return Down(kb?.rKey) || Down(pad?.buttonNorth) || WheelDown(WheelReset); } }

        /// <summary>Удерживаемое направление: стрелки, крестовина геймпада, крестовина руля, колесо-селектор (←→).</summary>
        public static Vector2Int HeldDirection
        {
            get
            {
                var kb = Keyboard.current; var pad = Gamepad.current;
                if (Held(kb?.upArrowKey) || Held(pad?.dpad.up) || WheelHeld("hat/up")) return new Vector2Int(0, 1);
                if (Held(kb?.downArrowKey) || Held(pad?.dpad.down) || WheelHeld("hat/down")) return new Vector2Int(0, -1);
                if (Held(kb?.leftArrowKey) || Held(pad?.dpad.left) || WheelHeld("hat/left") || WheelDown("button23")) return new Vector2Int(-1, 0);
                if (Held(kb?.rightArrowKey) || Held(pad?.dpad.right) || WheelHeld("hat/right") || WheelDown("button22")) return new Vector2Int(1, 0);
                return Vector2Int.zero;
            }
        }
    }

    /// <summary>Повтор направления при удержании: первое срабатывание сразу, затем через 0,35 с каждые 0,07 с.</summary>
    public sealed class DirectionRepeater
    {
        Vector2Int held; float repeatAt;

        public Vector2Int Next(Vector2Int d)
        {
            if (d == Vector2Int.zero) { held = d; return d; }
            float now = Time.unscaledTime;
            if (d != held) { held = d; repeatAt = now + 0.35f; return d; }
            if (now >= repeatAt) { repeatAt = now + 0.07f; return d; }
            return Vector2Int.zero;
        }
    }

    /// <summary>
    /// Модуль UI Input System и руль. В стандартных действиях UI навигация привязана к «&lt;Joystick&gt;/stick», а у G29
    /// это руль и педаль сцепления: отпущенная педаль держит «стрелку» и меню «уплывает». Здесь эти привязки
    /// выключаются, а навигация, выбор и отмена привязываются к крестовине, ✕/красной кнопке и ○.
    /// </summary>
    public sealed class WheelMenuBridge : MonoBehaviour
    {
        readonly HashSet<int> patched = new HashSet<int>();
        float nextCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<WheelMenuBridge>() != null) return;
            var go = new GameObject("WheelMenuBridge");
            DontDestroyOnLoad(go);
            go.AddComponent<WheelMenuBridge>();
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            foreach (var m in FindObjectsByType<InputSystemUIInputModule>(FindObjectsSortMode.None))
                if (m.actionsAsset != null && patched.Add(m.actionsAsset.GetInstanceID())) Patch(m);
        }

        public static void Patch(InputSystemUIInputModule module)
        {
            var asset = module.actionsAsset;
            if (asset == null) return;
            bool wasEnabled = asset.enabled;
            asset.Disable();
            foreach (var action in asset)
            {
                // Оси джойстика в навигации — это руль и педаль у G29: выключить.
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var b = action.bindings[i];
                    if (!b.isComposite && b.path != null && b.path.StartsWith("<Joystick>/stick"))
                        action.ApplyBindingOverride(i, new InputBinding { overridePath = "<Joystick>/__disabled__" });
                }
            }
            var nav = module.move != null ? module.move.action : null;
            var submit = module.submit != null ? module.submit.action : null;
            var cancel = module.cancel != null ? module.cancel.action : null;
            nav?.AddBinding("<Joystick>/hat");
            if (submit != null) { submit.AddBinding("<Joystick>/trigger"); submit.AddBinding("<Joystick>/button24"); }
            cancel?.AddBinding("<Joystick>/button3");
            if (wasEnabled) asset.Enable();
            Debug.Log("[Wheel] меню: навигация крестовиной руля, ✕ — выбрать, ○ — назад");
        }
    }
}
