using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's rear ramp door (rule DV118, the door is a rule state: ZObject.ShuttleRampPhase / ShuttleRamp01). The ship
    /// model has no door of its own, so the door is a MODELLED kit (Resources/OpenZ/Vehicles/L16_ShuttleKit, made in the ship model
    /// file's own frame): at the bind the hull's mesh is swapped for the kit's hull with a clean doorway in its back (its frame walls
    /// run forward to the bay's bulkhead), and the kit adds the ramp leaf (the cut skin as a solid ramp: the hull's own paint outside,
    /// a flat tread deck inside), the closed bay behind the doorway (bulkhead, floor, walls, roof, two benches for six, an amber lamp
    /// strip and a red lamp in the doorway head), the two downward rocket nozzles under the engine pods with their throat glow discs,
    /// and the two belly searchlight housings. Every point the view needs comes from the kit's markers (hinge, tip, foot, the
    /// doorway, the aisle and the six seats, the nozzle mouths, the searchlights). The kit's reference markers are checked against
    /// the ship's own markers at the bind, so a kit that does not sit in the ship's frame is reported, never drawn wrong.
    /// The ramp's angle follows the rules exactly: angle = open angle x ease(ShuttleRamp01) (a hydraulic ease opening, a smooth one
    /// closing; the start and end moments are the rules'). The open angle is solved at each opening so the ramp's underside rests on
    /// the ground under it (+2 cm). Sounds on the rules' counters: the opening = the door hiss with its clunk as the ramp lands; the
    /// closing = the ramp's hydraulic, then the door close timed so its clunk is the shut; the shut = the latch. The bay lamp and its
    /// amber light burn while the ramp is not shut. BoardingView reads the same surface as on the APC's ramp (RampPoint, FootPoint,
    /// AislePoint, SeatPoint, AisleDir, HoldFigure) and walks the squad up and down it. View only.
    /// </summary>
    [DefaultExecutionOrder(205)]
    public sealed class ShuttleRearDoor : MonoBehaviour
    {
        public const string KitPath = "OpenZ/Vehicles/L16_ShuttleKit";
        /// <summary>A passenger's height inside (m): under the doorway head (2.3 m above the sill) and the bay roof.</summary>
        public const float FitHeight = 2.0f;
        public const int Seats = 6;
        /// <summary>The deck's walking surface above the chord from the hinge to the tip (m), and how far past the tip a figure steps
        /// on and off (m, on the ground).</summary>
        public const float DeckLift = 0.065f, FootPast = 0.25f;
        /// <summary>Bay lamp: amber light, range (m), intensity; the lamp strip's emission while the ramp is down.</summary>
        public static Color LampColour = new Color(1f, 0.45f, 0.18f); public static float LampRange = 4.2f, LampIntensity = 3.6f, StripGain = 3.5f;   // K256e (review M14): 3.2 -> 4.2 m, 2.4 -> 3.6
        /// <summary>K256e (review M6d): the bay's warm glow while the ramp is down, on its floor (and a share on its walls): by day this share of
        /// the night's (so the doorway reads lit in the sun, not as a black hold), its colour and strength.</summary>
        public static float DayGlow = 0.35f, BayGlowGain = 1.6f, BayWallShare = 0.45f; public static Color BayGlowColour = new Color(1f, 0.62f, 0.30f);
        /// <summary>K256e: a field searchlight is kept this far off by a landing (m: its stand and lamp, as a plant's crown).</summary>
        public static float FieldLightKeepOutM = 1.2f;
        /// <summary>The open angle is searched in this range (degrees).</summary>
        public static float OpenMin = 60f, OpenMax = 110f;

        // ------------------------------------------------------------------ proof counters
        public static int Built, KitMissing, KitMisaligned, Openings, Closings, Shuts, Latches, RampLandings, DoorOpenSounds, HydraulicSounds, DoorCloseSounds;
        /// <summary>K256e: hydraulic sounds at an opening; the figures sitting on the benches at the last shut and the most ever at a shut (the
        /// boarders sat down: seated = boarded); the rules' keep-out hand-overs (the level's plants) and their circles.</summary>
        public static int OpenHydraulics, SeatedAtShut = -1, SeatedPeak, KeepOutFed, KeepOutCircles;
        public static string KitLog = "-";
        public static float LastOpenDeg, LastAlignError;

        static GameObject kit; static bool kitTried;
        static readonly Dictionary<Material, Material[]> matCache = new Dictionary<Material, Material[]>();

        UnitView view; ZObject obj; Transform root, model, hinge, leaf, bay; Light lamp; MeshRenderer bayR, leafR;
        MaterialPropertyBlock mpb;
        // root space points (from the kit's markers)
        Vector3 hingeL, tipL, inDoorL, bayLampL, redLampL, nOut; readonly Vector3[] seatL = new Vector3[Seats], aisleL = new Vector3[Seats];
        readonly List<Vector3> underL = new List<Vector3>();       // the leaf's lowest skin points (rest pose), for the ground fit
        float openDeg = 92f, sign = 1f, angle, len;
        int seenOpens = -1, seenShuts = -1; ZShuttleRamp seenPhase; bool landedSound, closeQueued, seenPending; double closeAt;
        readonly List<(GameObject go, int id)> held = new List<(GameObject, int)>();

        public static ShuttleRearDoor Of(UnitView v) => v ? v.GetComponent<ShuttleRearDoor>() : null;
        public Transform Root => root ? root : transform;
        public Transform Hinge => hinge;
        public ZObject Obj => obj;

        /// <summary>Points of the kit in the ship's root space (the shuttle's other view parts read them).</summary>
        public Vector3 NozzleL { get; private set; }
        public Vector3 NozzleR { get; private set; }
        public Vector3 SearchL { get; private set; }
        public Vector3 SearchR { get; private set; }
        public Vector3 SearchAimL { get; private set; }
        public Vector3 SearchAimR { get; private set; }
        public Renderer NozzleGlowL { get; private set; }
        public Renderer NozzleGlowR { get; private set; }
        /// <summary>The deck's length from the hinge to the tip (m) and the angle drawn now (degrees, 0 = shut).</summary>
        public float Len => len;
        public float Angle => angle;
        public float OpenDeg => openDeg;
        /// <summary>The drawn opening, 0 shut .. 1 the full open angle.</summary>
        public float Open01 => openDeg > 1f ? Mathf.Clamp01(angle / openDeg) : 0f;
        public bool IsOpen => obj != null && obj.ShuttleRampPhase == ZShuttleRamp.Open;
        public bool IsShut => obj == null || (obj.ShuttleRampPhase == ZShuttleRamp.Shut && obj.ShuttleRamp01 <= 0.0);
        public int Seated => held.Count;
        /// <summary>The robot ids of the figures sitting in the bay now (the lost squad's bodies: BoardingView.ShuttleCrewLost).</summary>
        public void HeldIds(List<int> ids) { foreach (var h in held) if (h.go) ids.Add(h.id); }

        // ------------------------------------------------------------------ build
        /// <summary>OzPlayableShuttle.Bind: the kit onto the ship. Null (and an error in the log) when the kit is missing or does not fit.</summary>
        public static ShuttleRearDoor Ensure(UnitView v)
        {
            if (!v || v.Obj == null || !v.Obj.IsShuttle) return null;
            var d = v.GetComponent<ShuttleRearDoor>(); if (d) return d;
            d = v.gameObject.AddComponent<ShuttleRearDoor>();
            if (!d.Build(v)) { Destroy(d); return null; }
            return d;
        }

        static GameObject Kit()
        {
            if (!kitTried) { kitTried = true; kit = Resources.Load<GameObject>(KitPath); }
            return kit;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = Find(c, name); if (r) return r; }
            return null;
        }

        bool Build(UnitView v)
        {
            view = v; obj = v.Obj; root = v.transform; mpb = new MaterialPropertyBlock();
            model = root.Find("Model");
            var k = Kit();
            if (!k || !model) { KitMissing++; KitLog = !k ? "the kit " + KitPath + " is missing" : "the ship has no Model node"; Debug.LogError("[OpenZ] shuttle door: " + KitLog + ": no rear door (the K256b proof fails)"); return false; }
            var kr = k.transform;
            Matrix4x4 Rel(Transform t) => kr.worldToLocalMatrix * t.localToWorldMatrix;
            Vector3 M(string n)
            {
                var t = Find(kr, n); if (!t) throw new System.Exception("kit marker " + n + " missing");
                return root.InverseTransformPoint(model.TransformPoint(kr.InverseTransformPoint(t.position)));
            }
            try
            {
                // the kit sits in the ship's frame: its copies of the ship's own markers meet the ship's markers
                float worst = 0f;
                foreach (var (km, sm) in new[] { ("MK_Ref_Front", "MK_Front"), ("MK_Ref_Top", "MK_Top"), ("MK_Ref_Pad_FL", "MK_Pad_FL"), ("MK_Ref_Pad_BR", "MK_Pad_BR") })
                {
                    var s = Find(model, sm); if (!s) continue;
                    worst = Mathf.Max(worst, (M(km) - root.InverseTransformPoint(s.position)).magnitude);
                }
                LastAlignError = worst;
                if (worst > 0.03f) { KitMisaligned++; KitLog = $"the kit is {worst:F3} m off the ship's own markers"; Debug.LogError("[OpenZ] shuttle door: " + KitLog + ": not used (the K256b proof fails)"); return false; }
                var hullT = Find(model, "SH_Hull"); var hullMf = hullT ? hullT.GetComponent<MeshFilter>() : null; var hullMr = hullT ? hullT.GetComponent<MeshRenderer>() : null;
                var doorT = Find(kr, "SH_Hull_Door"); var doorMf = doorT ? doorT.GetComponent<MeshFilter>() : null;
                if (!hullMf || !hullMr || !doorMf || !doorMf.sharedMesh) throw new System.Exception("SH_Hull / SH_Hull_Door missing");
                var mat = hullMr.sharedMaterial;
                hullMf.sharedMesh = doorMf.sharedMesh;                                                     // the hull with its doorway (same frame, same paint)
                // parts drawn with the hull's own material, under the Model node like the SH_ parts
                Renderer Part(string n, Transform parent)
                {
                    var src = Find(kr, n); var mf = src ? src.GetComponent<MeshFilter>() : null; if (!mf || !mf.sharedMesh) throw new System.Exception("kit part " + n + " missing");
                    var go = new GameObject(n) { layer = root.gameObject.layer };
                    go.transform.SetParent(parent, false);
                    var m = Rel(src); go.transform.localPosition = m.GetColumn(3); go.transform.localRotation = m.rotation; go.transform.localScale = m.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                    return r;
                }
                Part("SH_LiftNozzle_L", model); Part("SH_LiftNozzle_R", model);
                NozzleGlowL = Part("SH_NozzleGlow_L", model); NozzleGlowR = Part("SH_NozzleGlow_R", model);
                NozzleGlowL.shadowCastingMode = NozzleGlowR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Part("SH_BellyLamp_L", model); Part("SH_BellyLamp_R", model);
                // the hinge on the root's x axis through the hinge line, the leaf under it at its rest pose
                hingeL = M("MK_Hinge"); tipL = M("MK_RampTip");
                hinge = new GameObject("DR_Hinge").transform; hinge.SetParent(root, false); hinge.localPosition = hingeL; hinge.localRotation = Quaternion.identity;
                var leafSrc = Find(kr, "DR_RampLeaf"); var leafMf = leafSrc ? leafSrc.GetComponent<MeshFilter>() : null;
                if (!leafMf || !leafMf.sharedMesh) throw new System.Exception("DR_RampLeaf missing");
                var lg = new GameObject("DR_RampLeaf") { layer = root.gameObject.layer }; leaf = lg.transform;
                var lm = Rel(leafSrc);
                leaf.SetParent(hinge, true);
                leaf.SetPositionAndRotation(model.TransformPoint(lm.GetColumn(3)), model.rotation * lm.rotation); leaf.localScale = Vector3.Scale(lm.lossyScale, model.lossyScale);
                lg.AddComponent<MeshFilter>().sharedMesh = leafMf.sharedMesh;
                leafR = lg.AddComponent<MeshRenderer>(); leafR.sharedMaterial = mat;
                // the bay
                var baySrc = Find(kr, "DR_Bay"); var bayMf = baySrc ? baySrc.GetComponent<MeshFilter>() : null;
                if (!bayMf || !bayMf.sharedMesh) throw new System.Exception("DR_Bay missing");
                bay = new GameObject("DR_Bay") { layer = root.gameObject.layer }.transform;
                bay.SetParent(model, false); var bm = Rel(baySrc); bay.localPosition = bm.GetColumn(3); bay.localRotation = bm.rotation; bay.localScale = bm.lossyScale;
                bay.gameObject.AddComponent<MeshFilter>().sharedMesh = bayMf.sharedMesh;
                bayR = bay.gameObject.AddComponent<MeshRenderer>(); bayR.sharedMaterials = Mats(mat, bayMf.sharedMesh.subMeshCount);
                bayR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; bayR.receiveShadows = true;
                // markers
                FootFlat = M("MK_Foot"); inDoorL = M("MK_InDoor"); bayLampL = M("MK_BayLamp"); redLampL = M("MK_RedLamp");
                for (int i = 0; i < Seats; i++) { seatL[i] = M("MK_Seat_" + i); aisleL[i] = M("MK_Aisle_" + i); }
                NozzleL = M("MK_Nozzle_L"); NozzleR = M("MK_Nozzle_R");
                SearchL = M("MK_Search_L"); SearchR = M("MK_Search_R"); SearchAimL = M("MK_SearchAim_L"); SearchAimR = M("MK_SearchAim_R");
            }
            catch (System.Exception ex)
            {
                KitMissing++; KitLog = "the kit is incomplete: " + ex.Message; Debug.LogError("[OpenZ] shuttle door: " + KitLog + " (the K256b proof fails)");
                return false;
            }
            var d = tipL - hingeL; len = d.magnitude;
            nOut = new Vector3(0f, d.z, -d.y).normalized; if (nOut.z > 0f) nOut = -nOut;                  // the chord's outer side: back and down
            // which way about the root's x axis carries the tip down (the file's axes decide it)
            sign = (Quaternion.AngleAxis(10f, Vector3.right) * d).y < d.y ? 1f : -1f;
            // the leaf's underside: its vertices farthest out on the skin side of the chord (rest pose, root space)
            var vs = leaf.GetComponent<MeshFilter>().sharedMesh.vertices; var l2r = root.worldToLocalMatrix * leaf.localToWorldMatrix;
            var cand = new List<(float off, Vector3 p)>();
            for (int i = 0; i < vs.Length; i++) { var p = l2r.MultiplyPoint3x4(vs[i]); cand.Add((Vector3.Dot(p - hingeL, nOut), p)); }
            cand.Sort((a, b) => b.off.CompareTo(a.off));
            for (int i = 0; i < cand.Count && underL.Count < 48; i += Mathf.Max(1, cand.Count / 400)) underL.Add(cand[i].p);
            underL.Add(tipL);
            // the bay lamp
            var lgo = new GameObject("DR_BayLight"); lgo.transform.SetParent(root, false); lgo.transform.localPosition = bayLampL;
            lamp = lgo.AddComponent<Light>(); lamp.type = LightType.Point; lamp.color = LampColour; lamp.range = LampRange; lamp.intensity = LampIntensity;
            lamp.shadows = LightShadows.None; lamp.renderMode = LightRenderMode.ForcePixel; lamp.enabled = false;
            var tp = v.GetComponent<TeamPaint>(); if (tp) tp.Apply();
            openDeg = SolveOpen();
            Built++;
            KitLog = $"kit fitted: deck {len:F2} m, open {openDeg:F1} deg on the ground here, reference markers {LastAlignError * 100f:F1} cm off";
            Debug.Log("[OpenZ] shuttle door: " + KitLog);
            ApplyAngle(0f);
            return true;
        }

        /// <summary>The ramp foot on flat ground (root space, from the kit).</summary>
        public Vector3 FootFlat { get; private set; }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor"),
                            EmMapColorId = Shader.PropertyToID("_EmissionMapColor"), SmoothId = Shader.PropertyToID("_Smoothness"), TeamModeId = Shader.PropertyToID("_TeamMode"),
                            VcId = Shader.PropertyToID("_UseVertexColor"), PfaId = Shader.PropertyToID("_PaintFromAlpha");
        /// <summary>The bay's materials (sub-meshes shell, floor, benches, lamp, red lamp): plain clones of the hull's material.</summary>
        static Material[] Mats(Material src, int n)
        {
            if (!matCache.TryGetValue(src, out var m) || !m[0])
            {
                m = new[] { Clone(src, "OZ_ShuttleBayShell", new Color(0.11f, 0.12f, 0.13f), 0.12f),
                            Clone(src, "OZ_ShuttleBayFloor", new Color(0.30f, 0.31f, 0.33f), 0.35f),
                            Clone(src, "OZ_ShuttleBayBench", new Color(0.24f, 0.26f, 0.18f), 0.15f),
                            Clone(src, "OZ_ShuttleBayLamp", new Color(1f, 0.55f, 0.22f), 0.5f),
                            Clone(src, "OZ_ShuttleBayRedLamp", new Color(0.9f, 0.12f, 0.08f), 0.5f) };
                matCache[src] = m;
            }
            var o = new Material[Mathf.Max(1, n)];
            for (int i = 0; i < o.Length; i++) o[i] = m[Mathf.Min(i, m.Length - 1)];
            return o;
        }
        static Material Clone(Material src, string name, Color c, float smooth)
        {
            var m = new Material(src) { name = name };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, null);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c); else m.color = c;
            if (m.HasProperty(SmoothId)) m.SetFloat(SmoothId, smooth);
            if (m.HasProperty(VcId)) m.SetFloat(VcId, 0f);
            if (m.HasProperty(PfaId)) m.SetFloat(PfaId, 0f);
            if (m.HasProperty(TeamModeId)) m.SetFloat(TeamModeId, 0f);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, Color.black);
            if (m.HasProperty(EmMapColorId)) m.SetColor(EmMapColorId, Color.black);
            return m;
        }

        // ------------------------------------------------------------------ points for BoardingView (world; a robot's transform = its feet + RobotY)
        Vector3 Turn(Vector3 pRoot) => hingeL + Quaternion.AngleAxis(sign * angle, Vector3.right) * (pRoot - hingeL);
        Vector3 DeckRest(float u) => Vector3.Lerp(hingeL, tipL, Mathf.Clamp01(u)) - nOut * DeckLift;
        /// <summary>On the drawn ramp's deck, u = 0 at the hinge (the sill) .. 1 at the tip.</summary>
        public Vector3 RampPoint(float u, float robotY) => Root.TransformPoint(Turn(DeckRest(u))) + Root.up * robotY;
        /// <summary>The ground spot past the ramp's tip where a figure steps on / off (not grounded yet): the rules' ramp foot (slot 0,
        /// ShuttleBackM behind the middle), or FootPast metres past the tip of the ramp opened on this ground when that reaches further.</summary>
        public Vector3 FootPoint()
        {
            var tipOpen = hingeL + Quaternion.AngleAxis(sign * openDeg, Vector3.right) * (tipL - hingeL);
            float z = Mathf.Min(tipOpen.z - FootPast, -(float)ZWorld.ShuttleBackM);
            return Root.TransformPoint(new Vector3(0f, 0f, z));
        }
        /// <summary>The bay floor in the aisle at a seat's row; k = -1 just inside the doorway.</summary>
        public Vector3 AislePoint(int k, float robotY) => Root.TransformPoint(k < 0 ? inDoorL : aisleL[((k % Seats) + Seats) % Seats]) + Root.up * robotY;
        /// <summary>The top of the bench under a passenger's hips (k 0..5: 0,1 the front pair, even = +x side).</summary>
        public Vector3 SeatPoint(int k) => Root.TransformPoint(seatL[((k % Seats) + Seats) % Seats]);
        /// <summary>Facing of a seated passenger: toward the aisle.</summary>
        public Vector3 AisleDir(int k) { var s = seatL[((k % Seats) + Seats) % Seats]; return Root.TransformDirection(new Vector3(-Mathf.Sign(s.x), 0f, 0f)); }
        /// <summary>The seat that is k-th from the front (boarding fills the bay from the front) and k-th from the doorway (unloading).</summary>
        public static int FrontSeat(int k) => ((k % Seats) + Seats) % Seats;
        public static int DoorSeat(int k) => Seats - 1 - FrontSeat(k);

        /// <summary>BoardingView: a seated figure stays on its bench until the ramp is shut.</summary>
        public void HoldFigure(GameObject go, int robotId = -1) { if (go) held.Add((go, robotId)); }

        // ------------------------------------------------------------------ the angle
        /// <summary>The open angle that rests the ramp's underside on the ground under it (+2 cm): 8 halvings in OpenMin .. OpenMax.</summary>
        float SolveOpen()
        {
            var g = ZGame.I; if (!g || underL.Count == 0) return 92f;
            float Clear(float deg)
            {
                var q = Quaternion.AngleAxis(sign * deg, Vector3.right); float worst = float.MaxValue;
                foreach (var p in underL)
                {
                    var w = OzPlayableShuttle.RampSolvePoint(obj, Root, hingeL + q * (p - hingeL));   // K426: on the PARKED pose (a ramp started while the legs settle rests on the ground: View/OzPlayableShuttle.ExitSooner.cs)
                    worst = Mathf.Min(worst, w.y - g.GroundY(w.x, w.z));
                }
                return worst;
            }
            float lo = OpenMin, hi = OpenMax;
            if (Clear(hi) > 0.02f) return hi;
            for (int i = 0; i < 8; i++) { float mid = (lo + hi) * 0.5f; if (Clear(mid) > 0.02f) lo = mid; else hi = mid; }
            LastOpenDeg = (lo + hi) * 0.5f;
            return LastOpenDeg;
        }

        void ApplyAngle(float deg)
        {
            angle = deg;
            if (hinge) hinge.localRotation = Quaternion.AngleAxis(sign * angle, Vector3.right);
        }

        /// <summary>The rules' ramp, eased: opening r^2 (3 - 2 r) (a hydraulic start, a firm stop), closing SmoothStep. The sim value is moved
        /// on by the part of the tick already elapsed.</summary>
        float EasedNow()
        {
            double r = obj.ShuttleRamp01; var g = ZGame.I; var ph = obj.ShuttleRampPhase;
            double t = g ? g.TickAlpha * ZConst.TICK : 0.0;
            if (ph == ZShuttleRamp.Opening) r = System.Math.Min(1.0, r + t / ZWorld.ShuttleRampOpenSec);
            else if (ph == ZShuttleRamp.Closing) r = System.Math.Max(0.0, r - t / ZWorld.ShuttleRampCloseSec);
            float f = (float)r;
            return ph == ZShuttleRamp.Closing ? Mathf.SmoothStep(0f, 1f, f) : f * f * (3f - 2f * f);
        }

        // ------------------------------------------------------------------ per frame: angle, lamp, sounds on the rules' counters
        void LateUpdate()
        {
            if (!hinge || obj == null) return;
            bool alive = !obj.Destroyed && view && !view.Dying;
            var w = ZGame.I ? ZGame.I.World : null;
            FeedKeepOut(w);                                                                              // K256e: the level's plants to the rules' landing spot rule (once, again only if the list changes)
            if (seenOpens < 0) { seenOpens = obj.ShuttleRampOpens; seenShuts = obj.ShuttleRampShuts; seenPhase = obj.ShuttleRampPhase; }
            var ph = obj.ShuttleRampPhase;
            if (alive && obj.ShuttleRampOpens != seenOpens)
            {   // an opening began (also a reversal while closing): the angle that rests on this ground, the hiss (its clunk = the tip landing)
                seenOpens = obj.ShuttleRampOpens; Openings++; landedSound = false; closeQueued = false;
                openDeg = SolveOpen();
                OzShuttleRampSound.DoorOpen(this, hinge.position, obj.Id); DoorOpenSounds++;                   // K422: its clunk on the quicker landing (View/OzShuttleRampSound.cs)
                OzShuttleRampSound.Hydraulic(this, hinge.position, obj.Id, 1.05f, true); HydraulicSounds++; OpenHydraulics++;                 // K256e (review M6e): the hydraulic at EVERY opening, not only at a reversal
                OzSoundLog.Add("DOOR", "shuttle_ramp", null, "ramp opening " + openDeg.ToString("F1") + " deg", 0f, hinge.position);
            }
            if (alive && ph == ZShuttleRamp.Closing && seenPhase != ZShuttleRamp.Closing)
            {   // the closing starts: the hydraulic now, the door close so that its clunk (0.5 s into the clip) is the shut
                Closings++;
                OzShuttleRampSound.Hydraulic(this, hinge.position, obj.Id, 1f, false); HydraulicSounds++;      // K422: fades as the ramp shuts
                closeQueued = true; closeAt = (w != null ? w.Time : 0.0) + ZWorld.ShuttleRampCloseSec - 0.5;
                OzSoundLog.Add("DOOR", "shuttle_ramp", null, "ramp closing", 0f, hinge.position);
            }
            if (closeQueued && w != null && w.Time >= closeAt && ph == ZShuttleRamp.Closing) { closeQueued = false; Fire("shuttle_door_close", 1f, 1f); DoorCloseSounds++; }
            if (ph != ZShuttleRamp.Closing) closeQueued = false;
            if (obj.ShuttleRampShuts != seenShuts)
            {   // fully shut: the latch locks; the figures inside are no longer seen
                seenShuts = obj.ShuttleRampShuts; Shuts++;
                SeatedAtShut = held.Count; SeatedPeak = Mathf.Max(SeatedPeak, held.Count);                    // K256e: the boarders on their benches when the ramp shut
                if (alive && BoardingView.I && BoardingView.I.sounds) { BoardingView.Latch(hinge.position, obj.Id); Latches++; }
                OzSoundLog.Add("DOOR", "latch", null, "ramp shut + latched", 0f, hinge.position);
                ClearHeld();
                if (BoardingView.I) BoardingView.I.ShuttleClearWaiting();
            }
            OzShuttleRampSound.Tick(this, ph, hinge.position);                                          // K422: the hydraulic fades once the ramp stops (View/OzShuttleRampSound.cs)
            seenPhase = ph;
            // EXIT given: the passengers sit on the benches by the doorway while the ramp comes down (BoardingView turns them into the exit figures)
            bool pend = alive && obj.ShuttleExitPending;
            if (pend && !seenPending && BoardingView.I) BoardingView.I.ShuttleSeatWaiting(view, this);
            seenPending = pend;
            if (BoardingView.I) BoardingView.I.ShuttleSeatTick(view, this);
            ApplyAngle(openDeg * EasedNow());
            if (alive && ph == ZShuttleRamp.Open && !landedSound)
            {   // the tip has come down: a small puff of dust where it lands
                landedSound = true; RampLandings++;
                var fx = OpenZFxPlayer.I; var tip = Root.TransformPoint(Turn(tipL));
                if (fx && fx.isActiveAndEnabled && WreckFx.OnScreen(tip)) { var gy = ZGame.I ? ZGame.I.GroundY(tip.x, tip.z) : tip.y; if (!fx.WetAt(tip)) fx.Dust(new Vector3(tip.x, gy + 0.1f, tip.z), 0.35f); }
            }
            float o01 = Open01; bool lit = alive && o01 > 0.02f;
            if (lamp && lamp.enabled != lit) lamp.enabled = lit;
            if (lamp && lit) lamp.intensity = LampIntensity * Mathf.Clamp01(o01 * 3f);
            if (bayR)
            {
                float k = lit ? Mathf.Clamp01(o01 * 3f) : 0f;
                Emit(bayR, 3, new Color(1f, 0.42f, 0.12f) * (StripGain * k));
                float glow = BayGlowGain * k * Mathf.Lerp(DayGlow, 1f, Mathf.Clamp01(NightGrade.NightK));      // K256e (M6d): the warm bay by day too (35 % of the night's)
                Emit(bayR, 1, BayGlowColour * glow); Emit(bayR, 0, BayGlowColour * (glow * BayWallShare));
                Emit(bayR, 4, new Color(1f, 0.10f, 0.05f) * (2.5f * (lit ? 0.6f + 0.4f * Mathf.Sin(Time.time * 6f) : 0f)));
            }
        }

        float[] lastEm = { -1f, -1f, -1f, -1f, -1f, -1f };
        int baySubs = -1;                                                                              // the bay's sub-mesh count, read once (sharedMaterials makes a new array per call)
        void Emit(Renderer r, int sub, Color c)
        {
            if (baySubs < 0) baySubs = r.sharedMaterials.Length;
            if (sub >= baySubs || sub >= lastEm.Length) return;
            float key = c.r + c.g + c.b; if (Mathf.Abs(lastEm[sub] - key) < 0.01f) return; lastEm[sub] = key;
            r.GetPropertyBlock(mpb, sub); mpb.SetColor(EmissionId, c); r.SetPropertyBlock(mpb, sub);
        }

        void Fire(string id, float vol, float pitch) { if (AudioDirector.Has(id)) AudioDirector.Fire(id, hinge.position, obj.Id, vol, pitch); else OzSoundLog.Add("DOOR", id, null, "DROP no event", vol, hinge.position); }

        void ClearHeld() { foreach (var h in held) if (h.go) Destroy(h.go); held.Clear(); }

        static int keepOutFrame = -1;
        /// <summary>K256e (review M7): the rules' landing spot rule keeps every landing off the level's drawn plants (their crowns: the same
        /// list and the same numbers OzShuttleSkyTop takes: foot, height x scale, radius x scale) and the field searchlights. Handed over once
        /// (the level's plants are fixed by the level), again only when the plant list changes (more plants hooked after the start).</summary>
        static void FeedKeepOut(ZWorld w)
        {
            if (w == null || keepOutFrame == Time.frameCount) return;
            keepOutFrame = Time.frameCount;
            var fw = OzFloraWind.I; int np = fw != null ? fw.All.Count : 0, nl = OzFieldSearchlight.All.Count;
            int stamp = np * 64 + Mathf.Min(63, nl);
            if (w.ShuttleKeepOutStamp == stamp) return;
            var list = new List<double>(np * 4 + nl * 4 + 4);
            if (fw != null)
                foreach (var p in fw.All)
                {
                    if (p == null || !p.R || p.Gone) continue;
                    var t = p.R.transform; float sc = Mathf.Abs(t.lossyScale.y), sr = Mathf.Abs(t.lossyScale.x);
                    float h = p.Height * sc, r = Mathf.Max(0.5f, p.Radius * sr);
                    list.Add(t.position.x * 8.0); list.Add(-t.position.z * 8.0); list.Add(r * 8.0); list.Add(h);
                }
            foreach (var l in OzFieldSearchlight.All)
                if (l) { var q = l.transform.position; list.Add(q.x * 8.0); list.Add(-q.z * 8.0); list.Add(FieldLightKeepOutM * 8.0); list.Add(3.0); }
            w.ShuttleSetKeepOut(list, stamp);
            KeepOutFed++; KeepOutCircles = w.ShuttleKeepOutCount;
        }
        void OnDestroy() => ClearHeld();

        /// <summary>Takes the seated figures off their benches for the break-up (they become the lost squad's bodies).</summary>
        public List<(GameObject go, int id)> TakeHeld() { var l = new List<(GameObject, int)>(held); held.Clear(); return l; }

        /// <summary>OzShuttleBreakup: the ramp leaf leaves as its own piece (returned: its renderer at the drawn pose, now hidden), the bay
        /// and its lamp go dark.</summary>
        public MeshRenderer BreakOff()
        {
            if (lamp) lamp.enabled = false;
            if (bay) bay.gameObject.SetActive(false);
            var r = leafR; if (leafR) leafR.enabled = false;
            enabled = false;
            return r;
        }
    }
}
