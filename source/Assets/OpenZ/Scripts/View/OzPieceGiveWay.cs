using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: nobody stands on or inside a resting wreck piece. The big falling pieces are view only (they never
    /// block a unit: the units walk and drive by the rules), so a unit whose way crosses a piece lying at rest would walk through it or
    /// be drawn standing in it. A resting big piece (a vehicle wreck piece, a flipped hull, a building's big chunk, a shuttle piece, the
    /// radar dish) gives way instead, the way every wreck of the game leaves: it settles into the ground and fades (a faint dust puff and
    /// the debris settle sound), quickly, as soon as a live ground unit (a robot or a vehicle) comes within its reach or will in the next
    /// <see cref="LookAheadS"/> s on its present way. Pushing a piece aside was not chosen: it would need physics again, could shove it
    /// into a rock, a building or another unit, and would still be drawn through the unit while it moves; the settle is the game's own
    /// end of a wreck, costs nothing and finishes before the unit gets there. Robot bodies stay (a unit steps over a fallen robot).
    /// View only. Switch: <see cref="Enabled"/>.
    /// </summary>
    public static class OzPieceGiveWay
    {
        public static bool Enabled = true;
        /// <summary>How far ahead (s) a unit's present way is looked along; the settle's length (s); clear air round a unit (m).</summary>
        public static float LookAheadS = 0.9f, SettleSeconds = 0.8f, ClearM = 0.25f;
        /// <summary>Statistics (Play check): pieces that gave way, per OzRockBody kind; to robots / to vehicles; looks.</summary>
        public static readonly int[] Gave = new int[OzRockBody.Kinds];
        public static int ToRobot, ToVehicle, Looks;

        struct Unit { public Vector3 p, v; public float r; public bool robot; }
        static readonly List<Unit> units = new List<Unit>(128);
        static readonly Dictionary<int, Vector3> lastPos = new Dictionary<int, Vector3>();
        static readonly Dictionary<OzRockBody, Bounds> boxes = new Dictionary<OzRockBody, Bounds>();
        static readonly List<OzRockBody> stale = new List<OzRockBody>(8);
        static float next, lastSound = -10f, lastT;

        /// <summary>OzStaticSolid.Update: every 0.2 s (nothing when no big piece rests).</summary>
        internal static void Step(ZGame g)
        {
            if (!Enabled || Time.time < next) return;
            float dt = Time.time - lastT; next = Time.time + 0.2f; lastT = Time.time;
            var all = OzRockBody.All;
            bool any = false;
            for (int i = 0; i < all.Count && !any; i++) { var b = all[i]; any = b && b.Rested && b.kind != OzRockBody.Kind.RobotBody && b.gameObject.activeInHierarchy; }
            if (!any) { if (boxes.Count > 0) boxes.Clear(); if (lastPos.Count > 0) lastPos.Clear(); return; }   // idle: no old place is kept (a way measured across a pause would be far too long)
            Looks++;
            units.Clear();
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!v || !v.gameObject.activeInHierarchy) continue;
                var o = v.Obj; if (o == null || o.Destroyed || (o.Kind != ZKind.Robot && o.Kind != ZKind.Vehicle)) continue;
                var p = v.transform.position;
                var vel = Vector3.zero;
                if (lastPos.TryGetValue(o.Id, out var lp) && dt > 0.01f && dt < 1f) { vel = (p - lp) / dt; vel.y = 0f; if (vel.sqrMagnitude > 400f) vel = Vector3.zero; }   // a jump (a new view): no way
                lastPos[o.Id] = p;
                bool robot = o.Kind == ZKind.Robot;
                units.Add(new Unit { p = p, v = vel, r = robot ? 0.45f : Mathf.Max(1.2f, Mathf.Max(o.WidthPix, o.HeightPix) / 8f * 0.42f), robot = robot });
            }
            if (lastPos.Count > 600) lastPos.Clear();
            if (units.Count == 0) return;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var b = all[i];
                if (!b || !b.Rested || b.kind == OzRockBody.Kind.RobotBody || !b.gameObject.activeInHierarchy || PopOut.IsPopping(b.transform) || b.GetComponent<OzGiveWaySettle>() || b.GetComponent<OzGiveWayHullFade>()) continue;
                var brb = b.GetComponent<Rigidbody>();
                if (brb && !brb.isKinematic) { boxes.Remove(b); continue; }                                    // woken (what it lay on went): it falls; it gives way only once it rests again, from its new place
                if (b.kind == OzRockBody.Kind.ShuttlePiece) { var sp = b.GetComponent<OzShuttleBreakup>(); if (sp && sp.BurningNow) continue; }   // a burning hull piece stays (its fire is drawn on it)
                if (!boxes.TryGetValue(b, out var box)) { box = Box(b.gameObject); boxes[b] = box; }
                for (int u = 0; u < units.Count; u++)
                {
                    var un = units[u];
                    if (un.p.y > box.max.y + 1.5f || un.p.y < box.min.y - 2.5f) continue;                       // over it (a bridge deck, the air) or under it (a piece on a roof)
                    float reach = un.r + ClearM, far = un.v.magnitude * LookAheadS;
                    var d = un.p - box.center; d.y = 0f;
                    if (d.magnitude > far + reach + Mathf.Max(box.extents.x, box.extents.z) + 0.5f) continue;
                    bool hit = false;
                    for (int s = 0; s <= 3 && !hit; s++) hit = Gap(un.p + un.v * (LookAheadS * s / 3f), box) <= reach;
                    if (!hit) continue;
                    if (GiveWay(b, box)) { Gave[(int)b.kind]++; if (un.robot) ToRobot++; else ToVehicle++; }
                    break;
                }
            }
            if (boxes.Count > 64)
            {
                stale.Clear(); foreach (var kv in boxes) if (!kv.Key || !kv.Key.Rested) stale.Add(kv.Key);
                foreach (var k in stale) boxes.Remove(k);
            }
        }

        static float Gap(Vector3 p, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - p.x, p.x - b.max.x)), dz = Mathf.Max(0f, Mathf.Max(b.min.z - p.z, p.z - b.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static Bounds Box(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero); bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return b;
        }

        /// <summary>The piece settles into the ground and fades now (a flipped hull: its own wreck fade). True = it gives way.</summary>
        static bool GiveWay(OzRockBody b, Bounds box)
        {
            var go = b.gameObject;
            if (b.kind == OzRockBody.Kind.FlipHull)
            {
                var v = go.GetComponent<UnitView>();
                if (!v || !v.OzGiveWay()) return false;
            }
            else go.AddComponent<OzGiveWaySettle>().Begin(box);
            OzRockBody.CountPop(go);                                                                       // its maker never pops it on a rock afterwards
            var at = new Vector3(box.center.x, box.min.y, box.center.z);
            if (WreckFx.OnScreen(at))
            {
                FxManager.I?.Dust(at + Vector3.up * 0.15f, Mathf.Clamp(Mathf.Max(box.size.x, box.size.z) * 0.15f, 0.2f, 0.6f));
                if (Time.time - lastSound > 0.25f) { lastSound = Time.time; AudioDirector.Fire("dst_debris_settle", at, go.GetInstanceID(), 0.7f); }
            }
            return true;
        }
    }

    /// <summary>A resting big piece settling into the ground (OzPieceGiveWay): kinematic, no contacts, sinks by its drawn height over
    /// OzPieceGiveWay.SettleSeconds, then hidden; its maker removes it at the end of its life as it always does.</summary>
    public sealed class OzGiveWaySettle : MonoBehaviour
    {
        Vector3 from; float depth, t;

        public void Begin(Bounds box)
        {
            foreach (var rb in GetComponentsInChildren<Rigidbody>()) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            var wp = GetComponent<WreckPieces>(); if (wp) wp.OzDouse();                                     // a burning piece: its fire and smoke stop with it
            from = transform.position; depth = Mathf.Max(0.3f, box.size.y * 1.05f + 0.05f); t = 0f;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.1f, OzPieceGiveWay.SettleSeconds)));
            transform.position = from + Vector3.down * depth * k;
            if (k < 1f) return;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            enabled = false;
        }
    }
}
