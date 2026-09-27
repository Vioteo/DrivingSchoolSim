using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Живой фон главного меню (T46). При каждом входе в меню выбирается случайная сцена из списка
    /// (шоурум с седаном, ж/д переезд с поездами, автодром), грузится аддитивно и «обезвреживается»
    /// до первого Start: игрок, директоры, их камеры и горячие клавиши выключаются. Снимает её камера меню,
    /// медленно облетая точку интереса. Сцены-источники не меняются — фон всегда совпадает с актуальной сценой.
    /// </summary>
    public sealed class MenuBackdropDirector : MonoBehaviour
    {
        [Serializable]
        public sealed class Backdrop
        {
            public string title;
            [Tooltip("Имя сцены из Build Settings")] public string sceneName;
            [Tooltip("Точка, вокруг которой летает камера (мир)")] public Vector3 pivot;
            public float radius = 8f, height = 2f, fieldOfView = 45f;
            [Tooltip("Начальный азимут; 0 — камера южнее точки и смотрит на север")] public float startYaw;
            [Tooltip("Непрерывный облёт, град/с")] public float orbitDegPerSec;
            [Tooltip("Покачивание азимута ± град (вместо облёта или вместе с ним)")] public float swayDeg;
            public float swayPeriod = 40f;
            [Tooltip("На сколько градусов сместить объект вправо в кадре — слева меню")] public float subjectRightDeg = 10f;
            [Tooltip(">0: переезд вызывает поезд с этим интервалом, первый — почти сразу")] public float trainIntervalSeconds;
        }

        public Backdrop[] backdrops = new Backdrop[0];
        public Camera view;
        [Tooltip("Затемнение под меню; гаснет, когда фон готов")] public CanvasGroup curtain;
        public float fadeSeconds = 1.5f;
        [Tooltip("-1 — случайно; иначе номер фона (для отладки)")] public int forceIndex = -1;

        // Компоненты, которые в фоне не должны работать (управление, OnGUI-подсказки, горячие клавиши, выход из игры).
        static readonly string[] DisabledBehaviours = { "VehicleTestRangeDirector", "TrainingGroundDirector", "ModelDemonstrator", "DriverCameraRig" };
        // Объекты с этими компонентами в фоне не нужны вовсе (машина игрока с физикой, чужой UI).
        static readonly string[] DeactivatedWith = { "VehicleController", "Canvas", "EventSystem" };

        static int lastIndex = -1;

        public Backdrop Current { get; private set; }
        public bool IsReady { get; private set; }
        float readyAt, firstTrainAt = -1f;
        RailwayCrossingView crossing;

        /// <summary>Случайный номер фона, не совпадающий с предыдущим (если фонов больше одного).</summary>
        public static int Pick(int count, int previous, System.Random rng)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            int i = rng.Next(count - (previous >= 0 && previous < count ? 1 : 0));
            if (previous >= 0 && previous < count && i >= previous) i++;
            return i;
        }

        void Start()
        {
            if (curtain != null) curtain.alpha = 1f;
            int index = forceIndex >= 0 && forceIndex < backdrops.Length ? forceIndex : Pick(backdrops.Length, lastIndex, new System.Random());
            if (index < 0) return;
            if (!Application.CanStreamedLevelBeLoaded(backdrops[index].sceneName))
            {
                Debug.LogWarning($"[MenuBackdrop] сцены «{backdrops[index].sceneName}» нет в Build Settings — фон не загружен");
                return;
            }
            lastIndex = index;
            Current = backdrops[index];
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadSceneAsync(Current.sceneName, LoadSceneMode.Additive);
            Debug.Log($"[MenuBackdrop] {Current.title} ({Current.sceneName})");
        }

        void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || Current == null || scene.name != Current.sceneName) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Strip(scene);
            SceneManager.SetActiveScene(scene); // небо, туман и освещение — из сцены фона

            if (view != null)
            {
                view.clearFlags = CameraClearFlags.Skybox;
                view.fieldOfView = Current.fieldOfView;
                view.nearClipPlane = 0.1f;
                view.farClipPlane = 2000f;
                view.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                PlaceCamera(0f);
            }
            if (crossing != null && Current.trainIntervalSeconds > 0f)
            {
                crossing.autoIntervalSeconds = Current.trainIntervalSeconds;
                firstTrainAt = Time.time + 2f;
            }
            IsReady = true;
            readyAt = Time.unscaledTime;
        }

        void Strip(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    cam.enabled = false;
                    if (cam.CompareTag("MainCamera")) cam.tag = "Untagged"; // Camera.main в Start сцены фона = камера меню
                }
                foreach (var l in root.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
                foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (b == null) continue;
                    string n = b.GetType().Name;
                    if (Array.IndexOf(DisabledBehaviours, n) >= 0) b.enabled = false;
                    if (b is WeatherController w) { w.hotkeys = false; w.viewCamera = view; }
                    if (b is RailwayCrossingView r && crossing == null) crossing = r;
                }
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                    if (c != null && Array.IndexOf(DeactivatedWith, c.GetType().Name) >= 0) c.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (!IsReady) return;
            float t = Time.unscaledTime - readyAt;
            if (curtain != null) curtain.alpha = fadeSeconds > 0f ? Mathf.Clamp01(1f - t / fadeSeconds) : 0f;
            if (view != null) PlaceCamera(t);
            if (firstTrainAt > 0f && Time.time >= firstTrainAt && crossing != null)
            {
                firstTrainAt = -1f;
                if (crossing.Current == RailwayCrossingView.Phase.Open) crossing.CallTrain();
            }
        }

        void PlaceCamera(float t)
        {
            var b = Current;
            float yaw = b.startYaw + b.orbitDegPerSec * t;
            if (b.swayDeg > 0f && b.swayPeriod > 0f) yaw += b.swayDeg * Mathf.Sin(t * 2f * Mathf.PI / b.swayPeriod);
            var pos = b.pivot + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, -b.radius) + Vector3.up * b.height;
            var look = Quaternion.LookRotation(b.pivot - pos, Vector3.up);
            view.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, -b.subjectRightDeg, 0f) * look);
        }
    }
}
