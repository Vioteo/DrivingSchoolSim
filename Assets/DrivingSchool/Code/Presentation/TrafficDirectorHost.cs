using System.Collections.Generic;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Simulation.Traffic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// The only MonoBehaviour that runs traffic (ADR-014): ticks <see cref="TrafficDirector"/> in FixedUpdate, feeds it
    /// the player's car and applies the snapshot to vehicle views and signal heads. It makes no traffic decisions.
    /// Positions are used as float directly: fine for a district, the floating origin (T13) is applied later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrafficDirectorHost : MonoBehaviour
    {
        [Tooltip("Compiled district graph (WorldDocumentV2 JSON written by the district builder).")]
        [SerializeField] TextAsset worldJson;
        [SerializeField] Transform player;
        [SerializeField] float playerLengthM = 4.4f, playerWidthM = 1.8f;
        [SerializeField] GameObject[] vehiclePrefabs = new GameObject[0];
        [SerializeField] int seed = 1;
        [SerializeField] int maxVehicles = 8;
        [Tooltip("Pedestrians on the sidewalks (T51); their prefabs are picked by id.")]
        [SerializeField] GameObject[] pedestrianPrefabs = new GameObject[0];
        [SerializeField] int maxPedestrians = 0;
        [Tooltip("A car that left the district disappears after this many seconds even in sight of the player.")]
        [SerializeField] float finishedLingerSeconds = 2f;
        [SerializeField] LayerMask groundMask = 1 << 9;
        [Tooltip("Level crossing in the district (T56): while it is not open its signal groups are red for bots and pedestrians.")]
        [SerializeField] RailwayCrossingView railway;
        [SerializeField] string[] railwayGroups = new string[0];
        readonly HashSet<string> pedestrianGroups = new HashSet<string>();

        TrafficDirector director;
        readonly Dictionary<string, TrafficVehicleView> views = new Dictionary<string, TrafficVehicleView>();
        readonly Dictionary<string, List<TrafficSignalView>> heads = new Dictionary<string, List<TrafficSignalView>>();
        readonly Stack<TrafficVehicleView> pool = new Stack<TrafficVehicleView>();
        readonly Dictionary<string, PedestrianView> walkers = new Dictionary<string, PedestrianView>();
        VehiclePhysicsAdapter playerCar;
        long tick;
        double simSeconds;
        Vector3 lastPlayerPosition;
        bool playerLeft, playerRight, playerHazard;

        public TrafficDirector Director => director;
        public TrafficSnapshot Snapshot => director?.Snapshot;

        /// <summary>The player's car reports its lamps here (the solver owns them, not this host).</summary>
        public void SetPlayerSignals(bool left, bool right, bool hazard) { playerLeft = left; playerRight = right; playerHazard = hazard; }

        /// <summary>The car the director treats as the player (PlayerVehicleSelector calls it when the car changes).</summary>
        public void SetPlayer(Transform car)
        {
            player = car;
            playerCar = car != null ? car.GetComponent<VehiclePhysicsAdapter>() : null;
            if (car != null) lastPlayerPosition = car.position;
        }

        public int PedestrianViewCount => walkers.Count;
        public int VehicleViewCount => views.Count;

        void Awake()
        {
            if (worldJson == null) { Debug.LogWarning("TrafficDirectorHost: no world graph assigned"); enabled = false; return; }
            var world = JsonUtility.FromJson<WorldDocumentV2>(worldJson.text);
            director = new TrafficDirector(world, new TrafficProfile { MaxVehicles = maxVehicles, MaxPedestrians = maxPedestrians, FinishedLingerSeconds = finishedLingerSeconds }, seed);
            foreach (var g in world.signalGroups) if (g.kind == SignalGroupKind.Pedestrian) pedestrianGroups.Add(g.id);
            // Signal heads are named by their attachment id in the generated scene (T30).
            // Repeaters of one attachment (overhead, far side; T54) are named "<id>#<suffix>" and show the same group.
            var byName = new Dictionary<string, List<TrafficSignalView>>();
            foreach (var view in FindObjectsByType<TrafficSignalView>(FindObjectsSortMode.None))
            {
                var key = view.gameObject.name.Split('#')[0];
                if (!byName.TryGetValue(key, out var same)) byName[key] = same = new List<TrafficSignalView>();
                same.Add(view);
            }
            foreach (var head in world.signals)
            {
                if (!byName.TryGetValue(head.id, out var views)) continue;
                if (!heads.TryGetValue(head.signalGroupId, out var list)) heads[head.signalGroupId] = list = new List<TrafficSignalView>();
                list.AddRange(views);
            }
            SetPlayer(player);
        }

        void FixedUpdate()
        {
            if (director == null) return;
            // Indicators come from the car's own lamps (the solver owns them), unless someone set them explicitly.
            if (playerCar != null) { var st = playerCar.CurrentState; playerLeft = st.leftIndicator; playerRight = st.rightIndicator; playerHazard = st.leftIndicator && st.rightIndicator; }
            director.SetPlayer(SamplePlayer());
            if (railway != null)
            {
                // Red from the first flash until the booms are up again (ПДД РФ 15.3); open: no signal for cars, walk for people.
                bool closed = railway.IsClosedForTraffic;
                foreach (var g in railwayGroups)
                    director.SetExternalAspect(g, closed ? SignalAspect.Red : pedestrianGroups.Contains(g) ? SignalAspect.Green : SignalAspect.Off);
            }
            director.Tick(tick++, simSeconds);
            simSeconds += Time.fixedDeltaTime;
            Apply(director.Snapshot);
        }

        PlayerSample SamplePlayer()
        {
            if (player == null) return default;
            var p = player.position;
            float speed = (p - lastPlayerPosition).magnitude / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            lastPlayerPosition = p;
            var forward = player.forward;
            return new PlayerSample
            {
                Present = true, X = p.x, Y = p.y, Z = p.z, HeadingRad = Mathf.Atan2(forward.x, forward.z), SpeedMps = speed,
                LeftIndicator = playerLeft, RightIndicator = playerRight, Hazard = playerHazard, LengthM = playerLengthM, WidthM = playerWidthM,
            };
        }

        void Apply(TrafficSnapshot snapshot)
        {
            var alive = new HashSet<string>();
            foreach (var p in snapshot.Participants)
            {
                if (p.Kind == ParticipantKind.Pedestrian) { alive.Add(p.Id); ApplyPedestrian(p); continue; }
                if (p.Kind != ParticipantKind.Vehicle) continue;
                alive.Add(p.Id);
                if (!views.TryGetValue(p.Id, out var view))
                {
                    view = Take(p.Id);
                    if (view == null) continue;
                    views[p.Id] = view;
                }
                view.Apply(p);
            }
            var gone = new List<string>();
            foreach (var kv in views) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { Return(views[id]); views.Remove(id); }
            gone.Clear();
            foreach (var kv in walkers) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { if (walkers[id] != null) Destroy(walkers[id].gameObject); walkers.Remove(id); }
            foreach (var s in snapshot.Signals)
                if (heads.TryGetValue(s.GroupId, out var list))
                    foreach (var head in list) head.SetAspect(s.Aspect);
        }

        void ApplyPedestrian(ParticipantState p)
        {
            if (!walkers.TryGetValue(p.Id, out var view))
            {
                if (pedestrianPrefabs.Length == 0) return;
                var prefab = pedestrianPrefabs[Mathf.Abs(StableHash(p.Id)) % pedestrianPrefabs.Length];
                if (prefab == null) return;
                var go = Instantiate(prefab, transform);
                go.name = p.Id + " / " + prefab.name;
                view = go.GetComponent<PedestrianView>();
                if (view == null) view = go.AddComponent<PedestrianView>();
                view.groundMask = groundMask;
                view.Bind(this, p.Id);
                walkers[p.Id] = view;
            }
            view.Apply(p);
        }

        static int StableHash(string s) { unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return h; } }

        TrafficVehicleView Take(string id)
        {
            TrafficVehicleView view;
            if (pool.Count > 0) view = pool.Pop();
            else
            {
                if (vehiclePrefabs.Length == 0) return null;
                var prefab = vehiclePrefabs[Mathf.Abs(id.GetHashCode()) % vehiclePrefabs.Length];
                var go = Instantiate(prefab, transform);
                view = go.GetComponent<TrafficVehicleView>();
                if (view == null) view = go.AddComponent<TrafficVehicleView>(); // no ?? with UnityEngine.Object
            }
            view.Bind(this, id);
            view.gameObject.SetActive(true);
            // The simulation keeps gaps and stops at lines by the car's length: give it the real size of the model shown.
            var car = director.Vehicle(id);
            car.Profile = car.Profile.WithSize(view.LengthM, view.WidthM, view.WheelbaseM);
            return view;
        }

        void Return(TrafficVehicleView view)
        {
            view.gameObject.SetActive(false);
            pool.Push(view);
        }

        /// <summary>Knocked-down pedestrians (T53), latest last: the drive session can turn them into a violation.</summary>
        public readonly List<string> HitPedestrians = new List<string>();
        public event System.Action<string, float> PedestrianHit;

        internal void ReportPedestrianHit(string pedestrianId, float speedMps, bool byPlayer)
        {
            director?.KnockPedestrian(pedestrianId, byPlayer ? TrafficDirector.PlayerId : "object");
            HitPedestrians.Add(pedestrianId);
            Debug.Log($"[Traffic] pedestrian {pedestrianId} hit at {speedMps * 3.6f:F0} km/h" + (byPlayer ? " by the player" : ""));
            PedestrianHit?.Invoke(pedestrianId, speedMps);
        }

        internal void ReportContact(string agentId, bool withPlayer) => director?.ReportContact(agentId, withPlayer ? TrafficDirector.PlayerId : "object");

        internal bool IsPlayer(Transform t) => player != null && (t == player || t.IsChildOf(player));
    }
}
