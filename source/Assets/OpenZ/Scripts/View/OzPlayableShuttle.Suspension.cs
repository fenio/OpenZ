using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's VISIBLE leg suspension (rule DV118, view only; the twin tools/susp.py of the kit holds the same numbers and
    /// rules). It takes over K256g's leg springs (OzPlayableShuttle.Touches.cs: its dip and leg swing stay as the fallback when this rig
    /// cannot be fitted):
    /// <list type="bullet">
    /// <item>PARKED = the model's pose, unchanged (the ramp, the boarding, the deck fit, every proof's numbers).</item>
    /// <item>IN THE AIR the legs are unloaded: each foot hangs <see cref="SuspTravelM"/> lower under the hull than parked. The pads are drawn
    /// at the rules' height all the way down (the hull that much over them), so they touch at the rules' landing moment; between the dark
    /// rod's foot and the pad's collar the strut's chrome PISTON shows, and the struts hang a few degrees steeper.</item>
    /// <item>THE TOUCHDOWN (a normal landing, the repair station's deck): the jets cut and the weight comes onto the legs over
    /// <see cref="SuspLoadSec"/>: a damped spring (<see cref="SuspLandHz"/>, damping <see cref="SuspLandZeta"/>) sinks the hull the whole
    /// travel and on past the parked pose (the struts slide up into their pods), one soft rebound, settled in about 1.2 s; a settle rock
    /// (<see cref="SuspRockPitchDeg"/> nose down first, <see cref="SuspRockRollDeg"/> of roll to a side of its own) eases out with it. The
    /// pads stay exactly where they touched (each foot is the exact inverse of the hull's offset: no slide, never into the ground), the
    /// pistons slide into the struts, the struts splay out. A small light ground-coloured dust puff at each pad (the downwash's own ring;
    /// a deck gives its grey dust), a hydraulic hiss at once, the struts' soft stop (a thud) at the deepest point, a creak at the rebound.</item>
    /// <item>THE TAKE-OFF is the reverse: the legs unload over <see cref="SuspUnloadSec"/> (the hull rises off its springs, a hiss) while the
    /// pads stay on the ground: the drawn base lags the rules' climb by a smoothstep over <see cref="SuspPadLagSec"/>; when the legs are fully
    /// out the pads leave the ground (a soft clunk).</item>
    /// <item>A CRASH LANDING keeps K256e's whole pose (OzPlayableShuttle.Crash.cs: the tilt, the slam, the hop, the slump, the skid). Its legs,
    /// still out, take the hit: each foot is planted the moment it touches and its strut is pushed in to the model length, then it locks
    /// (the low side at the slam, the other side as it bangs down); the struts' give at the slump is the dark rod sliding up into its pod;
    /// the final level settle dips <see cref="SuspCrashDipM"/> (hiss and creak).</item>
    /// <item>CALM SCREEN: the same landing, more damped (no visible rebound), the rock halved.</item>
    /// </list>
    /// THE RIG (per leg, cut once from the ship's own mesh, view only): the FOOT (the pad and its collar: every triangle whose three corners
    /// lie at or under <see cref="SuspAnkleY"/>) stays on the leg's own object, so the pad-level lamps and neon rings riding on it keep their
    /// place; the STRUT (the dark rod and the red sleeve) is a new part that turns about the hip (on the rod's axis, inside the pod) to aim
    /// at the ankle (the axis at the collar's top) and keeps its length; the chrome PISTON is a new cylinder on that axis, as long as the
    /// gap. The pieces take the leg's paint every frame (TeamPaint's block, the accent band of the whole leg).
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        // ------------------------------------------------------------------ the numbers (the twin's: tools/susp.py)
        /// <summary>Off = K256g's springs as before (a test switch, read when a ship's view first poses; the rig also falls back by itself,
        /// with a warning, when the model is not as measured).</summary>
        public static bool SuspensionOn = true;
        /// <summary>The legs' free travel (m): the hull over its pads in the air, over the parked pose. Under the downwash's deck test (0.5 m).</summary>
        public static float SuspTravelM = 0.42f;
        /// <summary>The landing: spring (Hz), damping, the weight coming on (s); CALM SCREEN's damping.</summary>
        public static float SuspLandHz = 1.45f, SuspLandZeta = 0.34f, SuspLoadSec = 0.20f, SuspCalmZeta = 0.62f;
        /// <summary>The settle rock: its first swing (deg, nose down; roll), spring (Hz), damping, push (s); its pivot (root space).</summary>
        public static float SuspRockPitchDeg = 3.0f, SuspRockRollDeg = 1.5f, SuspRockHz = 1.25f, SuspRockZeta = 0.40f, SuspRockPushSec = 0.16f;
        public static Vector3 SuspRockPivot = new Vector3(0f, 1.2f, -0.869f);
        /// <summary>The take-off: the legs unload (s); the drawn base lags the rules' climb (s).</summary>
        public static float SuspUnloadSec = 0.35f, SuspPadLagSec = 0.60f;
        /// <summary>The hull eases onto its full travel in the air (s); a crash descent eases the hull onto K256e's pose between these
        /// seconds before the contact.</summary>
        public static float SuspAirSmoothSec = 0.25f, SuspRoughFrom = 1.3f, SuspRoughTo = 0.5f;
        /// <summary>The crash's final level settle: its dip (m) on K256g's curve (2.0 Hz, 0.30, a 0.08 s push).</summary>
        public static float SuspCrashDipM = 0.10f;
        /// <summary>A crash: a leg whose pad is down (within this, m) locks; its travel eases to 0 at this rate (m/s).</summary>
        public static float SuspJamM = 0.05f, SuspJamRate = 1.0f;
        /// <summary>The cut between the foot and the strut (root space y, m); the piston reaches this far into the rod and the collar (m).</summary>
        public static float SuspAnkleY = 0.402f, SuspPistonIn = 0.12f, SuspPistonDown = 0.03f;
        /// <summary>The hip: the rod's axis at this height (root space, m; inside the pod).</summary>
        public static float SuspHipY = 1.20f;
        const int SuspPistonSides = 14, SuspRate = 240, SuspSub = 10; const float SuspSec = 3f;
        /// <summary>The atlas texel of the piston's steel: a 12 x 12 uniform light grey block (no team paint, no glow; L16_Shuttle_Albedo).</summary>
        static readonly Vector2 SuspChromeUv = new Vector2(574.5f / 2048f, 1f - 406.5f / 2048f);
        // the rig measured from L16_Shuttle.fbx (tools/rig_measure.py, out/rig_measure.txt), root space, FL FR BL BR (LegNames' order):
        // the ankle (the rod's axis at y 0.40), the axis (unit, up), the piston's radius (0.8 x the rod's inscribed radius); the triangles
        // of the foot and of the strut (the check that the model is the measured one)
        static readonly Vector3[] SuspAnkle = { new Vector3(2.1800f, 0.4000f, 1.0490f), new Vector3(-2.1930f, 0.4000f, 1.0550f), new Vector3(1.7500f, 0.4000f, -2.7400f), new Vector3(-1.7560f, 0.4000f, -2.7440f) };
        static readonly Vector3[] SuspAxis = { new Vector3(-0.2805f, 0.9388f, -0.1997f), new Vector3(0.3972f, 0.8791f, -0.2633f), new Vector3(-0.2726f, 0.9436f, 0.1880f), new Vector3(0.2944f, 0.9364f, 0.1909f) };
        static readonly float[] SuspPistonR = { 0.063f, 0.069f, 0.078f, 0.074f };
        static readonly int[] SuspFootTris = { 150, 145, 148, 133 }, SuspStrutTris = { 190, 187, 202, 210 };

        // ------------------------------------------------------------------ read by the Play checks
        /// <summary>Rigs fitted, fallbacks (logged), landings / crash settles / take-offs drawn on it, its sounds and pad puffs.</summary>
        public static int SuspRigs, SuspFallbacks, SuspLandings, SuspCrashSettles, SuspTakeoffs, SuspHisses, SuspThuds, SuspPuffs, SuspLocks;
        /// <summary>The last landing: the hull's whole sink from its touchdown height (m), past the parked pose (m), its rebound over the
        /// parked pose (m), the struts' widest turn from the model (deg), the most a strut slid into its pod (m), the piston's gap at the
        /// touchdown (m), the rock (deg), settled (s after the touchdown, the hull within 1 cm for good), the worst slide and sink of a
        /// planted pad (m, measured on the drawn feet), the time the pads stayed down at the last take-off (s).</summary>
        public static float SuspSinkSeen, SuspOverSeen, SuspReboundSeen, SuspTurnSeen, SuspSlideSeen, SuspPistonAirSeen, SuspRockSeen, SuspSettleSeen,
                            SuspPadSlideMax, SuspPadSinkMax, SuspTakeoffPlantedSec = -1f;
        /// <summary>This ship: the rig is fitted; the hull over its parked pose now (m); the mode; each leg's piston gap (m).</summary>
        public bool SuspRigged => susRig != null;
        public float SuspXNow => susX;
        public string SuspMode => susMode.ToString();
        public float SuspGap(int i) => susRig != null && i >= 0 && i < 4 ? susGap[i] : 0f;

        enum SusMode { Parked, Air, Land, Unload, Crash, CrashSettle }
        SusMode susMode; bool susInit, susCalm; float susX, susXv, susLastX, susT, susX0, susV0, susSide = 1f, susPitch, susRoll, susDTake, susLag, susStartH;
        int susEv; float susBaseY;
        readonly float[] susD = new float[4], susFree = new float[4], susGap = new float[4]; readonly bool[] susJam = new bool[4];
        readonly Vector3[] susPadAt = new Vector3[4]; bool susPadSet;
        Quaternion susRk = Quaternion.identity;
        SusLeg[] susRig;
        MaterialPropertyBlock susMpb;
        static float[] susLand, susCalmTab, susRockTab, susCrashTab;
        static Mesh susPistonMesh;
        static readonly int SusAccentFromId = Shader.PropertyToID("_AccentFrom"), SusAccentToId = Shader.PropertyToID("_AccentTo"),
                            SusAccentLoId = Shader.PropertyToID("_AccentLo"), SusAccentHiId = Shader.PropertyToID("_AccentHi");

        sealed class SusLeg
        {
            public Transform leg, strut, piston; public MeshRenderer legR, strutR, pistonR; public Vector3 restPos, ankle, axis, hip; public Quaternion restRot;
            public float len, r, accLo = -1e6f, accHi = 1e6f; public bool accent; public Vector3 padLocal; public int subs;
        }

        // ------------------------------------------------------------------ the curves (the twin's tables)
        static float[] SusStep(float hz, float zeta, float loadSec)
        {
            int n = Mathf.CeilToInt(SuspSec * SuspRate) + 1; var c = new float[n];
            double w = 2.0 * System.Math.PI * hz, h = 1.0 / (SuspRate * SuspSub), y = 0.0, v = 0.0;
            for (int i = 1; i < n; i++)
            {
                for (int s = 0; s < SuspSub; s++)
                {
                    double tt = ((i - 1) * SuspSub + s) * h, u = System.Math.Min(1.0, System.Math.Max(0.0, tt / loadSec)), L = u * u * (3.0 - 2.0 * u);
                    double a = w * w * (L - y) - 2.0 * zeta * w * v;
                    v += a * h; y += v * h;
                }
                c[i] = (float)y;
            }
            return c;
        }

        static float[] SusKick(float hz, float zeta, float push)
        {
            int n = Mathf.CeilToInt(SuspSec * SuspRate) + 1; var c = new float[n];
            double w = 2.0 * System.Math.PI * hz, h = 1.0 / (SuspRate * SuspSub), x = 0.0, v = 0.0;
            for (int i = 1; i < n; i++)
            {
                for (int s = 0; s < SuspSub; s++)
                {
                    double tt = ((i - 1) * SuspSub + s) * h, f = tt < push ? System.Math.Sin(System.Math.PI * tt / push) : 0.0;
                    double a = -w * w * x - 2.0 * zeta * w * v + f;
                    v += a * h; x += v * h;
                }
                c[i] = (float)x;
            }
            int ip = 0;
            for (int i = 1; i < n; i++) { if (c[i] > c[ip]) ip = i; else if (c[i] < c[ip] - 1e-9f) break; }
            float pk = c[ip] > 0f ? c[ip] : 1f;
            for (int i = 0; i < n; i++) c[i] /= pk;
            return c;
        }

        static void SusTables()
        {
            if (susLand != null) return;
            susLand = SusStep(SuspLandHz, SuspLandZeta, SuspLoadSec); susCalmTab = SusStep(SuspLandHz, SuspCalmZeta, SuspLoadSec);
            susRockTab = SusKick(SuspRockHz, SuspRockZeta, SuspRockPushSec); susCrashTab = SusKick(2.0f, 0.30f, 0.08f);
        }

        static float SusSample(float[] c, float t)
        {
            float f = t * SuspRate; int i = Mathf.FloorToInt(f);
            if (i < 0) return c[0];
            if (i >= c.Length - 1) return c[c.Length - 1];
            return c[i] + (c[i + 1] - c[i]) * (f - i);
        }

        /// <summary>The twin's checks call these (the curves alone): the hull over its parked pose t s after a touchdown; the rock's share.</summary>
        public static float SuspLandCurve(float t, bool calm) { SusTables(); return SuspTravelM * (1f - SusSample(calm ? susCalmTab : susLand, t)); }
        public static float SuspRockCurve(float t) { SusTables(); return SusSample(susRockTab, t); }
        public static float SuspCrashCurve(float t) { SusTables(); return SusSample(susCrashTab, t); }

        static float SusHermite(float x0, float v0, float x1, float T, float u)
        {
            u = Mathf.Clamp01(u);
            float h00 = 2f * u * u * u - 3f * u * u + 1f, h10 = u * u * u - 2f * u * u + u, h01 = -2f * u * u * u + 3f * u * u;
            return h00 * x0 + h10 * T * v0 + h01 * x1;
        }

        // ------------------------------------------------------------------ the rig, cut once from the ship's own legs
        bool SuspReady()
        {
            if (susRig != null) return true;
            if (susInit || !SuspensionOn) return false;
            susInit = true;
            string why = SusFit();
            if (why != null) { susRig = null; SuspFallbacks++; Debug.LogWarning("[OpenZ] shuttle #" + (Obj != null ? Obj.Id : -1) + ": the visible leg suspension could not be fitted (" + why + "): K256g's leg springs draw it"); return false; }
            SuspRigs++;
            bool air = Obj != null && Obj.ShuttleAirborne;
            susMode = air ? SusMode.Air : SusMode.Parked; susX = susLastX = air ? SuspTravelM : 0f; susXv = 0f;
            for (int i = 0; i < 4; i++) { susFree[i] = SuspTravelM; susJam[i] = false; }
            return true;
        }

        string SusFit()
        {
            if (!legs[0] || !legs[1] || !legs[2] || !legs[3]) return "the legs were not found by name";
            var rig = new SusLeg[4]; var made = new List<Object>();
            SusPiston();
            for (int i = 0; i < 4; i++)
            {
                var lg = legs[i]; var mf = lg.GetComponent<MeshFilter>(); var mr = lg.GetComponent<MeshRenderer>();
                if (!mf || !mr || !mf.sharedMesh || !lg.parent) return LegNames[i] + " has no mesh";
                var src = mf.sharedMesh; if (!src.isReadable) return LegNames[i] + "'s mesh is not readable";
                var p = lg.parent; var toRoot = transform.worldToLocalMatrix * lg.localToWorldMatrix;
                var pm = transform.worldToLocalMatrix * p.localToWorldMatrix; var ps = pm.lossyScale;
                if (Mathf.Abs(ps.x - ps.y) > 1e-3f || Mathf.Abs(ps.x - ps.z) > 1e-3f) return "the model's frame is not uniformly scaled";
                var vs = src.vertices; var R = new float[vs.Length];
                for (int k = 0; k < vs.Length; k++) R[k] = toRoot.MultiplyPoint3x4(vs[k]).y;
                int subs = src.subMeshCount; var foot = new List<int>[subs]; var strut = new List<int>[subs]; int nf = 0, ns = 0;
                for (int s = 0; s < subs; s++)
                {
                    foot[s] = new List<int>(); strut[s] = new List<int>();
                    var tri = src.GetTriangles(s);
                    for (int k = 0; k + 2 < tri.Length; k += 3)
                    {
                        bool f = R[tri[k]] <= SuspAnkleY && R[tri[k + 1]] <= SuspAnkleY && R[tri[k + 2]] <= SuspAnkleY;
                        var dst = f ? foot[s] : strut[s]; dst.Add(tri[k]); dst.Add(tri[k + 1]); dst.Add(tri[k + 2]);
                        if (f) nf++; else ns++;
                    }
                }
                if (nf != SuspFootTris[i] || ns != SuspStrutTris[i]) return LegNames[i] + " is not the measured model (" + nf + " / " + ns + " triangles, measured " + SuspFootTris[i] + " / " + SuspStrutTris[i] + ")";
                var L = new SusLeg { leg = lg, legR = mr, subs = mr.sharedMaterials.Length, restPos = legRest[i], restRot = lg.localRotation, ankle = SuspAnkle[i], axis = SuspAxis[i].normalized, r = SuspPistonR[i] };
                L.hip = L.ankle + L.axis * ((SuspHipY - L.ankle.y) / L.axis.y); L.len = (L.hip - L.ankle).magnitude;
                L.padLocal = lg.InverseTransformPoint(transform.TransformPoint(new Vector3(PadSoles[i].x, 0f, PadSoles[i].z)));
                var mat = mr.sharedMaterial;
                if (mat && mat.HasProperty(SusAccentFromId))
                {   // TeamPaint's accent band for the whole leg (its mesh space): the strut keeps the leg's band, not one of its own
                    float from = mat.GetFloat(SusAccentFromId), to = mat.GetFloat(SusAccentToId); var b = src.bounds;
                    if (from < 1.5f) { L.accent = true; L.accLo = b.min.y + from * b.size.y; L.accHi = b.min.y + to * b.size.y; }
                }
                var footMesh = SusPart(src, foot, src.name + "_Foot"); var strutMesh = SusPart(src, strut, src.name + "_Strut");
                // the strut: a new part beside the leg (the same frame, paint and settings)
                var sg = new GameObject(LegNames[i] + "_Strut") { layer = lg.gameObject.layer };
                sg.transform.SetParent(p, false); sg.transform.localPosition = lg.localPosition; sg.transform.localRotation = lg.localRotation; sg.transform.localScale = lg.localScale;
                sg.AddComponent<MeshFilter>().sharedMesh = strutMesh;
                var sr = sg.AddComponent<MeshRenderer>(); SusCopy(mr, sr);
                // the piston: under the root, root-space units
                var pg = new GameObject(LegNames[i] + "_Piston") { layer = lg.gameObject.layer };
                pg.transform.SetParent(transform, false);
                pg.AddComponent<MeshFilter>().sharedMesh = susPistonMesh;
                var pr = pg.AddComponent<MeshRenderer>(); SusCopy(mr, pr); pr.sharedMaterials = new[] { mr.sharedMaterial };
                mf.sharedMesh = footMesh;                                                                  // the leg's own object keeps the foot (its lamps ride on it)
                L.strut = sg.transform; L.strutR = sr; L.piston = pg.transform; L.pistonR = pr;
                made.Add(footMesh); made.Add(strutMesh);
                rig[i] = L;
            }
            var keeper = GetComponent<OzShuttleSuspensionMeshes>(); if (!keeper) keeper = gameObject.AddComponent<OzShuttleSuspensionMeshes>();
            keeper.Own.AddRange(made);
            susRig = rig;
            return null;
        }

        static void SusCopy(MeshRenderer from, MeshRenderer to)
        {
            to.sharedMaterials = from.sharedMaterials; to.shadowCastingMode = from.shadowCastingMode; to.receiveShadows = from.receiveShadows;
            to.lightProbeUsage = from.lightProbeUsage; to.reflectionProbeUsage = from.reflectionProbeUsage; to.enabled = from.enabled;
        }

        /// <summary>The triangles of one part as a compact mesh in the leg's own mesh space (every vertex channel the leg has).</summary>
        static Mesh SusPart(Mesh src, List<int>[] tris, string name)
        {
            var map = new Dictionary<int, int>(); var order = new List<int>();
            for (int s = 0; s < tris.Length; s++) foreach (int k in tris[s]) if (!map.ContainsKey(k)) { map[k] = order.Count; order.Add(k); }
            var vs = src.vertices; var ns = src.normals; var ts = src.tangents; var uv = src.uv; var uv2 = src.uv2; var cs = src.colors;
            int n = order.Count;
            var v2 = new Vector3[n]; var n2 = ns.Length == vs.Length ? new Vector3[n] : null; var t2 = ts.Length == vs.Length ? new Vector4[n] : null;
            var u2 = uv.Length == vs.Length ? new Vector2[n] : null; var w2 = uv2.Length == vs.Length ? new Vector2[n] : null; var c2 = cs.Length == vs.Length ? new Color[n] : null;
            for (int j = 0; j < n; j++)
            {
                int k = order[j]; v2[j] = vs[k];
                if (n2 != null) n2[j] = ns[k]; if (t2 != null) t2[j] = ts[k]; if (u2 != null) u2[j] = uv[k]; if (w2 != null) w2[j] = uv2[k]; if (c2 != null) c2[j] = cs[k];
            }
            var m = new Mesh { name = name };
            m.vertices = v2; if (n2 != null) m.normals = n2; if (t2 != null) m.tangents = t2; if (u2 != null) m.uv = u2; if (w2 != null) m.uv2 = w2; if (c2 != null) m.colors = c2;
            m.subMeshCount = tris.Length;
            for (int s = 0; s < tris.Length; s++) { var t = new int[tris[s].Count]; for (int k = 0; k < t.Length; k++) t[k] = map[tris[s][k]]; m.SetTriangles(t, s, false); }
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The piston: a unit cylinder round +y (radius 1, y 0..1), smooth sides, both ends always hidden (no caps), every corner on
        /// the steel texel (one texel: no stretch, no seam).</summary>
        static void SusPiston()
        {
            if (susPistonMesh) return;
            int n = SuspPistonSides; var v = new Vector3[(n + 1) * 2]; var nr = new Vector3[v.Length]; var tg = new Vector4[v.Length]; var uv = new Vector2[v.Length];
            for (int k = 0; k <= n; k++)
            {
                float a = 2f * Mathf.PI * k / n, c = Mathf.Cos(a), s = Mathf.Sin(a);
                for (int e = 0; e < 2; e++)
                {
                    int j = k * 2 + e; v[j] = new Vector3(c, e, s); nr[j] = new Vector3(c, 0f, s); tg[j] = new Vector4(-s, 0f, c, 1f); uv[j] = SuspChromeUv;
                }
            }
            var tri = new int[n * 6]; int q = 0;
            for (int k = 0; k < n; k++) { int a0 = k * 2, a1 = k * 2 + 1, b0 = k * 2 + 2, b1 = k * 2 + 3; tri[q++] = a0; tri[q++] = a1; tri[q++] = b0; tri[q++] = b0; tri[q++] = a1; tri[q++] = b1; }
            susPistonMesh = new Mesh { name = "OZ_ShuttlePiston", hideFlags = HideFlags.DontSave };
            susPistonMesh.vertices = v; susPistonMesh.normals = nr; susPistonMesh.tangents = tg; susPistonMesh.uv = uv; susPistonMesh.triangles = tri;
            susPistonMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ the touchdown, the crash's level settle
        /// <summary>Landed() asks here first (in place of K256g's SpringLanded): a normal landing or the repair deck goes onto the legs now;
        /// a crash landing is drawn by K256e's pose with the legs locked as they touch (<see cref="SuspPose"/>). False: the rig is not fitted.</summary>
        bool SuspLanded(bool rough)
        {
            if (!SuspReady()) return false;
            if (!rough) SuspBegin(1f);
            return true;
        }

        /// <summary>k = 1 a normal landing or the repair deck; k &lt; 1 the crash's level settle (OzPlayableShuttle.Crash.cs asks here first).
        /// False: the rig is not fitted (K256g's spring runs).</summary>
        bool SuspBegin(float k)
        {
            if (!SuspReady()) return false;
            susT = 0f; susEv = 0;
            if (k >= 1f)
            {
                susMode = SusMode.Land; susCalm = OzMotion.Reduced; susSide = (Hash(Obj.Id + 31, Obj.ShuttleLandings) & 1) == 1 ? 1f : -1f;
                SuspLandings++; SpringsStarted++;
                SuspSinkSeen = 0f; SuspOverSeen = 0f; SuspReboundSeen = 0f; SuspTurnSeen = 0f; SuspSlideSeen = 0f; SuspRockSeen = 0f; SuspSettleSeen = -1f;
                SuspPadSlideMax = 0f; SuspPadSinkMax = 0f; susPadSet = false; susStartH = susX;
                SuspPistonAirSeen = 0f; for (int i = 0; i < 4; i++) SuspPistonAirSeen = Mathf.Max(SuspPistonAirSeen, susGap[i]);
                SusSound("shuttle_leg_hiss", 1f, 0.92f);
                SusPuffs();
            }
            else
            {
                susMode = SusMode.CrashSettle; SuspCrashSettles++; SpringsStarted++; SpringsCrash++; SpringDipSeen = 0f; SpringSettleSeen = -1f;   // K256g's numbers (its T4 line reads them)
                SusSound("shuttle_leg_hiss", 0.55f, 0.95f); SusSound("shuttle_leg_spring", 0.45f, 1f);
            }
            return true;
        }

        void SusSound(string id, float vol, float pitch)
        {
            if (!AudioDirector.Has(id)) return;
            AudioDirector.Fire(id, transform.position + Vector3.up * 0.6f, Obj != null ? Obj.Id : -1, vol, pitch);
            LastSound = id;
            if (id == "shuttle_leg_hiss") SuspHisses++; else if (id == "shuttle_leg_thud") SuspThuds++; else if (id == "shuttle_leg_spring") SpringCreaks++;
        }

        /// <summary>A small light ground-coloured puff at each pad as it touches: the downwash's own ring of soft puffs (the ground's dust
        /// colour, OzShuttleDownwash.GroundColour); on a repair station's deck the deck's grey dust (OzShuttleDownwash.DeckDust). One method,
        /// so the premium downwash dust can take it over.</summary>
        void SusPuffs()
        {
            bool deck = DeckStation >= 0 && deckK > 0.5f; var fx = OpenZFxPlayer.I;
            for (int i = 0; i < 4; i++)
            {
                var at = transform.TransformPoint(new Vector3(PadSoles[i].x, 0.05f, PadSoles[i].z));
                if (!DetailKit.OnScreen(Camera.main, at, 0.3f)) continue;
                if (!deck) { if (wash != null) SuspPuffs += wash.Ring(at, 0.35f, 4, 0.45f); continue; }
                if (!fx || !fx.isActiveAndEnabled) continue;
                for (int k = 0; k < 3; k++)
                {
                    float a = (k + Random.value * 0.6f) * 2.094f; var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    fx.EmitChannel("m12_dust", at + o * 0.3f, o * 1.6f + Vector3.up * 0.25f, Random.Range(0.9f, 1.3f), Random.Range(0.5f, 0.8f), OzShuttleDownwash.DeckDust, 0.22f);
                    SuspPuffs++;
                }
            }
        }

        // ------------------------------------------------------------------ Pose(): before the root is placed
        /// <summary>Pose(), in place of K256g's dip: the hull's offset over its base (the pads' frame: the rules' height, or the stand), the
        /// take-off's base lag, and how far each foot hangs under the base. by = the base under its middle (the ground, or the deck).</summary>
        void SuspPose(float dt, ZShuttleState st, float by, ref Vector3 pos, ref Quaternion rot)
        {
            if (!SuspReady()) { pos.y -= SpringStep(dt, st); return; }
            susPitch = 0f; susRoll = 0f; susLag = 0f; susDTake = 0f;
            if (lastState == ZShuttleState.Landed && st == ZShuttleState.Climb && susMode != SusMode.Unload)
            {   // the take-off: the legs unload, the pads stay down until they are fully out
                susMode = SusMode.Unload; susT = 0f; susX0 = susX; susV0 = susXv; susEv = 0; SuspTakeoffs++; SuspTakeoffPlantedSec = -1f;
                for (int i = 0; i < 4; i++) { susJam[i] = false; susFree[i] = SuspTravelM; }
                SusSound("shuttle_leg_hiss", 0.6f, 1.12f);
            }
            if (crashOn && susMode != SusMode.Crash && susMode != SusMode.CrashSettle) { susMode = SusMode.Crash; susT = 0f; }
            bool rough = st == ZShuttleState.Descend && roughArmed;
            switch (susMode)
            {
                case SusMode.Land:
                {
                    susT += dt;
                    susX = SuspLandCurve(susT, susCalm);
                    float r = SuspRockCurve(susT) * (susCalm ? 0.5f : 1f);
                    susPitch = SuspRockPitchDeg * r; susRoll = SuspRockRollDeg * susSide * r;
                    if ((susEv & 1) == 0 && susT > 0.05f && susX > susLastX) { susEv |= 1; SusSound("shuttle_leg_thud", 1f, 0.66f); }             // the deepest point: the struts' soft stop
                    if ((susEv & 3) == 1 && susT > 0.2f && susX < susLastX) { susEv |= 2; SusSound("shuttle_leg_spring", 0.55f, 1f); }          // the rebound's top: the creak
                    SuspSinkSeen = Mathf.Max(SuspSinkSeen, susStartH - susX); SuspOverSeen = Mathf.Max(SuspOverSeen, -susX);
                    if ((susEv & 1) != 0) SuspReboundSeen = Mathf.Max(SuspReboundSeen, susX);
                    SuspRockSeen = Mathf.Max(SuspRockSeen, Mathf.Max(Mathf.Abs(susPitch), Mathf.Abs(susRoll)));
                    if (Mathf.Abs(susX) >= 0.01f || Mathf.Abs(susPitch) >= 0.2f) SuspSettleSeen = susT;                           // the last moment it moved: settled after it
                    if (susT >= SuspSec) { susMode = SusMode.Parked; susX = 0f; }
                    break;
                }
                case SusMode.Parked: susX = 0f; break;
                case SusMode.Crash:
                    susX = 0f;
                    if (!crashOn && st == ZShuttleState.Landed) susMode = SusMode.Parked;
                    break;
                case SusMode.CrashSettle:
                    susT += dt; susX = -SuspCrashDipM * SuspCrashCurve(susT) * (OzMotion.Reduced ? 0.5f : 1f);
                    SpringDipSeen = Mathf.Max(SpringDipSeen, -susX); if (Mathf.Abs(susX) >= 0.01f) SpringSettleSeen = susT;
                    if (susT >= SuspSec) { susMode = SusMode.Parked; susX = 0f; }
                    break;
                case SusMode.Unload:
                {
                    susT += dt;
                    susX = SusHermite(susX0, susV0, SuspTravelM, SuspUnloadSec, susT / SuspUnloadSec);
                    if (susT < SuspPadLagSec) { float r = Mathf.Max(0f, pos.y - by), u = susT / SuspPadLagSec; susLag = -r * (1f - u * u * (3f - 2f * u)); }
                    if (susT >= Mathf.Max(SuspUnloadSec, SuspPadLagSec)) susMode = SusMode.Air;
                    break;
                }
                default:                                                                                   // in the air
                    if (rough) { float u = Mathf.Clamp01((SusLift() * (float)ZWorld.ShuttleDescendSec - SuspRoughTo) / (SuspRoughFrom - SuspRoughTo)); susX = SuspTravelM * u * u * (3f - 2f * u); susXv = 0f; }
                    else susX = Mathf.SmoothDamp(susX, SuspTravelM, ref susXv, SuspAirSmoothSec, Mathf.Infinity, Mathf.Max(1e-5f, dt));
                    break;
            }
            if (susMode != SusMode.Air && dt > 0f) susXv = (susX - susLastX) / dt;
            pos.y += susLag;
            float rd = pos.y - by;
            // the feet: how far each hangs under the base (along its up)
            for (int i = 0; i < 4; i++)
            {
                if (susMode == SusMode.Crash || (susMode == SusMode.Air && rough))
                {
                    float h = susMode == SusMode.Crash ? padH4[i] - (PadSoles[i].x * crashSide > 0f ? crashStrutNow : 0f)   // K256e's model pad over the ground fit (the strut stroke taken out)
                                                      : (pos + rot * PadSoles[i]).y - by;
                    if (!susJam[i] && susMode == SusMode.Crash && h <= SuspJamM && h <= susFree[i] - susX) { susJam[i] = true; susFree[i] = Mathf.Clamp(h + susX, 0f, SuspTravelM); SuspLocks++; }
                    if (susJam[i]) susFree[i] = Mathf.Max(0f, susFree[i] - dt * SuspJamRate);
                    susD[i] = Mathf.Min(h, susFree[i] - susX);
                }
                else if (susMode == SusMode.Unload) susD[i] = Mathf.Min(Mathf.Max(0f, rd), SuspTravelM - susX);
                else { susD[i] = 0f; if (susMode != SusMode.CrashSettle) { susJam[i] = false; susFree[i] = SuspTravelM; } }
            }
            if (susMode == SusMode.Unload)
            {
                susDTake = Mathf.Min(Mathf.Min(susD[0], susD[1]), Mathf.Min(susD[2], susD[3]));
                if ((susEv & 4) == 0 && rd > SuspTravelM - susX + 1e-4f) { susEv |= 4; SuspTakeoffPlantedSec = susT; SusSound("shuttle_leg_thud", 0.4f, 1.2f); }   // fully out: the pads leave
            }
            susLastX = susX;
            // the hull over its base: lifted susX, rocked about the pivot (Unity's Euler(pitch, 0, roll)), the base kept for the feet
            susRk = Quaternion.Euler(susPitch, 0f, susRoll);
            susBaseY = pos.y;
            pos += rot * (new Vector3(0f, susX, 0f) + SuspRockPivot - susRk * SuspRockPivot);
            rot = rot * susRk;
            springDipNow = susBaseY - susDTake - pos.y;                                                   // ShownHeight / FeetHeight: the pads' height (K256g's field)
        }

        float SusLift() => Obj != null ? (float)Obj.ShuttleLift : 0f;

        // ------------------------------------------------------------------ the legs (after the root is placed)
        /// <summary>Pose()'s legs loop, in place of K256g's LegPose: leg i's foot (planted: the inverse of the hull's offset, hanging its drop
        /// under the base), its strut turned about the hip onto the ankle (slid into its pod when shorter), its piston in the gap.</summary>
        void SuspLeg(int i, Transform lg, float crash)
        {
            if (susRig == null) { LegPose(i, lg, crash); return; }
            var L = susRig[i]; if (L == null || !L.leg) return;
            // the foot in the root's space: F(p) = c + Rk^-1 (p - (0, x + d, 0) - c)
            var rkInv = Quaternion.Inverse(susRk); var c = SuspRockPivot;
            var footT = c - rkInv * (c + new Vector3(0f, LegsFoldHang(susX + susD[i]), 0f));   // K388: the piston closes as the legs fold (OzPlayableShuttle.LegsFold.cs)
            var A1 = rkInv * L.ankle + footT;
            var dv = L.hip - A1; float D = dv.magnitude; var u1 = D > 1e-5f ? dv / D : L.axis;
            var q = Quaternion.FromToRotation(L.axis, u1);
            Vector3 B; float gap, slide;
            if (D >= L.len) { B = L.hip - u1 * L.len; gap = D - L.len; slide = 0f; }
            else { B = A1; gap = 0f; slide = L.len - D; }
            susGap[i] = gap;
            // the strut in the root's space: G(p) = B + q (p - ankle)
            var strutT = B - q * L.ankle;
            SusPlace(L.leg, L.restPos, L.restRot, rkInv, footT);
            SusPlace(L.strut, L.restPos, L.restRot, q, strutT);
            float len = gap + SuspPistonIn + SuspPistonDown;
            L.piston.localPosition = A1 - u1 * SuspPistonDown; L.piston.localRotation = Quaternion.FromToRotation(Vector3.up, u1); L.piston.localScale = new Vector3(L.r, len, L.r);
            // the paint: the leg's block (TeamPaint, the empty look) on the new parts, the accent band of the whole leg
            susMpb ??= new MaterialPropertyBlock();
            int subs = L.subs;                                                                          // counted once at the fit (the getter allocates)
            for (int s = 0; s < subs; s++)
            {
                L.legR.GetPropertyBlock(susMpb, s);
                if (L.accent) { susMpb.SetFloat(SusAccentLoId, L.accLo); susMpb.SetFloat(SusAccentHiId, L.accHi); }
                L.strutR.SetPropertyBlock(susMpb, s);
                if (s == 0) { susMpb.SetFloat(SusAccentLoId, -1e6f); susMpb.SetFloat(SusAccentHiId, 1e6f); L.pistonR.SetPropertyBlock(susMpb, 0); }
            }
            if (L.strutR.enabled != L.legR.enabled) L.strutR.enabled = L.legR.enabled;
            bool pon = L.legR.enabled && gap > 0.002f; if (L.pistonR.enabled != pon) L.pistonR.enabled = pon;
            // the proof: the struts' turn and slide, and the planted pads (on the drawn feet)
            if (susMode == SusMode.Land)
            {
                SuspTurnSeen = Mathf.Max(SuspTurnSeen, Vector3.Angle(L.axis, u1)); SuspSlideSeen = Mathf.Max(SuspSlideSeen, slide);
                var pw = L.leg.TransformPoint(L.padLocal);
                if (!susPadSet) { if (i == 3) susPadSet = true; susPadAt[i] = pw; }
                else
                {
                    var dd = pw - susPadAt[i];
                    SuspPadSlideMax = Mathf.Max(SuspPadSlideMax, new Vector2(dd.x, dd.z).magnitude); SuspPadSinkMax = Mathf.Max(SuspPadSinkMax, -dd.y);
                }
            }
        }

        /// <summary>A part of the leg moved by the root-space rigid motion (q, t), from its rest pose under the leg's parent.</summary>
        void SusPlace(Transform part, Vector3 restPos, Quaternion restRot, Quaternion q, Vector3 t)
        {
            var par = part.parent; if (!par) return;
            var pm = transform.worldToLocalMatrix * par.localToWorldMatrix;                                // the parent in the root's space (rigid, uniform scale)
            var pr = pm.rotation; float ps = pm.lossyScale.x; var pp = (Vector3)pm.GetColumn(3);
            var prInv = Quaternion.Inverse(pr);
            var qK = prInv * q * pr; var tK = prInv * (q * pp + t - pp) / Mathf.Max(1e-6f, ps);
            part.localPosition = qK * restPos + tK; part.localRotation = qK * restRot;
        }
    }

    /// <summary>The visible leg suspension's own meshes (the legs' feet and struts, cut per ship): destroyed with the ship.</summary>
    public sealed class OzShuttleSuspensionMeshes : MonoBehaviour
    {
        public readonly List<Object> Own = new List<Object>();
        void OnDestroy() { foreach (var o in Own) if (o) Destroy(o); Own.Clear(); }
    }
}
