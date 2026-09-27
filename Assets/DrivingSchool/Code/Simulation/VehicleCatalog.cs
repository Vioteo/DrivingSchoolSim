using System;
using System.Collections.Generic;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Vehicle roster (T48): one entry per car model. The same assembler turns any entry into a player car
    /// (full stack: physics, visuals, lights, dashboard, mirrors, rain) or a traffic car, so everything built for
    /// the first sedan applies to every model that meets the contract in docs/vehicle-models.md.
    /// Data: Assets/DrivingSchool/Data/Vehicles/vehicles.json (read with JsonUtility on the Unity side).
    /// </summary>
    [Serializable] public sealed class VehicleCatalog
    {
        public int schemaVersion = 1;
        public VehicleEntry[] vehicles = Array.Empty<VehicleEntry>();

        public VehicleEntry Find(string id)
        {
            foreach (var v in vehicles) if (v != null && v.id == id) return v;
            return null;
        }

        public IEnumerable<VehicleEntry> Players() { foreach (var v in vehicles) if (v.player) yield return v; }
        public IEnumerable<VehicleEntry> Traffic() { foreach (var v in vehicles) if (v.traffic) yield return v; }

        /// <summary>Throws with the first problem found: ids, paths, roles and every spec.</summary>
        public void Validate()
        {
            if (schemaVersion != 1) throw new ArgumentException("Unsupported vehicle catalog version: " + schemaVersion);
            if (vehicles == null || vehicles.Length == 0) throw new ArgumentException("Vehicle catalog is empty.");
            var ids = new HashSet<string>();
            bool anyPlayer = false;
            foreach (var v in vehicles)
            {
                if (v == null) throw new ArgumentException("Null vehicle entry.");
                v.Validate();
                if (!ids.Add(v.id)) throw new ArgumentException("Duplicate vehicle id: " + v.id);
                anyPlayer |= v.player;
            }
            if (!anyPlayer) throw new ArgumentException("Vehicle catalog has no player car.");
        }
    }

    [Serializable] public sealed class VehicleEntry
    {
        public string id, title;
        /// <summary>Full model (player car): project path of the FBX or prefab.</summary>
        public string model;
        /// <summary>Simplified model for traffic (optional; the full model is used when empty).</summary>
        public string trafficModel;
        public bool player, traffic;
        /// <summary>Rotation of the model under the car root, degrees. Legacy Blender export (axis_forward='Y', axis_up='Z') needs (-90, 180, 0).</summary>
        public float[] modelEulerDeg = { -90f, 180f, 0f };
        /// <summary>Bottom of the body collider above the ground: wheels are raycasts, the hull must clear speed bumps.</summary>
        public float hullClearanceM = 0.28f;
        /// <summary>Interior style tag for the UI and docs ("classic", "modern", "simple").</summary>
        public string interior = "";
        public VehicleSpec spec = new VehicleSpec();

        public string TrafficModelOrFull => string.IsNullOrEmpty(trafficModel) ? model : trafficModel;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Vehicle without id.");
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException(id + ": no model path.");
            if (!player && !traffic) throw new ArgumentException(id + ": neither player nor traffic.");
            if (modelEulerDeg == null || modelEulerDeg.Length != 3) throw new ArgumentException(id + ": modelEulerDeg needs 3 values.");
            if (hullClearanceM < 0.05f || hullClearanceM > 0.6f) throw new ArgumentException(id + ": hullClearanceM out of range.");
            if (spec == null) throw new ArgumentException(id + ": no spec.");
            try { spec.Validate(); }
            catch (ArgumentException e) { throw new ArgumentException(id + ": " + e.Message, e); }
        }
    }
}
