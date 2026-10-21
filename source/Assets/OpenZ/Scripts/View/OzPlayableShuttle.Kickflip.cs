using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K344, the shuttle's kickflip (an easter egg, view only; the curve, the gesture and the start rules: View/OzShuttleKickflip.Core.cs).
    /// A double right-click that gives a selected shuttle a move order at least 30 m away arms ONE flip for that order; in the steady
    /// part of the cruise, on screen, it pops its nose up, rolls once round its long axis (eased in and out) and catches itself level.
    /// <list type="bullet">
    /// <item>THE POSE: one extra turn on top of the drawn pose, round the hull's middle (<see cref="KickPivot"/>: the long axis through the
    /// hull and pods, so the hull turns in place), plus the pop's nose-up pitch and the hop. Everything that reads the ship during the
    /// frame sees it LEVEL: the frame, the health bar, the click box, the landing mark, the dust, the night lights' aim, the shots' aim
    /// (the overlays run before this view's LateUpdate, the rules' readers in Update after the gate <see cref="OzShuttleKickflipGate"/>,
    /// order -47, has put the level pose back). The flipped pose is set twice a frame: for the jets (their flames, fire puffs, streaks and
    /// lights come out of the rolled nozzles along the rolled axis: they trail with it) and, at the end of LateUpdate, for the picture. The
    /// lamp heads (the two belly searchlights, the nose searchlight's drum, the ramp lamp) keep their aim through it (gimballed): their beams
    /// and pools stay on the ground. Lamp fix (review K344): no beam is ever seen passing through the ship. Each lamp is dimmed in the
    /// picture by where its beam can still shine (View/OzShuttleKickflip.Lamps.cs: a field of clear margins made from the ship's own model):
    /// its lens, halo, beam cone, real light and pool of light ease out before the hull, a pod, a leg or a housing comes into the beam's way
    /// (around 180 degrees the belly lamps sit on top of the hull) and back in after, 1 at both ends of the flip, no step. The nose
    /// searchlight's post and yoke stay on the hull (only its drum keeps its aim: it turns in place, nothing of it leaves the ship); the
    /// belly lamps' lenses stay in their housings. Shot down during it: the pose stays as it was drawn (no snap), the break-up takes it from
    /// there.</item>
    /// <item>THE THRUSTERS (on screen only, nothing off screen): the main jets flare at the pop and at the catch (a second, longer flame
    /// over each jet, a larger halo, brighter thruster lights, more fire puffs); small attitude jets at the pods' outer ends puff while it
    /// rolls, on the pair that drives the roll (the roll speeding up: the +x pod's bottom port and the -x pod's top port for a +x-up roll,
    /// then the other pair as it slows down to the catch), their size following the roll's acceleration.</item>
    /// <item>THE SOUNDS (positional, louder the nearer the camera; the rows shuttle_flip_*, on screen only): the pop (a low thrust whomp), a
    /// rolling whoosh through the roll, a hiss of the attitude jets at the spin-up and at the spin-down (from the firing pod), the soft
    /// catch at the end.</item>
    /// </list>
    /// View only: nothing here changes the rules, and nothing reads the rules differently.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>The roll's axis point in the root's space (m): the hull's and pods' middle (hull y 0.59 .. 5.15, pods 1.08 .. 4.50).</summary>
        public static Vector3 KickPivot = new Vector3(0f, 2.8f, 0f);
        /// <summary>The attitude ports at each pod's outer end (root space, x for the +x pod; the -x pod mirrors it): the top and the bottom one.</summary>
        public static Vector3 KickPortTop = new Vector3(2.95f, 4.42f, -0.1f), KickPortBottom = new Vector3(2.95f, 1.16f, -0.1f);
        /// <summary>The attitude puffs: particles a second at full strength (x the graphics preset's share), their speed (m/s), size (m) and
        /// life (s), colour; the main jets' flare: extra fire puffs a second, the extra flame's scale and the lights' gain at full flare.</summary>
        public static float KickPuffRate = 46f, KickPuffSpeed = 8f, KickPuffSize = 0.62f, KickPuffLife = 0.42f, KickFlareRate = 70f, KickFlareCore = 0.9f, KickFlareLight = 1.4f;
        public static Color KickPuffColour = new Color(0.86f, 0.90f, 0.98f);

        readonly OzKickflipState flip = new OzKickflipState();
        /// <summary>The flip's state (the Play proof reads it).</summary>
        public OzKickflipState Kickflip => flip;
        /// <summary>This frame's flip pose (deg / m) and effects; whether the drawn pose is the flipped one now.</summary>
        public OzKickflipOut KickNow { get; private set; }
        public bool KickDrawn => kickOn;
        /// <summary>Proof counts: frames drawn flipped, attitude puff particles, flare frames, the flip's sounds fired, lamp heads kept on aim,
        /// frames the gate put the level pose back, flips whose pose was kept when the ship was destroyed.</summary>
        public int KickFrames { get; private set; }
        public int KickPuffs { get; private set; }
        public int KickFlareFrames { get; private set; }
        public int KickSounds { get; private set; }
        public int KickGimbals { get; private set; }
        public int KickRestores { get; private set; }
        public int KickBaked { get; private set; }
        /// <summary>The last flip event bits seen (OzKickflipState.Ev*) and the last sound fired.</summary>
        public int KickEvents { get; private set; }
        public string KickLastSound { get; private set; } = "";

        bool kickOn, kickGimballed; Vector3 kickLevelPos; Quaternion kickLevelRot = Quaternion.identity;
        int kickClickFrame = -1, kickClickX, kickClickY, kickGoalBeforeX, kickGoalBeforeY, kickLastGoalX = int.MinValue, kickLastGoalY; bool kickClickDouble, kickLastTook;
        float kickPuffAcc, kickFlareAcc; bool kickOnScreen;
        Transform[] kickHeads; readonly Quaternion[] kickHeadWorld = new Quaternion[4], kickHeadLocal = new Quaternion[4];
        // lamp fix (review K344): the nose searchlight's DRUM keeps its aim (OZ_FxNosePitch); its post and yoke (OZ_FxNoseYaw) roll with the hull
        static readonly string[] KickHeadNames = { "OZ_FxSearchHead0", "OZ_FxSearchHead1", "OZ_FxNosePitch", "OZ_FxRampLamp" };
        // lamp fix: each lamp's drawn pieces (lens, halo, beam, pool; [lamp * 4 + piece]), its real light, what the picture's fade changed
        // (put back by the gate), the belly lamps' lenses kept in their housings
        Renderer[] kickLampR; Light[] kickLampL; bool kickLampsFound;
        readonly Color[] kickLampSaved = new Color[16]; readonly bool[] kickLampDimmed = new bool[16];
        readonly float[] kickLightSaved = new float[4]; readonly bool[] kickLightDimmed = new bool[4];
        readonly float[] kickFade = { 1f, 1f, 1f, 1f };
        readonly Transform[] kickLens = new Transform[2]; readonly Quaternion[] kickLensLocal = new Quaternion[2], kickLensRolled = new Quaternion[2];
        static readonly MaterialPropertyBlock kickMpb = new MaterialPropertyBlock();
        /// <summary>Lamp fix proof counts: drawn frames in which a lit lamp was dimmed by the flip's fade, and fully out.</summary>
        public int KickLampDimFrames { get; private set; }
        public int KickLampOutFrames { get; private set; }
        /// <summary>The fade of lamp h (0 / 1 the belly lamps, 2 the nose searchlight, 3 the ramp light) in this frame's picture (1 = as drawn).</summary>
        public float KickLampFade(int h) => h >= 0 && h < 4 ? kickFade[h] : 1f;
        /// <summary>Lamp h's beam cone and real light (the Play proof measures them against the ship's model).</summary>
        internal Renderer KickLampBeam(int h) { KickLampsFind(); return kickLampR != null && h >= 0 && h < 4 ? kickLampR[h * 4 + 2] : null; }
        internal Light KickLampLight(int h) { KickLampsFind(); return kickLampL != null && h >= 0 && h < 4 ? kickLampL[h] : null; }
        /// <summary>Lamp h's colours are set on its renderers' first material (the nose searchlight, the ramp light), else on the renderer.</summary>
        internal static bool KickLampPerMaterial(int h) => h >= 2;

        /// <summary>OzShuttleKickflip.OrderClick (PlayerInput's order click, this ship in the selection): resolved in this frame's LateUpdate,
        /// once the order has gone to the rules (dbl = the second click of a double click; (px, py) = the click's ground point, px).</summary>
        internal void KickflipClick(bool dbl, int px, int py)
        {
            kickClickFrame = Time.frameCount; kickClickDouble = dbl; kickClickX = px; kickClickY = py;
            if (Obj != null) { kickGoalBeforeX = Obj.ShuttleGoalX; kickGoalBeforeY = Obj.ShuttleGoalY; }               // before the orders are flushed
        }

        Vector3 KickCentre => transform.TransformPoint(KickPivot);

        /// <summary>LateUpdate, before the jets: the click of this frame, one step of the flip, the flipped pose for the jets.</summary>
        void KickflipBeforeJets(float dt)
        {
            if (game == null || game.World == null || Obj == null) return;
            if (kickOn)
            {   // the gate did not run this frame (it is made on the first flip): the rules' pose of this frame stands; only the lamp heads go back
                KickUngimbal();                                                                           // (and the lamps' own numbers: lamp fix)
                kickOn = false;
            }
            if (flip.Id != Obj.Id) flip.Id = Obj.Id;
            var o = Obj;
            double vmaxPx = Mathf.Max(1f, (float)o.RealMoveSpeed);
            double dx = o.ShuttleGoalX - o.ShuttleBaseX, dy = o.ShuttleGoalY - o.ShuttleBaseY, remainPx = System.Math.Sqrt(dx * dx + dy * dy);
            KickLuckFrame(o, remainPx);                                                                 // K411: a hit noted; a sent Shift queue is an order (OzPlayableShuttle.KickLuck.cs)
            if (kickClickFrame == Time.frameCount)
            {   // the order of this frame's click is with the rules now: did this ship take it (its goal is the click's point)?
                kickClickFrame = -1;
                var (cx, cy) = game.World.ShuttleClamp(kickClickX, kickClickY);
                bool sameAsLast = o.ShuttleGoalX == kickLastGoalX && o.ShuttleGoalY == kickLastGoalY;
                bool took = (o.ShuttleGoalX == cx && o.ShuttleGoalY == cy)                                          // the click's point
                            || o.ShuttleGoalX != kickGoalBeforeX || o.ShuttleGoalY != kickGoalBeforeY                  // or a goal the rules made of it (moved to a free spot)
                            || (kickClickDouble && kickLastTook && sameAsLast);                                         // the double's second click: the first one's goal again
                kickLastTook = took; kickLastGoalX = o.ShuttleGoalX; kickLastGoalY = o.ShuttleGoalY;
                int e0 = flip.Click(kickClickDouble, took, remainPx / 8.0, o.ShuttleGoalX, o.ShuttleGoalY);
                e0 |= KickLuckOrder(took, remainPx / 8.0, e0);                                            // K411: a far normal order may arm the same flip by itself (5 %, once per order)
                if (kickClickDouble) OzSoundLog.Add("FLIP", "shuttle_kickflip", null, $"double click #{o.Id}: {(took ? "taken" : "not taken")}, {remainPx / 8.0:F1} m{((e0 & OzKickflipState.EvArmed) != 0 ? ": ARMED" : remainPx / 8.0 < OzKickflipState.FarM ? ": near, nothing" : "")}", 0f, transform.position);
            }
            KickEvadeWatch(o);                                                                           // K436: a flip the rules started (the evasive self-flip) pops here (OzPlayableShuttle.KickEvade.cs)
            if (flip.Phase == OzKickflipPhase.Idle && !kickOn) { KickNow = default; KickTrailsScaleJets(); return; }   // round 2: the jets' puffs come back as the trails fade
            var cam = Camera.main;
            kickOnScreen = cam && DetailKit.OnScreen(cam, KickCentre, -0.06f);                               // the whole flip in view (a little inside the edges)
            double arcBlocked = flip.Phase == OzKickflipPhase.Armed ? KickArcBlocked() : 0.0;                       // round 2: the room round the ship for the arc (OzPlayableShuttle.KickTrails.cs)
            var i = new OzKickflipIn
            {
                Alive = !o.Destroyed && o.KillAt < 0, State = (int)o.ShuttleState, AtHeight = o.ShuttleLift >= 0.999, Braking = o.ShuttleBraking,
                HoverTurn = o.ShuttleDeckTurning || o.ShuttleLandTurning, Strafing = o.ShuttleStrafeStage != 0, UnderFire = o.ShuttleUnderHeavyFire, OnScreen = kickOnScreen && (flip.Phase != OzKickflipPhase.Armed || (KickArcOnScreen(cam) && !KickLuckHold())),   // round 2: it starts with the whole arc in view
                SpeedMps = o.ShuttleSpeedPx / 8.0, VmaxMps = vmaxPx / 8.0, AccelMps2 = o.ShuttleAccelPx / 8.0, AccMaxMps2 = ZWorld.ShuttleAccelMax(vmaxPx) / 8.0,
                TurnRateDeg = o.ShuttleTurnRateDeg, RemainM = KickWayLeftM(o, remainPx), BrakeM = ZWorld.ShuttleBrakeDistance(o.ShuttleSpeedPx, o.ShuttleAccelPx, vmaxPx) / 8.0,
                FeetM = FeetHeight, BankDeg = bank, LeanDeg = strafeLean, PitchDeg = pitch, Health = o.Health, GoalX = o.ShuttleGoalX, GoalY = o.ShuttleGoalY, ArcBlocked = arcBlocked,
            };
            KickEvadeIn(o, ref i);                                                                       // K436: the rules' word (OzPlayableShuttle.KickEvade.cs)
            flip.Step(i, dt, out var k);
            KickEvadeAfter(o, k);                                                                        // K436: the double click's pop opens the rules' window
            KickNow = k; KickEvents = k.Events;
            if ((k.Events & (OzKickflipState.EvStart | OzKickflipState.EvAbort | OzKickflipState.EvWrap | OzKickflipState.EvEnd)) != 0)
                OzSoundLog.Add("FLIP", "shuttle_kickflip", null, $"#{o.Id} {EvName(k.Events)} at {game.World.Time:F2} s (speed {i.SpeedMps:F2} m/s, {i.RemainM:F1} m to go, dir {flip.Dir})", 0f, transform.position);
            KickPose(true, false);
            KickTrailsScaleJets();                                                                       // round 2: the jets' own fire puffs give way to the smooth trails (back after the jets)
        }

        static string EvName(int e) => (e & OzKickflipState.EvStart) != 0 ? "START" : (e & OzKickflipState.EvAbort) != 0 ? "EASED BACK before the roll"
                                      : (e & OzKickflipState.EvWrap) != 0 ? "EASED to level (the cruise ended)" : "END";

        /// <summary>LateUpdate, after the jets (still flipped): the main jets' flare and the attitude puffs, the sounds; then level again.</summary>
        void KickflipAfterJets(float dt)
        {
            KickWhooshWatchTick();                                                                       // K423: the long whoosh played out, cut or silenced (OzPlayableShuttle.KickForward.cs)
            KickTrailsTick(dt);                                                                          // round 2: the jets' smooth trails and the attitude streaks (OzPlayableShuttle.KickTrails.cs), also while they fade after the flip
            if (!kickOn) return;
            var k = KickNow;
            var fx = OpenZFxPlayer.I;
            bool draw = kickOnScreen && fx && fx.isActiveAndEnabled && !(game && game.paused) && jets != null;
            if (draw && k.Flare > 0.01)
            {   // the main jets flare (the pop, the catch): a second, longer flame over each jet, a larger halo, brighter lights, more fire
                float f = (float)k.Flare, T = Mathf.Max(0.7f, jets.Throttle);
                var axis = -transform.up; KickFlareFrames++;
                for (int s = 0; s < 2; s++)
                {
                    var m = transform.TransformPoint(door ? (s == 0 ? door.NozzleL : door.NozzleR) : new Vector3(s == 0 ? 2.45f : -2.45f, 2.26f, -0.2f));
                    fx.DrawExhaust(m + axis * (0.35f + 0.5f * f), transform.up, OzShuttleJets.CoreScale * KickFlareCore * f * T, t + 0.19f * s);
                    fx.DrawHalo(m + axis * 0.6f, OzShuttleJets.HaloM * (0.55f + 0.55f * f), OzShuttleJets.Halo, 0.85f * f);
                }
                for (int g = 0; g < glow.Length; g++) if (glow[g] && glow[g].enabled) glow[g].intensity *= 1f + KickFlareLight * f;   // the jets set them afresh every frame
                if (!kickTrailMat)
                {   // round 2: only without the trails' material: the flare's fire puffs (with it, the smooth trails carry the flare)
                    kickFlareAcc += dt * KickFlareRate * f * PuffShare;
                    while (kickFlareAcc >= 1f)
                    {
                        kickFlareAcc -= 1f;
                        for (int s = 0; s < 2; s++)
                        {
                            var m = transform.TransformPoint(door ? (s == 0 ? door.NozzleL : door.NozzleR) : new Vector3(s == 0 ? 2.45f : -2.45f, 2.26f, -0.2f));
                            fx.EmitChannel("m12_fire", m + axis * 0.2f, axis * 11f + Velocity + Random.insideUnitSphere * 0.8f, Random.Range(0.22f, 0.32f), Random.Range(0.6f, 0.85f) * (0.8f + 0.4f * f), OzShuttleJets.Fire, 0.9f);
                        }
                    }
                }
                else kickFlareAcc = 0f;
            }
            else kickFlareAcc = 0f;
            if (!kickTrailMat)
            {   // round 2: only without the trails' material: the attitude jets' gas puffs (with it, smooth streaks: OzPlayableShuttle.KickTrails.cs)
                bool burst = (k.Events & (OzKickflipState.EvPuffUp | OzKickflipState.EvPuffDown)) != 0;
                float pf = (float)k.Puff;
                if (burst && Mathf.Abs(pf) < 0.2f) pf = 0.2f * PuffSign(k);                                      // the burst's frame: the roll's acceleration is just turning over there
                float pa = Mathf.Abs(pf);
                if (draw && pa > 0.04f)
                {   // the attitude jets on the pair that drives the roll (+: the +x bottom port down and the -x top port up)
                    kickPuffAcc += dt * KickPuffRate * pa * PuffShare + (burst ? 2f : 0f);
                    while (kickPuffAcc >= 1f)
                    {
                        kickPuffAcc -= 1f;
                        for (int s = 0; s < 2; s++)
                        {
                            bool plusSide = s == 0, top = plusSide ? pf < 0f : pf > 0f;
                            var port = top ? KickPortTop : KickPortBottom; if (!plusSide) port.x = -port.x;
                            var at = transform.TransformPoint(port); var dir = top ? transform.up : -transform.up;
                            float sz = KickPuffSize * (0.6f + 0.4f * pa) * Random.Range(0.85f, 1.15f);
                            fx.EmitChannel("m12_smoke", at + dir * 0.1f, dir * (KickPuffSpeed * (0.7f + 0.3f * pa)) + Velocity + Random.insideUnitSphere * 0.5f, KickPuffLife * Random.Range(0.8f, 1.15f), sz, KickPuffColour, 0.55f);
                            if (burst) fx.EmitChannel("m12_core", at, dir * 2f, 0.07f, 0.4f, new Color(0.85f, 0.92f, 1f), 0.8f);
                            KickPuffs++;
                        }
                        burst = false;
                    }
                }
                else kickPuffAcc = 0f;
            }
            else kickPuffAcc = 0f;
            if (kickOnScreen && k.Events != 0) KickSoundsFor(k);
            KickPose(false, false);
        }

        /// <summary>The push of this frame's burst: the spin-up pushes the way the roll goes, the spin-down the other way.</summary>
        float PuffSign(in OzKickflipOut k) => (k.Events & OzKickflipState.EvPuffUp) != 0 ? flip.Dir : (k.Events & OzKickflipState.EvPuffDown) != 0 ? -flip.Dir : (k.Puff >= 0 ? 1f : -1f);

        static float PuffShare => OzQuality.Level == 0 ? 1f : OzQuality.Level == 1 ? 0.75f : 0.5f;

        void KickSoundsFor(in OzKickflipOut k)
        {
            var c = KickCentre; int id = Obj != null ? Obj.Id : -1;
            if ((k.Events & OzKickflipState.EvPop) != 0) KickSound("shuttle_flip_pop", c, id, 1f);
            if ((k.Events & OzKickflipState.EvRoll) != 0) { KickWhoosh(c, id); KickWhooshMix(); }             // K436: the mix makes room under it (OzPlayableShuttle.KickEvade.cs)                                    // K423: the long whoosh, its voice watched to the end (OzPlayableShuttle.KickForward.cs)                  // round 2: the long whoosh through the whole roll (shuttle_flip_whoosh)
            if ((k.Events & (OzKickflipState.EvPuffUp | OzKickflipState.EvPuffDown)) != 0)
            {   // from the +x pod's firing port: its bottom one for a positive push, its top one for a negative one
                var port = PuffSign(k) > 0f ? KickPortBottom : KickPortTop;
                KickSound("shuttle_flip_puff", transform.TransformPoint(port), id, (k.Events & OzKickflipState.EvPuffUp) != 0 ? 1f : 0.8f);
            }
            if ((k.Events & OzKickflipState.EvPuffDown) != 0 && KickRollSwishOn) KickSound("shuttle_flip_roll", c, id, 1f);   // K436: the swish at the roll's fastest turn (over the top)
            if ((k.Events & OzKickflipState.EvCatch) != 0) KickSound("shuttle_flip_catch", c, id, 1f);
        }

        void KickSound(string ev, Vector3 at, int id, float vol)
        {
            if (!AudioDirector.Has(ev)) { OzSoundLog.Add("FLIP", ev, null, "MISSING row", 0f, at); return; }
            AudioDirector.Fire(ev, at, id, vol); KickSounds++; KickLastSound = ev; LastSound = ev;
        }

        /// <summary>LateUpdate's end: the flipped pose for the picture (the lamp heads kept on aim); the gate puts the level pose back first
        /// thing next frame.</summary>
        void KickflipDraw()
        {
            if (!KickNow.Active) { KickFrameOffset = Vector3.zero; return; }                             // K436: the frame's offset (OzPlayableShuttle.KickEvade.cs)
            KickPose(true, true);
            KickFrameNote();
            if (kickOn) { KickFrames++; OzShuttleKickflipGate.Ensure(); KickForwardMeasure(); }                   // K423: the proof's eye on the forward pace
        }

        /// <summary>The gate (order -47, after the rules' tick, before every reader): the level pose back. Destroyed in the meantime (the
        /// tick that killed it): the pose stays as drawn, the flip ends, nothing snaps.</summary>
        internal void KickflipUndo()
        {
            if (!kickOn) return;
            bool gone = Obj == null || game == null || game.World == null || Obj.Destroyed || Obj.KillAt >= 0 || game.World.Get(Obj.Id) != Obj || BrokenUp || dying || crashing;
            if (gone)
            {   // the lamps stay as drawn too (a beam the hull is in front of never comes back on): they go out with the ship (OzShuttleSearchlights.Kill)
                kickOn = false; kickGimballed = false; flip.Kill(); KickNow = default; KickBaked++;
                for (int i = 0; i < kickLampDimmed.Length; i++) kickLampDimmed[i] = false;
                for (int h = 0; h < kickLightDimmed.Length; h++) kickLightDimmed[h] = false;
                return;
            }
            KickPose(false, false); KickRestores++;
        }

        /// <summary>on: the flip's extra turn round <see cref="KickPivot"/> (and the hop) on top of the pose as it is; off: that pose back.
        /// gimbal: the lamp heads keep their world aim through it.</summary>
        void KickPose(bool on, bool gimbal)
        {
            if (on)
            {
                if (kickOn || !KickNow.Active) return;
                var k = KickNow;
                kickLevelPos = transform.position; kickLevelRot = transform.rotation;
                var d = Quaternion.Euler(-(float)k.PitchUpDeg, 0f, 0f) * Quaternion.Euler(0f, 0f, (float)k.RollDeg);
                var q = kickLevelRot * d;
                var p = kickLevelPos + kickLevelRot * KickPivot - q * KickPivot + Vector3.up * (float)(k.HopM + k.RiseM)
                        + kickLevelRot * new Vector3((float)k.SideM, 0f, 0f) + KickLeadVec(k);                              // K423: the lead along the flight (OzPlayableShuttle.KickForward.cs)                                    // round 2: the barrel roll's arc round its line (OzKickflipCurve.Arc)
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsNaN(q.x)) return;          // never a broken pose
                kickGimballed = false;
                if (gimbal)
                {
                    KickLampsFind();
                    for (int h = 0; h < kickHeads.Length; h++) if (kickHeads[h]) { kickHeadWorld[h] = kickHeads[h].rotation; kickHeadLocal[h] = kickHeads[h].localRotation; }
                    for (int h = 0; h < kickLens.Length; h++) if (kickLens[h]) kickLensLocal[h] = kickLens[h].localRotation;
                }
                transform.SetPositionAndRotation(p, q);
                if (gimbal)
                {
                    for (int h = 0; h < kickLens.Length; h++) if (kickLens[h]) kickLensRolled[h] = kickLens[h].rotation;              // the lens as its housing turned it
                    for (int h = 0; h < kickHeads.Length; h++) if (kickHeads[h]) { kickHeads[h].rotation = kickHeadWorld[h]; KickGimbals++; }
                    for (int h = 0; h < kickLens.Length; h++) if (kickLens[h]) kickLens[h].rotation = kickLensRolled[h];              // stays in its housing
                    kickGimballed = true;
                    KickLampsFade(k);
                }
                kickOn = true;
                return;
            }
            if (!kickOn) return;
            KickUngimbal();
            transform.SetPositionAndRotation(kickLevelPos, kickLevelRot);
            kickOn = false;
        }

        /// <summary>The lamp heads back on the hull (their own local turn), the lenses, and every lamp piece the picture's fade changed.</summary>
        void KickUngimbal()
        {
            if (kickGimballed && kickHeads != null)
            {
                for (int h = 0; h < kickHeads.Length; h++) if (kickHeads[h]) kickHeads[h].localRotation = kickHeadLocal[h];
                for (int h = 0; h < kickLens.Length; h++) if (kickLens[h]) kickLens[h].localRotation = kickLensLocal[h];
            }
            kickGimballed = false;
            KickUndim();
        }

        // ================================================================== lamp fix (review K344): no beam through the ship
        /// <summary>Once: the lamp heads, the belly lamps' lenses, each lamp's lens / halo / beam / pool and real light.</summary>
        void KickLampsFind()
        {
            if (kickHeads == null) { kickHeads = new Transform[KickHeadNames.Length]; for (int h = 0; h < KickHeadNames.Length; h++) kickHeads[h] = FindDeep(transform, KickHeadNames[h]); }
            if (kickLampsFound) return;
            kickLampsFound = true;
            kickLampR = new Renderer[16]; kickLampL = new Light[4];
            for (int h = 0; h < 2; h++)
            {
                if (search != null && search.KickParts(h, out var lens, out var halo, out var beam, out var pool, out var light))
                { kickLampR[h * 4] = lens; kickLampR[h * 4 + 1] = halo; kickLampR[h * 4 + 2] = beam; kickLampR[h * 4 + 3] = pool; kickLampL[h] = light; }
                kickLens[h] = kickHeads[h] ? kickHeads[h].Find("Lens") : null;
            }
            var night = search != null ? search.Night : null; int id = Obj != null ? Obj.Id : -1;
            Renderer R(Transform under, string n) { var t = under ? FindDeep(under, n) : null; return t ? t.GetComponent<Renderer>() : null; }
            Renderer Pool(string n) { var go = GameObject.Find(n); return go ? go.GetComponent<Renderer>() : null; }
            kickLampR[8] = R(kickHeads[2], "OZ_FxNoseLens"); kickLampR[9] = R(kickHeads[2], "OZ_FxNoseHalo"); kickLampR[10] = R(kickHeads[2], "OZ_FxNoseBeam");
            kickLampR[11] = Pool("OZ_FxNosePool_" + id); kickLampL[2] = night != null ? night.NoseLight : null;
            kickLampR[12] = R(kickHeads[3], "OZ_FxRampLens"); kickLampR[13] = R(kickHeads[3], "OZ_FxRampHalo"); kickLampR[14] = R(kickHeads[3], "OZ_FxRampBeam");
            kickLampR[15] = Pool("OZ_FxRampPool_" + id); kickLampL[3] = night != null ? night.RampLight : null;
        }

        /// <summary>The picture's pose is on, the heads on their aim: each lamp dimmed by where its beam can still shine.</summary>
        void KickLampsFade(in OzKickflipOut k)
        {
            bool dim = false, outLit = false;
            for (int h = 0; h < 4; h++)
            {
                kickFade[h] = 1f;
                if (!kickHeads[h]) continue;
                var a = Quaternion.Inverse(kickLevelRot) * (kickHeadWorld[h] * Vector3.forward);            // the beam's aim in the level ship's root space
                float f = (float)OzKickflipLamps.Fade(h == 3 ? OzKickflipLamps.Ramp : h, a.x, a.y, a.z, k.RollDeg, k.PitchUpDeg);
                kickFade[h] = f;
                if (f >= 0.999f) continue;
                bool lit = KickDim(h, f);
                dim |= lit; outLit |= lit && f <= 0.002f;
            }
            if (dim) KickLampDimFrames++;
            if (outLit) KickLampOutFrames++;
        }

        /// <summary>Lamp h's lens, halo, beam, pool and real light at f of what its own code drew (out at 0.002); true = any of it was lit.</summary>
        bool KickDim(int h, float f)
        {
            bool any = false, perMat = KickLampPerMaterial(h);
            for (int p = 0; p < 4; p++)
            {
                int i = h * 4 + p; var r = kickLampR != null ? kickLampR[i] : null;
                if (!r || !r.enabled) continue;
                if (perMat) r.GetPropertyBlock(kickMpb, 0); else r.GetPropertyBlock(kickMpb);
                var c = kickMpb.GetColor(NightMeshes.ColorId);
                if (c.r + c.g + c.b <= 0f) continue;                                                       // no colour of its own: left alone
                kickLampSaved[i] = c; kickLampDimmed[i] = true; any = true;
                if (f <= 0.002f) { r.enabled = false; continue; }
                kickMpb.SetColor(NightMeshes.ColorId, c * f);
                if (perMat) r.SetPropertyBlock(kickMpb, 0); else r.SetPropertyBlock(kickMpb);
            }
            var l = kickLampL != null ? kickLampL[h] : null;
            if (l && l.enabled)
            {
                kickLightSaved[h] = l.intensity; kickLightDimmed[h] = true; any = true;
                if (f <= 0.002f) l.enabled = false; else l.intensity *= f;
            }
            return any;
        }

        /// <summary>Every lamp piece the picture's fade changed back to what the lamps' own code drew (their own caches stay true).</summary>
        void KickUndim()
        {
            if (kickLampR != null)
                for (int i = 0; i < kickLampDimmed.Length; i++)
                {
                    if (!kickLampDimmed[i]) continue;
                    kickLampDimmed[i] = false;
                    var r = kickLampR[i]; if (!r) continue;
                    bool perMat = KickLampPerMaterial(i / 4);
                    if (!r.enabled) r.enabled = true;
                    if (perMat) r.GetPropertyBlock(kickMpb, 0); else r.GetPropertyBlock(kickMpb);
                    kickMpb.SetColor(NightMeshes.ColorId, kickLampSaved[i]);
                    if (perMat) r.SetPropertyBlock(kickMpb, 0); else r.SetPropertyBlock(kickMpb);
                }
            if (kickLampL != null)
                for (int h = 0; h < kickLightDimmed.Length; h++)
                {
                    if (!kickLightDimmed[h]) continue;
                    kickLightDimmed[h] = false;
                    var l = kickLampL[h]; if (l) { l.enabled = true; l.intensity = kickLightSaved[h]; }
                }
            for (int h = 0; h < kickFade.Length; h++) kickFade[h] = 1f;
        }

        /// <summary>The level pose under the flip (the proof: the frame, the mark and the aim are this pose's).</summary>
        public bool KickLevelPose(out Vector3 pos, out Quaternion rot) { pos = kickLevelPos; rot = kickLevelRot; return kickOn; }
    }
}
