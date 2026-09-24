using DrivingSchool.Simulation.Traffic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>Kinematic body of an AI car: placed from the snapshot, reports contacts. No decisions here.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TrafficVehicleView : MonoBehaviour
    {
        [SerializeField] Transform[] frontWheels = new Transform[0];
        [SerializeField] Renderer[] brakeLamps = new Renderer[0], leftLamps = new Renderer[0], rightLamps = new Renderer[0];
        TrafficDirectorHost host;
        Rigidbody body;
        string id;

        public string AgentId => id;

        internal void Bind(TrafficDirectorHost owner, string agentId)
        {
            host = owner; id = agentId;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        internal void Apply(ParticipantState p)
        {
            var position = new Vector3((float)p.Position.x, (float)p.Position.y, (float)p.Position.z);
            var rotation = Quaternion.Euler(0, (float)(p.HeadingRad * Mathf.Rad2Deg), 0);
            body.MovePosition(position);
            body.MoveRotation(rotation);
            foreach (var w in frontWheels) if (w != null) w.localRotation = Quaternion.Euler(0, p.SteeringRad * Mathf.Rad2Deg, 0);
            bool blink = Mathf.Repeat(Time.time, 0.8f) < 0.4f;
            Set(brakeLamps, p.BrakeLight);
            Set(leftLamps, p.LeftIndicator && blink);
            Set(rightLamps, p.RightIndicator && blink);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (host != null) host.ReportContact(id, host.IsPlayer(collision.transform));
        }

        static void Set(Renderer[] lamps, bool on)
        {
            foreach (var r in lamps) if (r != null) r.enabled = on;
        }
    }
}
