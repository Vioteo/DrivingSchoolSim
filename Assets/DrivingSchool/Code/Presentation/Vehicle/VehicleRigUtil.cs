using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Helpers for animating named parts of an imported car model without trusting FBX axis conventions:
    /// rotation axes are chosen in car space from the part's own local axes.
    /// </summary>
    public static class VehicleRigUtil
    {
        public static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public static List<Transform> FindPrefix(Transform root, string prefix)
        {
            var list = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith(prefix, StringComparison.Ordinal)) list.Add(t);
            return list;
        }

        /// <summary>The part's local axis (±X/±Y/±Z) most parallel to carDirection, expressed in car space and pointing along it.</summary>
        public static Vector3 AlignedAxisInCar(Transform car, Transform part, Vector3 carDirection)
        {
            Vector3 best = carDirection; float bestDot = -1f;
            foreach (var a in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                Vector3 inCar = car.InverseTransformDirection(part.TransformDirection(a)).normalized;
                float d = Vector3.Dot(inCar, carDirection);
                if (Mathf.Abs(d) > bestDot) { bestDot = Mathf.Abs(d); best = d >= 0 ? inCar : -inCar; }
            }
            return best;
        }

        public static Bounds WorldBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// <summary>Car-space bounds of renderer vertices (accurate for rotated meshes).</summary>
        public static Bounds CarSpaceBounds(Transform car, Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
            {
                var wb = r.bounds; var b0 = new Bounds(car.InverseTransformPoint(wb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    b0.Encapsulate(car.InverseTransformPoint(wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                return b0;
            }
            var v = mf.sharedMesh.vertices;
            var b = new Bounds(car.InverseTransformPoint(r.transform.TransformPoint(v[0])), Vector3.zero);
            foreach (var p in v) b.Encapsulate(car.InverseTransformPoint(r.transform.TransformPoint(p)));
            return b;
        }

        /// <summary>Stores a part's rest rotation in car space and re-applies it with an extra car-space rotation.</summary>
        public sealed class Pose
        {
            public readonly Transform part; readonly Transform car; readonly Quaternion restInCar; readonly Vector3 restPosInCar;
            public Pose(Transform car, Transform part)
            {
                this.car = car; this.part = part;
                restInCar = Quaternion.Inverse(car.rotation) * part.rotation;
                restPosInCar = car.InverseTransformPoint(part.position);
            }
            public Vector3 RestPositionInCar => restPosInCar;
            public void Apply(Quaternion carSpaceRotation) { part.rotation = car.rotation * carSpaceRotation * restInCar; }
            public void Apply(Quaternion carSpaceRotation, Vector3 carSpacePosition)
            {
                part.SetPositionAndRotation(car.TransformPoint(carSpacePosition), car.rotation * carSpaceRotation * restInCar);
            }
            /// <summary>Rotates about a car-space pivot point (for meshes whose origin is not the hinge).</summary>
            public void ApplyAbout(Vector3 carPivot, Quaternion carSpaceRotation)
            {
                Vector3 p = carPivot + carSpaceRotation * (restPosInCar - carPivot);
                part.SetPositionAndRotation(car.TransformPoint(p), car.rotation * carSpaceRotation * restInCar);
            }
        }
    }
}
