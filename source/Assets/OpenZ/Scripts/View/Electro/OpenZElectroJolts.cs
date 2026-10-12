using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// electrobot_v1: the smooth body motions of the tesla fight, applied on top of UnitView's pose (the OpenZFortShockwave rule: added
    /// after the unit was placed, undone if UnitView did not re-pose it this frame, so nothing ever accumulates):
    /// <list type="bullet">
    /// <item><b></b>
    /// per-frame random offset: A sin(2 pi 6 t) e^(-t/0.17) along the bolt + 0.5 A sin(2 pi 5 t) e^(-t/0.17) across it and a smooth
    /// (1 - cos) bounce, faded to exactly 0 over the last 30 % of its 0.55 s (no end pop), 0 at t = 0 (no start pop); A = 9 cm for
    /// swung at 14 / 11 Hz = 4.3 frames a cycle at 60 fps and 2 at 30 fps, frame-alternating motion that reads as jitter; 6 Hz = 10
    /// frames a cycle at 60 fps (5 at 30), 2-3 visible swings, the first the largest (sin starts it). Robots also snap the head and
    /// neck back (a 0.3 s flinch, head / neck bones only, so the hands stay on the gun). The stun's end = a softer shake-off.</item>
    /// <item><b>Recoil</b> (the Electrobot fires): the body rocks back 3.5 deg about its feet and 4 cm along the shot, eased out over
    /// 0.25 s (the gun's own 4 deg climb comes from RobotWeaponPose.OnRound, the root jolt from UnitView.OnFire).</item>
    /// </list>
    /// Order 95: after UnitView (60), RobotLegIK (70), WeaponHold (80), BS01TypeKit (85), before ElectrobotView (96) and OpenZElectroFx
    /// (97) read the sockets, so the arcs sit on the moved body. View only; the sim never sees it.
    /// </summary>
    [DefaultExecutionOrder(95)]
    public sealed class OpenZElectroJolts : MonoBehaviour
    {
        static OpenZElectroJolts I;
        public static int Applied { get; private set; }

        struct Impulse { public int kind; public float start, dur, amp; public Vector3 along, across; public bool vehicle; }
        sealed class Entry
        {
            public UnitView v; public Transform t, head, neck;
            public readonly List<Impulse> list = new List<Impulse>(4);
            public bool has; public Vector3 prePos, appliedPos; public Quaternion preRot, appliedRot;
            public bool headHas; public Quaternion headPre, headApplied, neckPre, neckApplied;
            public Renderer anyRenderer;
        }
        readonly Dictionary<UnitView, Entry> map = new Dictionary<UnitView, Entry>();
        readonly List<UnitView> keys = new List<UnitView>();

        void Awake() { I = this; }
        void OnDestroy() { foreach (var e in map.Values) Undo(e); if (I == this) I = null; }

        static Entry Get(UnitView v)
        {
            if (!I || !v) return null;
            if (!I.map.TryGetValue(v, out var e))
            {
                e = new Entry { v = v, t = v.transform };
                var anim = v.GetComponentInChildren<Animator>();
                if (anim && anim.isHuman && v.Obj != null && v.Obj.Kind == ZKind.Robot) { e.head = anim.GetBoneTransform(HumanBodyBones.Head); e.neck = anim.GetBoneTransform(HumanBodyBones.Neck); }
                e.anyRenderer = v.GetComponentInChildren<SkinnedMeshRenderer>() as Renderer ?? v.GetComponentInChildren<Renderer>();
                I.map[v] = e;
            }
            return e;
        }

        /// <summary>The hit shake. dir = the bolt direction (flattened here).</summary>
        public static void Zap(UnitView v, Vector3 dir, float amp, bool vehicle, float dur = 0.55f)
        {
            var e = Get(v); if (e == null || amp <= 0f) return;
            var along = dir; along.y = 0f; along = along.sqrMagnitude > 1e-6f ? along.normalized : v.transform.forward;
            var across = Vector3.Cross(Vector3.up, along);
            e.list.Add(new Impulse { kind = 0, start = Time.time, dur = dur, amp = amp, along = along, across = across, vehicle = vehicle });
        }

        /// <summary>The Electrobot's recoil (rock back about the feet).</summary>
        public static void Recoil(UnitView v, Vector3 aimDir)
        {
            var e = Get(v); if (e == null) return;
            var along = aimDir; along.y = 0f; along = along.sqrMagnitude > 1e-6f ? along.normalized : -v.transform.forward;
            e.list.Add(new Impulse { kind = 1, start = Time.time, dur = 0.7f, amp = 1f, along = along, across = Vector3.Cross(Vector3.up, along) });
        }

        static void Undo(Entry e)
        {
            if (e.has && e.t) e.t.SetPositionAndRotation(e.prePos, e.preRot);
            e.has = false;
            if (e.headHas) { if (e.head) e.head.localRotation = e.headPre; if (e.neck) e.neck.localRotation = e.neckPre; }
            e.headHas = false;
        }

        void LateUpdate()
        {
            keys.Clear(); keys.AddRange(map.Keys);
            float now = Time.time; int n = 0;
            foreach (var k in keys)
            {
                var e = map[k];
                if (!k || !e.t || k.Dying || !k.gameObject.activeInHierarchy) { if (k && e.t) Undo(e); map.Remove(k); continue; }
                // not re-posed by UnitView this frame: take our last offset back first (never accumulate)
                if (e.has && (e.t.position - e.appliedPos).sqrMagnitude < 1e-10f && Quaternion.Angle(e.t.rotation, e.appliedRot) < 0.01f) e.t.SetPositionAndRotation(e.prePos, e.preRot);
                e.has = false;
                if (e.headHas)
                {
                    if (e.head && Quaternion.Angle(e.head.localRotation, e.headApplied) < 0.01f) e.head.localRotation = e.headPre;   // the Animator did not write it (culled)
                    if (e.neck && Quaternion.Angle(e.neck.localRotation, e.neckApplied) < 0.01f) e.neck.localRotation = e.neckPre;
                    e.headHas = false;
                }
                Vector3 off = Vector3.zero; Quaternion rot = Quaternion.identity; float flinch = 0f; Vector3 flinchAxis = Vector3.zero;
                for (int i = e.list.Count - 1; i >= 0; i--)
                {
                    var p = e.list[i]; float tau = now - p.start;
                    if (tau >= p.dur) { e.list.RemoveAt(i); continue; }
                    float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f * p.dur, p.dur, tau));   // exactly 0 at the end
                    if (p.kind == 0)
                    {
                        float env = Mathf.Exp(-tau / 0.17f) * fade;
                        off += p.along * (p.amp * Mathf.Sin(6.2831853f * 6f * tau) * env)
                             + p.across * (0.5f * p.amp * Mathf.Sin(6.2831853f * 5f * tau) * env)
                             + Vector3.up * (0.25f * p.amp * 0.5f * (1f - Mathf.Cos(6.2831853f * 6f * tau)) * Mathf.Exp(-tau / 0.12f) * fade);
                        if (p.vehicle) rot = Quaternion.AngleAxis(1.5f * Mathf.Sin(6.2831853f * 5f * tau) * Mathf.Exp(-tau / 0.2f) * fade, p.along) * rot;
                        else { float f = 14f * (1f - Mathf.Exp(-tau / 0.03f)) * Mathf.Exp(-tau / 0.18f) * fade * Mathf.Min(1.2f, p.amp / 0.09f); if (f > flinch) { flinch = f; flinchAxis = p.across; } }
                    }
                    else
                    {
                        float r = Mathf.Min(1.2f, (1f - Mathf.Exp(-tau / 0.02f)) * Mathf.Exp(-tau / 0.25f) * 1.3f) * fade;
                        off -= p.along * (0.04f * r);
                        rot = Quaternion.AngleAxis(-3.5f * r, p.across) * rot;               // + about across = the top toward along: minus = rocks back
                    }
                }
                if (e.list.Count == 0) { map.Remove(k); continue; }
                e.prePos = e.t.position; e.preRot = e.t.rotation;
                var basePos = e.prePos;                                                        // rotate about the feet (the root)
                e.t.SetPositionAndRotation(basePos + off, rot * e.preRot);
                e.appliedPos = e.t.position; e.appliedRot = e.t.rotation; e.has = true; n++;
                if (flinch > 0.05f && e.head && (!e.anyRenderer || e.anyRenderer.isVisible))
                {   // head / neck snap back (after WeaponHold: the arms and the gun are not touched)
                    e.headPre = e.head.localRotation; e.neckPre = e.neck ? e.neck.localRotation : Quaternion.identity;
                    var axisW = flinchAxis.sqrMagnitude > 1e-6f ? flinchAxis : e.t.right;          // + about across = the top thrown along the bolt (away from the shooter)
                    if (e.neck) e.neck.rotation = Quaternion.AngleAxis(flinch * 0.45f, axisW) * e.neck.rotation;   // the neck first (it carries the head)
                    e.head.rotation = Quaternion.AngleAxis(flinch * 0.55f, axisW) * e.head.rotation;
                    e.headApplied = e.head.localRotation; e.neckApplied = e.neck ? e.neck.localRotation : Quaternion.identity; e.headHas = true;
                }
            }
            Applied = n;
        }
    }
}
