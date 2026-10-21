using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's rough landing, a CRASH landing drawn with physics (rule DV118: the rules decide it and own its timing,
    /// ZObject.ShuttleRoughLanding / ShuttleRoughSide / ShuttleTouchdownAt / ShuttleLevelStartAt / ShuttleLevelledAt; no health is lost).
    /// The last 0.5 s of the descent it comes in fast (5 m/s at the touch) and leaning hard (12 degrees to the side the rules name, 3.5
    /// nose down), so one corner's pad hits first. The touch is a small rigid body, stepped at 1/120 s on the rules' clock: its height,
    /// its roll about its forward axis and its pitch about its side axis, gravity, a spring and damper at each of its four real pad soles
    /// against the ground under it, a pad never shown more than 3 cm in the ground (a hard stop that bounces). So the first side slams
    /// down and bounces up (about 0.4 m), comes down on it again and its two struts give (the legs slide 0.62 m up into their pods), the
    /// other side bangs down, it rocks and pitches; it slides on 1.1 m along its flight with the friction of its pads and slews a few
    /// degrees (dust off the pads and a metal scrape while it slides). It comes to rest CROOKED on its four pads (about 8 degrees, the
    /// first side's struts still down) and lies still; at the rules' levelling start the hydraulic pushes those struts out again, so it
    /// rights itself smoothly onto its four-pad ground fit and back onto the rules' point and heading, done exactly when the rules let
    /// the ramp, EXIT and the take-off go. OzPlayableShuttle draws the legs' strokes (the foot lamps ride on the legs). Sounds and effects,
    /// all the game's own: the first slam = the wreck crash (the tank impact bank), the heavy metal crash, a big dust burst round the
    /// ship and a camera kick; the second side = the debris clank; the slide = the debris scrape; the levelling = the shuttle's levelling
    /// hydraulic and a servo whine; metal sparks at every hitting pad. Output: an additive pose (height, roll, pitch, slide, slew) that
    /// OzPlayableShuttle puts on top of the ground fit. The numbers have a line-for-line twin off the editor: tools/c_view_touch_twin.py.
    /// </summary>
    public sealed class OzShuttleTouchdown
    {
        public static float TouchSpeed = 5f, LeanDeg = 12f, PitchDeg = 3.5f, Stiff = 2600f, Damp = 48f, MaxIn = 0.03f, Bounce = 0.6f;
        /// <summary>The first side's struts give this much (m) over StrutSec when the hull comes down on them again; the angular damping (1/s);
        /// the slide (m) over SkidSec and the slew with it (deg).</summary>
        public static float StrutDrop = 0.62f, StrutSec = 0.18f, AngDamp = 6f, Skid = 1.1f, SkidSec = 0.85f, SlewDeg = 5f, ArmR2 = 4f, ArmP2 = 6f;
        /// <summary>The first slam's dust burst round the ship (m) and its camera kick.</summary>
        public static float BurstM = 8f, Kick = 0.4f;
        /// <summary>The rules' timing (s after the touch) when the view runs without them: crash, rest, levelling.</summary>
        public static float CrashSec = 0.8f, RestSec = 0.2f, LevelSec = 0.7f;   // K426: the rules' crash timing (was 1.4 / 1.0 / 1.2 s; Sim/ZWorld.ShuttleExitSooner.cs)
        /// <summary>Proof: rough landings begun, pad contacts, the worst pad depth (m), the time the tilt stayed over 6 deg, levelling (s),
        /// the most it pitched (deg), how far off the rules' level moment the drawn ship stood level (s), skid dust puffs, the first slam's
        /// speed (m/s), the bounce after it (m), the tilt at rest (deg), the slide (m).</summary>
        public static int Rough, Contacts, SkidPuffs, Scrapes;
        public static float WorstPadIn, TiltHeld, LevelTook = -1f, PitchPeak, LevelOff = -1f, FirstSlam, BounceM, RestTiltDeg, SlideM;
        public static readonly StringBuilder Log = new StringBuilder();

        // pad soles (root space x = across, z = along)
        static readonly Vector2[] Pads = { new Vector2(2.228f, 1.029f), new Vector2(-2.168f, 1.079f), new Vector2(1.829f, -2.843f), new Vector2(-1.771f, -2.838f) };
        public bool Active { get; private set; }
        /// <summary>The side whose pads hit first: +1 = the ship's +x side, -1 = the -x side (the rules' ShuttleRoughSide).</summary>
        public float Side { get; private set; } = 1f;
        public float Y { get; private set; }
        public float RollDeg { get; private set; }
        public float PitchNowDeg { get; private set; }
        public Vector3 SkidNow { get; private set; }
        /// <summary>The slew (deg of yaw) on top of the rules' heading while it slides and rests (0 again when level).</summary>
        public float SlewNowDeg { get; private set; }
        /// <summary>The touch's own clock (s after the touch, the rules' clock).</summary>
        public float T => t;
        /// <summary>Still crooked: before the rules' levelled moment (the ramp and EXIT must wait for it).</summary>
        public bool Righting => Active && !leveled;

        readonly Transform root; readonly int id;
        float y, yd, phi, phid, th, thd, t, tiltT, levelStart, levelEnd, puffT, hitLow = -1f, highAt = -1f, restSum; int lowHits, restN; Vector3 dir; bool touchedLow, touchedHigh, levelSounded, leveled, scraped;
        readonly bool[] down = new bool[4];
        readonly float[] strut = new float[4];
        /// <summary>How far pad i's strut is pushed up into its pod now (m; 0 = out): OzPlayableShuttle moves that leg by it.</summary>
        public float Strut(int i) => Active && i >= 0 && i < 4 ? strut[i] : 0f;

        public OzShuttleTouchdown(Transform shipRoot, int shipId) { root = shipRoot; id = shipId; }

        static float Roll0(float side) => -LeanDeg * side;                                                   // the first side low (+ roll = +x up)

        /// <summary>The descent's last 0.5 s (tau = seconds left to the touch, 0.5 .. 0): the height over the ground fit and the lean.
        /// h0 / v0 = the rules' drawn height and its sink speed at tau 0.5.</summary>
        public float DescentHeight(float tau, float h0, float v0, out float rollDeg, out float pitchDeg)
        {
            float s = Mathf.Clamp01(tau / 0.5f), lean = Mathf.SmoothStep(0f, 1f, 1f - s);
            rollDeg = Roll0(Side) * lean; pitchDeg = PitchDeg * lean;
            float touch = TouchLift(rollDeg, pitchDeg);
            // Hermite on the time left: (0.5 s, h0, sinking v0) -> (0, touch, sinking TouchSpeed)
            float h00 = 2 * s * s * s - 3 * s * s + 1, h10 = s * s * s - 2 * s * s + s, h01 = -2 * s * s * s + 3 * s * s, h11 = s * s * s - s * s;
            return h00 * touch + h10 * 0.5f * TouchSpeed + h01 * h0 + h11 * 0.5f * v0;
        }

        /// <summary>How high the root must be for the lowest pad to just touch at this roll and pitch.</summary>
        static float TouchLift(float rollDeg, float pitchDeg)
        {
            float sr = Mathf.Sin(rollDeg * Mathf.Deg2Rad), sp = Mathf.Sin(pitchDeg * Mathf.Deg2Rad), lo = 0f;
            foreach (var p in Pads) lo = Mathf.Max(lo, -(p.x * sr - p.y * sp));
            return lo;
        }

        /// <summary>The descent's start: the side whose pads hit first (+1 = +x).</summary>
        public void Arm(float side) { Side = side >= 0f ? 1f : -1f; }

        /// <summary>The touch: flightDir = where it flew (world, flat); levelStart / levelEnd = the rules' levelling (s after the touch).</summary>
        public void Begin(Vector3 flightDir, float levelStartSec, float levelEndSec)
        {
            Active = true; Rough++; t = 0f; tiltT = 0f; puffT = 0f; hitLow = -1f; highAt = -1f; lowHits = 0; restSum = 0f; restN = 0; touchedLow = touchedHigh = levelSounded = leveled = scraped = false;
            LevelTook = -1f; WorstPadIn = 0f; TiltHeld = 0f; PitchPeak = 0f; LevelOff = -1f; FirstSlam = 0f; BounceM = 0f; RestTiltDeg = 0f; SlideM = 0f;
            levelStart = levelStartSec > 0.2f ? levelStartSec : CrashSec + RestSec; levelEnd = levelEndSec > levelStart ? levelEndSec : levelStart + LevelSec;
            for (int i = 0; i < 4; i++) { down[i] = false; strut[i] = 0f; }
            phi = Roll0(Side) * Mathf.Deg2Rad; th = PitchDeg * Mathf.Deg2Rad; phid = thd = 0f;
            y = TouchLift(Roll0(Side), PitchDeg); yd = -TouchSpeed;
            dir = new Vector3(flightDir.x, 0f, flightDir.z); dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : root.forward;
            Log.Clear(); Log.AppendLine($"rough landing #{Rough}: the {(Side > 0 ? "+x" : "-x")} pads down first, lean {LeanDeg:F1} deg, nose {PitchDeg:F1} deg down, touch {TouchSpeed:F1} m/s; their struts give {StrutDrop:F2} m; slide {Skid:F2} m, slew {SlewDeg:F1} deg; levelling {levelStart:F2} .. {levelEnd:F2} s (the rules' clock)");
        }

        public void Stop() { Active = false; Y = 0f; RollDeg = 0f; PitchNowDeg = 0f; SkidNow = Vector3.zero; SlewNowDeg = 0f; }

        float PadH(int i, out float rate)
        {
            float x = Pads[i].x, z = Pads[i].y;
            rate = yd + x * Mathf.Cos(phi) * phid - z * Mathf.Cos(th) * thd;
            return y + x * Mathf.Sin(phi) - z * Mathf.Sin(th) + strut[i];
        }

        /// <summary>One frame of the touch. since = the rules' seconds since the touch (World.Time - ShuttleTouchdownAt; below 0 = unknown:
        /// the frame's dt x the game speed); the body steps until it has caught up with it.</summary>
        public void Step(float dt, float since = -1f)
        {
            if (!Active) return;
            const float h = 1f / 120f;
            float target = since >= 0f ? Mathf.Min(since, t + 0.5f) : t + dt;
            int guard = 0;
            while (t + h <= target + 1e-5f && guard++ < 64) Sub(h);
            Y = y; RollDeg = phi * Mathf.Rad2Deg; PitchNowDeg = th * Mathf.Rad2Deg;
            PitchPeak = Mathf.Max(PitchPeak, Mathf.Abs(PitchNowDeg));
            // the slide and the slew: on along the flight with the pads' friction, held while it rests crooked, back onto the rules' point
            // and heading as it levels
            float sk = t < SkidSec ? 1f - (1f - t / SkidSec) * (1f - t / SkidSec) : 1f;
            if (t >= levelStart) sk *= 1f - Mathf.SmoothStep(0f, 1f, (t - levelStart) / Mathf.Max(0.1f, levelEnd - levelStart));
            SkidNow = dir * (Skid * sk); SlewNowDeg = SlewDeg * Side * sk; SlideM = Mathf.Max(SlideM, Skid * sk);
            if (t < SkidSec && t > 0.05f && (puffT -= dt) <= 0f) { puffT = 0.05f; SkidDust(); }
            if (!scraped && t > 0.05f && t < SkidSec) { scraped = true; Scrape(); }
            if (Mathf.Abs(RollDeg) >= 6f) tiltT += dt; TiltHeld = Mathf.Max(TiltHeld, tiltT);
            if (t >= CrashSec - 0.15f && t < levelStart) { restSum += Mathf.Abs(RollDeg); restN++; RestTiltDeg = restSum / restN; }
            if (highAt < 0f && hitLow < 0f && touchedLow && t > 0.05f) BounceM = Mathf.Max(BounceM, LowestPad());
            if (leveled && t > levelEnd + 0.4f) { Log.AppendLine($"  done at {t:F2} s (pads at most {WorstPadIn * 100f:F1} cm in the ground, pitch peak {PitchPeak:F1} deg, bounce {BounceM:F2} m, rest tilt {RestTiltDeg:F1} deg, slide {SlideM:F2} m)"); Stop(); }
        }

        float LowestPad() { float lo = 99f; for (int i = 0; i < 4; i++) lo = Mathf.Min(lo, PadH(i, out _)); return lo; }

        /// <summary>A pad's first touch since it was last up: the first side's struts give when the hull comes down on them again (its
        /// second hard slam, or the other side's first).</summary>
        void Touched(int i, float speed)
        {
            down[i] = true; Contacts++;
            bool low = Pads[i].x * Side > 0f;
            if (speed > 1f)
            {
                if (low) lowHits++;
                if (hitLow < 0f && ((low && lowHits >= 2) || !low)) hitLow = t;
                if (!low && highAt < 0f) highAt = t;
            }
            Hit(i, speed);
        }

        void Sub(float h)
        {
            t += h;
            float fy = -9.81f, tr = 0f, tp = 0f;
            for (int i = 0; i < 4; i++)
            {
                float hi = PadH(i, out float rate);
                if (hi < 0f)
                {
                    float f = Mathf.Max(0f, -Stiff * hi - Damp * rate);
                    fy += f; tr += f * Pads[i].x * Mathf.Cos(phi); tp -= f * Pads[i].y * Mathf.Cos(th);
                    if (!down[i]) Touched(i, -rate);
                }
                else if (hi > 0.05f) down[i] = false;
            }
            // the first side's struts give when the hull comes down on them again and stay down; from the rules' levelling start the
            // hydraulic pushes them out again, so it rests crooked on its four pads and then rights itself onto the ground fit by the rules'
            // levelled moment
            float k = t < levelStart ? 0f : Mathf.SmoothStep(0f, 1f, (t - levelStart) / Mathf.Max(0.1f, levelEnd - levelStart));
            float give = hitLow < 0f ? 0f : Mathf.SmoothStep(0f, 1f, (t - hitLow) / StrutSec);
            for (int i = 0; i < 4; i++) strut[i] = Pads[i].x * Side > 0f ? StrutDrop * give * (1f - k) : 0f;
            if (t >= levelStart && !levelSounded) { levelSounded = true; Level(); }
            if (!leveled && t >= levelEnd) { leveled = true; LevelTook = t - levelStart; LevelOff = Mathf.Abs(t - levelEnd); Log.AppendLine($"  levelled at {t:F2} s ({LevelTook:F2} s after the levelling began)"); }
            float phidd = tr / ArmR2 - AngDamp * phid, thdd = tp / ArmP2 - AngDamp * thd;
            yd += fy * h; phid += phidd * h; thd += thdd * h; y += yd * h; phi += phid * h; th += thd * h;
            // the hard stop: no pad more than MaxIn in the ground (a rebound); a fast pad meets it before its spring has counted it
            for (int i = 0; i < 4; i++)
            {
                float hi = PadH(i, out float rate);
                WorstPadIn = Mathf.Max(WorstPadIn, Mathf.Min(MaxIn, -hi));
                if (hi < -MaxIn)
                {
                    if (!down[i]) Touched(i, -rate);
                    y += -MaxIn - hi; if (rate < 0f) yd += -rate * (1f + Bounce);
                }
            }
            if (leveled) { float e = 1f - Mathf.Exp(-h * 6f); y = Mathf.Lerp(y, 0f, e); phi = Mathf.Lerp(phi, 0f, e); th = Mathf.Lerp(th, 0f, e); }
        }

        void Hit(int pad, float speed)
        {
            var at = root.TransformPoint(new Vector3(Pads[pad].x, 0.05f, Pads[pad].y));
            bool low = Pads[pad].x * Side > 0f;
            Log.AppendLine($"  pad contact {pad} ({(low ? "low" : "high")} side) at {t:F3} s, {speed:F2} m/s");
            if (speed < 0.6f) return;
            OpenZHitSparks.Burst(at + Vector3.up * 0.2f, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 1.4f + 0.2f * speed, false);
            var fx = OpenZFxPlayer.I;
            if (fx && fx.isActiveAndEnabled && !fx.WetAt(at)) fx.Dust(at, Mathf.Clamp(0.5f * speed, 1.0f, 2.4f));
            var au = CombatAudio.I;
            if (low && !touchedLow)
            {   // the first slam: the crash, a big dust burst round the whole ship, a camera kick
                touchedLow = true; FirstSlam = speed;
                if (au)
                {
                    au.PlayPitched("wreck_crash", at, 1f, 0f, id, 0.85f, LegacyMatch.BankTankImpact);
                    au.Play("turret", at, 0.8f, 0f, id, OzSoundV3.BankMetalHeavy);
                }
                var c = root.position;
                if (fx && fx.isActiveAndEnabled && WreckFx.OnScreen(c))
                {
                    fx.DemolitionDust(c, BurstM, BurstM, 0.6f);
                    for (int i = 0; i < 4; i++) { var p = root.TransformPoint(new Vector3(Pads[i].x * 1.3f, 0.1f, Pads[i].y * 1.2f)); if (!fx.WetAt(p)) fx.Dust(p, 2.2f); }
                }
                var rc = Object.FindFirstObjectByType<RtsCamera>(); if (rc) rc.Shake(Kick, at, "wreck_slam");
                Log.AppendLine($"  wreck_crash + metal crash + the dust burst ({BurstM:F0} m) at the first slam, {speed:F2} m/s");
            }
            else if (!low && !touchedHigh)
            {
                touchedHigh = true;
                AudioDirector.Fire("dst_debris_metal", at, id, Mathf.Clamp01(0.6f + speed * 0.1f));
                if (au) au.Play("turret", at, 0.55f, 0f, id + 7, OzSoundV3.BankMetalHeavy);
                Log.AppendLine("  dst_debris_metal + metal crash at the second side");
            }
        }

        /// <summary>While it slides: dust off the two pads of the first side.</summary>
        void SkidDust()
        {
            var fx = OpenZFxPlayer.I; if (!fx || !fx.isActiveAndEnabled) return;
            for (int i = 0; i < 4; i++)
            {
                if (Pads[i].x * Side <= 0f) continue;
                var at = root.TransformPoint(new Vector3(Pads[i].x, 0.05f, Pads[i].y));
                if (fx.WetAt(at)) continue;
                fx.Dust(at, 0.85f); SkidPuffs++;
            }
        }

        /// <summary>The slide's sound: the shuttle's own skid scrape when the table has it, else the debris scrape of the game.</summary>
        void Scrape()
        {
            var at = root.TransformPoint(new Vector3(Side * 2f, 0.1f, -0.9f));
            if (AudioDirector.Has("shuttle_skid_scrape")) AudioDirector.Fire("shuttle_skid_scrape", at, id, 1f);
            else AudioDirector.Fire("dst_debris_metal", at, id + 3, 0.8f, 0.7f);
            Scrapes++; Log.AppendLine($"  the slide's scrape at {t:F2} s");
        }

        void Level()
        {
            var at = root.position + Vector3.up * 1.2f;
            if (AudioDirector.Has("shuttle_level_hydraulic")) AudioDirector.Fire("shuttle_level_hydraulic", at, id, 1f);
            if (AudioDirector.Has("rob_servo")) AudioDirector.Fire("rob_servo", at, id, 0.6f, 0.9f);
            Log.AppendLine($"  levelling starts at {t:F2} s (shuttle_level_hydraulic + rob_servo)");
        }
    }
}
