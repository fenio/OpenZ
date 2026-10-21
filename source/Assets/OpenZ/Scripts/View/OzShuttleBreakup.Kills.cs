using UnityEngine;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// How the shuttle breaks depends on what killed it (rule DV118, view only; the third round). Before, every kill used the same
    /// mid-air burst; only the throw differed. Now (the killing hit's power, OzShuttleAim.Power):
    /// <list type="bullet">
    /// <item>rifle-class fire (power up to <see cref="SmallArmsPower"/>: rifles, machine guns, lasers, sniper, flame, tesla) with the pads
    /// at least <see cref="FallMinFeet"/> m up: the ship loses its lift and drops WHOLE, tilting toward the side the rounds pushed (26
    /// deg roll, 12 deg nose down, a slow turn), trailing dark smoke and fire from its hit side with the fire's crackle; the drop lasts
    /// 1.0-1.5 s (a fall from the cruise height at a little over the real gravity); it breaks where it hits the ground (or the water, or a
    /// roof): the ground blast of a dying vehicle, a low rolling break, the camera kick;</item>
    /// <item>rockets, tank shells, grenades (the middle): the mid-air burst as before, with the dark cloud below;</item>
    /// <item>a heavy shell (power from <see cref="HeavyPower"/>: the howitzer, the fort's gun): the burst, then <see cref="SecondBlastAt"/>
    /// s later a second blast in the middle of the body's panels splits them apart: the panels thrown 2.6-3.6 m/s wider, the pods and
    /// domes 1.8-2.6, the legs 0.5-1.2, everything spun 5-9 rad/s harder (the plain-number twin of the break: the body
    /// panels' field about twice as wide as a burst's).</item>
    /// </list>
    /// An air break also leaves a dark grey cloud (<see cref="AirCloudSec"/> s) with an orange core for its first <see cref="AirCloudFireSec"/> s,
    /// drifting with the ship's way and rising (never tan dust: OpenZFxPlayer's air-burst gate keeps the blast's own smoke dark too and lays
    /// nothing on the ground under it).
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        // ------------------------------------------------------------------ numbers
        public static float SmallArmsPower = 0.62f, HeavyPower = 1.35f;
        /// <summary>The drop: the least pad height for it (m), its longest time (s), roll / extra roll per s after 0.6 s / nose-down / turn
        /// (deg, deg/s), how often the trail puffs (s), the paint's burn while it falls, the break's throw against a plain ground break.</summary>
        public static float FallMinFeet = 3f, FallMaxSec = 1.5f, FallRollDeg = 26f, FallRollMoreDeg = 8f, FallPitchDeg = 12f, FallYawDeg = 30f, FallSmokeEvery = 0.05f, FallFireEvery = 0.2f,
                            FallCharred = 0.25f, DropThrowK = 0.8f;
        /// <summary>The second blast: when (s after the break), the kicks (m/s: body panels, pods / domes / ramp leaf, legs / shards), the spin added (rad/s).</summary>
        public static float SecondBlastAt = 0.28f, SecondBodyKick0 = 2.6f, SecondBodyKick1 = 3.6f, SecondHeavyKick0 = 1.8f, SecondHeavyKick1 = 2.6f, SecondLightKick0 = 0.5f, SecondLightKick1 = 1.2f,
                            SecondSpin0 = 5f, SecondSpin1 = 9f;
        /// <summary>The air break's cloud: how long (s), its fire core (s), how often it puffs (s).</summary>
        public static float AirCloudSec = 3.6f, AirCloudFireSec = 2.4f, AirCloudEvery = 0.1f;
        /// <summary>A blast this high over the ground (m) is an air burst (OpenZFxPlayer: no ground light, shock rings or dust under it,
        /// its sparks and fire clouds round it, its smoke dark); never for a nuke or a building (their ground rings are the point). 3 m, not
        /// 2: every blast at the flying shuttle bursts at its hull's middle, at least 3.2 m up (pads 1 m up on take-off + 2.2 m), while a
        /// tank's turret popping off (up to about 2.5 m) and a hit on the LANDED shuttle's hull (2.2-2.6 m) keep their ring on the ground.</summary>
        public static float AirBurstM = 3f;
        public static Color AirBurstSmoke = new Color(0.21f, 0.20f, 0.19f, 1f);

        // ------------------------------------------------------------------ counts (the Play proof reads them)
        public static int Falls, DropBreaks, Splits, Bursts, SecondBlasts, AirClouds, CloudPuffs, FallSmoke, FallTimeouts, AirBurstsGated, GroundFxUnderAirShip, GroundBlastsUnderAirShip;
        /// <summary>The last drop: how long it fell (s), its largest tilt (deg), how high it started (m); the last break's kind ("burst",
        /// "split", "drop", "ground"; "falling" while a drop runs), the last second blast (Time.time), where it broke (the ground under it).</summary>
        public static float LastFallSec = -1f, LastFallTilt, LastFallHeight, LastSecondBlastAt = -1f;
        public static string LastHow = "-";
        public static Vector3 LastBreakGround;
        /// <summary>A drop running now (the proof waits for its end).</summary>
        public static bool Falling => fallsNow > 0;
        static int fallsNow;

        static readonly Vector3 Pivot = new Vector3(0f, 2.6f, 0f);
        static readonly Vector3[] FallPoints =
        {   // the pad soles, the pods' bottoms and tops, the belly, the domes' sides, the nose, the top (ship root space)
            new Vector3(2.228f, 0f, 1.029f), new Vector3(-2.168f, 0f, 1.079f), new Vector3(1.829f, 0f, -2.843f), new Vector3(-1.771f, 0f, -2.838f),
            new Vector3(2.47f, 1.1f, 1.2f), new Vector3(-2.47f, 1.1f, 1.2f), new Vector3(2.47f, 1.1f, -1.4f), new Vector3(-2.47f, 1.1f, -1.4f),
            new Vector3(2.6f, 4.5f, 0f), new Vector3(-2.6f, 4.5f, 0f), new Vector3(0f, 0.6f, 3.0f), new Vector3(0f, 0.6f, -3.0f),
            new Vector3(2.0f, 2.6f, 2.8f), new Vector3(-2.0f, 2.6f, 2.8f), new Vector3(0f, 3.3f, 4.3f), new Vector3(0f, 5.1f, 0f),
        };

        sealed class FallState
        {
            public ZObject o; public UnitView v; public OzPlayableShuttle ps; public Transform root; public FxManager fx; public CombatAudio au;
            public Vector3 pivot, vel; public Quaternion rot0; public float t, g, yaw, rollSign, tilt, smokeT, fireT, expect;
            public string hitKind, type, loopKey; public int aboard; public bool loop;
        }
        FallState fall;

        // ------------------------------------------------------------------ the drop (rifle-class kills)
        static bool StartFall(ZObject o, UnitView v, OzPlayableShuttle ps, FxManager fx, CombatAudio au, bool hitKnown, Vector3 hitDir, Vector3 hitAt, float power, string hitKind, int aboard, string type)
        {
            if (!hitKnown || power > SmallArmsPower || ps.FeetHeight < FallMinFeet) return false;
            var root = ps.transform;
            var f = new FallState { o = o, v = v, ps = ps, root = root, fx = fx, au = au, pivot = root.TransformPoint(Pivot), vel = ps.Velocity, rot0 = root.rotation, hitKind = hitKind, type = type, aboard = aboard };
            var hd = hitDir; hd.y = 0f;
            f.rollSign = hd.sqrMagnitude > 1e-4f ? (Vector3.Dot(hd, root.right) >= 0f ? -1f : 1f) : ((o.Id & 1) == 0 ? 1f : -1f);   // the side the rounds pushed goes down
            float h = Mathf.Max(0.5f, ps.FeetHeight);
            float T = Mathf.Min(Mathf.Sqrt(2f * h / 9.81f), FallMaxSec);
            f.g = Mathf.Clamp(2f * (h + f.vel.y * T) / (T * T), 9.81f, 30f);                                // h = -vy T + g T^2 / 2: down in T (at least the real gravity)
            f.expect = T;
            OzTreeCrush.ShuttleDrop(o, ref f.vel, f.pivot, T);                                              // item 360: the dropped ship comes down where the rules fell the trees (OzTreeCrush.cs)
            var tp = root.GetComponent<TeamPaint>(); if (tp) tp.Charred = Mathf.Max(tp.Charred, FallCharred);  // scorched where it was hit
            ps.OnBrokenUp(true);                                                                             // its own pose, jets, shadow, lights and loops stop: it falls
            var holder = new GameObject("OZ_ShuttleFall_" + o.Id);
            var k = holder.AddComponent<OzShuttleBreakup>(); k.keeper = true; k.fall = f;
            f.loopKey = "shuttle_fall_fire_" + o.Id;
            var ad = AudioDirector.I;
            if (ad && AudioDirector.Has("dst_fire_loop")) f.loop = ad.StartLoop(f.loopKey, "dst_fire_loop", f.pivot, root, 0.65f) > 0;   // the fire on its hit side crackles as it falls
            Falls++; fallsNow++; LastFallSec = -1f; LastFallTilt = 0f; LastFallHeight = h; LastHow = "falling";
            Debug.Log($"[OpenZ] shuttle #{o.Id} lost its lift ({hitKind}, power {power:F2}) {h:F1} m up: it drops whole (expected {T:F2} s, {f.g:F1} m/s2), rolling {(f.rollSign > 0f ? "left" : "right")} side down");
            return true;
        }

        void FallGone()
        {
            var f = fall; fall = null; fallsNow = Mathf.Max(0, fallsNow - 1);
            if (f != null && f.loop) { var ad = AudioDirector.I; if (ad) ad.StopLoop(f.loopKey, 0.3f); }
        }

        void FallStep(float dt)
        {
            var f = fall;
            if (!f.root) { FallGone(); Destroy(gameObject); return; }                                       // the level was left
            if (dt <= 0f) return;
            f.t += dt;
            f.vel.y -= f.g * dt;
            float drag = Mathf.Exp(-0.5f * dt); f.vel.x *= drag; f.vel.z *= drag;
            f.pivot += f.vel * dt;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f.t / 0.6f));
            float roll = f.rollSign * (FallRollDeg * k + FallRollMoreDeg * Mathf.Max(0f, f.t - 0.6f)), pitch = FallPitchDeg * k;
            f.yaw += f.rollSign * FallYawDeg * k * dt;
            var rot = Quaternion.AngleAxis(f.yaw, Vector3.up) * f.rot0 * Quaternion.Euler(pitch, 0f, roll);
            f.root.SetPositionAndRotation(f.pivot - rot * Pivot, rot);
            f.tilt = Mathf.Max(f.tilt, Vector3.Angle(rot * Vector3.up, Vector3.up));
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled)
            {   // the trail: dark smoke from the hit side's pod and the back, the fire on the pod
                var pod = f.root.TransformPoint(new Vector3(-f.rollSign * 2.2f, 3.6f, -0.4f));
                if ((f.smokeT -= dt) <= 0f)
                {
                    f.smokeT = FallSmokeEvery;
                    fxp.EmitChannel("leg_smoke", pod + Random.insideUnitSphere * 0.3f, new Vector3(Random.Range(-0.3f, 0.3f), 1.1f + Random.value * 0.6f, Random.Range(-0.3f, 0.3f)),
                        2.2f + Random.value * 0.8f, 1.3f + Random.value * 0.7f, Random.value < 0.5f ? OpenZFxCatalog.WreckSmokeDark : OpenZFxCatalog.WreckBurnSmoke, 0.9f);
                    if ((FallSmoke & 1) == 0)
                        fxp.EmitChannel("leg_smoke", f.root.TransformPoint(new Vector3(0f, 4.4f, -1.8f)), new Vector3(0f, 0.9f, 0f), 2.0f + Random.value * 0.6f, 1.0f + Random.value * 0.5f, OpenZFxCatalog.WreckBurnSmoke, 0.8f);
                    FallSmoke++;
                }
                if ((f.fireT -= dt) <= 0f) { f.fireT = FallFireEvery; fxp.WreckBurn(pod, 0.9f, true, 0.3f, pod); }
            }
            float gap = FallGap(f, out var low);
            if (gap > 0.05f && f.t < FallMaxSec + 1.5f) return;
            if (gap > 0.05f) FallTimeouts++;
            if (gap < 0f) { f.pivot.y -= gap; f.root.position += Vector3.up * -gap; }                       // never drawn into the ground at the hit
            DropBreak(f, low);
            f.loop = false; FallGone();                                                                      // (DropBreak handed its fire sound over)
            Destroy(gameObject);
        }

        /// <summary>The falling hull's lowest point against what is under it: the ground, the water's surface, a roof (m, + = above).</summary>
        static float FallGap(FallState f, out Vector3 low)
        {
            var g = ZGame.I; low = f.root.position; float best = float.MaxValue, minY = float.MaxValue;
            foreach (var q in FallPoints)
            {
                var w = f.root.TransformPoint(q);
                float surf = g ? g.GroundY(w.x, w.z) : 0f;
                if (OzWaterLanding.OpenWaterAt(w, out float wy)) surf = Mathf.Max(surf, wy);
                float e = OzRockBody.FallGapRock(f.root.TransformPoint(Pivot), w, w.y - surf);                     // items 331 + 332: a rock under or beside the hull: it breaks there
                if (e < best) { best = e; low = new Vector3(w.x, surf, w.z); }
                minY = Mathf.Min(minY, w.y);
            }
            var c = f.root.TransformPoint(Pivot);
            foreach (var h in Physics.RaycastAll(c, Vector3.down, 9f, ~0, QueryTriggerInteraction.Ignore))
            {   // a roof or a piece of scenery under it (never its own parts, never the ground's own colliders: GroundY has them)
                if (!h.collider || h.collider.transform.IsChildOf(f.root) || h.collider.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(h.collider)) continue;
                if (h.collider.GetComponentInParent<OzShuttleBreakup>()) continue;
                float e = minY - h.point.y;
                if (e < best) { best = e; low = h.point; }
            }
            return best;
        }

        /// <summary>The dropped ship hits: the break where it lies (the ground blast and sound of a dying vehicle, a low rolling break along
        /// its way, the camera kick); its fall's fire sound hands over to the pieces' fires.</summary>
        static void DropBreak(FallState f, Vector3 low)
        {
            var g = ZGame.I;
            var centre = f.root.TransformPoint(Pivot);
            var vi = f.vel;
            var vel = new Vector3(vi.x * 0.55f, Mathf.Abs(vi.y) * 0.18f, vi.z * 0.55f);                  // most of the fall goes into the ground, a little bounce
            var dir = vi.sqrMagnitude > 1e-4f ? vi.normalized : Vector3.down;
            var p = new Vector3(centre.x, g ? g.GroundY(centre.x, centre.z) : low.y, centre.z);
            if (f.loop) { var ad = AudioDirector.I; if (ad) ad.StopLoop(f.loopKey, 0.6f); }
            LastFallSec = f.t; LastFallTilt = f.tilt;
            Debug.Log($"[OpenZ] shuttle #{f.o.Id} dropped {f.t:F2} s (expected {f.expect:F2} s), tilted up to {f.tilt:F1} deg, and hit the ground at {low} at {vi.magnitude:F1} m/s");
            Break(f.o, f.v, p, f.fx, f.au, f.ps, false, vel, centre, low, dir, 1f, f.hitKind, f.aboard, f.type, "drop");
            if (WreckFx.OnScreen(p)) { var rc = Object.FindFirstObjectByType<RtsCamera>(); if (rc) rc.Shake(0.45f, p, "shuttle_drop"); }
        }

        // ------------------------------------------------------------------ the keeper's own effects after a break
        void KeeperFx(float dt)
        {
            var gr = grp;
            if (!gr.second && gr.secondAt >= 0f && gr.age >= gr.secondAt) SecondBlast(gr);
            if (gr.cloudUntil > 0f && gr.age < gr.cloudUntil && (gr.cloudT -= dt) <= 0f) CloudPuff(gr);
            if (gr.fireLoopOn && !string.IsNullOrEmpty(gr.fireLoopKey))
            {   // the fire's crackle ends with the last flame (or when the wreck goes)
                bool any = false; foreach (var pc in gr.pieces) if (pc && pc.BurningNow) { any = true; break; }
                if (!any || gr.age >= gr.wreckAt) { var ad = AudioDirector.I; if (ad) ad.StopLoop(gr.fireLoopKey, 1.5f); gr.fireLoopKey = null; }
            }
            ClankStep(gr);
        }

        /// <summary>A heavy shell's second blast: in the middle of the body's panels, the table blast of a dying vehicle with its sound,
        /// every piece kicked out of it and spun harder.</summary>
        static void SecondBlast(Group gr)
        {
            gr.second = true;
            Vector3 c = Vector3.zero; float m = 0f;
            foreach (var pc in gr.pieces) if (pc && pc.Kind == "body" && pc.rb) { c += pc.Centre() * pc.MassKg; m += pc.MassKg; }
            if (m <= 0f) return;
            c /= m;
            var spec = LegacyMatch.ForDeath(gr.obj);
            LegacyMatch.DoBlast(c, spec, gr.emitter * 31 + 7, FxManager.I);
            var au = CombatAudio.I; if (au && spec.clips != null && spec.clips.Length > 0) au.PlayPitched(spec.key, c, spec.gain * 0.85f, 0f, gr.emitter + 5, spec.pitch * 1.07f, spec.clips);
            OpenZHitSparks.Burst(c, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 1.8f, false);
            var rnd = new System.Random(gr.emitter * 977 + 5);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            foreach (var pc in gr.pieces)
            {
                if (!pc || !pc.rb || pc.rb.isKinematic) continue;
                var d = pc.Centre() - c; d.y = 0f; if (d.sqrMagnitude < 1e-3f) d = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)); d.Normalize();
                d = Quaternion.Euler(0f, R(-20f, 20f), 0f) * d;
                float kick = pc.Kind == "body" ? R(SecondBodyKick0, SecondBodyKick1) : pc.Kind == "dome" || pc.Kind == "engine" || pc.Kind == "leaf" ? R(SecondHeavyKick0, SecondHeavyKick1) : R(SecondLightKick0, SecondLightKick1);
                pc.rb.maxAngularVelocity = 20f;
                pc.rb.linearVelocity += d * kick + Vector3.up * R(0.8f, 2.2f);
                pc.rb.angularVelocity += Random.onUnitSphere * R(SecondSpin0, SecondSpin1);
                pc.rb.WakeUp();
            }
            gr.cloudUntil = Mathf.Max(gr.cloudUntil, gr.age + AirCloudSec * 0.8f);                           // the cloud goes on from the second blast
            gr.centre = Vector3.Lerp(gr.centre, c, 0.5f);
            SecondBlasts++; LastSecondBlastAt = Time.time;
        }

        /// <summary>The air break's cloud, every AirCloudEvery s: dark grey billows drifting on with the ship's way and rising, an orange
        /// fire core in its first AirCloudFireSec s.</summary>
        static void CloudPuff(Group gr)
        {
            gr.cloudT = AirCloudEvery;
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled) return;
            float a = gr.age;
            var drift = gr.vel * (0.6f * (1f - Mathf.Exp(-a * 1.5f)) / 1.5f) + Vector3.up * (0.45f * a);
            var at = gr.centre + drift + Random.insideUnitSphere * 1.1f;
            float k = Mathf.Lerp(1f, 0.6f, a / Mathf.Max(0.1f, gr.cloudUntil));
            fxp.EmitChannel("leg_smoke", at, new Vector3(Random.Range(-0.5f, 0.5f), 0.7f + Random.value * 0.6f, Random.Range(-0.5f, 0.5f)), 2.6f + Random.value * 0.9f,
                (2.2f + Random.value * 1.2f) * k, Random.value < 0.55f ? OpenZFxCatalog.WreckSmokeDark : OpenZFxCatalog.WreckBurnSmoke, 0.92f);
            if (a < AirCloudFireSec)
            {
                fxp.EmitChannel("leg_fire", gr.centre + drift * 0.8f + Random.insideUnitSphere * 0.7f, new Vector3(Random.Range(-0.4f, 0.4f), 0.8f + Random.value * 0.8f, Random.Range(-0.4f, 0.4f)),
                    0.4f + Random.value * 0.25f, (1.8f + Random.value * 1.0f) * (1f - 0.5f * a / AirCloudFireSec), Random.value < 0.5f ? OpenZFxCatalog.WreckFlameB : OpenZFxCatalog.WreckFlameC, 1f);
                if (((int)(a / AirCloudEvery) & 3) == 0) fxp.WreckBurn(gr.centre + drift, 1f, true, 0.6f, gr.centre + drift);   // its light and the larger tongues now and then
            }
            CloudPuffs++;
        }

        // ------------------------------------------------------------------ OpenZFxPlayer's air-burst gate
        /// <summary>OpenZFxPlayer.BlastInternal: this blast is up in the air (more than AirBurstM over the ground; a nuke or a building's
        /// collapse never).</summary>
        public static bool IsAirBurst(Vector3 at, float groundY, FxBlastKind kind) => kind != FxBlastKind.Nuke && kind != FxBlastKind.Building && at.y - groundY > AirBurstM;

        /// <summary>OpenZFxPlayer.BlastInternal's count: a blast that still lays its ground light / rings under a flying shuttle (pads 2.5 m
        /// or more up, within 8 m flat): from 1 m up = a fault (not a hit on the ground), lower = a real hit on the ground under it.</summary>
        public static void NoteBlastFx(Vector3 at, Vector3 flat, float height, bool airBurst, bool groundFx)
        {
            if (airBurst) AirBurstsGated++;
            if (!groundFx) return;
            for (int i = 0; i < OzPlayableShuttle.Count; i++)
            {
                var s = OzPlayableShuttle.At(i); if (!s || s.Obj == null || !s.Obj.ShuttleAirborne || s.BrokenUp || s.ShownHeight < 2.5f) continue;
                var p = s.transform.position;
                if (new Vector2(p.x - flat.x, p.z - flat.z).magnitude > 8f) continue;
                if (height >= 1f) GroundFxUnderAirShip++; else GroundBlastsUnderAirShip++;
                return;
            }
        }
    }
}
