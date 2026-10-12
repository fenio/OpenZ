using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// travelled / radius (tanks per side, like driveDistance(x) = travel + yawTravel * x), front wheels steer with the turn,
    /// the suspension node bounces (sin 7t x .075 m) and rolls (sin 5.4t x .05 rad) while moving, the whole vehicle wobbles
    /// (pitch sin 5.7t x .055, roll sin 4.1t x .045 rad), plus a little lean into turns and a squat on braking / accelerating
    /// from the measured speed change. Track meshes scroll their texture. Models without rig nodes (imported medium tank, APC,
    /// crane) get the body motion on the whole model. Driven by UnitView each frame; visual only.
    /// </summary>
    public class VehicleMotion : MonoBehaviour
    {
        struct Wheel { public Transform t; public Quaternion rest; public float radius, side; }
        struct Steer { public Transform t; public Quaternion rest; }
        readonly List<Wheel> wheels = new List<Wheel>();
        readonly List<Steer> steers = new List<Steer>();
        readonly List<(Renderer r, float side)> tracks = new List<(Renderer, float)>();
        Transform body; Vector3 bodyPos; Quaternion bodyRot;
        float travel, yawTravel, motionAge, speedSmooth, accel, prevSpeed, lean;
        bool wasMoving;
        MaterialPropertyBlock mpb;
        static readonly int StId = Shader.PropertyToID("_BaseMap_ST");

        public bool HasBody => body != null;

        void Awake() => Rescan();

        /// <summary>vehicles_nb_v1 NB23: finds the wheel / steer / track / suspension nodes (again, after WheelSplit cut the APC's and the
        /// crane's wheels out of their single imported mesh).</summary>
        public void Rescan()
        {
            wheels.Clear(); steers.Clear(); tracks.Clear();
            var root = transform;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.StartsWith("WheelSpin_") || n.StartsWith("Wheel_") || System.Text.RegularExpressions.Regex.IsMatch(n, "^TankWheel_\\d+$"))
                {
                    var mf = t.GetComponentInChildren<MeshFilter>();
                    float r = mf && mf.sharedMesh ? Mathf.Max(mf.sharedMesh.bounds.size.y, mf.sharedMesh.bounds.size.z) * 0.5f * t.lossyScale.y : 0.65f;
                    wheels.Add(new Wheel { t = t, rest = t.localRotation, radius = Mathf.Max(0.2f, r), side = Mathf.Sign(root.InverseTransformPoint(t.position).x) });
                }
                else if (n.StartsWith("WheelSteer_F") || n.StartsWith("Steer_F")) steers.Add(new Steer { t = t, rest = t.localRotation });
                else if (!body && (n == "Suspension" || n == "TankSuspension" || n == "MLRSSuspension" || n == "Chassis" || n == "BuggyChassis")) { body = t; bodyPos = t.localPosition; bodyRot = t.localRotation; }
                var rr = t.GetComponent<Renderer>();
                if (rr && (n.Contains("track") || n.Contains("Track") || n.Contains("Tread") || n.Contains("tread")))
                    tracks.Add((rr, Mathf.Sign(root.InverseTransformPoint(rr.bounds.center).x + 1e-4f)));
            }
        }

        /// <summary>speed in m/s along the path, yawRate in deg/s. Returns the extra rotation for the whole model.</summary>
        /// <summary></summary>
        public void Kick(float rollDeg, float pitchDeg) { kRollV += rollDeg * KickFreq; kPitchV += pitchDeg * KickFreq; }
        const float KickFreq = 8f, KickDamp = 4.5f;
        float kRoll, kRollV, kPitch, kPitchV;

        public Quaternion Step(float speed, float yawRate, float dt)
        {
            kRollV += (-KickFreq * KickFreq * kRoll - 2f * KickDamp * kRollV) * dt; kRoll += kRollV * dt;
            kPitchV += (-KickFreq * KickFreq * kPitch - 2f * KickDamp * kPitchV) * dt; kPitch += kPitchV * dt;
            var kickRot = Quaternion.Euler(Mathf.Clamp(kPitch, -6f, 6f), 0f, Mathf.Clamp(-kRoll, -6f, 6f));
            bool moving = Mathf.Abs(speed) > 0.05f;                                                  // motion_v1: speed < 0 = backing up (UnitView / HullSteer)
            if (moving && !wasMoving) motionAge = 0; wasMoving = moving; motionAge += dt;
            travel += speed * dt; yawTravel += yawRate * Mathf.Deg2Rad * dt;
            speedSmooth = Mathf.Lerp(speedSmooth, speed, 1f - Mathf.Exp(-10f * dt));
            float a = dt > 0 ? (speedSmooth - prevSpeed) / dt : 0; prevSpeed = speedSmooth;
            accel = Mathf.Lerp(accel, a, 1f - Mathf.Exp(-6f * dt));
            float wobble = Mathf.Clamp01(Mathf.Abs(speed) / 0.8f), bounce = Mathf.Clamp01(Mathf.Abs(speed));

            foreach (var w in wheels)
            {
                float dist = travel + yawTravel * w.side * 1.3f;                                      // tracks: outer side runs farther
                w.t.localRotation = w.rest * Quaternion.AngleAxis(-dist / w.radius * Mathf.Rad2Deg, Vector3.right);   // front = -z
            }
            float steerDeg = Mathf.Clamp(yawRate * 0.22f, -28f, 28f);
            foreach (var s in steers) s.t.localRotation = Quaternion.Slerp(s.t.localRotation, s.rest * Quaternion.AngleAxis(steerDeg, Vector3.up), 1f - Mathf.Exp(-14f * dt));
            if (tracks.Count > 0)
            {
                mpb ??= new MaterialPropertyBlock();
                foreach (var (r, side) in tracks)
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetVector(StId, new Vector4(1, 1, 0, (travel + yawTravel * side * 1.3f) * 0.25f));
                    r.SetPropertyBlock(mpb);
                }
            }

            // squat: nose dips when braking, rises when accelerating (front is -z: +pitch lifts it); lean into turns
            float squat = Mathf.Clamp(accel * 1.6f, -4.5f, 4.5f);
            lean = Mathf.Lerp(lean, Mathf.Clamp(yawRate * 0.03f, -5f, 5f) * Mathf.Clamp01(Mathf.Abs(speed) / 2f), 1f - Mathf.Exp(-6f * dt));
            var wob = moving ? Quaternion.Euler(Mathf.Sin(motionAge * 5.7f) * 0.055f * wobble * Mathf.Rad2Deg, 0, Mathf.Sin(motionAge * 4.1f) * 0.045f * wobble * Mathf.Rad2Deg) : Quaternion.identity;
            if (body)
            {
                var pos = bodyPos; var rot = bodyRot * Quaternion.Euler(squat, 0, lean) * kickRot;
                if (moving) { pos.y += Mathf.Sin(motionAge * 7f) * 0.075f * bounce; rot = rot * Quaternion.Euler(0, 0, Mathf.Sin(motionAge * 5.4f) * 0.05f * bounce * Mathf.Rad2Deg); }
                body.localPosition = pos; body.localRotation = rot;
                return wob;
            }
            // no suspension node: the whole model carries the body motion
            return wob * kickRot * Quaternion.Euler(squat, 0, lean) * (moving ? Quaternion.Euler(0, 0, Mathf.Sin(motionAge * 5.4f) * 0.05f * bounce * Mathf.Rad2Deg) : Quaternion.identity);
        }

        public float Bounce => wasMoving && !body ? Mathf.Sin(motionAge * 7f) * 0.06f : 0f;
    }
}
