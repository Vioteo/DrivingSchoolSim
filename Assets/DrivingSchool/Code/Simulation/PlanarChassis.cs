using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Headless 3-DOF chassis (x, z, yaw) on a flat or uniformly graded road with static + longitudinal/lateral
    /// load transfer. Used by EditMode tests and tools; the game uses the Unity Rigidbody adapter instead.
    /// Yaw is Unity-style: 0 = +Z, positive = clockwise seen from above (turning right).
    /// </summary>
    public sealed class PlanarChassis
    {
        public double X, Z;
        public float Yaw, VelocityRight, VelocityForward, YawRate;
        public float GradeRad;              // positive = uphill along the car heading
        public SurfaceType Surface = SurfaceType.DryAsphalt;
        public readonly WheelContact[] Contacts = new WheelContact[4];
        float accelForward, accelRight;

        public void Step(VehicleSolver solver, DriverCommand cmd, float dt)
        {
            var s = solver.Spec; float m = s.massKg, g = SurfaceFrictionModel.Gravity;
            float a = s.wheelbaseM * 0.5f - s.CentreOfMassForwardM;   // CoM → front axle
            float b = s.wheelbaseM - a;                                 // CoM → rear axle
            float halfT = s.trackM * 0.5f, hcg = s.CentreOfMassHeightM;
            float weight = m * g * (float)Math.Cos(GradeRad);
            float mu = SurfaceFrictionModel.GetFrictionCoefficient(Surface);
            float dLong = m * accelForward * hcg / s.wheelbaseM;
            float dLat = m * accelRight * hcg / s.trackM;
            for (int k = 0; k < 4; k++)
            {
                bool front = k < 2; float x = (k % 2 == 0) ? -halfT : halfT; float z = front ? a : -b;
                float axle = weight * (front ? b : a) / s.wheelbaseM + (front ? -dLong : dLong);
                float fz = 0.5f * axle - Math.Sign(x) * dLat * (front ? b : a) / s.wheelbaseM; // accel to the right loads the left wheels
                Contacts[k] = new WheelContact
                {
                    grounded = true, normalForceN = Math.Max(0f, fz), frictionCoefficient = mu,
                    velocityRightMps = VelocityRight + YawRate * z,
                    velocityForwardMps = VelocityForward - YawRate * x
                };
            }
            solver.Step(cmd, Contacts, dt);

            float fr = 0f, ff = solver.DragForceForwardN - m * g * (float)Math.Sin(GradeRad), mz = 0f;
            for (int k = 0; k < 4; k++)
            {
                bool front = k < 2; float x = (k % 2 == 0) ? -halfT : halfT; float z = front ? a : -b;
                var w = solver.Wheels[k];
                fr += w.forceRightN; ff += w.forceForwardN;
                mz += z * w.forceRightN - x * w.forceForwardN;
            }
            accelForward = ff / m; accelRight = fr / m;
            VelocityRight += (accelRight - YawRate * VelocityForward) * dt;
            VelocityForward += (accelForward + YawRate * VelocityRight) * dt;
            YawRate += mz / s.yawInertiaKgm2 * dt;
            float sn = (float)Math.Sin(Yaw), cs = (float)Math.Cos(Yaw);
            X += (VelocityRight * cs + VelocityForward * sn) * dt;
            Z += (-VelocityRight * sn + VelocityForward * cs) * dt;
            Yaw += YawRate * dt;
        }
    }
}
