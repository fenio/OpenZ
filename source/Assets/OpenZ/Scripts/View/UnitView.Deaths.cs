using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>deaths_mech_v1 switches (all view only; the sim is never touched).</summary>
    public static class WreckFx
    {
        /// <summary></summary>
        public static bool Flips = true;
        /// <summary></summary>
        public static bool BreakAtDeath = true;
        /// <summary>DV18: every other wreck that did not flip burns out (Zod's 5-7 s EDeath) and then cooks off into parts: a dead
        /// vehicle is never left whole.</summary>
        public static bool CookOffs = true;
        /// <summary>DV18: a dead cannon's base falls apart when its turret flies (Zod: 2-4 s after the kill), one in two.</summary>
        public static bool CannonBreak = true;
        /// <summary>Test aid (OpenZ/Deaths menu): 0 = seeded chances, 1 = every vehicle flips / breaks, 2 = never (plain wrecks).</summary>
        public static int Force;
        /// <summary>After Zod's burn + spark fountain the wreck smoulders (thinning smoke) and fades (sinks) at this age.</summary>
        public static float SmoulderSeconds = 14f, FadeSeconds = 2.5f;
        /// <summary>Burnt paint: Charred ramps from Start to End (TeamPaint: 1 = the old flat black, 0 = clean paint).</summary>
        public static float CharStart = 0.66f, CharEnd = 0.82f;
        /// <summary>A burning vehicle wreck also shows a few of the burning ground's flame tongues (OpenZ/PyroTongues): 2-4 spots, one on
        /// the hull and the rest on the ground right beside it, on FlameWrecksOf3 of every 3 wrecks (seeded per unit: 3 = every
        /// wreck, 0 = none), for as long as the wreck burns. The puffs and the smoke stay as they are.</summary>
        public static bool GroundFlames = true;
        public static int FlameWrecksOf3 = 2;
        /// <summary>FlameDeck: the flame on the hull is rooted at this share of the hull's height (the hull hides what is under its deck,
        /// so the flame comes out of the deck; too high and its foot hangs over a low deck). FlameHeight / FlameWidth: the tongues'
        /// scale (the burning ground's own are .85 / .8).</summary>
        public static float FlameDeck = 0.7f, FlameHeight = 0.7f, FlameWidth = 0.7f;
        /// <summary>Proof: wrecks that got flames / wrecks the seeded chance left with puffs only.</summary>
        public static int FlameWrecks, FlameSkips;

        static PhysicsMaterial mat;
        /// <summary></summary>
        public static PhysicsMaterial Material => mat ? mat : (mat = new PhysicsMaterial("OZ_Wreck") { dynamicFriction = 0.9f, staticFriction = 0.9f, bounciness = 0.12f, bounceCombine = PhysicsMaterialCombine.Maximum });

        public static bool OnScreen(Vector3 p, float margin = 0.05f)
        {
            var cam = Camera.main; if (!cam) return true;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -margin && v.x < 1f + margin && v.y > -margin && v.y < 1f + margin;
        }

        internal static bool Chance(uint h, uint n) => Force == 1 || (Force == 0 && h % n == 0u);

        /// <summary>Zod EDeath::DoSparks (edeath.cpp): when a wreck has burnt 5-7 s, 40-70 sparks fly out (1.5-1.7 s, up to 180 px
        /// sideways, rising): a spark fountain. Also ECannonDeath (the wasted gun, 2-4 s) and map objects (20-40).</summary>
        public static void SparkFountain(Vector3 at, int count, float spread, uint seed)
        {
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled || !OnScreen(at, 0.2f)) return;
            uint s = seed | 1u;
            for (int i = 0; i < count; i++)
            {
                float a = Rnd(ref s) * 6.283f, h = Rnd(ref s) * spread;
                var v = new Vector3(Mathf.Cos(a) * h, 4.5f + Rnd(ref s) * 5f, Mathf.Sin(a) * h);
                var c = i % 3 == 0 ? new Color(1f, 0.93f, 0.62f) : new Color(1f, 0.6f, 0.22f);
                fxp.EmitChannel(i % 4 == 0 ? "m12_spark" : "leg_spark", at + new Vector3(Rnd(ref s) - 0.5f, Rnd(ref s) * 0.4f, Rnd(ref s) - 0.5f), v, 1.5f + Rnd(ref s) * 0.2f, 0.1f + Rnd(ref s) * 0.08f, c, 1f);
            }
            fxp.AddLight(at + Vector3.up, new Color(1f, 0.62f, 0.3f), 14f, 7f, 0.5f, 2f, 0.3f, false);
            OpenZHitSparks.Burst(at + Vector3.up * 0.8f, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 1.4f, false);
            CombatAudio.I?.PlayPitched(OpenZHitSparks.SoundKey, at, 0.5f, 0f, -1, 1f, OpenZHitSparks.Whine);
        }

        internal static float Rnd(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216f; }
    }

    public partial class UnitView
    {
        /// <summary>deaths_mech_v1: this robot died in a pop (its model is hidden; CombatView skips the ragdoll death dust + sound).</summary>
        public bool Popped { get; private set; }
        /// <summary>combat_nb_v1 NB3: this robot is dead in the sim and waits (pose held, not hidden) until CombatView throws it when the
        /// blast front reaches it (BlastWave). The hold is a safety cap; Die() ends it.</summary>
        public void HoldForDeath(float seconds) { if (death == Death.None) deathHoldUntil = Time.time + seconds; }
        float deathHoldUntil;
        /// <summary>deaths_mech_v1: knocked down by a blast (UnitAim leaves the torso alone, shots wait until it is up).</summary>
        public bool Reacting => reactKind == 3 && reactT >= 0f;

        /// <summary>Ragdoll impulse for ragdolls without the Zod flight (DeathMix.Shares bullets): scaled with the drawn robot's
        /// </summary>
        float RagdollStrength(bool missile, float proximity)
        {
            float size = Mathf.Sqrt(RobotHeight() / 1.8f);
            return missile ? 2.4f * size * Mathf.Clamp(proximity, 0.2f, 1.8f) : 1f * size;
        }
        /// <summary>Airborne fallback (ragdoll cap reached): Zod's flight time and apex instead of the old 1.3 s / 3.2 m hop.</summary>
        float airT = 1.3f, airH = 3.2f;
        float RobotHeight() => skin ? Mathf.Clamp(skin.bounds.max.y - transform.position.y, 1.2f, 4.5f) : 3.5f;

        // =============================================================================================== wrecks
        float charNow; int charStep; bool flipWreck, cookOff, breakDone, cannonToss, sparked, fading; float breakAt = -1f, tossAt = -1f, burnEnd = 6f, fadeT, fadeDepth;
        Rigidbody wreckBody; Vector3 fadeFrom;

        /// <summary>A vehicle or cannon died (replaces the flat Charred = 1 black clone). Zod first: tanks throw their turret at once
        /// (vlight.cpp:261-278, ETurrentMissile to the sim's debris point, 3-4 s); a cannon stays a burning "wasted" gun for 2-4 s
        /// (ecannondeath.cpp:28-29), then sparks and its turret / barrel flies for the rest of Zod's 7-10 s; a vehicle wreck burns
        /// 5-7 s (edeath.cpp:22, 3-5 little fires + big smoke) and ends in a spark fountain (EDeath::DoSparks, 40-70 sparks).
        /// OpenZ on top (DV17 / DV18, seeded per unit, never every time): a light vehicle flips up spinning (1 in 3); a vehicle
        /// explodes into its parts at once (1 in 3 of the rest) or cooks off into parts when it has burnt out (1 in 3); a cannon's
        /// base falls apart when its barrel flies (1 in 2). What stays is a partly burnt wreck that smoulders and then fades (sinks),
        /// never a flat black clone.</summary>
        void BeginWreck()
        {
            death = Death.Wreck; weight = 0f; smokeT = 0f;
            charNow = Mathf.Max(WreckFx.CharStart, paint ? paint.Charred : 0f); charStep = 0;   // pyro_fire_v2 review: a hull the fire charred (up to .62) never flashes lighter as it dies
            if (paint) paint.Charred = charNow;
            uint h = RobotDeathRule.Hash(Obj.Id, 0, 11u);
            burnEnd = 5f + h % 3u;                                                                       // Zod: 5 + rand() % 3 s
            bool light = Obj.Kind == ZKind.Vehicle && (Obj.Type == "jeep" || Obj.Type == "apc" || Obj.Type == "crane");
            flipWreck = WreckFx.Flips && light && WreckFx.Chance(h >> 2, 3u);
            if (Obj.Kind == ZKind.Cannon)
            {
                cannonToss = turret != null;
                tossAt = 2f + (h >> 8) % 201u / 100f;                                                    // Zod: 2 + rand() % 3 s
                if (WreckFx.CannonBreak && !OnTower && WreckFx.Chance(h >> 5, 2u)) breakAt = tossAt;     // tower guns stay on their pad
            }
            else
            {
                if (IsTank && turret) TossTurret();
                if (!flipWreck && WreckFx.BreakAtDeath && WreckFx.Chance(h >> 4, 3u)) breakAt = 0.05f;                              // explodes into parts now
                else if (!flipWreck && WreckFx.CookOffs && WreckFx.Force != 2) { cookOff = true; breakAt = burnEnd; }   // burns out, then cooks off
            }
            if (WheeledBreakup.Applies(Obj)) WheeledBreakup.Tear(this, transform.position + Vector3.up * 1f, flipWreck);
            if (flipWreck) StartFlip(h);
            OzTankWreck.Want(this);
            OzWaterLanding.Watch(transform, "wreck hull", wreckBody, true, wreckBody == null);        // b27: a wreck that falls into water (flipped into the river, or its bridge went) splashes and rests partly under the surface
        }

        /// <summary>A dead tower gun keeps its pad only while the tower stands. When its fort has collapsed (the tower tops are thrown
        /// with the upper model), the gun goes with it at once: its turret flies and its base, the plate with the turret ring, breaks
        /// into parts that fall to the ground. Without this the base stayed in the air over the broken tower until its smoulder ended.</summary>
        bool towerFell;
        bool TowerCollapsed() => towerState > 0 && towerFort && towerFort.TryGetComponent<BuildingBreaker>(out var bb) && bb.Collapsed;
        /// <summary>Play check: dead tower guns thrown down with their collapsed fort.</summary>
        public static int TowerGunsFell;

        void UpdateWreck(float dt)
        {
            UpdateToss(dt);
            if (!towerFell && Obj.Kind == ZKind.Cannon && TowerCollapsed())
            {
                towerFell = true; TowerGunsFell++;
                if (fading) { Hide(); return; }                                                         // already sinking into its pad: gone with the tower top
                if (!breakDone) { if (tossAt >= 0f) tossAt = deathT; breakAt = deathT; }                // turret and base go now (below)
            }
            if (fading) { UpdateFade(dt); return; }
            if (tossAt >= 0f && deathT >= tossAt)
            {
                tossAt = -1f;
                if (cannonToss && turret)
                {   // Zod ECannonDeath::Process -> DoSparks + ETurrentMissile: sparks, a whoosh, the turret flies to the sim's debris point
                    var tp = turret.position;
                    WreckFx.SparkFountain(tp, 24, 3f, RobotDeathRule.Hash(Obj.Id, 1, 12u));
                    AudioDirector.Fire("dst_turret_toss", tp, Obj.Id);
                    GunChairDebris.BreakOff(turret, Obj.Type, Obj.Id);
                    TossTurret();
                }
            }
            if (breakAt >= 0f && !breakDone && deathT >= breakAt && !OzTankWreck.Hold(this, deathT - breakAt)) { breakDone = true; StopWreckFire(0.8f); BreakApart(cookOff); }   // item 378: waits for its break-up (at most 2 s)
            if (towerFell && !breakDone)
            {   // nothing that could break into parts: what is left of the gun is not drawn any more (never left in the air)
                breakDone = true; breakAt = deathT;
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
            if (!sparked && !breakDone && deathT >= burnEnd && Obj.Kind == ZKind.Vehicle)
            {   // Zod EDeath::DoSparks: the burnt-out wreck throws its spark fountain
                sparked = true;
                WreckFx.SparkFountain(transform.position + Vector3.up * 0.8f, 40 + (int)(RobotDeathRule.Hash(Obj.Id, 2, 13u) % 31u), 6f, RobotDeathRule.Hash(Obj.Id, 3, 14u));
            }
            if (!breakDone && charStep < 3 && deathT > 1.3f * (charStep + 1))
            {   // the paint keeps burning: three steps (TeamPaint.Apply walks every renderer, so not every frame)
                charStep++; charNow = Mathf.Max(charNow, Mathf.Lerp(WreckFx.CharStart, WreckFx.CharEnd, charStep / 3f));   // pyro_fire_v2 review: never lighter than the fire left it
                if (paint) paint.Charred = charNow;
            }
            UpdateWreckFlames();                                                                        // a few of the burning ground's flame tongues on and beside the hull while it burns
            smokeT -= dt;
            float smokeEnd = WreckFx.SmoulderSeconds;
            if (!breakDone && smokeT <= 0f && deathT < smokeEnd)
            {   // Zod's burn (little fires, big smoke, flickering light) until burnEnd, then a thinning smoulder
                bool burning = deathT < burnEnd;
                float k = burning ? 1f : Mathf.Lerp(1f, 0.3f, (deathT - burnEnd) / Mathf.Max(0.1f, smokeEnd - burnEnd));
                smokeT = burning ? 0.25f : Mathf.Lerp(0.35f, 0.8f, 1f - k);
                var at = transform.position + Vector3.up * (flipWreck ? 0.7f : 1.2f);
                var fxp = OpenZFxPlayer.I;
                // the wreck burns, faded out when the burn ends, the wreck breaks or fades
                var adf = AudioDirector.I;
                if (float.IsNaN(burnTop)) burnTop = Mathf.Clamp(LocalBounds().max.y * transform.lossyScale.y, 0.8f, 3.2f);   // the hull top, once
                var fireAt = transform.position + Vector3.up * (flipWreck ? Mathf.Min(0.9f, burnTop) : burnTop + 0.1f);
                // the burning wreck takes the approved pyro sticky fire (flame licks on its own hull surface, fire light, dark smoke, heat
                // shimmer, the nearest-burn dst_fire_loop), lit once when the burn starts; it ends after the pyro's BurnSeconds (5 s,
                // Zod's 5-7 s burn). Without the pyro fire the wreck keeps its own fire loop as before.
                if (burning && !wreckPyroLit) { wreckPyroLit = true; wreckPyro = OpenZPyroFire.KeepBurning(Obj, this, gameObject, false, fireAt); }
                if (burning && !fireLoopOn && (!wreckPyro || !OpenZPyroFire.FullBurning(Obj.Id)) && adf && AudioDirector.Has("dst_fire_loop")) { adf.StartLoop("wreck_fire_" + Obj.Id, "dst_fire_loop", at, null, 0.6f); fireLoopOn = true; }   // also the last seconds of a 6-7 s burn, after the pyro fire's own 5 s crackle has ended
                else if (!burning && fireLoopOn) StopWreckFire(1.5f);
                if (fxp && fxp.isActiveAndEnabled) OzFireDesync.VehicleBurn(fxp, Obj.Id, deathT, ref smokeT, at, k, burning, 0.7f, fireAt);
                else if (FxManager.I) FxManager.I.Dust(at, 0.35f * k);
            }
            bool tossDone = !tossed || (tossLanded && tossT > tossDur + 3f);
            if (breakDone && tossDone && deathT > breakAt + 0.5f) { BeginFade(); return; }              // the pieces live on; a landed turret sinks away
            if (deathT > WreckFx.SmoulderSeconds && tossDone) BeginFade();
        }

        /// <summary>
        /// height) over FadeSeconds with a last puff of dust, then hides; a landed turret sinks with it.</summary>
        bool fireLoopOn, wreckPyroLit, wreckPyro; float burnTop = float.NaN;

        // ---- a few of the burning ground's flame tongues on and beside the burning wreck (WreckFx.GroundFlames)
        struct WreckFlame { public Vector3 p; public bool onHull; public float seed, size, speed; }
        WreckFlame[] wreckFlames; int wreckFlameN = -1; float wreckFlameT0; Vector3 wreckFlameAt;

        /// <summary>Every frame of a vehicle wreck's burn. The spots are chosen once (after the death blast; a flipped wreck when it lies
        /// still): one on the hull when it stands upright, the rest on the ground on the edge of its footprint, none over water.
        /// Each frame their tongues are handed to the pyro ground fire, which keeps nothing: when this wreck has burnt out, breaks
        /// apart, sinks, is hidden or destroyed, its flames are gone with it.</summary>
        void UpdateWreckFlames()
        {
            if (wreckFlameN == 0 || breakDone || !WreckFx.GroundFlames || Obj.Kind != ZKind.Vehicle || !OpenZPyroFire.TonguesReady) return;
            float left = burnEnd - deathT; if (left <= 0f) return;
            bool moving = wreckBody && !wreckBody.isKinematic && !wreckBody.IsSleeping() && (wreckBody.linearVelocity.sqrMagnitude > 0.09f || wreckBody.angularVelocity.sqrMagnitude > 0.25f);
            if (wreckFlameN > 0 && (moving || (transform.position - wreckFlameAt).sqrMagnitude > 0.25f)) wreckFlameN = -1;   // knocked on: lit again where it comes to rest
            if (wreckFlameN < 0)
            {
                if (deathT < 0.6f || moving || left < 1.5f) return;
                uint h = RobotDeathRule.Hash(Obj.Id, 4, 15u), s = (h >> 4) | 1u;
                if (h % 3u >= (uint)Mathf.Clamp(WreckFx.FlameWrecksOf3, 0, 3)) { wreckFlameN = 0; WreckFx.FlameSkips++; return; }   // here and there, not every wreck
                var lb = LocalBounds(true); var e = lb.extents; var ls = transform.lossyScale;             // the hull meshes still on the wreck (a tank's turret has flown)
                Vector3 ax = transform.right, ay = transform.up, az = transform.forward, cw = transform.TransformPoint(lb.center);
                // the two hull axes that lie along the ground span its footprint (a flipped wreck rests on its roof or on a side)
                Vector3 u = ax, v = az; float eu = e.x * Mathf.Abs(ls.x), ev = e.z * Mathf.Abs(ls.z), ey = e.y * Mathf.Abs(ls.y);
                if (Mathf.Abs(ax.y) > Mathf.Abs(ay.y) && Mathf.Abs(ax.y) >= Mathf.Abs(az.y)) { u = ay; eu = ey; }
                else if (Mathf.Abs(az.y) > Mathf.Abs(ay.y)) { v = ay; ev = ey; }
                bool upright = ay.y > 0.85f;
                int want = 2 + (int)((h >> 8) % 3u), n = 0;                                                 // 2-4 spots
                if (wreckFlames == null) { wreckFlames = new WreckFlame[4]; WreckFx.FlameWrecks++; }
                for (int i = 0; i < want; i++)
                {
                    float r0 = WreckFx.Rnd(ref s), r1 = WreckFx.Rnd(ref s), r2 = WreckFx.Rnd(ref s), r3 = WreckFx.Rnd(ref s);
                    var f = new WreckFlame { seed = r0, size = 0.8f + r1 * 0.5f, speed = 0.5f + r2 * 0.6f };
                    if (i == 0 && upright)
                    {   // on the hull: the middle of the deck, rooted inside the hull so the deck itself cuts the flame's foot (root-local: it rides the wreck)
                        f.onHull = true;
                        f.seed = (Mathf.Floor(r0 * 13f) + 0.5f + 0.5f * r3) / 13.73f;                       // the taller half of the tongue heights (the shader: frac(seed x 13.73))
                        f.p = new Vector3(lb.center.x + (r1 * 2f - 1f) * e.x * 0.3f, lb.min.y + lb.size.y * WreckFx.FlameDeck, lb.center.z + (r2 * 2f - 1f) * e.z * 0.3f);
                    }
                    else
                    {   // beside the hull: on the ground, on the edge of the footprint and 0.15-0.45 m out
                        float a = r3 * 6.2832f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        float d = Mathf.Min(eu / Mathf.Max(0.05f, Mathf.Abs(ca)), ev / Mathf.Max(0.05f, Mathf.Abs(sa))) + 0.15f + 0.3f * r1;
                        var w = cw + u * (ca * d) + v * (sa * d);
                        if (!OpenZPyroFire.DrySpot(w.x, w.z, out var g)) continue;                            // open water: no flame there
                        f.p = g + Vector3.up * 0.06f;
                    }
                    wreckFlames[n++] = f;
                }
                wreckFlameN = n; wreckFlameT0 = deathT; wreckFlameAt = transform.position;
                if (n == 0) return;
            }
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Min((deathT - wreckFlameT0) / 0.5f, left / 1f));    // in over half a second, out over the last second of the burn
            for (int i = 0; i < wreckFlameN; i++)
            {
                var f = wreckFlames[i];
                var root = f.onHull ? transform.TransformPoint(f.p) : f.p;
                float a2 = f.seed * 40f;
                OpenZPyroFire.Tongue(root, f.seed, f.size, f.speed, k, WreckFx.FlameHeight, WreckFx.FlameWidth);
                // its smaller neighbour a hand away: a spot reads as a small fire, not as one blade
                OpenZPyroFire.Tongue(root + new Vector3(Mathf.Cos(a2) * 0.22f, 0f, Mathf.Sin(a2) * 0.22f), Mathf.Repeat(f.seed + 0.37f, 1f), f.size * 0.75f, f.speed * 1.25f, k, WreckFx.FlameHeight * 0.75f, WreckFx.FlameWidth);
            }
        }
        void StopWreckFire(float fade)
        {
            if (!fireLoopOn) return;
            fireLoopOn = false;
            if (AudioDirector.I) AudioDirector.I.StopLoop("wreck_fire_" + Obj.Id, fade);
        }

        void BeginFade()
        {
            StopWreckFire(1f);
            fading = true; fadeT = 0f; fadeFrom = transform.position; fadeDepth = RockFadeDepth(Mathf.Max(0.6f, LocalBounds().size.y * 0.8f));   // items 331 + 332: a flipped hull on a rock pops away instead
            if (wreckBody) wreckBody.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            FxManager.I?.Dust(transform.position + Vector3.up * 0.3f, 0.5f);
        }

        void UpdateFade(float dt)
        {
            fadeT += dt; float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fadeT / WreckFx.FadeSeconds));
            transform.position = fadeFrom + Vector3.down * fadeDepth * k;
            if (tossed) tossed.position += Vector3.down * 0.5f * dt;
            if (fadeT >= WreckFx.FadeSeconds) Hide();
        }

        /// <summary>
        /// cloud and a small camera kick. The sim's debris missile explodes here in the same frame (40 HP, r 40 px, NULL team).</summary>
        void TossLanded(Vector3 p)
        {
            if (!WreckFx.OnScreen(p)) return;
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled) { fxp.Dust(p, 1.4f); fxp.DemolitionDust(p, 2.2f, 2.2f, 0.45f); }
            else FxManager.I?.Dust(p, 1.2f);
            var au = CombatAudio.I;
            if (au)
            {
                au.Play("turret", p, 1f, 0, Obj.Id, OzSoundV3.BankMetalHeavy);   // sound_v3: 8 takes
                au.PlayPitched("tank_impact", p, 0.55f, 0f, Obj.Id, 0.85f, LegacyMatch.BankTankImpact);
            }
        }

        // ---------------------------------------------------------------------------------- flip (DV17)
        /// <summary>
        /// get up 3.8 + 1.5 m/s, spin (sin a 4.3, 1.4 cos a, -cos a 4.3) rad/s, restitution .12, damping .28 / .95). Here the whole
        /// wreck takes that launch with a stronger kick (6.5-8.5 m/s up) so it goes up spinning, then crashes, bounces and burns.</summary>
        void StartFlip(uint h)
        {
            var lb = LocalBounds();
            var hull = new GameObject("OZ_WreckHull") { layer = BuildingBreaker.DebrisLayer };
            hull.transform.SetParent(transform, false);
            var bc = hull.AddComponent<BoxCollider>();
            bc.center = lb.center; bc.size = Vector3.Max(lb.size * 0.9f, Vector3.one * 0.3f); bc.sharedMaterial = WreckFx.Material;
            wreckBody = gameObject.AddComponent<Rigidbody>();
            wreckBody.mass = Mathf.Clamp(lb.size.x * lb.size.y * lb.size.z * 60f, 400f, 4000f);
            wreckBody.linearDamping = 0.28f; wreckBody.angularDamping = 0.95f; wreckBody.maxAngularVelocity = 14f;
            wreckBody.interpolation = RigidbodyInterpolation.Interpolate; wreckBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            float a = (h >> 3) % 6283u / 1000f, u = ((h >> 13) & 1023u) / 1023f;
            var away = new Vector3(Mathf.Cos(a * 1.7f), 0f, Mathf.Sin(a * 1.7f));
            wreckBody.linearVelocity = away * Mathf.Lerp(1.2f, 2.4f, u) + Vector3.up * Mathf.Lerp(6.5f, 8.5f, 1f - u);
            wreckBody.angularVelocity = new Vector3(Mathf.Sin(a) * 4.3f, 1.4f * Mathf.Cos(a), -Mathf.Cos(a) * 4.3f) * Mathf.Lerp(0.9f, 1.3f, u);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            gameObject.AddComponent<WreckContact>().Init(Obj.Id, true);
            RockFlip(bc);                                                                                          // items 331 + 332: it meets the rocks (UnitView.Rocks.cs)
        }

        Bounds LocalBounds(bool hullOnly = false)
        {
            bool any = false; var b = new Bounds(Vector3.zero, Vector3.zero);
            var inv = transform.worldToLocalMatrix;
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || !mf.TryGetComponent<MeshRenderer>(out var mr) || !mr.enabled) continue;
                if (hullOnly)
                {   // the footprint of the hull itself: no shadow / label quads, light cones, glows or decals (WreckPieces.Break sorts the same ones out)
                    var nm = mr.name.ToLowerInvariant();
                    if (nm.Contains("shadow") || nm.Contains("label") || (mr.sharedMaterial && mr.sharedMaterial.renderQueue >= 2450)) continue;
                }
                var mb = mf.sharedMesh.bounds; var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            return any ? b : new Bounds(Vector3.up, new Vector3(2f, 1.5f, 3f));
        }

        // ---------------------------------------------------------------------------------- break apart (DV18)
        void BreakApart(bool cook)
        {
            var c = transform.position + Vector3.up * 1f;
            if (cook)
            {   // burn-out cook-off: a small blast inside the wreck
                if (FxManager.I) FxManager.I.Explosion(c, 0.8f);
                CombatAudio.I?.PlayPitched("explosion", c, 0.6f, 0f, Obj.Id, 1.05f, "explosion-v1-20260909", "explosion-v2-20260909", "explosion-v3-20260909");
            }
            else OpenZHitSparks.Burst(c, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 1.6f, false);
            if (wreckBody) { Destroy(wreckBody); wreckBody = null; }
            ApcDome.BreakOff(this, c, cook ? 0.55f : 1f, Obj.Id); ApcRearDoor.BreakOff(this, c, cook ? 0.55f : 1f, Obj.Id);
            int n = WreckPieces.Break(this, tossed, c, cook ? 0.55f : 1f, Obj.Id, charNow);
            if (n == 0) { breakDone = false; breakAt = -1f; }                                          // nothing to break: stays a wreck
            else if (ApcCraneKit.HullStays(this)) { breakDone = false; breakAt = -1f; }
        }

        // =============================================================================================== hit reactions (DV19)
        int reactKind; float reactT, reactDur, flyEnd, lieEnd, upEnd, reactPush, reactTilt, reactFront, reactArc;
        Vector3 reactAnchor, reactLand, reactDir;
        readonly List<ZEvent> reactHeld = new List<ZEvent>();
        static CombatView combatView;

        /// <summary>kind 1 flinch, 2 stagger, 3 knockdown. blast = the explosion centre (HitReactions.SafeFrom: never toward the shooter);
        /// seed = per robot and blast (no squad in sync). hold (DV51) = real seconds until the sim lets this robot fire again (its
        /// KnockUntil): the knockdown's fall / lie / get-up are stretched so it stands just before then; &lt;= 0 = the view's own timing.</summary>
        public void React(Vector3 blast, int kind, uint seed, float delay, float hold = -1f, float pushScale = 1f)   // DV65: pushScale (a run-over topples short, in the vehicle's path)
        {
            if (Obj == null || Obj.Kind != ZKind.Robot || death != Death.None || !isActiveAndEnabled || OpenZEnding.Holds(Obj.Id)) return;
            if (reactKind == 3 && reactT < upEnd) return;                                              // already down
            if (kind < reactKind && reactT >= 0f && reactT < reactDur) return;                            // a stronger reaction is playing
            if (kind == 3 && hold <= 0f && HitReactions.DownCount() >= HitReactions.MaxDown) kind = 2;   // DV51: the sim caps it (ZWorld.KnockMaxDown)
            if (kind == 3 && fdMan) kind = 2;                                                            // fort_defender_v1 DV41: a stagger in the nest, never a fall off the deck
            uint s = seed | 1u; float R() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216f; }
            float hs = 1f;
            if (skin) hs = Mathf.Clamp(skin.bounds.max.y - transform.position.y, 1.2f, 4.5f) / 3.5f;
            reactDir = transform.position - blast; reactDir.y = 0f;
            if (reactDir.sqrMagnitude < 0.01f) { float a = R() * 6.283f; reactDir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            reactDir.Normalize();
            reactKind = kind; reactT = -delay; reactAnchor = transform.position;
            reactFront = Mathf.Atan2(-reactDir.x, -reactDir.z) * Mathf.Rad2Deg;                         // faces the blast, falls away from it
            if (kind == 3)
            {
                flyEnd = Mathf.Lerp(0.28f, 0.4f, R());
                lieEnd = flyEnd + Mathf.Lerp(0.55f, 0.95f, R());                                          // fall + lie
                upEnd = lieEnd + Mathf.Lerp(0.55f, 0.8f, R());                                            // scramble up
                if (hold > 0f)
                {
                    float want = Mathf.Max(0.8f, hold - delay - 0.12f - HitReactions.KnockMargin);
                    float lie0 = lieEnd - flyEnd, up0 = upEnd - lieEnd;
                    flyEnd = Mathf.Min(flyEnd * Mathf.Clamp(want / upEnd, 0.6f, 1.25f), want * 0.3f);    // the blast arc stays snappy
                    lieEnd = flyEnd + (want - flyEnd) * lie0 / Mathf.Max(0.01f, lie0 + up0);                 // lie and get-up share the rest
                    upEnd = want;
                }
                reactDur = upEnd + 0.12f;
                reactArc = Mathf.Lerp(0.5f, 0.9f, R()) * hs;
                reactPush = ClearPush(reactAnchor, reactDir, Mathf.Lerp(1.1f, 2f, R()) * hs * pushScale);
                reactLand = reactAnchor + reactDir * reactPush; reactLand.y = game.GroundY(reactLand.x, reactLand.z) + yOffset;
            }
            else
            {
                reactDur = kind == 2 ? Mathf.Lerp(0.45f, 0.62f, R()) : Mathf.Lerp(0.2f, 0.28f, R());
                reactPush = ClearPush(reactAnchor, reactDir, (kind == 2 ? Mathf.Lerp(0.35f, 0.6f, R()) : Mathf.Lerp(0.04f, 0.09f, R())) * hs);
                reactTilt = kind == 2 ? Mathf.Lerp(14f, 22f, R()) : Mathf.Lerp(3f, 5.5f, R());
            }
            OzSoundV3.Reacted(this, kind, delay, flyEnd, lieEnd);                                       // sound_v3
            OzDeathTrace.Reacted(this, kind);
        }

        /// <summary>The push never leaves a walkable robot tile (Zod's robot grid: rocks, cliffs, buildings, walls) and never climbs
        /// or drops more than 0.9 m: shortened in 0.25 m steps, down to none. Nobody ends up inside a wall or off the path.</summary>
        float ClearPush(Vector3 from, Vector3 dir, float want)
        {
            var path = game.World.Path; float y0 = game.GroundY(from.x, from.z), ok = 0f;
            for (float d = 0.25f; d <= want + 1e-3f; d += 0.25f)
            {
                var p = from + dir * d; var px = ZGame.WorldToPx(p);
                if (!path.TilePassable(px.x >> 4, px.y >> 4, true)) break;
                if (Mathf.Abs(game.GroundY(p.x, p.z) - y0) > 0.9f) break;
                ok = d;
            }
            return Mathf.Min(ok, want);
        }

        /// <summary>After Sync placed the robot at its live (sim) position: the reaction offsets the drawn body only.</summary>
        void ApplyReaction(float dt)
        {
            reactT += dt;
            if (reactT < 0f) return;
            if (reactT >= reactDur) { EndReaction(); return; }
            var live = transform.position; var liveRot = transform.rotation;
            if (reactKind == 3)
            {
                var face = Quaternion.Euler(0, reactFront + modelYaw, 0);
                if (reactT < flyEnd)
                {   // blown back off its feet in a short arc
                    float k = reactT / flyEnd;
                    var p = Vector3.Lerp(reactAnchor, reactLand, k); p.y = Mathf.Lerp(reactAnchor.y, reactLand.y, k) + Mathf.Sin(k * Mathf.PI) * reactArc;
                    transform.SetPositionAndRotation(p, face);
                }
                else if (reactT < lieEnd) transform.SetPositionAndRotation(reactLand, face);                // on the ground
                else
                {   // scrambling back up: blends to where the sim has the robot now (the small push goes back, nobody desyncs)
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((reactT - lieEnd) / Mathf.Max(0.05f, upEnd - lieEnd)));
                    var p = Vector3.Lerp(reactLand, live, k); p.y = Mathf.Max(p.y, game.GroundY(p.x, p.z) + yOffset);
                    transform.SetPositionAndRotation(p, Quaternion.Slerp(face, liveRot, k));
                }
                return;
            }
            float t = Mathf.Clamp01(reactT / reactDur), env = Mathf.Sin(t * Mathf.PI) * (1f - 0.35f * t);
            var q = live + reactDir * reactPush * env; q.y = Mathf.Max(q.y, game.GroundY(q.x, q.z) + yOffset);
            transform.SetPositionAndRotation(q, Quaternion.AngleAxis(reactTilt * env, Vector3.Cross(Vector3.up, reactDir)) * liveRot);   // leans away from the blast
        }

        /// <summary>Knockdown pose: blast_airborne while thrown, death_back to fall and lie, then death_back scrubbed backwards as the
        /// scramble up (the controllers have no get-up clip); true = the normal state machine waits.</summary>
        bool ReactAnim(float dt)
        {
            if (reactKind != 3 || reactT < 0f || !anim) return false;
            if (hold) hold.Suspended = true;
            if (anim.layerCount > 1) { upWeight = 0f; anim.SetLayerWeight(1, 0f); upState = null; }
            if (reactT < flyEnd) { if (state != "blast_airborne") { anim.speed = 1f; anim.CrossFadeInFixedTime("blast_airborne", 0.05f, 0, 0f); state = "blast_airborne"; } }
            else if (reactT < lieEnd) { if (state != "death_back") { anim.speed = 1.2f * animMul; anim.CrossFadeInFixedTime("death_back", 0.08f, 0, 0.15f); state = "death_back"; } }
            else
            {
                float k = Mathf.Clamp01((reactT - lieEnd) / Mathf.Max(0.05f, upEnd - lieEnd));
                anim.speed = 0f; anim.Play("death_back", 0, Mathf.Lerp(0.95f, 0.02f, Mathf.SmoothStep(0f, 1f, k))); state = "react_getup";
            }
            return true;
        }

        void EndReaction()
        {
            bool wasDown = reactKind == 3;
            reactKind = 0; reactT = 0f;
            if (wasDown && anim) { anim.speed = animMul; state = null; upState = null; if (hold) hold.Suspended = false; }
            if (reactHeld.Count > 0)
            {   // the shots the sim fired while it was down leave the gun now
                if (!combatView) combatView = FindFirstObjectByType<CombatView>();
                var list = new List<ZEvent>(reactHeld); reactHeld.Clear();
                if (combatView && death == Death.None) foreach (var e in list) combatView.DrawHeld(e);
            }
        }

        /// <summary>CombatView.DrawShot: a hitscan shot of a knocked-down robot waits until it is up (&lt;= 1.9 s; Zod's damage is already
        /// on the sim). A killing shot cannot wait (its death plays now): the robot scrambles up at once and fires.</summary>
        public bool HoldShotWhileDown(ZEvent e, ZObject target)
        {
            if (reactKind != 3 || reactT < 0f || reactT >= upEnd - 0.05f) return false;
            if (target == null || target.Destroyed) { QuickGetUp(); return false; }
            if (reactHeld.Count < 3) reactHeld.Add(e);
            return true;
        }

        /// <summary>CombatView.MissileFired: a rocket / grenade must leave now to land on the sim's blast: up fast, then it fires.</summary>
        /// <summary></summary>
        public bool KnockedDown => reactKind == 3 && reactT >= 0f && reactT < upEnd;

        public void ShotWhileDown() { if (reactKind == 3 && reactT >= 0f && reactT < upEnd) QuickGetUp(); }

        void QuickGetUp()
        {
            if (reactT < lieEnd) { lieEnd = Mathf.Max(reactT, flyEnd * 0.5f); flyEnd = Mathf.Min(flyEnd, lieEnd); }
            upEnd = Mathf.Min(upEnd, Mathf.Max(reactT, lieEnd) + 0.22f); reactDur = upEnd + 0.05f;
        }
    }

    /// <summary>
    /// <see cref="OnBlast"/> for every sim MissileExploded; every robot the sim's splash reached (ZWorld.ProcessMissileDamage: centre
    /// within the radius) and that is still alive reacts: a rocket / tank / cannon / howitzer / MLRS shell within 75 % of its radius
    /// knocks it down (blown up to 2 m, lies, scrambles up), farther out it staggers; a grenade staggers (inner 45 %) or flinches; a
    /// turret-debris landing flinches; every unit the splash reached (robots, vehicles, cannons) throws a few shrapnel chips. Seeded per robot and blast (delay 0-70 ms, durations +-20 %), at most <see cref="MaxDown"/> down
    /// at once. The sim position is never written: the drawn body blends back to it while getting up.
    /// </summary>
    public static class HitReactions
    {
        public static bool Enabled = true;
        /// <summary>
        /// splash reached (at most 6 units per blast).</summary>
        public static bool Shrapnel = true;
        public static int MaxDown = 24;
        /// <summary>
        /// chip size 15-30 cm (about 6-12 px at the h 55-70 game camera, still small at 22 m) and a slightly bigger spark burst.</summary>
        public static float ChipMin = 0.15f, ChipVar = 0.15f, ChipSpark = 1.0f;

        static void Chips(UnitView v, Vector3 blast, float frac)
        {
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled) return;
            var p = v.AimPos(); if (!WreckFx.OnScreen(p)) return;
            var away = p - blast; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            uint s = RobotDeathRule.Hash(v.Obj.Id, Time.frameCount, 41u) | 1u;
            int n = 2 + (int)(s % 4u);
            for (int i = 0; i < n; i++)
            {
                float a = WreckFx.Rnd(ref s) * 6.283f, sp = 2f + WreckFx.Rnd(ref s) * 4f * (1.2f - frac);
                var vel = away * sp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.5f + Vector3.up * (3f + WreckFx.Rnd(ref s) * 4f);
                fxp.EmitChannel("m12_metal", p + Random.insideUnitSphere * 0.4f, vel, 2.2f + WreckFx.Rnd(ref s), ChipMin + WreckFx.Rnd(ref s) * ChipVar, Color.white, 1f);
            }
            var bp = HitSurface.OnBody(v.transform, blast + Vector3.up * 0.8f, p, out var hp, out _) ? hp : p;   // IM1: the burst on the side facing the blast
            OpenZHitSparks.Burst(bp, -away + Vector3.down * 0.5f, FxWeapon.Rifle, FxSurface.Metal, ChipSpark, false);
            OzSoundV3.Shrapnel(bp);                                                                      // sound_v3
        }
        public static float KnockFrac = 0.75f;
        /// <summary>Test aid: blasts without an attacker count as heavy until this Time.time.</summary>
        public static float ForceHeavyUntil = -1f;
        static readonly HashSet<string> Heavy = new HashSet<string> { "tough", "light", "medium", "heavy", "howitzer", "gun", "missile_cannon", "missile_launcher" };

        public static int DownCount()
        {
            int n = 0; var g = ZGame.I; if (!g) return 0;
            foreach (var v in g.Views.Values) if (v && v.Reacting) n++;
            return n;
        }

        public static void OnBlast(ZGame game, ZEvent e, ZObject attacker, Vector3 at)
        {
            if (!Enabled || game == null || e.radius <= 0) return;
            bool debris = e.flag2, grenade = e.flag;
            string type = attacker == null ? null : attacker.IsFort ? "howitzer" : attacker.Type;
            bool heavy = !debris && !grenade && ((type != null && Heavy.Contains(type)) || (attacker == null && Time.time < ForceHeavyUntil));
            int chips = 0;
            foreach (var v in game.Views.Values)
            {
                if (!v || !v.isActiveAndEnabled || v.Dying) continue;
                var o = v.Obj; if (o == null || !o.IsUnit || o.Destroyed || o.KillAt >= 0) continue;
                int dx = e.x - o.CenterX, dy = e.y - o.CenterY;
                if (System.Math.Abs(dx) > e.radius || System.Math.Abs(dy) > e.radius) continue;
                float frac = Mathf.Sqrt(dx * dx + dy * dy) / e.radius;
                if (frac >= 1f) continue;                                                                 // the sim's splash did not reach it
                if (OzShuttleAim.AirBurstSkips(e, v)) continue;                                           // DV118: a blast at the flying shuttle does not reach the ground under it
                if (Shrapnel && chips < 6 && !debris) { chips++; Chips(v, at, frac); }
                if (o.Kind != ZKind.Robot) continue;
                int kind = debris ? 1 : heavy ? (frac < KnockFrac ? 3 : 2) : grenade ? (frac < 0.45f ? 2 : 1) : 2;
                float hold = -1f;
                if (ZWorld.KnockHoldDeviation)
                {
                    if (o.KnockUntil > game.World.Time + 1e-6) { kind = 3; hold = (float)(o.KnockUntil - game.World.Time) / Mathf.Max(0.05f, game.gameSpeed); }
                    else kind = 1;
                }
                uint seed = RobotDeathRule.Hash(o.Id, e.x * 31 + e.y, 5u);
                var p = v.transform.position; var from = at;
                bool has = ShooterPos(game, attacker, out var sp);
                if (has) from = SafeFrom(p, at, sp);
                ImpulseChecked?.Invoke(o, "react" + kind, p, at, from, has ? sp : at);
                v.React(from, kind, seed, BlastWave.SurvivorDelay(Mathf.Sqrt(dx * dx + dy * dy) / 8f) + (seed & 1023u) / 1023f * 0.05f, hold);   // combat_nb_v1 NB3: from the distance to the blast
            }
        }

        /// <summary>DV51: the get-up ends this long before the sim's KnockUntil (a hair of margin for frame timing).</summary>
        public const float KnockMargin = 0.06f;

        /// <summary>
        /// Every death ragdoll / pop / flight and every knockdown
        /// is thrown from a point (victim - from = the push); this keeps that push AWAY from the shooter: when the push from the blast
        /// centre points less than MinAwayDot along shooter -> victim (the shell burst beyond or beside the victim), the push becomes the
        /// shooter -> victim direction plus a little of the blast's sideways part (spread), so it never points at the attacker. A blast
        /// in front of the victim keeps its natural push. Same distance, so the proximity strength is unchanged.</summary>
        public const float MinAwayDot = 0.35f, SideKeep = 0.6f;
        /// <summary>Traces (proof harness): victim, what (react1..3 / death), victim pos, raw from, safe from, shooter pos.</summary>
        public static event System.Action<ZObject, string, Vector3, Vector3, Vector3, Vector3> ImpulseChecked;
        public static void NoteImpulse(ZObject o, string what, Vector3 p, Vector3 raw, Vector3 safe, Vector3 shooter) => ImpulseChecked?.Invoke(o, what, p, raw, safe, shooter);

        /// <summary>The shooter's drawn position (its view, else the sim's centre); false = no shooter known.</summary>
        public static bool ShooterPos(ZGame game, ZObject a, out Vector3 p)
        {
            p = default;
            if (game == null || a == null) return false;
            p = game.Views.TryGetValue(a.Id, out var v) && v ? v.transform.position : game.CenterWorld(a);
            return true;
        }

        /// <summary>The point to throw `victim` from so the push never points toward `shooter` (see MinAwayDot).</summary>
        public static Vector3 SafeFrom(Vector3 victim, Vector3 from, Vector3 shooter)
        {
            var s = victim - shooter; s.y = 0f;
            if (s.sqrMagnitude < 0.04f) return from;                                                    // the shooter stands on the victim: keep
            s.Normalize();
            var d = victim - from; d.y = 0f; float m = d.magnitude;
            Vector3 dir;
            if (m < 0.05f) dir = s;
            else
            {
                dir = d / m;
                float k = Vector3.Dot(dir, s);
                if (k >= MinAwayDot) return from;                                                      // the blast is on the shooter's side: natural push
                var lat = dir - k * s;                                                                  // its sideways part, |lat| <= 1
                dir = (s + lat * SideKeep).normalized;                                                  // dot(dir, s) >= 0.85
            }
            var q = victim - dir * Mathf.Max(m, 0.6f); q.y = from.y;
            return q;
        }
    }
}
