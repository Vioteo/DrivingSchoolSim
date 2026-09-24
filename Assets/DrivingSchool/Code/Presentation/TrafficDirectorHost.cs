using System.Collections.Generic;
using DrivingSchool.Contracts;
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

        TrafficDirector director;
        readonly Dictionary<string, TrafficVehicleView> views = new Dictionary<string, TrafficVehicleView>();
        readonly Dictionary<string, List<TrafficSignalView>> heads = new Dictionary<string, List<TrafficSignalView>>();
        readonly Stack<TrafficVehicleView> pool = new Stack<TrafficVehicleView>();
        long tick;
        double simSeconds;
        Vector3 lastPlayerPosition;
        bool playerLeft, playerRight, playerHazard;

        public TrafficDirector Director => director;
        public TrafficSnapshot Snapshot => director?.Snapshot;

        /// <summary>The player's car reports its lamps here (the solver owns them, not this host).</summary>
        public void SetPlayerSignals(bool left, bool right, bool hazard) { playerLeft = left; playerRight = right; playerHazard = hazard; }

        void Awake()
        {
            if (worldJson == null) { Debug.LogWarning("TrafficDirectorHost: no world graph assigned"); enabled = false; return; }
            var world = JsonUtility.FromJson<WorldDocumentV2>(worldJson.text);
            director = new TrafficDirector(world, new TrafficProfile { MaxVehicles = maxVehicles }, seed);
            // Signal heads are named by their attachment id in the generated scene (T30).
            var byName = new Dictionary<string, TrafficSignalView>();
            foreach (var view in FindObjectsByType<TrafficSignalView>(FindObjectsSortMode.None)) byName[view.gameObject.name] = view;
            foreach (var head in world.signals)
            {
                if (!byName.TryGetValue(head.id, out var view)) continue;
                if (!heads.TryGetValue(head.signalGroupId, out var list)) heads[head.signalGroupId] = list = new List<TrafficSignalView>();
                list.Add(view);
            }
            if (player != null) lastPlayerPosition = player.position;
        }

        void FixedUpdate()
        {
            if (director == null) return;
            director.SetPlayer(SamplePlayer());
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
            foreach (var s in snapshot.Signals)
                if (heads.TryGetValue(s.GroupId, out var list))
                    foreach (var head in list) head.SetAspect(s.Aspect);
        }

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
            return view;
        }

        void Return(TrafficVehicleView view)
        {
            view.gameObject.SetActive(false);
            pool.Push(view);
        }

        internal void ReportContact(string agentId, bool withPlayer) => director?.ReportContact(agentId, withPlayer ? TrafficDirector.PlayerId : "object");

        internal bool IsPlayer(Transform t) => player != null && (t == player || t.IsChildOf(player));
    }
}
