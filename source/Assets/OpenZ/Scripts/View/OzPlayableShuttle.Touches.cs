using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's touches (rule DV118, K256g, view only; the twin tools/spring_twin.py of the kit holds the same numbers):
    /// <list type="bullet">
    /// <item>
    /// At every normal touchdown (and on a repair station's deck) the cabin dips <see cref="SpringDipM"/> and springs back, a
    /// damped spring (<see cref="SpringHz"/>, damping <see cref="SpringZeta"/>) pushed by a short half-sine (<see cref="SpringPushSec"/>):
    /// the first dip 0.146 s after the contact, one rebound 0.08 m up at 0.41 s, a last 0.03 m down, settled (under 1 cm for good) at
    /// 0.97 s. The four legs swing out about their tops (where they meet the pods: measured from the model) by up to
    /// <see cref="SpringSpreadDeg"/> with the dip, and each strut slides up into its pod by the dip less the lift the swing gives its pad,
    /// so the pads stay on the ground (within a millimetre in the twin) and only spread out by about 0.1 m. A soft spring creak
    /// (shuttle_leg_spring: the suspension creaks, lower) at the dip and a quieter one at the rebound. The crash landing's final settle (the
    /// moment the hydraulic has levelled it) gets the same at <see cref="SpringCrashK"/>. CALM SCREEN: half. A take-off right away lets it
    /// go in 0.15 s. The pads' lamps and neon rings ride on their legs (SplitFootParts).</item>
    /// <item>
    /// rules' ShuttleAirborne), 1 standing, eased over <see cref="GroundFadeSec"/> from the moment it touches down or lifts off. Its smoothstep
    /// k is the ship's own sun / moon shadow (OzShuttleRealShadow: off in the air, a dithered caster in between) and 1 - k the landing mark's
    /// weight (OzShuttleShadow): at a touchdown the real shadow eases in while the mark eases out, at a take-off the other way round.</item>
    /// <item>K256g2, SHOT DOWN IN THE AIR: during the dying drift (hit .. the drawn kill) the mark keeps following the drifting ship
    /// (<see cref="TouchesDying"/>; it froze where it was hit). At the drawn kill the mark and the real shadow are handed over
    /// (<see cref="TouchesHandOver"/>, OzShuttleShadowHandOver): over <see cref="OzShuttleShadowHandOver.Sec"/> the mark (1 - k) follows the
    /// falling hull or the pieces' middle while their real shadows come in (k, the same dithered caster), the two always adding up to
    /// one; before, the mark went in one frame and the full shadow came at once.</item>
    /// <item>K256g2, ITS OWN SHADOW ON ITSELF IN THE AIR: while its real shadow is off or fading (k under 1) the ship draws its own
    /// small shadow map along the main light (OzShuttleSelfShadow) that only its own renderers read, so the pods still shade the hull
    /// up there and the ground under it still gets no real shadow.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>The springs: the cabin's first dip (m), the spring (Hz), its damping, the push (s), the legs' spread at the full dip (deg),
        /// the crash's final settle (x), how long it runs at most (s), the let-go at a take-off (s).</summary>
        public static float SpringDipM = 0.22f, SpringHz = 2.0f, SpringZeta = 0.30f, SpringPushSec = 0.08f, SpringSpreadDeg = 5f, SpringCrashK = 0.45f, SpringSec = 2.0f, SpringLetGoSec = 0.15f;
        /// <summary>The cross-fade of the ship's own shadow and its landing mark at a touchdown and a take-off (s).</summary>
        public static float GroundFadeSec = 0.5f;
        /// <summary>The top of each leg (where it meets its pod: the middle of its vertices above 1.1 m) and the middle of its pad sole, in the
        /// root's space (FL, FR, BL, BR as <see cref="LegNames"/>; measured from L16_Shuttle.fbx, tools/leg_pivots.py).</summary>
        static readonly Vector3[] LegTops = { new Vector3(1.862f, 1.209f, 0.802f), new Vector3(-1.793f, 1.212f, 0.834f), new Vector3(1.419f, 1.201f, -2.547f), new Vector3(-1.413f, 1.187f, -2.552f) };
        static readonly Vector3[] LegPads = { new Vector3(2.208f, 0f, 1.088f), new Vector3(-2.202f, 0f, 1.089f), new Vector3(1.826f, 0f, -2.826f), new Vector3(-1.828f, 0f, -2.828f) };
        const int SpringRate = 240, SpringSub = 10;
        static float[] springCurve; static float springReboundAt;

        /// <summary>Proof counters: springs started (normal, crash settle), creaks played; the last spring's deepest dip (m), its rebound
        /// up (m), its widest spread (deg), when it settled (s after it began, under 1 cm for good), the worst pad sole off the ground
        /// (m, the legs' own geometry: 0 = planted); frames with the ship's own shadow on while it was 2.5 m or more up; frames where both
        /// the real shadow and the mark were over 0.75 (never two full shadows).</summary>
        public static int SpringsStarted, SpringsCrash, SpringCreaks, AirOwnShadowFrames, TwoShadowFrames;
        public static float SpringDipSeen, SpringReboundSeen, SpringSpreadSeen, SpringSettleSeen, SpringPadOffMax;
        /// <summary>The cabin's dip now (m, + = down) and whether a spring runs; the ground share (0 air .. 1 standing) and the ship's own shadow.</summary>
        public float SpringDipNow => springDipNow;
        public bool SpringOn => springOn;
        public float GroundShare => groundK;
        public OzShuttleRealShadow RealShadow => realShadow;

        float springT, springK, springFade = 1f, springDipNow, springLastBig; int springEv; bool springOn, legRotSet, groundSet;
        float groundK = 1f; OzShuttleRealShadow realShadow;
        OzShuttleSelfShadow selfShadow; Vector3 dyingLast, dyingVelS; bool dyingSeen;                        // K256g2
        /// <summary>K256g2: the ship's own self-shadow in the air (null until it first flies).</summary>
        public OzShuttleSelfShadow SelfShadow => selfShadow;
        readonly Quaternion[] legRestRot = new Quaternion[4];

        /// <summary>The spring's dip curve (first peak = 1) at SpringRate Hz: semi-implicit Euler at SpringRate x SpringSub Hz (the twin's).</summary>
        static void SpringBuild()
        {
            int n = Mathf.CeilToInt(SpringSec * SpringRate) + 1; var c = new float[n];
            double w = 2.0 * System.Math.PI * SpringHz, h = 1.0 / (SpringRate * SpringSub), x = 0.0, v = 0.0, pk = 0.0;
            for (int i = 1; i < n; i++)
            {
                for (int s = 0; s < SpringSub; s++)
                {
                    double tt = ((i - 1) * SpringSub + s) * h, f = tt < SpringPushSec ? System.Math.Sin(System.Math.PI * tt / SpringPushSec) : 0.0;
                    double a = -w * w * x - 2.0 * SpringZeta * w * v + f;
                    v += a * h; x += v * h;
                }
                c[i] = (float)x; if (x > pk) pk = x;
            }
            int ip = 0; for (int i = 1; i < n; i++) if (c[i] > c[ip]) ip = i;
            int iu = ip; for (int i = ip; i < n; i++) if (c[i] < c[iu]) iu = i;
            for (int i = 0; i < n; i++) c[i] = pk > 0 ? (float)(c[i] / pk) : 0f;
            springCurve = c; springReboundAt = iu / (float)SpringRate;
        }

        static float SpringCurve(float t)
        {
            if (springCurve == null) SpringBuild();
            float f = t * SpringRate; int i = Mathf.FloorToInt(f);
            if (i < 0) return 0f;
            if (i >= springCurve.Length - 1) return springCurve[springCurve.Length - 1];
            return Mathf.Lerp(springCurve[i], springCurve[i + 1], f - i);
        }

        /// <summary>A touchdown on its legs (k = 1: a normal landing or the repair deck; SpringCrashK: the crash's final settle).</summary>
        void SpringBegin(float k)
        {
            if (springCurve == null) SpringBuild();
            springOn = true; springT = 0f; springK = k; springFade = 1f; springEv = 0; springLastBig = 0f;
            SpringsStarted++; if (k < 1f) SpringsCrash++;
            SpringDipSeen = 0f; SpringReboundSeen = 0f; SpringSpreadSeen = 0f; SpringSettleSeen = -1f; SpringPadOffMax = 0f;
            Creak(k);
        }

        void Creak(float vol)
        {
            if (!AudioDirector.Has("shuttle_leg_spring")) return;
            AudioDirector.Fire("shuttle_leg_spring", transform.position + Vector3.up * 0.6f, Obj != null ? Obj.Id : -1, vol);
            LastSound = "shuttle_leg_spring"; SpringCreaks++;
        }

        /// <summary>Landed(): a normal landing or the repair deck springs at once; a crash landing springs lightly when it stands level
        /// (OzPlayableShuttle.Crash.cs).</summary>
        void SpringLanded(bool rough) { if (!rough) SpringBegin(1f); }

        /// <summary>Pose(), before the root is placed: the cabin's dip now (m, + = down) on the game's clock.</summary>
        float SpringStep(float dt, ZShuttleState st)
        {
            if (!springOn) { springDipNow = 0f; return 0f; }
            springT += dt;
            if (st != ZShuttleState.Landed) springFade = Mathf.MoveTowards(springFade, 0f, dt / Mathf.Max(0.02f, SpringLetGoSec));   // a take-off right away: let go
            float d = SpringDipM * springK * SpringCurve(springT) * springFade * (OzMotion.Reduced ? 0.5f : 1f);
            if ((springEv & 1) == 0 && springT >= springReboundAt) { springEv |= 1; if (springFade > 0.5f) Creak(0.55f * springK); }   // the quieter creak at the rebound's top
            SpringDipSeen = Mathf.Max(SpringDipSeen, d); SpringReboundSeen = Mathf.Max(SpringReboundSeen, -d);
            if (Mathf.Abs(d) >= 0.01f) springLastBig = springT;
            if (springT >= SpringSec || springFade <= 0f)
            {
                springOn = false; d = 0f; SpringSettleSeen = springLastBig;
            }
            springDipNow = d;
            return d;
        }

        /// <summary>Pose(), the legs (after the root is placed): leg i's spring swing about its top and its strut slide, plus a crash
        /// landing's strut stroke (crash, m up into the pod along the hull's up); its rest pose when neither runs.</summary>
        void LegPose(int i, Transform lg, float crash)
        {
            if (!legRotSet) { for (int k = 0; k < 4; k++) legRestRot[k] = legs[k] ? legs[k].localRotation : Quaternion.identity; legRotSet = true; }
            float d = springDipNow; var par = lg.parent;
            if ((d == 0f && crash <= 0f) || !par) { lg.localPosition = legRest[i]; lg.localRotation = legRestRot[i]; return; }
            Vector3 top = LegTops[i], lean = LegPads[i] - top; float L = lean.magnitude;
            var hz = new Vector3(lean.x, 0f, lean.z); float hn = hz.magnitude; hz = hn > 1e-4f ? hz / hn : Vector3.right;
            float lean0 = Mathf.Atan2(hn, -lean.y);
            float a = SpringDipM > 1e-4f ? SpringSpreadDeg * Mathf.Max(0f, d) / SpringDipM : 0f;            // out with the dip (5 deg at a normal landing's full dip)
            float rise = L * (Mathf.Cos(lean0) - Mathf.Cos(lean0 + a * Mathf.Deg2Rad));                      // the swing lifts its pad this much
            float slide = d - rise;                                                                       // so the strut slides up the rest: the pad stays down
            var axisRoot = Vector3.Cross(hz, Vector3.up);                                                 // its foot swings outward
            var topP = par.InverseTransformPoint(transform.TransformPoint(top));
            var axisP = par.InverseTransformDirection(transform.TransformDirection(axisRoot));
            var q = Quaternion.AngleAxis(a, axisP);
            lg.localPosition = topP + q * (legRest[i] - topP) + par.InverseTransformVector(transform.up * (slide + crash));
            lg.localRotation = q * legRestRot[i];
            if (springOn)
            {   // the proof: the pad sole's place over the ground under it (the root dipped d, the sole swung and slid up)
                var sole = top + Quaternion.AngleAxis(a, axisRoot) * lean; sole.y += slide;
                SpringPadOffMax = Mathf.Max(SpringPadOffMax, Mathf.Abs(sole.y - d)); SpringSpreadSeen = Mathf.Max(SpringSpreadSeen, a);
            }
        }

        /// <summary>LateUpdate, instead of the mark's own call: the ground share eased, the ship's own shadow (k) and the landing mark (1 - k).</summary>
        void TouchesShadows(float rdt, float dt)
        {
            bool ground = !Obj.ShuttleAirborne;
            if (!groundSet) { groundK = ground ? 1f : 0f; groundSet = true; }
            groundK = Mathf.MoveTowards(groundK, ground ? 1f : 0f, dt / Mathf.Max(0.05f, GroundFadeSec));
            float k = groundK * groundK * (3f - 2f * groundK);
            realShadow ??= new OzShuttleRealShadow(transform);
            realShadow.Step(k);
            shadow.GroundShare = groundK;
            shadow.Step(rdt, ShownHeight, FeetHeight, Obj.ShuttleState == ZShuttleState.Hover && !string.IsNullOrEmpty(Obj.ShuttleNoLand), true);
            if (FeetHeight >= 2.5f && realShadow.K > 0.01f) AirOwnShadowFrames++;
            if (realShadow.K > 0.75f && shadow.Shown && shadow.MarkWeight > 0.75f) TwoShadowFrames++;
            if (k < 0.999f && !selfShadow) selfShadow = OzShuttleSelfShadow.For(this);                  // K256g2: its own shadow on itself while the real one is off
            if (selfShadow) selfShadow.Feed(realShadow, transform);
        }

        /// <summary>K256g2, LateUpdate while it drifts shot down (until the drawn kill): the real shadow stays as it was (off in the air),
        /// the mark follows the drifting ship (its drawn velocity from its own moves), no red, no aim.</summary>
        void TouchesDying(float rdt, float dt)
        {
            if (shadow == null) return;
            realShadow ??= new OzShuttleRealShadow(transform);
            float k = groundK * groundK * (3f - 2f * groundK);
            realShadow.Step(k);
            var p = transform.position;
            if (!dyingSeen) { dyingVelS = Velocity; dyingSeen = true; }
            else if (rdt > 1e-4f) dyingVelS = Vector3.Lerp(dyingVelS, (p - dyingLast) / rdt, 1f - Mathf.Exp(-rdt * 12f));
            dyingLast = p;
            shadow.GroundShare = groundK;
            shadow.StepDown(rdt, p, dyingVelS, float.NaN);
            if (selfShadow) selfShadow.Feed(realShadow, transform);
        }

        /// <summary>K256g2, the drawn kill (OnBrokenUp): standing (its real shadow full, no mark) as before: the mark goes, the renderers
        /// cast as they always did. Else the mark and the real shadow go to an OzShuttleShadowHandOver that crosses them over
        /// <see cref="OzShuttleShadowHandOver.Sec"/> from where the cross-fade stands now.</summary>
        void TouchesHandOver(bool inAir)
        {
            springOn = false; springDipNow = 0f; dyingSeen = false;
            if (selfShadow && Obj != null)
            {
                // switch, map and matrix while it flew: off on the pieces, so the wreck never reads the gone ship's frozen map
                var hgo = GameObject.Find("OZ_ShuttleBreakup_" + Obj.Id);
                if (hgo) OzShuttleSelfShadow.Scrub(hgo.transform);
            }
            float k = groundK * groundK * (3f - 2f * groundK);
            bool markOn = shadow != null && shadow.Shown && shadow.MarkWeight > 0.001f;
            if (!OzShuttleShadowHandOver.Enabled || shadow == null || (k >= 0.999f && !markOn))
            {
                shadow?.Dispose(); TouchesStop(); return;
            }
            var rs = realShadow ?? new OzShuttleRealShadow(transform);
            realShadow = null;                                                                             // the hand-over owns it now (its end restores the renderers)
            OzShuttleShadowHandOver.Begin(this, shadow, rs, selfShadow, groundK, Obj != null ? Obj.Id : -1, inAir);
        }

        /// <summary>A break-up, the view going away: the ship's renderers cast as they always did.</summary>
        void TouchesStop() { realShadow?.Dispose(); realShadow = null; springOn = false; springDipNow = 0f; if (selfShadow) selfShadow.Stop(); }
    }
}
