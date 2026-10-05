using System.Collections.Generic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Automatic railway crossing with barriers: crossing signals (two red lamps flashing alternately, a slow white
    /// lamp while the crossing is open), a bell, automatic barriers (TK_RailwayBarrier, boom pivot "Boom_Pivot") and
    /// a train that passes while the crossing is closed.
    /// Cycle: Open → Warning (red lamps + bell) → Lowering → Closed (train passes) → Raising → Open.
    /// The train is called by <see cref="CallTrain"/> (test range: key G) or every <see cref="autoIntervalSeconds"/>.
    /// Timings are game values for the test range, not a railway standard.
    /// </summary>
    public sealed class RailwayCrossingView : MonoBehaviour
    {
        public enum Phase { Open, Warning, Lowering, Closed, Raising }

        [Header("Parts")]
        public Transform[] barriers;
        public Transform[] signals;
        public Rigidbody train;
        [Tooltip("T69: составы, которые идут по очереди (электричка, грузовой). Пусто — только train.")]
        public Rigidbody[] consists = new Rigidbody[0];
        [Tooltip("Длина каждого состава из consists, м (от сцепки головы до сцепки хвоста).")]
        public float[] consistLengths = new float[0];
        [Tooltip("Crossing centre on the track axis (world).")] public Vector3 crossingCentre;
        [Tooltip("Direction the train travels (world, horizontal).")] public Vector3 trainDirection = Vector3.left;
        [Tooltip("Train length, metres (front to rear).")] public float trainLength = 76f;
        [Tooltip("How far along the track the train appears and disappears from the crossing, metres.")] public float trackHalfLength = 240f;

        [Header("Timing (game values)")]
        public float trainSpeedKmh = 60f;
        [Tooltip("Seconds from the first red flash until the train's front reaches the crossing.")] public float trainArrivesAfter = 25f;
        public float warningBeforeLowering = 6f, lowerSeconds = 7f, raiseSeconds = 6f, clearDelaySeconds = 5f;
        [Tooltip("0 = only on request.")] public float autoIntervalSeconds = 120f;
        public float boomOpenAngle = 85f;

        [Header("Lamps")]
        public Color redEmission = new Color(1f, 0.06f, 0.02f) * 6f;
        public Color whiteEmission = new Color(0.75f, 0.82f, 1f) * 4f;

        public Phase Current { get; private set; } = Phase.Open;
        public bool IsClosedForTraffic => Current != Phase.Open;
        public float TrainDistanceToCrossing { get; private set; } = float.PositiveInfinity;
        /// <summary>Номер состава из <see cref="consists"/>, который идёт сейчас (или шёл последним); −1 — составов нет.</summary>
        public int ConsistIndex { get; private set; } = -1;
        public bool TrainRunning => trainRunning;
        /// <summary>Front of the train on the track axis (world); meaningful while <see cref="TrainRunning"/>.</summary>
        public Vector3 TrainFront => crossingCentre + trainDirection * (float.IsInfinity(trainS) ? trackHalfLength : trainS);

        sealed class Boom { public Transform pivot; public Quaternion closed, open; public List<Material> lamps = new List<Material>(); public List<Renderer> discs = new List<Renderer>(); }
        readonly List<Boom> booms = new List<Boom>();
        readonly List<Material> redL = new List<Material>(), redR = new List<Material>(), white = new List<Material>();
        // T75: the glowing discs in front of the lenses (Lamp_Red_L/R, Lamp_White, Lamp_Boom_*; docs/railway-crossing.md)
        // are shown only while their lamp is lit — before, they stayed on and a dark signal looked red and white.
        readonly List<Renderer> discRedL = new List<Renderer>(), discRedR = new List<Renderer>(), discWhite = new List<Renderer>();
        readonly List<AudioSource> bells = new List<AudioSource>();
        float phaseTime, idleTime, bellTimer, clearedFor, trainS = float.NegativeInfinity, boom01 = 0f;
        bool trainRunning;
        Renderer[] trainRenderers;
        static AudioClip bellClip;

        void Start()
        {
            // T75: head and tail lamps, searchlight, lit saloon windows (added before the trains are hidden below).
            if (Application.isPlaying)
            {
                foreach (var c in consists) if (c != null && c.GetComponent<TrainLightsView>() == null) c.gameObject.AddComponent<TrainLightsView>();
                if (train != null && train.GetComponent<TrainLightsView>() == null) train.gameObject.AddComponent<TrainLightsView>();
            }
            foreach (var b in barriers) if (b != null) SetupBarrier(b);
            foreach (var s in signals) if (s != null) SetupSignal(s);
            // T69: все составы — кинематические и спрятаны до вызова; текущий — первый.
            for (int i = 0; i < consists.Length; i++)
                if (consists[i] != null) { Prepare(consists[i]); SetVisible(consists[i], false); }
            // последний — «прошедший», так что первым вызовом придёт consists[0]
            if (consists.Length > 0 && consists[consists.Length - 1] != null) Select(consists.Length - 1);
            if (train != null)
            {
                Prepare(train);
                trainRenderers = train.GetComponentsInChildren<Renderer>(true);
                ShowTrain(false);
            }
            trainDirection.y = 0f; trainDirection.Normalize();
            if (train != null && Application.isPlaying)
            {
                var sound = new GameObject("TrainAudio").AddComponent<TrainAudio>();   // T67: rumble, clacks, horn
                sound.transform.SetParent(transform, false);
                sound.crossing = this;
            }
            ApplyBooms(0f);
            SetLamps(false, false, true);
        }

        static void Prepare(Rigidbody rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate; }

        static void SetVisible(Rigidbody rb, bool on)
        {
            foreach (var r in rb.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var c in rb.GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        /// <summary>Сделать текущим состав <paramref name="index"/> из <see cref="consists"/> (прячет прежний).</summary>
        void Select(int index)
        {
            if (train != null && train != consists[index]) SetVisible(train, false);
            ConsistIndex = index;
            train = consists[index];
            if (index < consistLengths.Length && consistLengths[index] > 0f) trainLength = consistLengths[index];
            trainRenderers = train.GetComponentsInChildren<Renderer>(true);
        }

        public void CallTrain()
        {
            if (Current != Phase.Open) return;
            // T69: составы идут по очереди — электричка, затем грузовой, и снова.
            if (consists.Length > 1)
            {
                int next = ConsistIndex;
                for (int k = 0; k < consists.Length; k++) { next = (next + 1) % consists.Length; if (consists[next] != null) break; }
                if (consists[next] != null) Select(next);
            }
            Enter(Phase.Warning);
            // Front of the train starts so that it reaches the crossing after trainArrivesAfter seconds.
            float v = trainSpeedKmh / 3.6f;
            trainS = -v * trainArrivesAfter;
            trainRunning = train != null;
            ShowTrain(trainRunning);
            PlaceTrain();
        }

        void Enter(Phase p) { Current = p; phaseTime = 0f; }

        void Update()
        {
            float dt = Time.deltaTime;
            phaseTime += dt;
            switch (Current)
            {
                case Phase.Open:
                    idleTime += dt;
                    if (autoIntervalSeconds > 0f && idleTime >= autoIntervalSeconds) { idleTime = 0f; CallTrain(); }
                    break;
                case Phase.Warning:
                    if (phaseTime >= warningBeforeLowering) Enter(Phase.Lowering);
                    break;
                case Phase.Lowering:
                    boom01 = Mathf.Clamp01(phaseTime / lowerSeconds);
                    if (boom01 >= 1f) Enter(Phase.Closed);
                    break;
                case Phase.Closed:
                    // Open once the train's rear has cleared the crossing (or at once if there is no train).
                    bool cleared = !trainRunning || trainS - trainLength > 6f;
                    if (!cleared) clearedFor = 0f; else clearedFor += dt;
                    if (phaseTime > 1f && clearedFor >= clearDelaySeconds) Enter(Phase.Raising);
                    break;
                case Phase.Raising:
                    boom01 = 1f - Mathf.Clamp01(phaseTime / raiseSeconds);
                    if (boom01 <= 0f) { Enter(Phase.Open); idleTime = 0f; }
                    break;
            }
            ApplyBooms(boom01);

            bool warning = Current != Phase.Open;
            bool blink = Mathf.Repeat(Time.time, 1f) < 0.5f;                  // red lamps alternate at 1 Hz
            bool slow = Mathf.Repeat(Time.time, 2f) < 0.8f;                   // white lamp: slow flashing while open
            SetLamps(warning && blink, warning && !blink, !warning && slow);
            bool boomOn = warning && (Current != Phase.Warning) && blink;
            foreach (var b in booms) { foreach (var m in b.lamps) SetLens(m, boomOn, redEmission); Show(b.discs, boomOn); }

            if (warning && Current != Phase.Raising)
            {
                bellTimer -= dt;
                if (bellTimer <= 0f) { bellTimer = 0.42f; float env = DrivingSchool.Settings.AudioMix.Current(DrivingSchool.Settings.AudioChannel.Environment); foreach (var s in bells) s.PlayOneShot(BellClip(), 0.7f * env); }
            }
            else bellTimer = 0f;
        }

        void FixedUpdate()
        {
            if (!trainRunning || train == null) return;
            trainS += trainSpeedKmh / 3.6f * Time.fixedDeltaTime;
            TrainDistanceToCrossing = -trainS;
            if (trainS - trainLength > trackHalfLength) { trainRunning = false; ShowTrain(false); TrainDistanceToCrossing = float.PositiveInfinity; return; }
            PlaceTrain(true);
        }

        void PlaceTrain(bool physics = false)
        {
            if (train == null) return;
            // trainS = signed distance of the train front past the crossing centre along trainDirection.
            Vector3 front = crossingCentre + trainDirection * trainS;
            Quaternion rot = Quaternion.LookRotation(trainDirection, Vector3.up);
            Vector3 pos = new Vector3(front.x, train.position.y, front.z);
            if (physics) { train.MovePosition(pos); train.MoveRotation(rot); }
            else { train.position = pos; train.rotation = rot; train.transform.SetPositionAndRotation(pos, rot); }
        }

        void ShowTrain(bool on)
        {
            if (trainRenderers != null) foreach (var r in trainRenderers) r.enabled = on;
            if (train != null) foreach (var c in train.GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        // ------------------------------------------------------------------ barriers

        void SetupBarrier(Transform root)
        {
            var b = new Boom { pivot = Find(root, "Boom_Pivot") };
            if (b.pivot == null) return;
            var tip = Find(root, "Boom_Tip");
            b.closed = b.pivot.localRotation;
            // The boom lies along the pivot's local X; turning about the horizontal axis across the boom raises it.
            Vector3 boomDir = tip != null ? (tip.position - b.pivot.position).normalized : b.pivot.right;
            Vector3 axisWorld = Vector3.Cross(boomDir, Vector3.up).normalized;
            Vector3 axisLocal = b.pivot.InverseTransformDirection(axisWorld);
            var up = b.closed * Quaternion.AngleAxis(boomOpenAngle, axisLocal);
            var down = b.closed * Quaternion.AngleAxis(-boomOpenAngle, axisLocal);
            // pick the rotation that lifts the tip
            b.pivot.localRotation = up; float yUp = tip != null ? tip.position.y : 0f;
            b.pivot.localRotation = down; float yDown = tip != null ? tip.position.y : 0f;
            b.open = yUp >= yDown ? up : down;
            b.pivot.localRotation = b.closed;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Lens_Boom")) b.lamps.Add(Instance(r));
                else if (r.name.StartsWith("Lamp_Boom")) b.discs.Add(Glow(r, redEmission));
            booms.Add(b);
            AddBell(root);
        }

        void ApplyBooms(float closed01)
        {
            float t = Mathf.SmoothStep(0f, 1f, closed01);
            foreach (var b in booms) b.pivot.localRotation = Quaternion.Slerp(b.open, b.closed, t);
        }

        // ------------------------------------------------------------------ signals

        void SetupSignal(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("Lamp_Red_L")) discRedL.Add(Glow(r, redEmission));
                else if (r.name.StartsWith("Lamp_Red_R")) discRedR.Add(Glow(r, redEmission));
                else if (r.name.StartsWith("Lamp_White")) discWhite.Add(Glow(r, whiteEmission));
                else if (r.name.StartsWith("Lens_Red_L")) redL.Add(Instance(r));
                else if (r.name.StartsWith("Lens_Red_R")) redR.Add(Instance(r));
                else if (r.name.StartsWith("Lens_White")) white.Add(Instance(r));
            }
            AddBell(root);
        }

        void SetLamps(bool left, bool right, bool whiteOn)
        {
            foreach (var m in redL) SetLens(m, left, redEmission);
            foreach (var m in redR) SetLens(m, right, redEmission);
            foreach (var m in white) SetLens(m, whiteOn, whiteEmission);
            Show(discRedL, left); Show(discRedR, right); Show(discWhite, whiteOn);
        }

        /// <summary>
        /// A lamp disc really glows: the RW_RedOn / RW_WhiteOn assets carry an emission colour but lost the _EMISSION
        /// keyword, so the disc showed its flat colour (bright by day, dark at night). The instance gets the keyword and the
        /// lamp's saturated emission (a plain ×3 of the asset colour burns out to yellow-white under tone mapping).
        /// </summary>
        static Renderer Glow(Renderer r, Color emission)
        {
            var m = r.material;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission);
            return r;
        }

        static void Show(List<Renderer> discs, bool on)
        {
            foreach (var r in discs) if (r != null && r.enabled != on) r.enabled = on;
        }

        // Lens colours: lit — the material's own colour; off (T75) — a dark glossy lens of the same hue, as a real unlit
        // signal lens looks (deep cherry red, smoky "moon-white"), so a dark signal does not read as lit.
        readonly Dictionary<Material, Color> litBase = new Dictionary<Material, Color>(), offBase = new Dictionary<Material, Color>();
        readonly Dictionary<Material, bool> lensOn = new Dictionary<Material, bool>();

        Material Instance(Renderer r)
        {
            var m = r.material;   // per-renderer instance
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_BaseColor"))
            {
                Color c = m.GetColor("_BaseColor");
                litBase[m] = c;
                bool red = c.r > c.g * 2f;
                offBase[m] = red ? new Color(0.11f, 0.01f, 0.01f) : new Color(0.13f, 0.14f, 0.15f);
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);   // glass lens: a sharp highlight when off
            return m;
        }

        void SetLens(Material m, bool on, Color emission)
        {
            if (m == null) return;
            if (lensOn.TryGetValue(m, out bool was) && was == on) return;
            lensOn[m] = on;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", on ? emission : Color.black);
            if (litBase.TryGetValue(m, out var lit)) m.SetColor("_BaseColor", on ? lit : offBase[m]);
        }

        static Transform Find(Transform root, string prefix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith(prefix)) return t;
            return null;
        }

        // ------------------------------------------------------------------ bell

        void AddBell(Transform at)
        {
            if (bells.Count >= 2) return;   // one per approach is enough
            var src = at.gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.minDistance = 6f; src.maxDistance = 120f; src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.playOnAwake = false;
            bells.Add(src);
        }

        /// <summary>Procedural bell strike: a few inharmonic partials with a fast decay.</summary>
        static AudioClip BellClip()
        {
            if (bellClip != null) return bellClip;
            const int rate = 44100; const float len = 0.38f;
            int n = (int)(rate * len);
            var data = new float[n];
            float[] freq = { 1180f, 2360f, 3310f, 4770f };
            float[] amp = { 0.55f, 0.3f, 0.18f, 0.08f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, s = 0f;
                for (int k = 0; k < freq.Length; k++) s += amp[k] * Mathf.Sin(2f * Mathf.PI * freq[k] * t) * Mathf.Exp(-t * (7f + 5f * k));
                data[i] = s * Mathf.Clamp01(t * 800f);
            }
            bellClip = AudioClip.Create("CrossingBell", n, 1, rate, false);
            bellClip.SetData(data, 0);
            return bellClip;
        }
    }
}
