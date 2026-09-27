using System;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Presentation.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Several fully assembled player cars live in one scene (built by VehicleAssembler, all but one inactive).
    /// Before anything else starts, the chosen car is switched on and the director, camera and weather are pointed at it.
    /// M picks the next car: the drive restarts with it (the same path as «Начать заново» in the pause menu).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class PlayerVehicleSelector : MonoBehaviour
    {
        /// <summary>Id of the car to drive (catalogue id, e.g. DS_Crossover_A); survives scene reloads. Null = the first one.</summary>
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
            CurrentIndex = Math.Max(0, Array.IndexOf(ids, SelectedId));
            for (int i = 0; i < cars.Length; i++) if (cars[i] != null && i != CurrentIndex) cars[i].gameObject.SetActive(false);
            Current = cars[CurrentIndex];
            // Every car stands at the same spawn; the chosen one takes over.
            Current.gameObject.SetActive(true);
            Bind(Current);
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
            Debug.Log("[Vehicle] next car: " + SelectedId);
            AppNavigator.RestartDrive();
        }
    }
}
