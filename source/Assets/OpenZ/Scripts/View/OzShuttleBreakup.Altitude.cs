using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shuttle's death at its drawn altitude (rule DV118, view only; the plain part: OzShuttleDeathPlan). Shot down in the air, the
    /// ship breaks on the drawn kill moment where it is drawn: the blast bursts at the hull's middle up there, every piece starts at its
    /// place on the drawn hull and falls with physics, the squad's bodies are thrown from their seats up there and fall among the pieces;
    /// destroyed on the ground everything stays at ground level, as before.
    /// <list type="bullet">
    /// <item>Rifle-class fire (OzShuttleAim.Power up to <see cref="SmallArmsPower"/>) cracks the hull open in the air ("crack"): a low,
    /// slow throw, the pieces tumbling harder, every body panel and the burning pieces trailing dark smoke while they fall, a smaller
    /// dark cloud; rockets and shells keep the burst, a heavy shell the split. The whole-ship drop of the third round (the hull falling
    /// whole and breaking on the ground) is a switch, <see cref="DropWhole"/>, off: on, the hull is held drawn all the way down.</item>
    /// <item>The dead ship's hull is never hidden by its unit view before the break-up takes it (<see cref="KeepsDrawn"/>, the unit
    /// view's one-line hook): a kill whose drawn round lands late is drawn up to its break.</item>
    /// <item>Landing effects only where a piece lands (<see cref="LandingContact"/>): the body's first slam (the wreck crash, sparks,
    /// scorch, dust, the camera kick, the debris rain), a heavy piece's crash with dust and every piece's first-contact landing sound
    /// come when it touches the ground or the water, touches anything near them, or comes down on something lying still (a roof, a rock,
    /// a piece that has landed); two pieces (or a piece and a body) knocking together up in the air make a quiet clank only.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>Switch: rifle-class fire in the air drops the whole ship first and it breaks where it hits the ground (the third
        /// round's drop). Off (the default): every air kill breaks at once where the ship is drawn.</summary>
        public static bool DropWhole = false;
        /// <summary>Switch: the dead ship's drawn hull is kept until the break-up takes it (on).</summary>
        public static bool KeepShipDrawn = true;
        /// <summary>The crack's smoke trail: how often a falling piece puffs (s), for how long at most (s), down to what height (m).</summary>
        public static float CrackTrailEvery = 0.07f, CrackTrailSec = 5f, CrackTrailDownToM = 1.2f;
        /// <summary>A knock of two flying pieces up in the air: heard from this speed (m/s), at most this many per piece.</summary>
        public static float AirClankFromMps = 2.5f; public static int AirClanksPerPiece = 2;

        // ------------------------------------------------------------------ counts (the Play proof reads them)
        public static int Cracks, CrackTrails, CrackPuffs, AirContacts, AirClanks, LandingContacts, LandingsOnTop, KeptDrawnFrames, DropHolds;
        /// <summary>The last break: the pads over the ground at the drawn kill moment (m), the blast's middle over the ground (m), the
        /// pieces' lowest and highest start over the ground (m), the farthest any modelled piece started from its place on the drawn hull
        /// (m: 0 in the air; on the ground the lift out of the ground), the highest landing contact (m over the ground).</summary>
        public static float LastBreakFeet = -1f, LastCentreUpM = -1f, LastSpawnLowM = -1f, LastSpawnHighM = -1f, LastSpawnOffM = -1f, LastLandingMaxM;
        /// <summary>The last break's landing contacts: (what = ground, water, near, on top of a roof / rock /
        /// lying piece / body).</summary>
        public static readonly List<string> LastLandings = new List<string>();

        float lastAirClank = -1f; int airClanks;

        static float Surface(Vector3 p)
        {
            var g = ZGame.I; float s = g ? g.GroundY(p.x, p.z) : 0f;
            if (OzWaterLanding.OpenWaterAt(p, out float wy)) s = Mathf.Max(s, wy);
            return s;
        }
        /// <summary>The height (m) of a point over the ground or the water under it.</summary>
        internal static float OverSurface(Vector3 p) => p.y - Surface(p);

        // ------------------------------------------------------------------ the break (TakesDeath's hooks)
        /// <summary>The break of this kill (OzShuttleDeathPlan.How; past the drop's line: no drop here).</summary>
        static string AirHow(OzPlayableShuttle ps, bool hitKnown, float power) => OzShuttleDeathPlan.How(ps.FeetHeight, hitKnown, power, SmallArmsPower, HeavyPower, false, FallMinFeet);

        /// <summary>The drop (switch DropWhole on): the hull is held drawn by its unit view for the whole fall (the break-up's keeper hides it).</summary>
        static void HoldDrawn(UnitView v) { if (!v) return; v.HoldForDeath(FallMaxSec + 2f); DropHolds++; }

        /// <summary>Break's launch loop: the crack's low, slow, tumbling throw.</summary>
        static void CrackThrow(string how, ref float vo, ref float vu, ref float sp)
        {
            if (how != OzShuttleDeathPlan.Crack) return;
            double a = vo, b = vu, c = sp; OzShuttleDeathPlan.Throw(how, ref a, ref b, ref c);
            vo = (float)a; vu = (float)b; sp = (float)c;
        }

        /// <summary>After the break: where everything started (for the proof); the crack's trails and its smaller cloud.</summary>
        static void BreakEnd(string how, OzPlayableShuttle ps)
        {
            var root = ps ? ps.transform : null;
            LastBreakFeet = ps ? ps.FeetHeight : -1f;
            LastCentreUpM = OverSurface(LastCentre);
            float lo = float.MaxValue, hi = float.MinValue, off = 0f;
            foreach (var pc in LastPieces)
            {
                if (!pc) continue;
                float h = OverSurface(pc.Centre()); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                if (root && pc.vis && pc.Kind != "leaf") off = Mathf.Max(off, Vector3.Distance(pc.vis.position, root.position));   // built in the ship's root space: at the root, unless lifted
            }
            LastSpawnLowM = lo < float.MaxValue ? lo : -1f; LastSpawnHighM = hi > float.MinValue ? hi : -1f; LastSpawnOffM = off;
            LastLandings.Clear(); LastLandingMaxM = 0f;
            if (how != OzShuttleDeathPlan.Crack) return;
            Cracks++;
            Group grp = null; foreach (var pc in LastPieces) if (pc && pc.grp != null) { grp = pc.grp; break; }
            if (grp != null && grp.cloudUntil > 0f) grp.cloudUntil *= (float)OzShuttleDeathPlan.CrackCloudK;
            foreach (var pc in LastPieces)
                if (pc && (pc.Kind == "body" || pc.bigFire)) { pc.gameObject.AddComponent<CrackTrail>().piece = pc; CrackTrails++; }
        }

        // ------------------------------------------------------------------ landings (OnCollisionEnter's hook)
        /// <summary>What a collider is to a falling piece or body (the ground, still scenery, something lying still, something flying).</summary>
        internal static OzShuttleDeathPlan.Touch TouchOf(Collider other)
        {
            if (!other) return OzShuttleDeathPlan.Touch.Scenery;
            if (other.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(other)) return OzShuttleDeathPlan.Touch.Ground;
            var rb = other.attachedRigidbody;
            if (!rb) return OzShuttleDeathPlan.Touch.Scenery;                                                        // a roof, a rock, a trunk
            var host = other.GetComponentInParent<OzShuttleBreakup>();
            if (host && !host.keeper) return host.frozen || host.Rested || host.hits > 0 || host.LyingStill() ? OzShuttleDeathPlan.Touch.Resting : OzShuttleDeathPlan.Touch.Flying;
            var body = rb.GetComponentInParent<OzShuttleCrewFall>();
            if (body) return body.Landed ? OzShuttleDeathPlan.Touch.Resting : OzShuttleDeathPlan.Touch.Flying;
            return rb.isKinematic || rb.IsSleeping() || rb.linearVelocity.sqrMagnitude < 1f ? OzShuttleDeathPlan.Touch.Resting : OzShuttleDeathPlan.Touch.Flying;
        }

        /// <summary>A piece whose bottom is this close over the ground or the water (m) and that moves slower than 1 m/s lies there, even
        /// without a hard landing of its own (a piece that settled softly in a break on the ground).</summary>
        public static float LyingOverM = 0.35f;
        /// <summary>This piece lies still on the ground or the water: asleep, or slow with its bottom on the surface (never up in the air).</summary>
        bool LyingStill()
        {
            if (!rb || !col || !col.enabled) return false;
            if (rb.isKinematic || rb.IsSleeping()) return true;
            if (rb.linearVelocity.sqrMagnitude >= 1f) return false;
            var b = col.bounds;
            return b.min.y - Surface(b.center) <= LyingOverM;
        }

        /// <summary>A hard contact of this piece: true = its landing (the landing effects follow as before); false = it knocked into a
        /// flying piece or body up in the air (a quiet clank at most; it is not counted as a hit).</summary>
        bool LandingContact(Collision c, Vector3 at, float sp)
        {
            var touch = TouchOf(c.collider);
            float over = OverSurface(at), up = c.contactCount > 0 ? c.GetContact(0).normal.y : 0f;
            if (OzShuttleDeathPlan.Lands(touch, up, over, OzShuttleDeathPlan.PieceNearM))
            {
                LandingContacts++;
                bool top = over > OzShuttleDeathPlan.PieceNearM && touch != OzShuttleDeathPlan.Touch.Ground && touch != OzShuttleDeathPlan.Touch.Water;
                if (top) LandingsOnTop++;
                LastLandingMaxM = Mathf.Max(LastLandingMaxM, over);
                if (grp != null && LastPieces.Contains(this) && LastLandings.Count < 200)
                    LastLandings.Add($"{PieceName} {over:F2} m {(top ? "on top of " + (touch == OzShuttleDeathPlan.Touch.Scenery ? "scenery" : "a lying piece / body") : touch == OzShuttleDeathPlan.Touch.Ground ? "ground" : touch == OzShuttleDeathPlan.Touch.Water ? "water" : "near the ground")}");
                return true;
            }
            AirContacts++;
            if (sp >= AirClankFromMps && airClanks < AirClanksPerPiece && age - lastAirClank > 0.3f)
            {
                lastAirClank = age; airClanks++;
                if (PlayAd("dst_debris_metal", at, GetInstanceID(), Mathf.Clamp(0.16f + sp * 0.025f, 0.16f, 0.32f), MassKg > 1500f ? 0.86f : 1.0f)) AirClanks++;
            }
            return false;
        }

        // ------------------------------------------------------------------ the dead ship stays drawn (UnitView.LateUpdate's hook)
        /// <summary>UnitView: the dead ship's view is not hidden yet: it is dying and not broken up (its drawn kill moment still to come)
        /// or falling whole after a failed break-up.</summary>
        internal static bool KeepsDrawn(UnitView v)
        {
            if (!KeepShipDrawn || !v || v.Obj == null || !v.Obj.IsShuttle) return false;
            var ps = OzPlayableShuttle.Of(v.Obj);
            if (!ps || !ps.KeepDrawnForBreak) return false;
            KeptDrawnFrames++;
            return true;
        }

        // ------------------------------------------------------------------ the crack's smoke trail on a falling piece
        sealed class CrackTrail : MonoBehaviour
        {
            internal OzShuttleBreakup piece; float t, next;
            void Update()
            {
                var g = ZGame.I; float dt = g && g.paused ? 0f : Time.deltaTime;
                if (dt <= 0f) return;
                t += dt;
                if (!piece || piece.retiring || piece.frozen || piece.hits > 0 || t > CrackTrailSec) { Destroy(this); return; }
                var c = piece.Centre();
                if (OverSurface(c) < CrackTrailDownToM) { Destroy(this); return; }
                if ((next -= dt) > 0f) return;
                next = CrackTrailEvery;
                var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled) return;
                fxp.EmitChannel("leg_smoke", c + Random.insideUnitSphere * 0.25f, new Vector3(Random.Range(-0.25f, 0.25f), 0.6f + Random.value * 0.5f, Random.Range(-0.25f, 0.25f)),
                    1.7f + Random.value * 0.6f, (piece.bigFire ? 1.1f : 0.75f) + Random.value * 0.4f, Random.value < 0.6f ? OpenZFxCatalog.WreckSmokeDark : OpenZFxCatalog.WreckBurnSmoke, 0.85f);
                CrackPuffs++;
            }
        }
    }
}
