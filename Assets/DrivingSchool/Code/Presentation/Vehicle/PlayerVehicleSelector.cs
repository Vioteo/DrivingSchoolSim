using System;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Several fully assembled player cars live in one scene (built by VehicleAssembler, all but one inactive).
    /// Before anything else starts, the chosen car is switched on and the director, camera and weather are pointed at it.
    /// M picks the next car: the drive restarts with it (the same path as «Начать заново» in the pause menu).
    /// The car and its setup (gearbox, ABS, paint) come from the player's profile (T65, menu «Автомобиль»); M also saves the
    /// new choice there.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class PlayerVehicleSelector : MonoBehaviour
    {
        /// <summary>Last car chosen with M (catalogue id); the profile's car wins when the profile has one.</summary>
        public static string SelectedId;

        public VehicleController[] cars = Array.Empty<VehicleController>();
        public string[] ids = Array.Empty<string>();
        public string[] titles = Array.Empty<string>();
        public VehicleTestRangeDirector director;
        public DriverCameraRig cameraRig;
        public WeatherController weather;
        [Tooltip("Трафик района (T51): следит за выбранной машиной.")]
        public TrafficDirectorHost traffic;

        public VehicleController Current { get; private set; }
        public int CurrentIndex { get; private set; }
        public string CurrentTitle => CurrentIndex < titles.Length ? titles[CurrentIndex] : Current != null ? Current.name : "";

        void Awake()
        {
            if (cars.Length == 0) return;
            var wanted = string.IsNullOrEmpty(ProfileService.Current.carId) ? SelectedId : ProfileService.Current.carId;
            CurrentIndex = Math.Max(0, Array.IndexOf(ids, wanted));
            for (int i = 0; i < cars.Length; i++) if (cars[i] != null && i != CurrentIndex) cars[i].gameObject.SetActive(false);
            Current = cars[CurrentIndex];
            // Every car stands at the same spawn; the chosen one takes over.
            Current.gameObject.SetActive(true);
            // The profile's setup of this car (T65): its gearbox becomes the settings' one (applied at drive start), ABS, paint.
            if (CurrentIndex < ids.Length) ApplySetup(Current, ProfileService.SelectCar(ids[CurrentIndex], save: false));
            Bind(Current);
        }

        /// <summary>ABS into the car's spec, the body paint on its own material copies (traffic cars keep the shared ones).</summary>
        public static void ApplySetup(VehicleController car, CarSetup setup)
        {
            if (car == null || setup == null) return;
            var adapter = car.GetComponent<VehiclePhysicsAdapter>();
            if (adapter != null)
            {
                try
                {
                    var spec = adapter.BuildSpec();
                    if (spec.abs != setup.abs) { spec.abs = setup.abs; adapter.ApplySpec(spec); }
                }
                catch (ArgumentException e) { Debug.LogWarning("[Vehicle] ABS from the profile not applied: " + e.Message); }
            }
            if (!CarPaints.TryGet(setup.paint, out float r, out float g, out float b)) return;
            var colour = new Color(r, g, b);
            foreach (var ren in car.GetComponentsInChildren<Renderer>(true))
            {
                var mats = ren.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith("Paint", StringComparison.Ordinal)) continue;
                    var m = new Material(mats[i]) { name = mats[i].name + " (профиль)" };
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour); else m.color = colour;
                    mats[i] = m; changed = true;
                }
                if (changed) ren.sharedMaterials = mats;
            }
        }

        void Bind(VehicleController car)
        {
            if (director != null) director.BindPlayer(car);
            if (cameraRig != null)
            {
                cameraRig.car = car.transform;
                var model = car.GetComponent<VehicleVisuals>();
                cameraRig.model = model != null && model.model != null ? model.model : car.transform;
            }
            if (traffic != null) traffic.SetPlayer(car.transform);
            if (weather != null)
            {
                weather.vehicles.RemoveAll(v => v == null || Array.Exists(cars, c => c != null && c.gameObject == v.gameObject));
                weather.vehicles.Add(car.GetComponent<VehiclePhysicsAdapter>());
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || cars.Length < 2 || Time.timeScale == 0f) return;
            if (kb.mKey.wasPressedThisFrame) Next();
        }

        /// <summary>Restarts the drive with the next car of the list.</summary>
        public void Next()
        {
            SelectedId = ids[(CurrentIndex + 1) % ids.Length];
            ProfileService.SelectCar(SelectedId);   // the profile remembers it (T65)
            Debug.Log("[Vehicle] next car: " + SelectedId);
            AppNavigator.RestartDrive();
        }
    }
}
