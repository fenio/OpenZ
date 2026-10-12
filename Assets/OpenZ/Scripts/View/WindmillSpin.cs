using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// details_v2 (L02 D03 wind-pump): the SPIN_ wheel turns about its AXIS_ with the wind (10-15 rpm, small eased gusts), the
    /// HEAD_ yaws a few degrees with the gusts, a BOB_ pump rod strokes once per wheel turn. Sounds: the creak loop (volume
    /// follows the wheel speed) and a metal creak every few seconds. Broken: the wheel flies off spinning (thrown with the
    /// DetailProp chunks, its own spin kept), the tower goes to chunks and the footings (RUIN_) stay.
    /// </summary>
    [RequireComponent(typeof(DetailProp))]
    public class WindmillSpin : MonoBehaviour
    {
        public float minRps = 0.6f, maxRps = 2.2f, headYaw = 8f, gustPeriod = 9f;
        public float strokeTurns = 3f, strokeLen = 0.35f;
        // as a blur / a still wheel (the blades strobing at 60 fps); a real wind pump turns slowly: 10-15 rpm with small gusts,
        // the pump rod strokes once per turn, the tail vane swings with the wind. New fields, so the serialized prefab values
        // (0.6 / 2.2 rev/s, 3 turns per stroke) do not win: Start overrides the old ones with these.
        public float rpmMin = 10f, rpmMax = 15f, pumpStrokeTurns = 1f, vaneYaw = 12f, gustEase = 0.05f;

        DetailProp prop; Transform wheel, head, bob; Vector3 axisLocal = Vector3.forward, bobRest, headUp = Vector3.up, bobUp = Vector3.up; Quaternion wheelRest, headRest;
        float angle, rps, seed; bool thrown;

        void Start()
        {
            prop = GetComponent<DetailProp>();
            minRps = rpmMin / 60f; maxRps = Mathf.Max(minRps, rpmMax / 60f); strokeTurns = Mathf.Max(0.5f, pumpStrokeTurns); headYaw = vaneYaw;
            wheel = prop.FindPrefix("SPIN_"); head = prop.FindPrefix("HEAD_"); bob = prop.FindPrefix("BOB_");
            if (wheel)
            {
                wheelRest = wheel.localRotation;
                Transform ax = null;
                foreach (Transform c in wheel.GetComponentsInChildren<Transform>(true)) if (DetailProp.Base(c.name).StartsWith("AXIS_")) { ax = c; break; }
                if (ax && wheel.parent)
                {   // the axis in the wheel's parent space: robust against any FBX axis conversion
                    var d = wheel.parent.InverseTransformDirection(ax.position - wheel.position);
                    if (d.sqrMagnitude > 1e-6f) axisLocal = d.normalized;
                }
            }
            // review fix (round 2): HEAD_ / BOB_ sit under the FBX root (-90 deg X, Blender Z-up parent space), so the world up
            // is converted into each parent's space, as BaseProp does; parent-space Vector3.up would be a horizontal axis there
            if (head) { headRest = head.localRotation; if (head.parent) { var u = head.parent.InverseTransformDirection(Vector3.up); if (u.sqrMagnitude > 1e-6f) headUp = u.normalized; } }
            if (bob) { bobRest = bob.localPosition; if (bob.parent) { var u = bob.parent.InverseTransformDirection(Vector3.up); if (u.sqrMagnitude > 1e-6f) bobUp = u.normalized; } }
            seed = Random.Range(0f, 100f);
            rps = minRps;
            prop.BrokenBy += OnBreak;
        }

        void OnBreak(DetailProp p, Vector3 from)
        {
            if (!wheel || thrown || !DetailPropHub.I) return;
            thrown = true;
            foreach (var r in wheel.GetComponentsInChildren<Renderer>(true)) r.enabled = true;       // DetailProp hid the body: the wheel flies
            var away = transform.position - from; away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
            var worldAxis = wheel.parent ? wheel.parent.TransformDirection(axisLocal) : axisLocal;
            // the wheel keeps its spin (deg/s about its axis) and flies off the head, down the rim
            DetailPropHub.I.Chunks.Throw(wheel, away * Random.Range(4f, 6.5f) + Vector3.up * Random.Range(4f, 6f), worldAxis * (rps * 360f + 240f), 12f);
            DetailProp.Play("", new[] { "sd_metal_creak_a", "sd_metal_creak_b" }, wheel.position, 0.7f, 0.8f);
        }

        void Update()
        {
            if (!prop || prop.Broken) return;
            var g = ZGame.I;
            float dt = g && g.paused ? 0f : Time.deltaTime;
            float t = Time.time + seed;
            float gust = Mathf.Clamp01(0.45f + 0.35f * Mathf.Sin(t * 2f * Mathf.PI / gustPeriod) + 0.35f * (Mathf.PerlinNoise(t * 0.23f, seed) - 0.5f) * 2f);
            float target = Mathf.Lerp(minRps, maxRps, gust);
            rps = Mathf.MoveTowards(rps, target, dt * gustEase);                               // a heavy wheel: eased
            angle = Mathf.Repeat(angle + rps * 360f * dt, 360f * strokeTurns);
            if (wheel) wheel.localRotation = Quaternion.AngleAxis(angle, axisLocal) * wheelRest;
            if (head) head.localRotation = Quaternion.AngleAxis(headYaw * (Mathf.PerlinNoise(t * 0.11f, seed + 3f) - 0.5f) * 2f, headUp) * headRest;
            if (bob) bob.localPosition = bobRest + bobUp * (strokeLen * 0.5f * Mathf.Sin(angle / strokeTurns * Mathf.Deg2Rad));
            prop.SetLoopLevel(0.35f + 0.65f * Mathf.InverseLerp(minRps, maxRps, rps));
        }
    }
}
