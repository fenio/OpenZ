using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256g2 (rule DV118, view only): the playable shuttle shot down in the air hands its landing mark over to the real shadows of
    /// what falls, instead of cutting it. Made by OzPlayableShuttle.Touches.cs at the drawn kill (OnBrokenUp) whenever the mark is still
    /// drawn or the ship's own shadow is not full; it owns the mark (OzShuttleShadow) and the real shadow's cross-fade (OzShuttleRealShadow)
    /// from then on:
    /// <list type="bullet">
    /// <item>over <see cref="Sec"/> s the cross-fade's u runs from where it stood (0 in the air) to 1; k = smoothstep(u) is the real
    /// shadow, 1 - k the mark: the two always add up to one (before: the mark went from 1 to 0 and the full shadow came in one frame).</item>
    /// <item>THE REAL SHADOW: the falling hull (the drop: the root falls whole) and the pieces of a burst or split (found under the
    /// break-up's holder, taken in at once, before their first frame: OzShuttleRealShadow.Adopt) cast through the same dithered caster
    /// at k; at 1 every renderer gets its own mode back.</item>
    /// <item>THE MARK: K256g's mark (its strength and look) under the falling hull, or under the pieces' middle (their mass-weighted
    /// centre moved by where the ship's root was at the kill: no jump), drawn x (1 - k), then gone.</item>
    /// <item>The falling hull keeps its own self-shadow (OzShuttleSelfShadow) while k is under 1.</item>
    /// </list>
    /// Switch: <see cref="Enabled"/> = false is K256g's cut (the mark goes, the shadows come at once).
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class OzShuttleShadowHandOver : MonoBehaviour
    {
        public static bool Enabled = true;
        public static float Sec = 0.6f;
        /// <summary>Proof: hand-overs begun / finished, pieces taken in; of the last one: the worst |mark weight + real shadow - 1|, the
        /// largest change of either in one frame, its length (s), frames.</summary>
        public static int Started, Finished, PiecesTaken, LastFrames;
        public static float LastSumErr, LastMarkStepMax, LastRealStepMax, LastSec;
        public float K { get; private set; }
        public float U => u;

        OzPlayableShuttle ship; OzShuttleShadow mark; OzShuttleRealShadow real; OzShuttleSelfShadow self; Transform root, pieces;
        int shipId, lastFrame = -1; float u, t0, lookAt, lastMark = -1f, lastReal = -1f; bool haveOff, haveV; Vector3 startOff, lastAt, velS;

        public static OzShuttleShadowHandOver Begin(OzPlayableShuttle s, OzShuttleShadow mark, OzShuttleRealShadow real, OzShuttleSelfShadow self, float groundK, int id, bool inAir)
        {
            var h = new GameObject("OZ_ShuttleShadowHandOver_" + id).AddComponent<OzShuttleShadowHandOver>();
            h.ship = s; h.mark = mark; h.real = real; h.self = self; h.root = s ? s.transform : null; h.shipId = id; h.u = Mathf.Clamp01(groundK); h.t0 = Time.unscaledTime;
            Started++; LastSumErr = 0f; LastMarkStepMax = 0f; LastRealStepMax = 0f; LastFrames = 0; LastSec = -1f;
            h.Tick(0f, 0f);                                                                                  // this frame: the pieces taken in before they are drawn
            return h;
        }

        void LateUpdate()
        {
            var g = ZGame.I; float rdt = Mathf.Min(Time.deltaTime, 0.05f);
            float dt = g && g.paused ? 0f : rdt * (g ? Mathf.Max(0.05f, g.gameSpeed) : 1f);
            Tick(dt, rdt);
        }

        bool RootDrawn()
        {
            if (!root) return false;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false)) if (r && r.enabled && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) return true;
            return false;
        }

        /// <summary>The pieces' mass-weighted middle, velocity and lowest drawn point (false: none yet).</summary>
        bool PiecesMiddle(out Vector3 at, out Vector3 vel, out float low)
        {
            at = vel = Vector3.zero; low = float.MaxValue; float m = 0f;
            if (!pieces) return false;
            foreach (var pc in pieces.GetComponentsInChildren<OzShuttleBreakup>(false))
            {
                if (!pc || pc.MassKg <= 0f) continue;                                                       // the holder's own keeper is no piece
                float w = pc.MassKg;
                at += pc.transform.position * w;
                if (pc.TryGetComponent<Rigidbody>(out var rb)) vel += rb.linearVelocity * w;
                m += w;
                foreach (var r in pc.GetComponentsInChildren<MeshRenderer>(false)) if (r.enabled) low = Mathf.Min(low, r.bounds.min.y);
            }
            if (m <= 0f) return false;
            at /= m; vel /= m;
            if (low == float.MaxValue) low = at.y;
            return true;
        }

        void Tick(float dt, float rdt)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount; LastFrames++;
            if (!pieces && Time.unscaledTime >= lookAt)
            {   // the break-up's holder (a burst or split: made before the kill's call; a drop: when the hull hits the ground)
                lookAt = Time.unscaledTime + 0.05f;
                var hgo = GameObject.Find("OZ_ShuttleBreakup_" + shipId);
                if (hgo) { pieces = hgo.transform; int n = real != null ? real.Adopt(pieces) : 0; PiecesTaken += n; OzShuttleSelfShadow.Scrub(pieces); }
            }
            u = Mathf.MoveTowards(u, 1f, dt / Mathf.Max(0.05f, Sec));
            K = u * u * (3f - 2f * u);
            // where the mark lies: the falling hull, else the pieces' middle (moved by the root's place at the kill: no jump)
            Vector3 at, vel; float low = float.NaN;
            if (!RootDrawn() && PiecesMiddle(out var pm, out var pv, out float pl))
            {
                if (!haveOff) { startOff = root ? root.position - pm : Vector3.zero; startOff.y = 0f; haveOff = true; }
                at = pm + startOff; vel = pv; low = pl;
            }
            else
            {
                at = root ? root.position : lastAt;
                if (!haveV) { velS = ship ? ship.Velocity : Vector3.zero; haveV = true; }
                else if (rdt > 1e-4f) velS = Vector3.Lerp(velS, (at - lastAt) / rdt, 1f - Mathf.Exp(-rdt * 12f));
                vel = velS;
            }
            lastAt = at;
            real?.Step(K);
            if (mark != null)
            {
                mark.GroundShare = u;
                mark.StepDown(rdt, at, vel, low);
            }
            if (self && root) self.Feed(real, root);
            float mw = mark != null && mark.Shown ? mark.MarkWeight : 0f, rk = real != null ? real.K : 1f;
            LastSumErr = Mathf.Max(LastSumErr, Mathf.Abs(mw + rk - 1f));
            if (lastMark >= 0f) { LastMarkStepMax = Mathf.Max(LastMarkStepMax, Mathf.Abs(mw - lastMark)); LastRealStepMax = Mathf.Max(LastRealStepMax, Mathf.Abs(rk - lastReal)); }
            lastMark = mw; lastReal = rk;
            if (u >= 1f)
            {
                real?.Dispose(); real = null;
                mark?.Dispose(); mark = null;
                if (self) self.Stop();
                Finished++; LastSec = Time.unscaledTime - t0;
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (real != null) { real.Dispose(); real = null; }                                               // the level was left half way: the renderers cast as before
            if (mark != null) { mark.Dispose(); mark = null; }
        }
    }
}
