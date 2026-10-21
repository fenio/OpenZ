using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's night lights (rule DV118, view only), drawn from what OzShuttleNightPlan decides each frame (the plan holds
    /// every rule and number; this file only builds and draws). Made by OzPlayableShuttle at its bind for every drawn shuttle (level 16's
    /// own ship and the ones the testing panel drops), held by its belly lamps (OzShuttleSearchlights.Night), stepped once a frame just
    /// before them.
    /// <list type="bullet">
    /// <item>(a) THE LANDING LIGHTS are the two belly lamps of OzShuttleSearchlights: this file gives them the plan's rule (when they
    /// burn) and turns their heads onto the landing point while the ship comes down or flies in to it (one lamp a little to each side,
    /// eased, never more than <see cref="LandingMaxAimDeg"/> from its housing's direction), else straight down.</item>
    /// <item>(b) THE RAMP LIGHT: a warm floodlight in the head of the back doorway, shining out over the ramp and its foot while the ramp
    /// is down: its lens, halo, a faint cone of light and the pool of light on the ground (the night lights' own pieces).</item>
    /// <item>(c, d) THE NOSE SEARCHLIGHT: the game's own searchlight head (the fort towers' drum and yoke, NightMeshes, at
    /// <see cref="NoseScale"/> of their size) hanging under the nose on a short post: lens, halo, beam cone and ground pool, the drum
    /// turned and tilted as the plan says (the slow sweep ahead, or following the enemy it has caught); calm white, or alarming
    /// (narrow, hard-edged, red-amber, a slow pulse; no pulse with CALM MODE) with the lock-on beep (<see cref="LockEvent"/>).</item>
    /// <item>REAL LIGHTS: each lamp owns one spot light that it offers to the night lights' budget (NightBudgetShare, as the belly lamps,
    /// the field searchlight and the alarm beacons do): a real light only inside the level's count; not granted, its painted pool carries
    /// the light. LOW (fewer lights): the second landing lamp and the ramp light are not offered (lens and painted pool only). The nose
    /// searchlight is a shadow candidate (the budget's searchlight shadows, the NIGHT LIGHT SHADOWS option) except on LOW.</item>
    /// <item>SHOT DOWN: everything goes out at once with the belly lamps (their Kill).</item>
    /// </list>
    /// </summary>
    public sealed class OzShuttleNightLights
    {
        // ------------------------------------------------------------------ the look (numbers to tune by eye in the game)
        /// <summary>The nose searchlight: the drum's axis in the ship's root space (under the nose), the head's size (x the fort's), reach
        /// (m), strength, rank in the budget.</summary>
        public static Vector3 NosePivot = new Vector3(0f, 0.80f, 2.45f);
        public static float NoseScale = 0.45f, NoseRange = 42f, NoseIntensity = 300f, NosePriority = 6f;
        /// <summary>The ramp light: in the doorway's head (root space), its cone (degrees), reach (m), strength, rank, cone of light share; colour.</summary>
        public static Vector3 RampLampAt = new Vector3(0f, 3.47f, -2.75f);
        public static float RampAngle = 85f, RampRange = 10f, RampIntensity = 60f, RampPriority = 5f, RampBeam = 0.25f;   // K256e: was 9 m / 40 (a faint smudge at 70 m)
        public static Color RampColour = new Color(1f, 0.78f, 0.52f, 1f);
        /// <summary>K256e: the amber lamp lens over the doorway (its size in m, its glow, the halo's size and gain), so the lamp itself reads
        /// at 70 m (FIX_LIST M14); its colour.</summary>
        public static float RampLensM = 0.24f, RampLensGlow = 9f, RampHaloM = 1.1f, RampHaloGain = 2.6f;
        public static Color RampLensColour = new Color(1f, 0.55f, 0.18f, 1f);
        /// <summary>K256e: the bay lamp in the doorway (ShuttleRearDoor's own light): at least this strong and this far (m) (FIX_LIST M14:
        /// 2.4 -> 3.6, 3.2 -> 4.2); raised at the bind only where the door's own numbers are lower.</summary>
        public static float BayLampIntensity = 3.6f, BayLampRange = 4.2f;
        /// <summary>K256e: the lights' switch click: at most one per ship within ClickGapSec (one click for one change of the lights,
        /// FIX_LIST M15: 35 clicks in 135 s), on / off loudness (x the row; at least 0.12 in the log).</summary>
        public static float ClickGapSec = 1.5f, ClickOnVol = 1f, ClickOffVol = 0.8f;
        public static int Clicks, ClicksMerged;
        /// <summary>The landing lamps' aim: each lamp this far to its side of the landing point (m), at most this far off its housing's
        /// direction (degrees), turning at this rate (1/s).</summary>
        public static float LandingSpreadM = 3.0f, LandingMaxAimDeg = 55f, LandingAimRate = 5f;   // K256e: one lamp onto the pad's front, one onto its rear (was 0.8 m to each side: one cone)
        /// <summary>Where the beam looks on what it follows: this high over the ground (m) for a robot, a vehicle, a gun; a flying ship's middle.</summary>
        public static float AimRobotM = 0.8f, AimVehicleM = 1.0f, AimGunM = 1.2f, AimShipM = 2.5f;
        /// <summary>The lock-on beep (sound table row; the game's own weapon lock is played when the row is missing).</summary>
        public static string LockEvent = "shuttle_light_lock";
        /// <summary>Tests only: the preset the lights act on (-1 = the game's GRAPHICS QUALITY; 2 = LOW).</summary>
        public static int ProofPreset = -1;

        // ------------------------------------------------------------------ proof counters (every ship)
        public static int Built, Frames, DayLitFrames, NoseOnFrames, RampOnFrames, LandingOnFrames, LandingAimFrames, AlarmFrames, PulseDipFrames,
                          CalmAlarmFrames, CalmPulseFrames, BeepsPlayed, RealNoseFrames, PoolNoseFrames, RealRampFrames, PoolRampFrames, ShadowNoseFrames, Kills,
                          NoseClicks, RampClicks, ReleaseClicks;
        /// <summary>The landing lamps' worst look off their landing point while it came down (degrees, the frames they aimed at it).</summary>
        public static float WorstLandingOffDeg;

        // ------------------------------------------------------------------ one ship
        readonly OzPlayableShuttle ship; readonly ZObject obj; readonly Transform root; readonly ShuttleRearDoor door; readonly OzShuttleSearchlights landing;
        readonly OzShuttleNightPlan plan = new OzShuttleNightPlan();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        sealed class Lamp
        {
            public Transform head, beamT, poolT; public Renderer lens, halo, beam, pool; public Light light; public NightFixture fix; public bool offered;
            public float lensShown = -1f, haloShown = -1f, beamShown = -1f, poolShown = -1f, real;
        }
        readonly Lamp nose = new Lamp(), ramp = new Lamp();
        Transform noseYaw, nosePitch;
        int lightsPreset = -1, shadowPreset = -1; bool dead; ZObject lockObj; float noseWas, rampWas, dayT; int releasesSeen;
        float lastClick = -100f;                                                                          // K256e: the ship's last switch click

        public OzShuttleNightPlan Plan => plan;
        /// <summary>The enemy the nose searchlight follows (null: none).</summary>
        public ZObject LockTarget => plan.LockId >= 0 ? lockObj : null;
        public Transform NoseHead => nosePitch;
        public Light NoseLight => nose.light;
        public Light RampLight => ramp.light;
        public NightFixture NoseFixture => nose.fix;
        public NightFixture RampFixture => ramp.fix;
        public bool Dead => dead;
        /// <summary>K256e proof: the nose searchlight's beam cone, its painted pool and its lens drawn now.</summary>
        public bool NoseBeamShown => nose.beam && nose.beam.enabled;
        public bool NosePoolShown => nose.pool && nose.pool.enabled;
        public bool NoseLensShown => nose.lens && nose.lens.enabled;
        /// <summary>Real lights this ship offers to the night budget now (the belly lamps included) and its shadow candidates.</summary>
        public int OfferedNow => (landing != null ? (landing.OfferedNow(0) ? 1 : 0) + (landing.OfferedNow(1) ? 1 : 0) : 0) + (ramp.offered ? 1 : 0) + (nose.offered ? 1 : 0);
        public int ShadowCandidatesNow => (nose.fix != null && nose.fix.ShadowCandidate ? 1 : 0) + (ramp.fix != null && ramp.fix.ShadowCandidate ? 1 : 0);

        public OzShuttleNightLights(OzPlayableShuttle ship, ShuttleRearDoor door, OzShuttleSearchlights landing)
        {
            this.ship = ship; this.door = door; this.landing = landing;
            obj = ship ? ship.Obj : null; root = ship ? ship.transform : null;
            if (!root || obj == null) { dead = true; return; }
            int layer = root.gameObject.layer;
            // the nose searchlight: the fort's head hanging under the nose (the yoke turned over, its turntable under a short post)
            var H = NightMeshes.Housing();
            noseYaw = new GameObject("OZ_FxNoseYaw") { layer = layer }.transform; noseYaw.SetParent(root, false);
            noseYaw.localPosition = NosePivot; noseYaw.localRotation = Quaternion.identity; noseYaw.localScale = Vector3.one * NoseScale;
            Layer(NightMeshes.Part("OZ_FxNosePost", noseYaw, NightMeshes.Cube(), H, true, new Vector3(0f, 0.71f, 0f), Quaternion.identity, new Vector3(0.16f, 0.76f, 0.16f)), layer);
            Layer(NightMeshes.Part("OZ_FxNoseYoke", noseYaw, NightMeshes.SearchYoke(), H, true, Vector3.zero, Quaternion.Euler(0f, 0f, 180f), Vector3.one), layer);
            nosePitch = new GameObject("OZ_FxNosePitch") { layer = layer }.transform; nosePitch.SetParent(noseYaw, false); nosePitch.localPosition = new Vector3(0f, -0.02f, 0f);
            Layer(NightMeshes.Part("OZ_FxNoseDrum", nosePitch, NightMeshes.SearchDrum(), H, true, Vector3.zero, Quaternion.identity, Vector3.one), layer);
            Layer(NightMeshes.Part("OZ_FxNoseGlass", nosePitch, NightMeshes.Disc(), NightMeshes.DarkGlass(), false, new Vector3(0f, 0f, 0.335f), Quaternion.identity, new Vector3(0.31f, 0.31f, 1f)), layer);
            nose.head = nosePitch;
            nose.lens = Layer(NightMeshes.Part("OZ_FxNoseLens", nosePitch, NightMeshes.Disc(), NightMeshes.Lens(), false, new Vector3(0f, 0f, 0.345f), Quaternion.identity, new Vector3(0.3f, 0.3f, 1f)), layer).GetComponent<Renderer>();
            nose.halo = Layer(NightMeshes.Part("OZ_FxNoseHalo", nosePitch, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0f, 0f, 0.5f), Quaternion.identity, Vector3.one * 1.3f), layer).GetComponent<Renderer>();
            var nb = Layer(NightMeshes.Part("OZ_FxNoseBeam", nosePitch, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0f, 0f, 0.36f), Quaternion.identity, Vector3.one), layer);
            nose.beam = nb.GetComponent<Renderer>(); nose.beamT = nb.transform;
            nose.light = MakeLight(nosePitch, new Vector3(0f, 0f, 0.38f), CalmColour, NoseRange, OzShuttleNightPlan.NoseAngleDeg, 0.6f);
            // the ids begin "vehicle_" as the headlights' do: never one of the NEARBY LIGHTS shadows (the nose searchlight's shadows are the
            // budget's searchlight shadows, off on LOW; the ramp light casts none)
            nose.fix = new NightFixture
            {
                Kind = NightKind.Searchlight, Id = "vehicle_shuttle_nose_searchlight_" + obj.Id, Light = nose.light, Root = root, Col = nose.light.color, ColLinear = nose.light.color.linear,
                Intensity = NoseIntensity, Range = NoseRange, Angle = OzShuttleNightPlan.NoseAngleDeg, Priority = NosePriority, ShadowCandidate = false, BeamK = 1f, PoolK = 1f,
            };
            nose.poolT = Pool("OZ_FxNosePool_" + obj.Id, out nose.pool);
            nosePitch.localRotation = Quaternion.Euler(plan.PitchNow, 0f, 0f);
            // the ramp light: a warm flood in the doorway's head (only its glowing parts: nothing stands in the closed ramp's way)
            ramp.head = new GameObject("OZ_FxRampLamp") { layer = layer }.transform; ramp.head.SetParent(root, false);
            ramp.head.localPosition = RampLampAt; ramp.head.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.8f, -0.6f), Vector3.up);
            ramp.lens = Layer(NightMeshes.Part("OZ_FxRampLens", ramp.head, NightMeshes.Disc(), NightMeshes.Lens(), false, new Vector3(0f, 0f, 0.01f), Quaternion.identity, new Vector3(RampLensM, RampLensM, 1f)), layer).GetComponent<Renderer>();   // K256e: the amber lens, bigger
            ramp.halo = Layer(NightMeshes.Part("OZ_FxRampHalo", ramp.head, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0f, 0f, 0.12f), Quaternion.identity, Vector3.one * RampHaloM), layer).GetComponent<Renderer>();
            var rb = Layer(NightMeshes.Part("OZ_FxRampBeam", ramp.head, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0f, 0f, 0.02f), Quaternion.identity, Vector3.one), layer);
            ramp.beam = rb.GetComponent<Renderer>(); ramp.beamT = rb.transform;
            ramp.light = MakeLight(ramp.head, new Vector3(0f, 0f, 0.04f), RampColour, RampRange, RampAngle, 0.3f);
            ramp.light.innerSpotAngle = RampAngle * 0.45f;
            ramp.fix = new NightFixture
            {
                Kind = NightKind.Flood, Id = "vehicle_shuttle_ramp_light_" + obj.Id, Light = ramp.light, Root = root, Col = RampColour, ColLinear = RampColour.linear,
                Intensity = RampIntensity, Range = RampRange, Angle = RampAngle, Priority = RampPriority, ShadowCandidate = false, BeamK = RampBeam, PoolK = 1f,
            };
            ramp.poolT = Pool("OZ_FxRampPool_" + obj.Id, out ramp.pool);
            nose.lens.enabled = nose.halo.enabled = nose.beam.enabled = ramp.lens.enabled = ramp.halo.enabled = ramp.beam.enabled = false;
            if (landing != null) landing.KeepOffNearbyShadows();
            ApplyPreset();
            BayLamp();                                                                                    // K256e: the doorway's bay lamp at least 3.6 / 4.2 m
            OzShuttleRampPaint.Apply(root);                                                               // K256e (S2): the ramp's inner face in light metal with hazard edges
            Built++;
        }

        static GameObject Layer(GameObject go, int layer) { go.layer = layer; return go; }
        static Color CalmColour => new Color(OzShuttleNightPlan.CalmRgb[0], OzShuttleNightPlan.CalmRgb[1], OzShuttleNightPlan.CalmRgb[2], 1f);

        Transform Pool(string name, out Renderer r)
        {
            var pg = NightMeshes.Part(name, null, NightMeshes.Cube(), NightMeshes.Pool(), false, Vector3.zero, Quaternion.identity, Vector3.one);
            r = pg.GetComponent<Renderer>(); r.enabled = false;
            return pg.transform;
        }

        // the searchlights' shadow maps at the medium tier, as the night lights make theirs (the field is serialized, its setter is not public)
        static readonly System.Reflection.FieldInfo TierField = typeof(UniversalAdditionalLightData).GetField("m_AdditionalLightsShadowResolutionTier",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        static Light MakeLight(Transform parent, Vector3 at, Color c, float range, float angle, float nearPlane)
        {
            var go = new GameObject("Light") { layer = parent.gameObject.layer }; go.transform.SetParent(parent, false); go.transform.localPosition = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.range = range; l.color = c; l.intensity = 0f; l.enabled = false;
            l.shadows = LightShadows.None; l.shadowStrength = 0.88f; l.renderMode = LightRenderMode.ForcePixel;
            l.spotAngle = Mathf.Clamp(angle, 1f, 179f); l.innerSpotAngle = l.spotAngle * 0.35f; l.shadowNearPlane = nearPlane;
            try
            {
                var ald = l.GetUniversalAdditionalLightData();
                if (ald && TierField != null) TierField.SetValue(ald, UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium);
            }
            catch { }
            return l;
        }

        // ------------------------------------------------------------------ the preset: which lamps are real lights, which may cast shadows
        void ApplyPreset()
        {
            if (dead) return;
            int p = ProofPreset >= 0 ? ProofPreset : OzLite.Active ? OzLite.Level : 0;
            int lights = p >= 2 && (ProofPreset >= 0 || OzLite.Cut(OzLiteTable.CutNightLights)) ? 2 : 0;
            int shadows = p >= 2 && (ProofPreset >= 0 || OzLite.Cut(OzLiteTable.CutNightShadows)) ? 2 : 0;
            if (lights == lightsPreset && shadows == shadowPreset) return;
            lightsPreset = lights; shadowPreset = shadows;
            if (landing != null) { landing.Offer(0, OzShuttleNightPlan.Offered(OzShuttleNightPlan.LandingL, lights)); landing.Offer(1, OzShuttleNightPlan.Offered(OzShuttleNightPlan.LandingR, lights)); }
            Offer(ramp, OzShuttleNightPlan.Offered(OzShuttleNightPlan.RampLamp, lights));
            Offer(nose, OzShuttleNightPlan.Offered(OzShuttleNightPlan.NoseLamp, lights));
            nose.fix.ShadowCandidate = OzShuttleNightPlan.ShadowCandidate(OzShuttleNightPlan.NoseLamp, shadows);
            ramp.fix.ShadowCandidate = OzShuttleNightPlan.ShadowCandidate(OzShuttleNightPlan.RampLamp, shadows);
        }

        static void Offer(Lamp L, bool on)
        {
            if (L.fix == null) return;
            bool has = NightBudgetShare.Fixtures.Contains(L.fix);
            if (on && !has) NightBudgetShare.Fixtures.Add(L.fix);
            else if (!on && has) { NightBudgetShare.Fixtures.Remove(L.fix); L.fix.RealK = 0f; L.fix.ShadowK = 0f; L.fix.Budgeted = false; }
            L.offered = on;
        }

        /// <summary>The belly lamps' rule (called inside their Step): they burn when the plan wants the landing lights, at its level.</summary>
        public void LandingWant(ref bool want, ref float share)
        {
            if (dead) return;                                                                      // (never built: the lamps' own rule)
            share = plan.LandingTarget; want = share > 0f;
        }

        // ------------------------------------------------------------------ per frame (OzPlayableShuttle.LateUpdate, before the belly lamps)
        public void Step(float dt)
        {
            if (dead || obj == null || !root) return;
            var g = ZGame.I; var W = g ? g.World : null; if (W == null) return;
            Frames++;
            bool alive = !obj.Destroyed && W.Get(obj.Id) == obj && !OzShuttleEmptyLook.Dark(obj);   // K256f: an empty ship has no pilot to work its lights (View/OzShuttleEmptyLook.cs)
            // the followed enemy as drawn now; every enemy in view when the plan looks round
            if (plan.LockId >= 0)
            {
                var t = W.Get(plan.LockId);
                if (Enemy(t) && Measure(g, t, out var s)) { plan.LockSeen = true; plan.LockDistM = s.DistM; plan.LockYawDeg = s.YawDeg; plan.LockPitchDeg = s.PitchDeg; lockObj = t; }
                else plan.LockSeen = false;
            }
            if (plan.ScanDue) Scan(g, W);
            ApplyPreset();
            var p = root.position;
            float goal = -1f; Vector3 goalW = p;
            if (obj.ShuttleGoalX >= 0 && obj.ShuttleViaCount == 0) { goalW = ZGame.PxToWorld(obj.ShuttleGoalX, obj.ShuttleGoalY); goal = new Vector2(goalW.x - p.x, goalW.z - p.z).magnitude; }   // K410 (DV125): no landing approach while middle points remain
            var f = new OzShuttleNightPlan.Frame
            {
                Dt = dt, RealDt = Mathf.Min(Time.deltaTime, 0.05f), State = (int)obj.ShuttleState, Airborne = obj.ShuttleAirborne, Alive = alive, FeetM = ship.FeetHeight, GoalDistM = goal,
                RampOpen01 = door ? door.Open01 : 0f, NightK = Mathf.Clamp01(NightGrade.NightK), Calm = OzMotion.Reduced,
            };
            plan.Step(f);
            if (plan.LockId < 0) lockObj = null;
            else if (lockObj == null || lockObj.Id != plan.LockId) lockObj = W.Get(plan.LockId);
            // (a) the landing lamps onto the landing point (or straight down)
            var at = plan.LandingAtGoal ? goalW : new Vector3(p.x, 0f, p.z);
            at.y = Base(g, at);
            bool aim = plan.LandingTarget > 0f || plan.Landing > 0f;
            if (landing != null)
            {
                var along = new Vector3(root.forward.x, 0f, root.forward.z); along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.forward;   // K256e: front and rear of the pad
                landing.AimLandingSteady(aim, at, along, LandingSpreadM, dt);                                 // K256j (M14): 80 deg of turn, eased in the world (OzShuttleSearchlights.Steady.cs)
                if (aim && plan.LandingAtGoal && obj.ShuttleState == ZShuttleState.Descend && plan.Landing > 0.5f && ship.FeetHeight > 2.5f)
                { LandingAimFrames++; WorstLandingOffDeg = Mathf.Max(WorstLandingOffDeg, Mathf.Max(landing.AimOffDeg(0, at, along, LandingSpreadM), landing.AimOffDeg(1, at, along, LandingSpreadM))); }
            }
            // (c, d) the nose searchlight, (b) the ramp light
            NoseDraw(g);
            RampDraw(g);
            if (plan.BeepNow) Beep();
            if (plan.Releases != releasesSeen)
            {   // let go: a soft relay click as it turns back to the calm white sweep
                if (plan.Releases > releasesSeen && plan.Nose > 0.05f && SwitchClick(nose.light.transform.position, true, 0.9f)) ReleaseClicks++;   // K256e: one click per change
                releasesSeen = plan.Releases;
            }
            Click(nose.light, ref noseWas, plan.Nose, 1.05f, ref NoseClicks);                              // every light that comes on or goes out is heard (the night lights' switch; K256e: one click per change)
            Click(ramp.light, ref rampWas, plan.Ramp, 1.15f, ref RampClicks);
            // counts for the proofs
            // by day (night share 0): no light may want to burn, and one still fading out from a sudden jump of the clock is out within its fade
            bool anyLit = plan.Landing > 0.001f || plan.Ramp > 0.001f || plan.Nose > 0.001f, anyWant = plan.LandingTarget > 0f || plan.RampTarget > 0f || plan.NoseTarget > 0f;
            dayT = NightGrade.NightK <= 0f ? dayT + dt : 0f;
            if (NightGrade.NightK <= 0f && (anyWant || (anyLit && dayT > OzShuttleNightPlan.DownSec + 0.25f))) DayLitFrames++;
            if (plan.Nose > 0.5f) NoseOnFrames++;
            if (plan.Ramp > 0.5f) RampOnFrames++;
            if (plan.Landing > 0.5f) LandingOnFrames++;
            if (plan.Alarm > 0.9f) { AlarmFrames++; if (f.Calm) CalmAlarmFrames++; }
            if (plan.Pulse < 0.98f) { PulseDipFrames++; if (f.Calm) CalmPulseFrames++; }
        }

        float Base(ZGame g, Vector3 at)
        {
            float gy = g.GroundY(at.x, at.z);
            if (ship.DeckStation >= 0 && ship.DeckTopY > gy && OzStationCrane.DeckY(ship.DeckStation, at, out float dy) && dy > gy) return dy;   // a repair station's deck
            return gy;
        }

        bool Enemy(ZObject o)
        {
            if (o == null || o == obj || o.Destroyed || o.KillAt >= 0) return false;
            var team = obj.Owner;
            if (team == Team.Neutral || o.Owner == team || o.Owner == Team.Neutral) return false;
            return o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon;
        }

        void Scan(ZGame g, ZWorld W)
        {
            plan.Candidates.Clear();
            var objs = W.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (!Enemy(o) || !Measure(g, o, out var s)) continue;
                plan.Candidates.Add(s);
            }
        }

        /// <summary>An enemy as drawn now, seen from the nose searchlight: flat distance, the drum's turn from the nose and its drop. False
        /// while it is not drawn (no view, hidden, dying).</summary>
        bool Measure(ZGame g, ZObject o, out OzShuttleNightPlan.Seen s)
        {
            s = default;
            if (!g.Views.TryGetValue(o.Id, out var v) || !v || !v.gameObject.activeInHierarchy || v.Dying) return false;
            Vector3 q;
            var other = o.IsShuttle ? OzPlayableShuttle.Of(o) : null;
            if (other && o.ShuttleAirborne) q = other.transform.position + other.transform.up * AimShipM;
            else { q = v.transform.position; q.y = g.GroundY(q.x, q.z) + (o.Kind == ZKind.Robot ? AimRobotM : o.Kind == ZKind.Cannon ? AimGunM : AimVehicleM); }
            var piv = noseYaw.position; var d = root.InverseTransformDirection(q - piv);
            float flatLocal = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            s.Id = o.Id; s.DistM = new Vector2(q.x - piv.x, q.z - piv.z).magnitude;
            s.YawDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; s.PitchDeg = Mathf.Atan2(-d.y, Mathf.Max(0.01f, flatLocal)) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>The searchlights' own switch sound as a light comes on (and, softer and lower, as it goes out).</summary>
        void Click(Light at, ref float was, float now, float pitch, ref int count)
        {
            bool on = now > 0f, before = was > 0f; was = now;
            if (on == before || !at) return;
            if (SwitchClick(at.transform.position, on, pitch)) count++;
        }

        /// <summary>K256e: the ship's light switch click (the game's searchlight switch, ngt_searchlight_on): at most one per ship within
        /// ClickGapSec, so one change of the lights (the landing lights and the nose searchlight coming on together at a take-off) is one
        /// click; the belly lamps' switch goes through here too. True = it played.</summary>
        public bool SwitchClick(Vector3 at, bool on, float pitch)
        {
            if (dead) return false;
            float now = Time.unscaledTime;
            if (now - lastClick < ClickGapSec) { ClicksMerged++; return false; }
            if (!AudioDirector.Has("ngt_searchlight_on")) return false;
            lastClick = now;
            AudioDirector.Fire("ngt_searchlight_on", at, obj.Id, on ? ClickOnVol : ClickOffVol, on ? pitch : pitch * 0.82f);
            Clicks++;
            return true;
        }

        /// <summary>K256e: the doorway's bay lamp (ShuttleRearDoor's DR_BayLight) at least BayLampIntensity strong and BayLampRange far; the
        /// door's own numbers are raised only where they are lower (its owner may set them itself).</summary>
        void BayLamp()
        {
            if (ShuttleRearDoor.LampIntensity < BayLampIntensity) ShuttleRearDoor.LampIntensity = BayLampIntensity;
            if (ShuttleRearDoor.LampRange < BayLampRange) ShuttleRearDoor.LampRange = BayLampRange;
            var t = root ? root.Find("DR_BayLight") : null; var l = t ? t.GetComponent<Light>() : null;
            if (l && l.range < BayLampRange) l.range = BayLampRange;
        }

        void Beep()
        {
            var at = nose.light ? nose.light.transform.position : root.position;
            string id = AudioDirector.Has(LockEvent) ? LockEvent : AudioDirector.Has("wpn_target_lock") ? "wpn_target_lock" : null;
            if (id != null) { AudioDirector.Fire(id, at, obj.Id, 1f, 1f); BeepsPlayed++; }
            else OzSoundLog.Add("SHIP", LockEvent, null, "DROP no event", 0f, at);
        }

        // ------------------------------------------------------------------ drawing (the night lights' own numbers: NightLights.Visuals)
        void SetColor(Renderer r, Color c, ref float shown)
        {
            if (!r) return;
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (shown >= 0f && Mathf.Abs(key - shown) <= Mathf.Max(0.004f, shown * 0.012f)) return;
            shown = key; bool on = key > 0.0015f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            mpb.Clear(); mpb.SetColor(NightMeshes.ColorId, c); r.SetPropertyBlock(mpb, 0);                   // the material's own block: a later TeamPaint.Apply of the ship keeps it
        }

        static void Dark(Lamp L)
        {
            if (L.lens) L.lens.enabled = false; if (L.halo) L.halo.enabled = false; if (L.beam) L.beam.enabled = false; if (L.pool) L.pool.enabled = false;
            if (L.light) L.light.enabled = false; if (L.fix != null) { L.fix.LightOut = 0f; L.fix.Target = null; }
            L.lensShown = L.haloShown = L.beamShown = L.poolShown = -1f; L.real = 0f;
        }

        float GroundHit(ZGame g, Vector3 origin, Vector3 dir, float range)
        {
            if (dir.y > -0.03f) return range;
            float s = (origin.y - g.GroundY(origin.x, origin.z)) / -dir.y;
            for (int i = 0; i < 2; i++) { var q = origin + dir * s; s = (origin.y - g.GroundY(q.x, q.z)) / -dir.y; }
            return Mathf.Clamp(s, 0.5f, range);
        }

        /// <summary>The painted pool where a beam meets the ground (the night lights' PlacePool: an ellipse stretched by the slant, its
        /// height band over the ground under the whole ellipse); returns the light at its middle (for the pool's strength).</summary>
        float PlacePool(ZGame g, Lamp L, Vector3 origin, Vector3 dir, float s, float angle, float range)
        {
            var c = origin + dir * s; float gy = g.GroundY(c.x, c.z);
            float half = Mathf.Tan(Mathf.Clamp(angle * 0.5f, 5f, 65f) * Mathf.Deg2Rad);
            float w = Mathf.Clamp(2f * s * half * (angle > 90f ? 0.7f : 1.2f), 1.2f, range * 1.3f);
            float slant = Mathf.Max(0.3f, -dir.y), len = Mathf.Min(w / slant, range * 1.6f);
            var hz = new Vector3(dir.x, 0f, dir.z);
            var rot = hz.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(hz.normalized, Vector3.up) : Quaternion.LookRotation(root.forward, Vector3.up);
            L.poolT.SetPositionAndRotation(new Vector3(c.x, gy + 0.35f, c.z), rot);
            float zl = dir.y < -0.9f ? w : len, reachY = 1.9f;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f; var q = c + rot * new Vector3(Mathf.Sin(a) * w * 0.48f, 0f, Mathf.Cos(a) * zl * 0.48f);
                reachY = Mathf.Max(reachY, Mathf.Abs(g.GroundY(q.x, q.z) - (gy + 0.35f)) + 0.9f);
            }
            L.poolT.localScale = new Vector3(w, 2f * Mathf.Min(reachY, 12f), zl);
            float d = Mathf.Max(1f, (c - origin).magnitude);
            return L.fix.Intensity / (d * d);
        }

        void NoseDraw(ZGame g)
        {
            noseYaw.localRotation = Quaternion.Euler(0f, plan.YawNow, 0f);
            nosePitch.localRotation = Quaternion.Euler(plan.PitchNow, 0f, 0f);
            float o = plan.NoseStrength;
            if (o <= 0.001f) { Dark(nose); return; }
            var nl = NightLights.I; float nk = Mathf.Clamp01(NightGrade.NightK), dusk = Mathf.Lerp(0.25f, 1f, nk);
            bool halos = !nl || nl.halos, beams = !nl || nl.beams, pools = !nl || nl.pools;
            float haloGain = nl ? nl.haloGain : 0.35f, beamGain = nl ? nl.beamGain : 1f, poolGain = nl ? nl.poolGain : 0.22f;
            float lightGain = nl ? nl.lightGain * nl.searchLightGain : 3f;
            plan.Colour(out float cr, out float cg, out float cb); var col = new Color(cr, cg, cb, 1f); var c = col.linear;
            float alarm = plan.Alarm, ang = plan.AngleDeg;
            SetColor(nose.lens, c * (7f * o), ref nose.lensShown);
            SetColor(nose.halo, c * (halos ? haloGain * dusk * o * 2.6f * (1f + 0.3f * alarm) : 0f), ref nose.haloShown);              // the fort's: x 1.3 on a target
            SetColor(nose.beam, c * (beams ? beamGain * o * Mathf.Pow(nk, 0.8f) * Mathf.Lerp(0.42f, 0.62f, alarm) : 0f), ref nose.beamShown);
            // the beam's length: to the ground, or to what it follows
            var lens = nose.light.transform.position; var dir = nosePitch.forward;
            float s = GroundHit(g, lens, dir, NoseRange);
            if (plan.Locked && lockObj != null && Measure(g, lockObj, out var seen)) s = Mathf.Min(s, Mathf.Min(NoseRange, Mathf.Sqrt(seen.DistM * seen.DistM + Mathf.Pow(lens.y - g.GroundY(lens.x, lens.z), 2f)) + 1f));
            float ta = Mathf.Tan(Mathf.Clamp(ang * 0.5f, 2f, 40f) * Mathf.Deg2Rad) * 1.2f, inv = 1f / Mathf.Max(0.01f, nosePitch.lossyScale.x);
            nose.beamT.localScale = new Vector3(s * ta * inv, s * ta * inv, (s + 0.8f) * inv);
            // the real light (when the budget grants it), else the painted pool carries it
            nose.fix.LightOut = nose.offered ? o : 0f; nose.fix.Col = col; nose.fix.ColLinear = c; nose.fix.Angle = ang; nose.fix.Target = plan.Locked ? lockObj : null;
            nose.real = nose.offered && NightBudgetShare.HookFrame >= Time.frameCount - 2 ? nose.fix.RealK : 0f;
            float k = o * nose.real; bool lit = k > 0.002f;
            if (nose.light.enabled != lit) nose.light.enabled = lit;
            if (lit)
            {
                nose.light.color = col; nose.light.spotAngle = ang; nose.light.innerSpotAngle = ang * plan.InnerShare;
                nose.light.intensity = NoseIntensity * k * lightGain;
                var sh = nose.fix.ShadowCandidate && nose.fix.ShadowK > 0.001f ? LightShadows.Soft : LightShadows.None;
                if (nose.light.shadows != sh) nose.light.shadows = sh;
                if (sh != LightShadows.None) { nose.light.shadowStrength = NightShadows.Strength * nose.fix.ShadowK; ShadowNoseFrames++; }
                RealNoseFrames++;
            }
            else if (nose.light.shadows != LightShadows.None) nose.light.shadows = LightShadows.None;
            float e = PlacePool(g, nose, lens, dir, s, ang, NoseRange);
            SetColor(nose.pool, c * (pools ? poolGain * o * dusk * (1f - nose.real) * Mathf.Min(10f, e * 4f) : 0f), ref nose.poolShown);
            if (nose.pool && nose.pool.enabled) PoolNoseFrames++;
        }

        void RampDraw(ZGame g)
        {
            float o = plan.Ramp;
            if (o <= 0.001f || !door) { Dark(ramp); return; }
            // shining out of the doorway onto the ramp's lower half and its foot
            var foot = door.FootPoint(); var mid = door.RampPoint(0.8f, 0f); var to = Vector3.Lerp(mid, foot, 0.5f); to.y = g.GroundY(to.x, to.z);
            var d = to - ramp.head.position; if (d.sqrMagnitude > 1e-4f) ramp.head.rotation = Quaternion.LookRotation(d.normalized, root.forward);
            var nl = NightLights.I; float nk = Mathf.Clamp01(NightGrade.NightK), dusk = Mathf.Lerp(0.25f, 1f, nk);
            bool halos = !nl || nl.halos, beams = !nl || nl.beams, pools = !nl || nl.pools;
            float haloGain = nl ? nl.haloGain : 0.35f, beamGain = nl ? nl.beamGain : 1f, poolGain = nl ? nl.poolGain : 0.22f, lightGain = nl ? nl.lightGain : 0.6f;
            var c = RampColour.linear; var lc = RampLensColour.linear;
            SetColor(ramp.lens, lc * (RampLensGlow * o), ref ramp.lensShown);                                   // K256e: the amber lamp lens, blooming
            SetColor(ramp.halo, lc * (halos ? haloGain * dusk * o * RampHaloGain : 0f), ref ramp.haloShown);
            SetColor(ramp.beam, c * (beams ? beamGain * RampBeam * o * Mathf.Pow(nk, 0.8f) * 0.22f : 0f), ref ramp.beamShown);
            var lens = ramp.light.transform.position; var dir = ramp.head.forward;
            float s = GroundHit(g, lens, dir, RampRange);
            float ta = Mathf.Tan(Mathf.Clamp(RampAngle * 0.5f, 2f, 40f) * Mathf.Deg2Rad) * 1.2f;
            ramp.beamT.localScale = new Vector3(s * ta, s * ta, s + 0.4f);
            ramp.fix.LightOut = ramp.offered ? o : 0f; ramp.fix.Col = RampColour; ramp.fix.ColLinear = c;
            ramp.real = ramp.offered && NightBudgetShare.HookFrame >= Time.frameCount - 2 ? ramp.fix.RealK : 0f;
            float k = o * ramp.real; bool lit = k > 0.002f;
            if (ramp.light.enabled != lit) ramp.light.enabled = lit;
            if (lit) { ramp.light.intensity = RampIntensity * k * lightGain; RealRampFrames++; }
            float e = PlacePool(g, ramp, lens, dir, s, RampAngle, RampRange);
            SetColor(ramp.pool, c * (pools ? poolGain * o * dusk * (1f - ramp.real) * Mathf.Min(10f, e * 4f) : 0f), ref ramp.poolShown);
            if (ramp.pool && ramp.pool.enabled) PoolRampFrames++;
        }

        // ------------------------------------------------------------------ out and away
        /// <summary>Shot down (the belly lamps' Kill): every light out at once, the lock let go.</summary>
        public void Kill()
        {
            if (dead) return; dead = true; Kills++;
            plan.Out(); lockObj = null;
            Dark(nose); Dark(ramp);
        }

        /// <summary>The view goes (the belly lamps' Dispose): the fixtures leave the budget, the pools on the ground are removed.</summary>
        public void Dispose()
        {
            dead = true;
            foreach (var L in new[] { nose, ramp })
            {
                if (L.fix != null) NightBudgetShare.Fixtures.Remove(L.fix);
                if (L.poolT) Object.Destroy(L.poolT.gameObject);
            }
            if (noseYaw) Object.Destroy(noseYaw.gameObject);
            if (ramp.head) Object.Destroy(ramp.head.gameObject);
        }
    }
}
