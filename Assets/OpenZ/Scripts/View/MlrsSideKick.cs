using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Each launch adds an underdamped spring impulse: the rack kicks back along its tubes, tips up and rolls toward the firing side;
    /// the body (VehicleMotion) rocks toward that side; a back-blast puff of smoke and dust leaves behind the tube. Applied additively
    /// after UnitAim (which poses the rack), so aiming is untouched. Pure view.
    /// </summary>
    [DefaultExecutionOrder(66)]
    public class MlrsSideKick : MonoBehaviour
    {
        public const float BackM = 0.22f, UpDeg = 5f, RollDeg = 6f, Freq = 9f, Damp = 5.5f;
        Transform rack; Vector3 lastPos; Quaternion lastRot; bool wrote;
        float kx, kv, rx, rv;                                                   // kick (back / up) and roll springs: value, velocity
        VehicleMotion motion;

        public static void Kick(UnitView v, Vector3 from, Vector3 to)
        {
            if (!v) return;
            var k = v.GetComponent<MlrsSideKick>(); if (!k) k = v.gameObject.AddComponent<MlrsSideKick>();
            k.Fire(from, to);
        }

        void Fire(Vector3 from, Vector3 to)
        {
            if (!rack) foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "MLRS_rack") { rack = t; break; }
            if (!motion) motion = GetComponentInChildren<VehicleMotion>();
            float side = 0f;
            if (rack) side = Mathf.Sign(Vector3.Dot(from - rack.position, rack.right)); if (side == 0f) side = 1f;
            kv += 1f * Freq; rv += side * Freq * Random.Range(0.85f, 1.15f);
            if (motion) motion.Kick(side * 1.6f, -1.2f);
            var dir = to - from; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : -transform.forward;
            var fx = FxManager.I;
            if (fx) { fx.Dust(from - dir * 1.4f + Vector3.up * 0.2f, 0.55f); fx.Dust(new Vector3(from.x, 0f, from.z) - dir * 2.2f + Vector3.up * (from.y * 0.2f), 0.4f); }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime; if (dt <= 0f || !rack) return;
            // damped springs toward 0 (sharp jolt, springy settle)
            kv += (-Freq * Freq * kx - 2f * Damp * kv) * dt; kx += kv * dt;
            rv += (-Freq * Freq * rx - 2f * Damp * rv) * dt; rx += rv * dt;
            if (Mathf.Abs(kx) < 1e-4f && Mathf.Abs(kv) < 1e-3f && Mathf.Abs(rx) < 1e-4f && Mathf.Abs(rv) < 1e-3f) { kx = kv = rx = rv = 0f; }
            // additive over whatever UnitAim left on the rack this frame
            Vector3 basePos = rack.localPosition; Quaternion baseRot = rack.localRotation;
            if (wrote && basePos == lastPos && baseRot == lastRot) { basePos = restPos; baseRot = restRot; }
            restPos = basePos; restRot = baseRot;
            float k = Mathf.Clamp(kx, -0.3f, 1.2f), r = Mathf.Clamp(rx, -1.2f, 1.2f);
            rack.localPosition = basePos + baseRot * (Vector3.forward * BackM * k);
            rack.localRotation = baseRot * Quaternion.Euler(UpDeg * k, 0f, -RollDeg * r);
            lastPos = rack.localPosition; lastRot = rack.localRotation; wrote = true;
        }
        Vector3 restPos; Quaternion restRot;
    }
}
