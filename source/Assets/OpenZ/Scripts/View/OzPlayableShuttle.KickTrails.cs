using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// K390, the kickflip's second round (view only; the flip itself: View/OzShuttleKickflip.Core.cs and View/OzPlayableShuttle.Kickflip.cs).
    /// <list type="bullet">
    /// <item>THE TRAILS: while it flips, each main jet leaves ONE smooth trail (View/OzShuttleKickflip.Trail.cs): a ribbon from the nozzle,
    /// a hot white-orange core inside a soft orange glow, widening and fading over its few metres, curling round through the roll. It takes
    /// over from the jets' own fire puffs, which ease down to a small share while it burns (the flame cores, the halo and the lights stay),
    /// so the flip shows continuous exhaust instead of separate blobs. The attitude jets at the pods' ends fire short smooth pale streaks
    /// (the same ribbon, short and thin) on the pair that drives the roll, with a small glow at each burst. Everything eases in and out;
    /// nothing is emitted off screen or while paused.</item>
    /// <item>THE ARC (the barrel roll's way round its line): <see cref="OzKickflipCurve.Arc"/> sets it over the flip; here the room round the
    /// ship is read before it starts (<see cref="KickArcBlocked"/>: the ground, buildings, rocks, trees and palms the ship's sky clearance
    /// knows, OzShuttleSkyTop): the arc is cut down until the hull keeps at least 1 m over every hard top and 2 m over every crown along the
    /// whole flip (never less room than the flip in place had). It starts only with the whole arc on screen.</item>
    /// </list>
    /// View only: the rules never know about any of it.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>The main jets' trails: the seconds between two points, a point's life (s), the air's slowing (1/s), the gas's speed out of
        /// the mouth (m/s), the share of the ship's speed it carries, how far down the axis the trail starts (m, inside the flame), the core's
        /// width at the mouth (m), its growth to the tail (x), the glow's width (x the core's) and strength, the material's strength.</summary>
        public static float KickTrailInterval = 1f / 120f, KickTrailLife = 0.55f, KickTrailDrag = 3f, KickTrailSpeed = 8f, KickTrailCarry = 0.5f, KickTrailStartM = 0.35f,
                            KickTrailWidth = 0.42f, KickTrailGrow = 1.8f, KickTrailGlowWidth = 2.6f, KickTrailGlowGain = 0.35f, KickTrailGain = 4f;
        /// <summary>The share of the jets' own fire puffs and flame streaks left while the trails burn (eased), the trails' ease in and out (s).</summary>
        public static float KickTrailJetsLeft = 0.15f, KickTrailIn = 0.25f, KickTrailOut = 0.35f;
        /// <summary>The attitude jets' streaks: life (s), slowing (1/s), speed (m/s), width (m), growth, strength; the burst's glow (m, s).</summary>
        public static float KickStreakLife = 0.22f, KickStreakDrag = 4f, KickStreakSpeed = 9f, KickStreakWidth = 0.22f, KickStreakGrow = 1.5f, KickStreakGain = 2.4f, KickBurstGlowM = 0.9f, KickBurstSec = 0.18f;
        public static Color KickTrailHot = new Color(1f, 0.86f, 0.58f), KickTrailMid = new Color(1f, 0.5f, 0.16f), KickTrailEnd = new Color(0.55f, 0.16f, 0.05f);
        public static Color KickStreakHot = new Color(0.92f, 0.95f, 1f), KickStreakEnd = new Color(0.6f, 0.68f, 0.8f);
        /// <summary>The arc's room: the hull's lowest point under the roll's axis by roll angle (m, every 15 deg from 0 to 180, the ship's
        /// model files: the pods' ends, the legs, the hull's top upside down; mirrored for 180 .. 360), the room kept over hard tops and
        /// crowns (m), the arc's shares tried (largest first), how often it is read while armed (s).</summary>
        static readonly float[] KickLowest = { 2.80f, 3.42f, 3.80f, 3.92f, 3.78f, 3.38f, 3.19f, 3.39f, 3.35f, 3.12f, 2.75f, 2.34f, 2.34f };
        public static float KickRoomHard = 1.0f, KickRoomPlant = 2.0f, KickArcEvery = 0.2f;
        static readonly double[] KickArcShares = { 1.0, 0.75, 0.5, 0.25, 0.0 };

        OzKickTrail[] kickJet, kickPort; Mesh[] kickTrailMesh; float kickTrailW, kickBurst, kickBurstSign; bool kickJetsScaled; float kickFire0, kickFlame0;
        float kickArcAt = -100f; double kickArcBlocked;
        static Material kickTrailMat, kickStreakMat; static bool kickTrailMatTried;
        static readonly List<Vector3> kickV = new List<Vector3>(512); static readonly List<Color> kickC = new List<Color>(512); static readonly List<Vector2> kickUV = new List<Vector2>(512);
        static readonly List<int> kickT = new List<int>(1536); static readonly List<Vector3> kickP = new List<Vector3>(256); static readonly List<Vector2> kickPA = new List<Vector2>(256);
        /// <summary>K390 proof counts: frames with the main jets' trails drawn, their largest gap between two neighbouring points while they
        /// burned (m), their farthest reach from the nozzle (m), frames with an attitude streak drawn, the long whooshes fired, the arc's share
        /// of the last flip's start.</summary>
        public int KickTrailFrames { get; private set; }
        public float KickTrailMaxGap { get; private set; }
        public float KickTrailMaxReach { get; private set; }
        public int KickStreakFrames { get; private set; }
        public int KickWhooshes { get; private set; }
        public double KickArcShare => 1.0 - kickArcBlocked;
        /// <summary>The Play proof: the trails' largest gap and longest length counted afresh (per flip).</summary>
        public void KickTrailProofReset() { KickTrailMaxGap = 0f; KickTrailMaxReach = 0f; }

        static void KickTrailMats()
        {
            if (kickTrailMatTried) return;
            kickTrailMatTried = true;
            var sh = Shader.Find("OpenZ/Beam");                                                          // the additive HDR beam (FxManager's beams): soft across the ribbon
            if (!sh || !sh.isSupported) { Debug.LogWarning("[OpenZ] shuttle kickflip: the beam shader is missing; the jets keep their own puffs in the flip"); return; }
            kickTrailMat = new Material(sh) { name = "ShuttleKickTrail (runtime)" }; kickTrailMat.SetColor("_Color", Color.white * KickTrailGain);
            kickStreakMat = new Material(sh) { name = "ShuttleKickStreak (runtime)" }; kickStreakMat.SetColor("_Color", Color.white * KickStreakGain);
        }

        void KickTrailsMake()
        {
            if (kickJet != null) return;
            kickJet = new[] { new OzKickTrail(KickTrailInterval, KickTrailLife, KickTrailDrag), new OzKickTrail(KickTrailInterval, KickTrailLife, KickTrailDrag) };
            kickPort = new OzKickTrail[4];
            for (int i = 0; i < 4; i++) kickPort[i] = new OzKickTrail(KickTrailInterval, KickStreakLife, KickStreakDrag);
            kickTrailMesh = new Mesh[6];
            for (int i = 0; i < 6; i++) { kickTrailMesh[i] = new Mesh { name = "OZ_KickTrail" + i }; kickTrailMesh[i].MarkDynamic(); }
        }

        // ================================================================== the arc's room and its view (read while armed)
        /// <summary>The share of the arc the room round the ship does not allow (0 = the whole arc), read every KickArcEvery s while armed.</summary>
        double KickArcBlocked()
        {
            if (Time.time - kickArcAt < KickArcEvery) return kickArcBlocked;
            kickArcAt = Time.time;
            kickArcBlocked = 0;
            if (sky == null) return 0;
            var c = KickCentre; var right = transform.right; right.y = 0f; right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            var v = Velocity; v.y = 0f;
            double best = 0;
            foreach (var k in KickArcShares)
            {
                if (KickArcRoomOk(c, right, v, k)) { best = k; break; }
            }
            kickArcBlocked = 1.0 - best;
            return kickArcBlocked;
        }

        /// <summary>The flip with the arc's share k, drawn ahead along the way (both roll directions): every sample keeps its room (or at least
        /// the room the flip in place has there).</summary>
        bool KickArcRoomOk(Vector3 c, Vector3 right, Vector3 v, double k)
        {
            for (int n = 0; n <= 12; n++)
            {
                double t = OzKickflipCurve.RollStartSec + OzKickflipCurve.RollSec * n / 12.0;
                OzKickflipCurve.At(t, out double r, out _, out _, out _, out _, out _, out double hop, out _, out _, out _);
                OzKickflipCurve.Arc(t, out double side0, out _, out _, out double rise0, out _, out _);
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    double side = side0 * dir * k, rise = rise0 * k;
                    var along = c + v * (float)t + Vector3.up * (float)hop;
                    var p = along + right * (float)side + Vector3.up * (float)rise + KickLeadAhead(c + v * (float)t, v, hop + rise, side);   // K423: the arc with its lead
                    float low = p.y - KickLowestAt((float)r);
                    float m = KickRoomAt(p, low);
                    if (m >= KickRoomHard) continue;
                    if (k <= 0.0) continue;                                                              // the flip in place: K344's own room
                    float m0 = KickRoomAt(along, along.y - KickLowestAt((float)r));
                    if (m < Mathf.Min(KickRoomHard, m0)) return false;
                }
            }
            return true;
        }

        /// <summary>The room under a hull whose lowest point is `low` at p: the least of (low - hard top - room kept) and (low - crown - room
        /// kept), plus the hard room (so 1.0 = just enough).</summary>
        float KickRoomAt(Vector3 p, float low)
        {
            sky.Query(p.x, p.z, OzShuttleSkyTop.Footprint, true, out float hard, out float plant);
            float m = 99f;
            if (hard > float.MinValue) m = Mathf.Min(m, low - hard);
            if (plant > float.MinValue) m = Mathf.Min(m, low - plant - (KickRoomPlant - KickRoomHard));
            return m;
        }

        static float KickLowestAt(float rollDeg)
        {
            float a = Mathf.Repeat(Mathf.Abs(rollDeg), 360f); if (a > 180f) a = 360f - a;
            float f = a / 15f; int i = Mathf.Min(11, (int)f);
            return Mathf.Lerp(KickLowest[i], KickLowest[i + 1], f - i) + 0.15f;
        }

        /// <summary>The whole flip in view: the hull's middle, the arc's top and both sides, its end along the way (a little inside the edges).</summary>
        bool KickArcOnScreen(Camera cam)
        {
            if (!cam) return false;
            double k = 1.0 - kickArcBlocked;
            var c = KickCentre; var right = transform.right; var v = Velocity; v.y = 0f;
            float side = (float)(OzKickflipCurve.ArcSideM * k), rise = (float)(OzKickflipCurve.ArcRiseM * k + OzKickflipCurve.HopPeakM), T = (float)OzKickflipCurve.TotalSec;
            var end = c + v * T;
            return DetailKit.OnScreen(cam, c, -0.06f) && DetailKit.OnScreen(cam, end, -0.04f) && DetailKit.OnScreen(cam, c + v * (0.5f * T) + Vector3.up * rise + KickLeadAhead(c + v * (0.5f * T), v, rise, 0.0), -0.04f)   // K423: with the lead
                   && DetailKit.OnScreen(cam, c + v * (0.31f * T) + right * side + Vector3.up * (rise * 0.5f) + KickLeadAhead(c + v * (0.31f * T), v, rise * 0.5f, side), -0.04f)
                   && DetailKit.OnScreen(cam, c + v * (0.69f * T) - right * side + Vector3.up * (rise * 0.5f) + KickLeadAhead(c + v * (0.69f * T), v, rise * 0.5f, -side), -0.04f);
        }

        // ================================================================== the jets' own puffs give way to the trails
        /// <summary>BeforeJets' end: while the trails burn, the jets' fire puffs and flame streaks at a small share (back after the jets).</summary>
        void KickTrailsScaleJets()
        {
            if (kickJetsScaled || kickTrailW <= 0.001f || !kickTrailMat) return;
            kickFire0 = OzShuttleJets.FireRate; kickFlame0 = OzShuttleJets.FlameRate; kickJetsScaled = true;
            float s = 1f - (1f - KickTrailJetsLeft) * Mathf.Clamp01(kickTrailW);
            OzShuttleJets.FireRate = kickFire0 * s; OzShuttleJets.FlameRate = kickFlame0 * s;
        }

        void KickTrailsUnscaleJets()
        {
            if (!kickJetsScaled) return;
            OzShuttleJets.FireRate = kickFire0; OzShuttleJets.FlameRate = kickFlame0; kickJetsScaled = false;
        }

        // ================================================================== one frame of the trails (AfterJets' start: still in the flipped pose)
        void KickTrailsTick(float dt)
        {
            KickTrailsUnscaleJets();
            bool live = kickJet != null && (kickJet[0].Live || kickJet[1].Live || kickPort[0].Live || kickPort[1].Live || kickPort[2].Live || kickPort[3].Live);
            var k = KickNow;
            bool burning = k.Active && (flip.Phase == OzKickflipPhase.Flip ? flip.T < OzKickflipCurve.TotalSec : flip.Phase == OzKickflipPhase.Settle);
            if (!live && !burning && kickTrailW <= 0f) return;                                          // nothing at all (the usual frame)
            KickTrailMats();
            if (!kickTrailMat) return;
            KickTrailsMake();
            kickTrailW = Mathf.MoveTowards(kickTrailW, burning ? 1f : 0f, dt / (burning ? KickTrailIn : KickTrailOut));
            bool paused = game && game.paused;
            bool emit = kickOnScreen && !paused && kickTrailW > 0.002f && jets != null && jets.Throttle > 0.01f && !(Obj == null || Obj.Destroyed || dying || crashing || BrokenUp);
            float f = (float)k.Flare;
            double w = kickTrailW * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(f));
            var axis = -transform.up; var carry = Velocity * KickTrailCarry;
            for (int s = 0; s < 2; s++)
            {
                var m = transform.TransformPoint(door ? (s == 0 ? door.NozzleL : door.NozzleR) : new Vector3(s == 0 ? 2.45f : -2.45f, 2.26f, -0.2f)) + axis * KickTrailStartM;
                kickJet[s].Step(dt, m.x, m.y, m.z, axis.x, axis.y, axis.z, KickTrailSpeed * (0.8f + 0.2f * (jets != null ? jets.Throttle : 1f)), carry.x, carry.y, carry.z, w, emit);
            }
            // the attitude jets: the pair that drives the roll (+: the +x pod's bottom port and the -x pod's top port), a burst at the spin-up / spin-down
            bool burst = (k.Events & (OzKickflipState.EvPuffUp | OzKickflipState.EvPuffDown)) != 0;
            if (burst) { kickBurst = 1f; kickBurstSign = PuffSign(k); }
            else kickBurst = Mathf.MoveTowards(kickBurst, 0f, dt / KickBurstSec);
            float pf = k.Active ? (float)k.Puff : 0f;
            float plusW = Mathf.Max(Mathf.Clamp01(pf), kickBurstSign > 0f ? kickBurst : 0f), minusW = Mathf.Max(Mathf.Clamp01(-pf), kickBurstSign < 0f ? kickBurst : 0f);
            var fx = OpenZFxPlayer.I; int born0 = kickPort[0].Born + kickPort[1].Born + kickPort[2].Born + kickPort[3].Born;
            for (int p = 0; p < 4; p++)
            {
                bool plusSide = p < 2, top = (p & 1) == 1;                                                  // 0 +x bottom, 1 +x top, 2 -x bottom, 3 -x top
                float pw = plusSide ? (top ? minusW : plusW) : (top ? plusW : minusW);
                var port = top ? KickPortTop : KickPortBottom; if (!plusSide) port.x = -port.x;
                var at = transform.TransformPoint(port); var dir = top ? transform.up : -transform.up;
                bool on = emit && k.Active && pw > 0.04f;
                kickPort[p].Step(dt, at.x, at.y, at.z, dir.x, dir.y, dir.z, KickStreakSpeed * (0.7f + 0.3f * pw), carry.x, carry.y, carry.z, Mathf.Clamp01(pw), on);
                if (on && kickBurst > 0.01f && fx && fx.isActiveAndEnabled && pw >= kickBurst - 1e-3f) fx.DrawHalo(at + dir * 0.15f, KickBurstGlowM, KickStreakHot, 1.4f * kickBurst);
            }
            KickPuffs += kickPort[0].Born + kickPort[1].Born + kickPort[2].Born + kickPort[3].Born - born0;
            // draw (on screen only)
            var cam = Camera.main;
            if (!cam || !DetailKit.OnScreen(cam, KickCentre, 0.3f)) return;
            var cp = cam.transform.position;
            bool any = false, anyStreak = false;
            float wid = KickTrailWidth * (1f + 0.35f * Mathf.Clamp01(f));
            for (int s = 0; s < 2; s++)
            {
                if (!KickRibbon(kickJet[s], kickTrailMesh[s], cp, wid, KickTrailGrow, KickTrailGlowWidth, KickTrailGlowGain, false)) continue;
                Graphics.DrawMesh(kickTrailMesh[s], Matrix4x4.identity, kickTrailMat, 0, null, 0, null, ShadowCastingMode.Off, false);
                any = true;
                if (emit)
                {
                    kickJet[s].Length(out double gap);
                    KickTrailMaxGap = Mathf.Max(KickTrailMaxGap, (float)gap); KickTrailMaxReach = Mathf.Max(KickTrailMaxReach, (float)kickJet[s].Reach());
                }
            }
            for (int p = 0; p < 4; p++)
            {
                if (!KickRibbon(kickPort[p], kickTrailMesh[2 + p], cp, KickStreakWidth, KickStreakGrow, 2.0f, 0.3f, true)) continue;
                Graphics.DrawMesh(kickTrailMesh[2 + p], Matrix4x4.identity, kickStreakMat, 0, null, 0, null, ShadowCastingMode.Off, false);
                anyStreak = true;
            }
            if (any) KickTrailFrames++;
            if (anyStreak) KickStreakFrames++;
        }

        /// <summary>One trail as a camera-facing ribbon mesh: a core strip and a glow strip (soft across, in the shader), the points joined
        /// smoothly (one Catmull-Rom point between each two), the width growing and the colour cooling and fading with the age.</summary>
        static bool KickRibbon(OzKickTrail tr, Mesh mesh, Vector3 cam, float w0, float grow, float glowW, float glowGain, bool streak)
        {
            int n = tr.Count;
            kickP.Clear(); kickPA.Clear();
            for (int i = 0; i < n; i++)
            {
                tr.Point(i, out double x, out double y, out double z, out double u, out double w);
                var q = new Vector3((float)x, (float)y, (float)z);
                if (kickP.Count > 0 && (q - kickP[kickP.Count - 1]).sqrMagnitude < 1e-4f) continue;          // a point on top of the last (the first frame)
                kickP.Add(q); kickPA.Add(new Vector2((float)u, (float)w));
            }
            if (kickP.Count < 2) { mesh.Clear(); return false; }
            kickV.Clear(); kickC.Clear(); kickUV.Clear(); kickT.Clear();
            int pts = kickP.Count;
            Vector3 lastSide = Vector3.right;
            for (int strip = 0; strip < 2; strip++)
            {
                int first = kickV.Count, made = 0;
                float ww = strip == 0 ? w0 : w0 * glowW, gain = strip == 0 ? 1f : glowGain;
                for (int i = 0; i < pts; i++)
                {
                    for (int half = 0; half < (i < pts - 1 ? 2 : 1); half++)
                    {
                        Vector3 p; Vector2 a; Vector3 tan;
                        if (half == 0) { p = kickP[i]; a = kickPA[i]; tan = kickP[Mathf.Min(pts - 1, i + 1)] - kickP[Mathf.Max(0, i - 1)]; }
                        else
                        {   // the Catmull-Rom middle of i .. i + 1
                            Vector3 p0 = kickP[Mathf.Max(0, i - 1)], p1 = kickP[i], p2 = kickP[i + 1], p3 = kickP[Mathf.Min(pts - 1, i + 2)];
                            p = 0.5625f * (p1 + p2) - 0.0625f * (p0 + p3); a = (kickPA[i] + kickPA[i + 1]) * 0.5f; tan = p2 - p1;
                        }
                        var side = Vector3.Cross(tan, cam - p);
                        if (side.sqrMagnitude < 1e-10f) side = lastSide; else { side.Normalize(); lastSide = side; }
                        float u = Mathf.Clamp01(a.x), width = ww * (1f + grow * u) * 0.5f;
                        Color c = streak ? Color.Lerp(KickStreakHot, KickStreakEnd, u)
                                         : (u < 0.25f ? Color.Lerp(KickTrailHot, KickTrailMid, u / 0.25f) : Color.Lerp(KickTrailMid, KickTrailEnd, (u - 0.25f) / 0.75f));
                        c.a = Mathf.Clamp01(a.y) * Mathf.Pow(1f - u, streak ? 1.2f : 1.6f) * gain;
                        kickV.Add(p - side * width); kickV.Add(p + side * width);
                        kickC.Add(c); kickC.Add(c);
                        kickUV.Add(new Vector2(u, 0f)); kickUV.Add(new Vector2(u, 1f));
                        made++;
                    }
                }
                for (int j = 0; j < made - 1; j++)
                {
                    int b = first + j * 2;
                    kickT.Add(b); kickT.Add(b + 2); kickT.Add(b + 1);
                    kickT.Add(b + 1); kickT.Add(b + 2); kickT.Add(b + 3);
                }
            }
            mesh.Clear();
            mesh.SetVertices(kickV); mesh.SetColors(kickC); mesh.SetUVs(0, kickUV); mesh.SetTriangles(kickT, 0, true);
            return true;
        }

        /// <summary>The long whoosh row of the roll (shuttle_flip_whoosh), the roll's short one when the table has not got it.</summary>
        string KickWhooshRow() { KickWhooshes++; return AudioDirector.Has("shuttle_flip_whoosh") ? "shuttle_flip_whoosh" : "shuttle_flip_roll"; }
    }
}
