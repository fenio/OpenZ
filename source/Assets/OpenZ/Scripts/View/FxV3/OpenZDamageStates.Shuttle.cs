using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle is a vehicle unit with a UnitView, so OpenZDamageStates already ran its ZF7 stages on it (the same thresholds,
    /// the clank, the ignite, the fire loop, the steam hiss and "Fire extinguished!" after a repair), but every particle was born INSIDE
    /// its hull: the spot finder of the other vehicles (TopOf: 85 % of the drawn height, at most 4 m; DeckOf: 60 % of it, at most
    /// 2.4 m) put the grey smoke 1.14 m under the 5.14 m hull's top skin and the small fire 2.55 m under it, and the depth test hid them.
    /// <list type="bullet">
    /// <item><b>The same effect and thresholds:</b> the others' calls (OpenZFxPlayer.WreckSmoke, OpenZPyroFire.KeepBurningSmall, the
    /// heat plume, the embers, the oil stain), the others' stages (SmokeAt 50 %, BurnAt 25 %, OutAt 40 %: the fire stays lit until a
    /// repair lifts it past 40 %, the smoke ends past 50 %) and their sounds, all from OpenZDamageStates itself.</item>
    /// <item><b>Its emitter points</b> (ship root space, measured on the drawn model: the hull's top skin 5.14 m, the engine pods' tops
    /// 4.46 m, the top vents' marker MK_Vent at 4.68 m): ONE engine pod's top (the damaged side: by its id), the hull's top vent on the
    /// other side, and the other pod's top. Battered (50 %): the pod and the vent smoke; burning (25 %): the small fire and its dark
    /// smoke on that pod, the vent and the other pod smoke. Scaled to its size: the shuttle stands 5.1 m against a tank's 2.6 m
    /// (CombatView.BodyHeight), so every puff is x1.96, shared over its points (x0.85 each).</item>
    /// <item><b>In flight it trails:</b> the puffs are the game's world-space smoke particles; while a point moves, one more puff
    /// is laid each <see cref="TrailSpacingM"/> of its way (about one puff's birth size; at most 8 a frame), so the smoke is one ribbon
    /// behind the ship instead of blobs 3-4 m apart; standing, it rises as the others' does (their 0.3-0.45 s beat). The points turn with
    /// the ship (bank, pitch, the kickflip), the small fire rides on its pod, and in flight the beat's flame tongues (the same leg_fire
    /// puff as WreckSmoke's) take the pod's own speed, so the fire stays ON the pod and only its smoke is left behind.</item>
    /// <item><b>Never through the dust or the searchlights' look:</b> every point sits on the top side of the ship (4.6-4.8 m up), so
    /// the smoke rises away from the belly: never into the downwash dust under it, never into the belly lamps' beams; nothing of the
    /// dust, the searchlights or the jets is touched. No oil stain while it flies (only standing, as the others).</item>
    /// <item><b>LOW (GRAPHICS QUALITY):</b> the others have no damage-smoke cut, so the shuttle's base beat stays theirs; its extras are
    /// thinned: one point less in each stage, and the trail's spacing doubled.</item>
    /// </list>
    /// Hooks in OpenZDamageStates.cs: the class is partial; TopOf / DeckOf ask <see cref="ShipTop"/> / <see cref="ShipFireLocal"/> for a
    /// shuttle (so the clank, the ignite, the fire loop and the last puff after a repair also sit on its real top); TickUnits hands a
    /// shuttle that is battered or burning to <see cref="ShipTick"/>. Play proof: View/OzShuttleDamageSmokeProof.cs.
    /// </summary>
    public partial class OpenZDamageStates
    {
        // ------------------------------------------------------------------ the numbers (ship root space, metres)
        /// <summary>The shuttle's drawn hull top over its pads' soles (model marker MK_Top, the hull's top skin) and the "top" the stage sounds
        /// and the last puff after a repair use (a little over the skin).</summary>
        public const float ShipHullTopM = 5.145f, ShipTopM = 5.39f;
        /// <summary>The engine pod's top (x, y, z for the pod at root +x; the other pod is x mirrored) and the top vent (MK_Vent_L, root +x).</summary>
        public static readonly Vector3 ShipPodTop = new Vector3(2.47f, 4.66f, -0.2f), ShipVent = new Vector3(0.8775f, 4.77f, -1.93f);
        /// <summary>The small fire's spot: on the burning pod's top (its licks rise from here).</summary>
        public static readonly Vector3 ShipFire = new Vector3(2.47f, 4.58f, -0.2f);
        /// <summary>Its size against a tank's (CombatView.BodyHeight 5.1 / 2.6) and the share per point (2-3 points).</summary>
        public const float ShipSizeK = 5.1f / 2.6f, ShipPointShare = 1.0f;   // K419a: was 0.85: every puff at the full size for the ship (OpenZDamageStates.ShipReadable.cs)
        /// <summary>The others' scales (OpenZDamageStates.TickUnits: smoke 1.1, the burning smoke 1.2, the plume 1.8), the shuttle's from them.</summary>
        public static float ShipSmokeScale => 1.1f * ShipSizeK * ShipPointShare;
        public static float ShipBurnScale => 1.2f * ShipSizeK * ShipPointShare;
        public static float ShipPlumeSize => 1.8f * ShipSizeK * ShipPointShare;
        /// <summary>In flight: one puff each this many metres of a point's way (LOW: x2); at most this many trail puffs per point a frame.</summary>
        public static float TrailSpacingM = 0.34f; public const int TrailMaxPerFrame = 8;   // K419a: was 0.4: a denser trail
        /// <summary>Test only: -1 = the GRAPHICS QUALITY preset decides; 0 = full; 1 = as on LOW.</summary>
        public static int ProofLite = -1;
        public static bool ShipLite => ProofLite >= 0 ? ProofLite == 1 : OzQuality.Level >= 2;

        // ------------------------------------------------------------------ proof counters (the Play check reads them)
        /// <summary>Puffs on the beat (all points), trail puffs (in flight), fire ticks (burning: flame + dark smoke), puffs while it flew /
        /// stood, the points used in the last tick, the largest gap between two puffs of one point while it moved (m, since the last reset).</summary>
        public static int ShipBeatPuffs, ShipTrailPuffs, ShipFireTicks, ShipAirPuffs, ShipGroundPuffs, ShipPointsUsed, ShipStains, ShipFlyingFlames;
        public static float ShipTrailGapMaxM;
        /// <summary>The last tick's emitter points (world) and their count, for the Play check's pictures and its hull test.</summary>
        public static readonly Vector3[] ShipLastPoints = new Vector3[3];
        public static void ShipProofReset() { ShipBeatPuffs = ShipTrailPuffs = ShipFireTicks = ShipAirPuffs = ShipGroundPuffs = ShipStains = ShipFlyingFlames = 0; ShipTrailGapMaxM = 0f; }
        /// <summary>The damage stage OpenZDamageStates holds for unit id (0 fine, 1 battered + smoke, 2 burning; -1 = not known).</summary>
        public static int StageOf(int id) => I && I.units.TryGetValue(id, out var u) ? u.stage : -1;
        /// <summary>True while the small fire (OpenZPyroFire's sticky small fire) burns unit id.</summary>
        public static bool StickyOf(int id) => I && I.units.TryGetValue(id, out var u) && u.sticky;

        sealed class ShipSmoke { public int frame = -10; public readonly Vector3[] last = new Vector3[3]; public readonly float[] gap = new float[3]; public Vector3 prevFire, fireVel; }
        readonly Dictionary<int, ShipSmoke> shipSmoke = new Dictionary<int, ShipSmoke>();

        /// <summary>The damaged side: the pod at root -x for an even id, +x for an odd one.</summary>
        static float ShipSide(ZObject o) => o != null && (o.Id & 1) == 1 ? 1f : -1f;
        static Vector3 Mirror(Vector3 p, float side) => new Vector3(p.x * side, p.y, p.z);

        /// <summary>TopOf hook: a shuttle's top (over its root) is its hull's real top, not the others' 4 m cap inside its hull.</summary>
        static bool ShipTop(UnitView v, U u)
        {
            if (v == null || v.Obj == null || !v.Obj.IsShuttle) return false;
            u.h = ShipTopM; return true;
        }

        /// <summary>DeckOf hook: a shuttle's small fire burns on its damaged pod's top (unit-local).</summary>
        static bool ShipFireLocal(UnitView v, U u)
        {
            if (v == null || v.Obj == null || !v.Obj.IsShuttle) return false;
            u.deck = Mirror(ShipFire, ShipSide(v.Obj)); return true;
        }

        /// <summary>TickUnits hook: a battered (1) or burning (2) shuttle, drawn and not hidden. The others' beat and calls at its own
        /// points, plus the trail while it moves.</summary>
        void ShipTick(int id, UnitView v, ZObject o, U u, float now, OpenZFxPlayer fx, bool fxOn)
        {
            var tf = v.transform; float side = ShipSide(o); bool lite = ShipLite, air = o.ShuttleAirborne;
            ShipCarryBegin(id);                                                                       // K432: this tick's hull velocity for its puffs (OpenZDamageStates.ShipCarry.cs)
            if (!shipSmoke.TryGetValue(id, out var sm)) { sm = new ShipSmoke(); shipSmoke[id] = sm; if (shipSmoke.Count > 16) Tidy(); }
            int n = u.stage >= 2 ? (lite ? 2 : 3) : (lite ? 1 : 2);
            // the points: [0] the damaged pod (the fire when burning), [1] the top vent on the other side, [2] the other pod
            Vector3 p0 = tf.TransformPoint(Mirror(ShipPodTop, side)), p1 = tf.TransformPoint(Mirror(ShipVent, -side)), p2 = tf.TransformPoint(Mirror(ShipPodTop, -side));
            var fire = tf.TransformPoint(DeckOf(v, u));
            ShipLastPoints[0] = u.stage >= 2 ? fire : p0; ShipLastPoints[1] = p1; ShipLastPoints[2] = p2; ShipPointsUsed = n;
            bool fresh = Time.frameCount - sm.frame > 2; sm.frame = Time.frameCount;
            if (fresh) { for (int i = 0; i < 3; i++) { sm.last[i] = ShipLastPoints[i]; sm.gap[i] = 0f; } sm.prevFire = fire; }   // a new stage or back in view: no streak from an old place
            sm.fireVel = Time.deltaTime > 1e-4f ? Vector3.ClampMagnitude((fire - sm.prevFire) / Time.deltaTime, 40f) : Vector3.zero; sm.prevFire = fire;   // the pod's own speed (in flight)
            if (u.stage == 2 && now >= u.nextSticky)
            {   // the others' small fire (OpenZPyroFire small: licks, dim light, thin smoke, its shimmer), re-lit every 2 s, on the pod
                u.nextSticky = now + 2f;
                u.sticky = OpenZPyroFire.KeepBurningSmall(o, v, v.gameObject, false, fire);
            }
            if (u.stage == 2 && !u.sticky) AddPlume(fire, ShipPlumeSize, 1f);                          // as the others: the lit small fire draws its own heat shimmer (no second one on top)
            bool onScreen = WreckFx.OnScreen(tf.position + Vector3.up * ShipTopM, 0.15f);
            if (!onScreen || !fxOn) { for (int i = 0; i < 3; i++) { sm.last[i] = ShipLastPoints[i]; sm.gap[i] = 0f; } return; }
            // the trail: a puff every TrailSpacingM of each point's way (the same puff as the beat's, smoke only: no light per puff)
            float spacing = TrailSpacingM * (lite ? 2f : 1f);
            for (int i = 0; i < n; i++)
            {
                var at = ShipLastPoints[i]; var from = sm.last[i];
                float d = Vector3.Distance(from, at);
                if (d > 25f) { sm.last[i] = at; sm.gap[i] = 0f; continue; }                              // a jump (a respawn, a teleport in a test): no line of puffs
                float sc = i == 0 && u.stage >= 2 ? ShipBurnScale : ShipSmokeScale * (i == 2 ? 0.8f : 1f);
                int k = 0; var dir = d > 1e-4f ? (at - from) / d : Vector3.zero; float walked = 0f;
                while (d - walked >= spacing && k < TrailMaxPerFrame)
                {
                    walked += spacing; k++;
                    ShipPuff(fx, from + dir * walked, sc, false);                                                  // K420: the trail with the ship's own big puffs (OpenZDamageStates.ShipPuff.cs)
                    ShipTrailPuffs++; if (air) ShipAirPuffs++; else ShipGroundPuffs++;
                }
                if (k > 0)
                {
                    ShipTrailGapMaxM = Mathf.Max(ShipTrailGapMaxM, k >= TrailMaxPerFrame ? (d - walked) + spacing : spacing);
                    sm.last[i] = k >= TrailMaxPerFrame ? at : from + dir * walked;                  // capped: catch up (never a growing debt)
                }
            }
            if (now < u.nextFx) return;
            // the beat, as the others': battered every 0.3-0.45 s (thin grey smoke), burning every 0.3 s (the fire's dark smoke + embers)
            if (u.stage == 1)
            {
                u.nextFx = now + Random.Range(0.3f, 0.45f) * ShipBeatK;   // K419a: the beat a little closer for the ship
                for (int i = 0; i < n; i++) Beat(fx, i, ShipSmokeScale, false, air, sm);
            }
            else
            {
                u.nextFx = now + 0.3f * ShipBeatK;   // K419a: the beat a little closer for the ship
                var shipFv = ShipFlameVel(sm.fireVel); if (shipFv.sqrMagnitude < 2.25f) Beat(fx, 0, ShipBurnScale, true, air, sm);   // K432: the hull's velocity, not the pod's frame-to-frame speed (a spin)       // standing / slow: the others' non-sticky call, scaled (flame + dark smoke + its flicker light)
                else { Beat(fx, 0, ShipBurnScale, false, air, sm); FlyingFlame(fx, ShipLastPoints[0], shipFv); }   // flying: its smoke stays behind, its flame rides on the pod
                ShipFireTicks++;
                for (int i = 1; i < n; i++) Beat(fx, i, ShipSmokeScale * (i == 2 ? 0.8f : 1f), false, air, sm);
                for (int e = 0; e < 2; e++)
                    if (Random.value < 0.6f)
                        fx.EmitChannel("leg_spark", fire + Random.insideUnitSphere * 0.3f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1.2f, 2.2f), Random.Range(-0.3f, 0.3f)) + ShipEmberVel(),   /* K432: with the hull's velocity */
                            Random.Range(0.6f, 1.1f), Random.Range(0.05f, 0.08f), new Color(1f, 0.55f, 0.18f), 1f);   // a few embers (the others': one draw of 0.6)
                if (!air && u.still > 1.5f && now >= u.nextStain)
                {   // the others' oil / burn stain under a burning hull that stands still (at most one per 10 s); never under it in the air
                    u.nextStain = now + 10f; OilStains++; ShipStains++;
                    var pos = tf.position; fx.Scorch(new Vector3(pos.x, game.GroundY(pos.x, pos.z), pos.z), 0.28f * ShipSizeK, 1);
                }
            }
        }

        void Beat(OpenZFxPlayer fx, int i, float scale, bool burning, bool air, ShipSmoke sm)
        {
            var at = ShipLastPoints[i];
            ShipWreckSmoke(fx, at, scale, burning);                                                   // K432: moving, the same puff with the hull's velocity (OpenZDamageStates.ShipCarry.cs)
            ShipPuff(fx, at, scale, burning);                                                               // K420: and the ship's big puff on top
            ShipBeatPuffs++; if (air) ShipAirPuffs++; else ShipGroundPuffs++;
            sm.last[i] = at;                                                                          // the trail counts on from here
        }

        /// <summary>WreckSmoke's flame tongue (leg_fire: 0.4 m under the point, up 0.9-1.7 m/s, life 0.35-0.55 s, (9..15) px x scale,
        /// colours WreckFlameB / C) with the pod's own velocity added, so a flying ship's fire does not drop behind it in blobs. The
        /// channel's drag (0.96 a 60th: k 2.45 /s) eats that speed, so it starts at x<see cref="FlameCarry"/>: level with the pod at
        /// 0.2 s, leaning back about 1 m at the end of its life (shrunk to 15 % by then): a flame streaming back from the pod.</summary>
        public const float FlameCarry = 1.3f;
        void FlyingFlame(OpenZFxPlayer fx, Vector3 at, Vector3 vel)
        {
            vel *= FlameCarry;
            float px = OpenZFxCatalog.LegPxToM;
            fx.EmitChannel("leg_fire", at + Vector3.down * 0.4f + vel * Time.deltaTime, vel + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.9f, 1.7f), Random.Range(-0.2f, 0.2f)),
                Random.Range(0.35f, 0.55f), Random.Range(9f, 15f) * px * ShipBurnScale, Random.value < 0.5f ? OpenZFxCatalog.WreckFlameB : OpenZFxCatalog.WreckFlameC, 1f);
            ShipFlyingFlames++;
        }

        void Tidy()
        {
            var drop = new List<int>();
            foreach (var k in shipSmoke.Keys) if (!game.Views.ContainsKey(k)) drop.Add(k);
            foreach (var k in drop) shipSmoke.Remove(k);
        }
    }
}
