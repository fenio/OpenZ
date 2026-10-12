using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    ///
    /// their two contact lines every ~.6 m (tank/MLRS .5 m wide, cars .32 m), the mech prints a foot on every stride
    /// (stride phase = travel x 3.2 / PI, MechAnkle_L/R) with a dust puff, prints only on the ground (|support - ground| < .1)
    /// them to linger, so here: solid for ~30 % of the life, then they dissolve grain by grain (wind blowing sand back
    /// in), older end first, gone after 25-40 s.
    ///
    /// and appends a cross-section every 0.4-0.5 m of travel or 8 degrees of turn, so tracks curve smoothly with the path,
    /// tanks pivoting in place leave twisted scuffs, jeeps weave. Jeeps: two tyre prints with chevron lugs; light / heavy
    /// tanks, MLRS, APC, crane: wide tread prints (grouser bars, a clean centre gap, the two bands far apart); the medium
    /// raised foot coming back down to the other foot's height, in the unit's own frame, so slopes, bobbing and body
    /// wobble cancel out; distance cadence while the animator is culled off screen).
    ///
    /// Surfaces (TrackSurface): Zod tile classes (roads, water, rock; bilinear so edges fade over a tile) + the level's own
    /// ground paint read back once from the GPU at 0.5 m (saturated = sand / dirt / flower fields = soft; grey = concrete,
    /// asphalt = hard), so the L17 concrete plates stay clean while its rust-brown dirt patches take prints. Full prints
    /// on sand / dirt, faint (0.2) on roads and concrete, none on water (the fx wading ripples own that), none on bridge
    /// decks (DustTrails puts faint dust there).
    ///
    /// Rendering: ONE dynamic mesh (ring buffer of quads, 4 vertices each, only the quads written since the last upload are
    /// sent), ONE material (OpenZ/TrackMark, SRP Batcher compatible), ONE draw call. The fade / dissolve runs in the shader
    /// from the per-vertex birth time and the global _OZTrackTime, so nothing is touched per frame on the CPU; expired quads
    /// collapse in the vertex shader. When traffic outruns the ring, new prints get a shorter life (capacity / write rate), so
    /// a quad has always dissolved before it is recycled. Every vertex sits on the RENDERED ground (TER_* meshes rasterised
    /// once to a 0.5 m height grid; one raycast per vertex against the walk layer only to tell bridge decks apart, only when a
    /// section is added); the shader tests against the camera depth texture with a 10 cm tolerance (18 cm when only the
    /// decimated COL_Terrain proxy is available) instead of the depth buffer, so the prints can never z-fight, never hide
    /// under a bump of the drawn ground and never paint a unit or rock standing in front of them.
    /// </summary>
    [DefaultExecutionOrder(80)]   // after UnitView (60) placed the units and OpenZWading (70) lowered the waders
    public class TrackMarks : MonoBehaviour
    {
        public static TrackMarks I { get; private set; }
        public static bool Active => I && I.isActiveAndEnabled && I.ready;

        /// <summary>Rim padding on each side of a print, as a fraction of its width (shader _BermPad).</summary>
        public const float BermPad = 0.18f;
        const float AlongWrap = 357f;                   // 0.21 m x 1700 = 0.17 m x 2100: tyre and tread pitches repeat seamlessly

        public enum Kind { Tyre = 0, Tread = 1, RobotFoot = 2, MechFoot = 3 }

        [Header("Pool")]
        [Tooltip("Quads in the ring buffer (4 vertices each). The oldest are overwritten first; under heavy traffic the life of new " +
                 "prints is shortened so that a quad has always dissolved before the ring comes back to it (never a pop).")]
        public int maxQuads = 65536;

        [Header("Life in game seconds: solid until fadeStart x life, then dissolving")]
        public float vehicleLife = 36f, vehicleLifeJitter = 4f, robotLife = 16f, robotLifeJitter = 3f, mechLife = 38f;
        [Range(0f, 1f)] public float fadeStart = 0.3f;

        [Header("Print strength per surface (1 = full)")]
        [Range(0f, 1f)] public float sandStrength = 1f, hardStrength = 0.2f, roadStrength = 0.2f, rockStrength = 0.3f;
        [Tooltip("Dust strength on bridge decks (no prints there)")] [Range(0f, 1f)] public float deckDust = 0.3f;

        [Header("Geometry")]
        [Tooltip("Lift above the walkable surface (m); the shader's depth tolerance does the rest")] public float lift = 0.02f;
        public float tyreSegment = 0.4f, treadSegment = 0.5f, turnSplitDeg = 8f;
        [Tooltip("A section is cut early (>= 0.2 m) when the ground between two sections bends more than this (m): crater rims, " +
                 "bank slopes and dune crests stay under the print instead of poking through a flat quad")] public float bendSplit = 0.025f;
        [Tooltip("Max width (m) of one quad across a ribbon / footprint; wider prints are subdivided so they hug the ground")]
        public float maxQuadWidth = 0.42f;
        [Tooltip("Metres of the terrain extent outside the Zod map (level_terrain_data.py APRON)")] public float paintApron = 24f;
        public float robotFootScale = 1f, mechFootScale = 1f;

        [Header("Look (sent to the material)")]
        [Range(0f, 2f)] public float intensity = 0.65f;
        [Range(0f, 0.6f)] public float compress = 0.24f;
        [Range(0f, 0.4f)] public float groove = 0.13f;
        [Range(0f, 0.3f)] public float berm = 0.07f;
        public float dissolveScale = 2.6f;
        [Tooltip("How far (m, eye depth) a print may lie behind the rendered ground and still show, when the heights come from " +
                 "the rendered terrain mesh (TER_*)")] public float depthTolerance = 0.1f;
        [Tooltip("The same when only the decimated collider COL_Terrain is available (it is up to ~9 cm off the rendered ground)")]
        public float depthToleranceProxy = 0.18f;

        public TrackSurface Surface { get; private set; }
        public int QuadsWritten => cursor;
        public int Capacity => capacity;
        public int Runners => runners.Count;
        /// <summary>Game clock used for the fade (stops while ZGame.paused, runs at ZGame.gameSpeed).</summary>
        public float Clock => clock;
        /// <summary>Quads written per game second (smoothed) and the life cap it forces (s).</summary>
        public float WriteRate => writeRate;
        public float LifeCap => lifeCap;

        // ------------------------------------------------------------------------------------------------ vertex ring
        [StructLayout(LayoutKind.Sequential)]
        struct MarkVertex { public Vector3 pos; public Color32 col; public Vector2 uv; public Vector4 data; }   // data = birth, life, death, kind + seed

        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds |
                                      MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;
        static readonly int TimeId = Shader.PropertyToID("_OZTrackTime");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity"), CompressId = Shader.PropertyToID("_Compress"),
            GrooveId = Shader.PropertyToID("_Groove"), BermId = Shader.PropertyToID("_Berm"), BermPadId = Shader.PropertyToID("_BermPad"),
            FadeStartId = Shader.PropertyToID("_FadeStart"), DissolveId = Shader.PropertyToID("_DissolveScale"),
            ToleranceId = Shader.PropertyToID("_DepthTolerance"), SceneDepthId = Shader.PropertyToID("_SceneDepthTest"), ZTestId = Shader.PropertyToID("_ZTest");

        MarkVertex[] verts; Mesh mesh; Material mat; int capacity, cursor, uploaded;
        float clock = 1f;                       // > 0 so never-written (all-zero) quads count as expired in the shader
        float writeRate, lifeCap = float.MaxValue;
        bool ready;
        ZGame game;

        // ------------------------------------------------------------------------------------------------ per unit state
        enum Gait { None, Robot, Mech, Wheels, Treads }

        /// <summary>One ribbon: the last cross-section (e = up to MaxAcross + 1 points across, left to right, on the ground).</summary>
        struct Strip
        {
            public bool live, deck; public Vector3 c, lat; public Vector3[] e; public float cy, along, birth, life, seed; public byte str;
        }
        const int MaxAcross = 4;                                                                           // quads across a ribbon
        const int MaxFootLen = 4, MaxFootWide = 3;                                                          // quads per footprint
        readonly Vector3[] sec = new Vector3[MaxAcross + 1];                                                // scratch: the new cross-section
        readonly Vector3[] grid = new Vector3[(MaxFootLen + 1) * (MaxFootWide + 1)];                        // scratch: footprint lattice

        sealed class Runner
        {
            public UnitView v; public Gait gait; public int lastSeen;
            public Vector3 lastPos; public bool hasLast; public float speed, maxSpeed;
            // vehicles: contact lines at offX +- gauge along the hull's lateral axis, rear contact halfLen behind the centre
            public float gauge, width, offX, halfLen, segLen, dustDist; public int dustSide = 1, across = 1;
            public Strip left, right;
            // feet
            public Transform footL, footR; public Renderer skin;
            public float footLen, footWid, halfSpacing, stride, sinceStep, amp, peak, minHyst; public int up, cadenceSide = 1;
        }

        readonly Dictionary<int, Runner> runners = new Dictionary<int, Runner>();
        readonly List<int> stale = new List<int>();
        float cleanT;

        // ================================================================================================ install
        /// <summary>Every game scene (a scene with a ZGame) gets one TrackMarks + DustTrails (PlayerPrefs "oz_tracks" = 0 turns
        /// both off, "oz_dusttrails" = 0 only the dust).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            Ensure();
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();

        public static TrackMarks Ensure()
        {
            if (PlayerPrefs.GetInt("oz_tracks", 1) == 0) return null;
            if (I) return I;
            var g = ZGame.I; if (!g) return null;
            var found = FindFirstObjectByType<TrackMarks>(); if (found) return found;
            var go = new GameObject("OpenZTrackMarks");
            var tm = go.AddComponent<TrackMarks>();
            if (PlayerPrefs.GetInt("oz_dusttrails", 1) != 0 && !FindFirstObjectByType<DustTrails>()) go.AddComponent<DustTrails>();
            return tm;
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            if (mesh) Destroy(mesh);
            if (mat) Destroy(mat);
        }

        void Start()
        {
            game = ZGame.I;
            if (!game || game.World == null || game.World.Map == null) { enabled = false; return; }
            var src = Resources.Load<Material>("OpenZ/FX/M_TrackMark");                 // keeps the shader in builds
            if (src && src.shader && src.shader.name == "OpenZ/TrackMark") mat = new Material(src);
            else { var sh = Shader.Find("OpenZ/TrackMark"); if (sh) mat = new Material(sh); }
            if (!mat) { Debug.LogError("TrackMarks: shader OpenZ/TrackMark missing (copy OpenZTrackMark.shader)"); enabled = false; return; }
            mat.name = "TrackMarks";
            Surface = TrackSurface.Build(game, paintApron);
            ApplySettings();
            BuildMesh();
            ready = true;
        }

        void OnValidate() { if (ready) ApplySettings(); }

        void ApplySettings()
        {
            if (Surface != null)
            {
                Surface.sand = sandStrength; Surface.hard = hardStrength; Surface.road = roadStrength; Surface.rock = rockStrength;
            }
            if (!mat) return;
            mat.SetFloat(IntensityId, intensity); mat.SetFloat(CompressId, compress); mat.SetFloat(GrooveId, groove);
            mat.SetFloat(BermId, berm); mat.SetFloat(BermPadId, BermPad); mat.SetFloat(FadeStartId, fadeStart);
            mat.SetFloat(DissolveId, dissolveScale);
            mat.SetFloat(ToleranceId, Surface != null && Surface.RenderHeights ? depthTolerance : Mathf.Max(depthTolerance, depthToleranceProxy));
            // the camera depth texture is on in PC_RPAsset (m_RequireDepthTexture: 1); without it fall back to the depth buffer
            bool sceneDepth = true;
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) sceneDepth = urp.supportsCameraDepthTexture;
            mat.SetFloat(SceneDepthId, sceneDepth ? 1f : 0f);
            mat.SetFloat(ZTestId, (float)(sceneDepth ? CompareFunction.Always : CompareFunction.LessEqual));
        }

        void BuildMesh()
        {
            capacity = Mathf.Clamp(maxQuads, 1024, 131072);
            verts = new MarkVertex[capacity * 4];
            mesh = new Mesh { name = "OpenZTrackMarks" };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(capacity * 4,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4, 0));
            var idx = new int[capacity * 6];
            for (int i = 0; i < capacity; i++)
            {
                int b = i * 4, k = i * 6;
                idx[k] = b; idx[k + 1] = b + 1; idx[k + 2] = b + 2; idx[k + 3] = b; idx[k + 4] = b + 2; idx[k + 5] = b + 3;
            }
            mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(idx, 0, 0, idx.Length, Quiet);
            mesh.SetVertexBufferData(verts, 0, 0, verts.Length, 0, Quiet);
            var bounds = Surface.Bounds;
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length, MeshTopology.Triangles) { bounds = bounds, firstVertex = 0, vertexCount = verts.Length }, Quiet);
            mesh.bounds = bounds;
            cursor = uploaded = 0;
        }

        /// <summary>Wipes every print (level restart, capture setups).</summary>
        public void Clear()
        {
            if (verts == null) return;
            System.Array.Clear(verts, 0, verts.Length);
            mesh.SetVertexBufferData(verts, 0, 0, verts.Length, 0, Quiet);
            cursor = uploaded = 0; writeRate = 0f; lifeCap = float.MaxValue;
            foreach (var kv in runners) Break(kv.Value);
        }

        // ================================================================================================ per frame
        void LateUpdate()
        {
            if (!ready) return;
            if (!game) { game = ZGame.I; if (!game) return; }
            if (game.World == null) return;
            float dt = game.paused ? 0f : Time.deltaTime * game.gameSpeed;
            clock += dt;
            if (dt > 0f)
            {
                int before = cursor, frame = Time.frameCount;
                foreach (var kv in game.Views)
                {
                    var v = kv.Value;
                    if (!v || v.Obj == null || v.Obj.Kind == ZKind.Cannon) continue;
                    if (!runners.TryGetValue(kv.Key, out var r) || r.v != v) { r = Bind(v); runners[kv.Key] = r; }
                    r.lastSeen = frame;
                    Step(r, dt);
                }
                // ring pressure: a quad must have dissolved before the ring comes back to it, so under heavy traffic new prints
                // live shorter (capacity / write rate, 10 % margin) instead of popping out while still visible
                writeRate = Mathf.Lerp(writeRate, (cursor - before) / dt, 1f - Mathf.Exp(-dt / 2f));
                lifeCap = writeRate > 1f ? Mathf.Max(4f, 0.9f * capacity / writeRate) : float.MaxValue;
                if ((cleanT += dt) > 3f) Clean();                                                           // game time: a pause never drops runners
            }
            Upload();
            Shader.SetGlobalFloat(TimeId, clock);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        /// <summary>Sends every quad written since the last upload (also those Stamp()ed by other scripts earlier this frame).</summary>
        void Upload()
        {
            int n = cursor - uploaded;
            if (n <= 0) return;
            int first = uploaded; uploaded = cursor;
            if (n >= capacity) { mesh.SetVertexBufferData(verts, 0, 0, verts.Length, 0, Quiet); return; }
            int s = first % capacity;
            if (s + n <= capacity) mesh.SetVertexBufferData(verts, s * 4, s * 4, n * 4, 0, Quiet);
            else
            {
                int n1 = capacity - s;
                mesh.SetVertexBufferData(verts, s * 4, s * 4, n1 * 4, 0, Quiet);
                mesh.SetVertexBufferData(verts, 0, 0, (n - n1) * 4, 0, Quiet);
            }
        }

        void Clean()
        {
            cleanT = 0f; stale.Clear();
            int frame = Time.frameCount;
            foreach (var kv in runners) if (!kv.Value.v || frame - kv.Value.lastSeen > 120) stale.Add(kv.Key);
            foreach (var k in stale) runners.Remove(k);
        }

        void Step(Runner r, float dt)
        {
            var v = r.v;
            if (!v.isActiveAndEnabled || v.Dying || v.Obj.Destroyed || r.gait == Gait.None) { Break(r); return; }
            var t = v.transform; var pos = t.position;
            var mv = pos - r.lastPos; mv.y = 0f; float md = mv.magnitude;
            float jump = 4f + 1.5f * Mathf.Max(r.maxSpeed, 3f) * dt;                                    // a frame hitch at 4x is no teleport
            if (!r.hasLast || md > jump) { Break(r); r.lastPos = pos; r.hasLast = true; return; }  // first sight / teleport (exit, spawn)
            r.lastPos = pos;
            r.speed = Mathf.Lerp(r.speed, md / dt, 1f - Mathf.Exp(-6f * dt));
            var lat = t.right; lat.y = 0f;
            if (lat.sqrMagnitude < 1e-4f) return;
            lat.Normalize();
            var front = new Vector3(lat.z, 0f, -lat.x);                                                  // = -transform.forward flattened: models face -z (UnitView.cs:61-63)
            switch (r.gait)
            {
                case Gait.Wheels: case Gait.Treads: StepVehicle(r, pos, lat, mv, md); break;
                default: StepFeet(r, t, pos, lat, front, md, dt); break;
            }
        }

        static void Break(Runner r) { r.left.live = r.right.live = false; r.hasLast = false; r.up = 0; r.peak = 0f; r.sinceStep = 0f; }

        // ================================================================================================ vehicles
        void StepVehicle(Runner r, Vector3 pos, Vector3 lat, Vector3 mv, float md)
        {
            bool wheels = r.gait == Gait.Wheels;
            var kind = wheels ? Kind.Tyre : Kind.Tread;
            if (md < 0.002f)
            {   // standing still: the ground under the last cross-section is still pressed by the vehicle, so it stays fresh (the
                // first quad after a long halt does not start half dissolved); a creeping vehicle ages normally
                if (r.left.live) r.left.birth = clock;
                if (r.right.live) r.right.birth = clock;
            }
            var mid = pos + lat * r.offX;
            StepStrip(ref r.left, mid - lat * r.gauge, lat, r, kind);
            StepStrip(ref r.right, mid + lat * r.gauge, lat, r, kind);

            // dust kicked up behind the vehicle on soft ground: more puffs, bigger and denser with speed (DustTrails)
            var dust = DustTrails.I;
            if (!dust || !dust.isActiveAndEnabled || md < 1e-4f) return;
            float s01 = Mathf.Clamp01(r.speed / Mathf.Max(0.5f, r.maxSpeed));
            r.dustDist += md;
            float every = Mathf.Lerp(wheels ? 1.1f : 1.4f, wheels ? 0.5f : 0.65f, s01);
            if (r.dustDist < every || s01 < 0.08f) return;
            r.dustDist = 0f; r.dustSide = -r.dustSide;
            var dir = mv / md;
            var at = mid + lat * (r.dustSide * r.gauge) - dir * r.halfLen;                                 // behind the rear contact
            bool deck; float y;
            Surface.Sample(at.x, at.z, out y, out deck);
            float ds = deck ? deckDust : Surface.DustStrength(at.x, at.z);
            if (ds < 0.02f) return;
            at.y = y;
            dust.VehicleWake(at, dir, lat * r.dustSide, s01, ds, Surface.DustColor(at.x, at.z), wheels);
        }

        void StepStrip(ref Strip s, Vector3 c, Vector3 lat, Runner r, Kind kind)
        {
            c.y = 0f;
            if (!s.live) { Section(ref s, c, lat, r, kind, true); return; }
            var d = c - s.c; d.y = 0f;
            float dist = d.magnitude;
            if (dist > 6f) { Section(ref s, c, lat, r, kind, true); return; }                           // teleports are cut in Step
            bool turned = dist > 0.04f && Vector3.Angle(s.lat, lat) > turnSplitDeg;
            if (dist < r.segLen && !turned)
            {   // bend check on the drawn ground (two grid lookups, no raycast): cut early where the chord leaves the surface
                if (dist < 0.2f || !Surface.RenderY(c.x, c.z, out float y1) || !Surface.RenderY((c.x + s.c.x) * 0.5f, (c.z + s.c.z) * 0.5f, out float ym)
                    || Mathf.Abs(ym - 0.5f * (s.cy + y1)) < bendSplit) return;
            }
            Section(ref s, c, lat, r, kind, false, dist);
        }

        /// <summary>Adds a cross-section to a strip (and the quads from the previous one). fresh = start a new strip here.
        /// A section is r.across + 1 points on the drawn ground (the two edges raycast for the bridge-deck test, the inner ones
        /// straight from the height grid), so a wide tread band bends over a crater rim instead of cutting through it.</summary>
        void Section(ref Strip s, Vector3 c, Vector3 lat, Runner r, Kind kind, bool fresh, float dist = 0f)
        {
            int K = r.across;
            float hw = r.width * 0.5f * (1f + 2f * BermPad);
            var e0 = c - lat * hw; var e1 = c + lat * hw;
            bool deck = Height(ref e0) | Height(ref e1);
            sec[0] = e0; sec[K] = e1;
            for (int m = 1; m < K; m++) { var p = Vector3.Lerp(e0, e1, (float)m / K); if (deck) Height(ref p); else OnGround(ref p); sec[m] = p; }
            float cy = Surface.RenderY(c.x, c.z, out float ry) ? ry : 0.5f * (e0.y + e1.y) - lift;
            float str = deck ? 0f : Surface.MarkStrength(c.x, c.z);
            byte sb = (byte)Mathf.RoundToInt(Mathf.Clamp01(str) * 255f);
            if (fresh)
            {
                s.live = true; s.c = c; s.cy = cy; s.lat = lat; s.birth = clock; s.str = sb; s.deck = deck;
                System.Array.Copy(sec, s.e, K + 1);
                s.life = Mathf.Max(4f, vehicleLife + Random.Range(-vehicleLifeJitter, vehicleLifeJitter)); s.seed = Random.value;
                return;
            }
            float along = s.along + dist;
            if (along > AlongWrap) { s.along -= AlongWrap; along -= AlongWrap; }                       // keeps the tread phase exact
            if (s.str > 2 || sb > 2)
            {
                float ks = (float)kind + s.seed * 0.9f;
                for (int m = 0; m < K; m++)
                {
                    float u0 = (float)m / K, u1 = (float)(m + 1) / K;
                    Quad(s.e[m], s.e[m + 1], sec[m + 1], sec[m], new Vector2(u0, s.along), new Vector2(u1, s.along), new Vector2(u1, along), new Vector2(u0, along),
                         s.birth, clock, s.str, sb, s.life, ks);
                }
            }
            System.Array.Copy(sec, s.e, K + 1);
            s.c = c; s.cy = cy; s.lat = lat; s.along = along; s.birth = clock; s.str = sb; s.deck = deck;
        }

        /// <summary>Puts p on the walkable surface + lift (one raycast: bridge-deck test). Returns true on a bridge deck.</summary>
        bool Height(ref Vector3 p)
        {
            Surface.Sample(p.x, p.z, out float y, out bool deck);
            p.y = y + lift;
            return deck;
        }

        /// <summary>Puts p on the drawn terrain + lift from the height grid (no raycast); a raycast where the grid has no answer.</summary>
        void OnGround(ref Vector3 p)
        {
            if (Surface.RenderY(p.x, p.z, out float y)) p.y = y + lift; else Height(ref p);
        }

        // ================================================================================================ feet
        void StepFeet(Runner r, Transform t, Vector3 pos, Vector3 lat, Vector3 front, float md, float dt)
        {
            r.sinceStep += md;
            if (r.gait == Gait.Mech && MechWalkDrives(r.v)) return;
            bool moving = r.v.Obj.IsMoving || md > 0.4f * dt;
            bool mech = r.gait == Gait.Mech;
            bool bones = r.footL && r.footR && (mech || !r.skin || r.skin.isVisible);   // culled animators freeze their bones
            if (bones)
            {
                // footfall = the raised foot comes back down to the other foot's height (own frame: slope, bob and wobble cancel)
                float yl = t.InverseTransformPoint(r.footL.position).y, yr = t.InverseTransformPoint(r.footR.position).y;
                float diff = (yl - yr) * Mathf.Abs(t.lossyScale.y);
                r.peak = Mathf.Max(r.peak, Mathf.Abs(diff));
                float h = Mathf.Max(r.minHyst, r.amp * 0.3f);
                if (r.up == 1 && diff < h * 0.3f) { r.up = 0; Footfall(r, t, true, r.footL.position, front, lat, moving); }
                else if (r.up == -1 && diff > -h * 0.3f) { r.up = 0; Footfall(r, t, false, r.footR.position, front, lat, moving); }
                if (diff > h) r.up = 1; else if (diff < -h) r.up = -1;
                return;
            }
            if (!moving || r.sinceStep < r.stride) return;
            r.cadenceSide = -r.cadenceSide;                                                                  // off screen / no bones: step cadence
            Footfall(r, t, r.cadenceSide < 0, pos + lat * (r.cadenceSide * r.halfSpacing), front, lat, true);
        }

        static bool MechWalkDrives(UnitView v) { var w = MechFoley.Walk(v); return w && w.Drives; }

        /// <summary>
        /// surface-aware dust puff (the deck's dust on a bridge, none on roads / water), also for steps in place.</summary>
        public void MechPlant(UnitView v, bool leftFoot, Vector3 at)
        {
            if (!ready || !v || v.Obj == null || Surface == null) return;
            if (!runners.TryGetValue(v.Obj.Id, out var r) || r.v != v || r.gait != Gait.Mech) return;
            var lat = v.transform.right; lat.y = 0f; if (lat.sqrMagnitude < 1e-4f) return; lat.Normalize();
            var front = new Vector3(lat.z, 0f, -lat.x);                                                      // models face -z (as Step)
            r.sinceStep = 0f;
            var c = new Vector3(at.x, 0f, at.z) + front * (r.footLen * 0.1f);                                // ankle over the heel
            Surface.Sample(c.x, c.z, out float gy, out bool deck);
            var dust = DustTrails.I;
            if (dust && dust.isActiveAndEnabled)
            {
                float ds = deck ? deckDust : Surface.DustStrength(c.x, c.z);
                if (ds > 0.02f) dust.Footfall(new Vector3(c.x, gy, c.z), ds, Surface.DustColor(c.x, c.z), true);
            }
            if (deck) return;
            float str = Surface.MarkStrength(c.x, c.z);
            if (str < 0.01f) return;
            Stamp(c, front, r.footLen, r.footWid, Kind.MechFoot, str, mechLife, leftFoot);
        }

        /// <summary>
        /// bridge, none on roads / concrete / water).</summary>
        public void MechDust(Vector3 at, float mul)
        {
            var dust = DustTrails.I;
            if (!ready || Surface == null || !dust || !dust.isActiveAndEnabled) return;
            Surface.Sample(at.x, at.z, out float gy, out bool deck);
            float ds = Mathf.Clamp01((deck ? deckDust : Surface.DustStrength(at.x, at.z)) * mul);
            if (ds > 0.02f) dust.Footfall(new Vector3(at.x, gy, at.z), ds, Surface.DustColor(at.x, at.z), true);
        }

        void Footfall(Runner r, Transform t, bool leftFoot, Vector3 foot, Vector3 front, Vector3 lat, bool moving)
        {
            r.amp = Mathf.Lerp(r.amp, Mathf.Max(r.peak, r.minHyst), 0.5f); r.peak = 0f;
            if (!moving || r.sinceStep < 0.12f) return;                                                      // turning on the spot, idle shuffles
            r.stride = Mathf.Clamp(Mathf.Lerp(r.stride, r.sinceStep, 0.3f), 0.3f, 1.8f);
            r.sinceStep = 0f;
            bool mech = r.gait == Gait.Mech;
            var c = new Vector3(foot.x, 0f, foot.z) + front * (r.footLen * (mech ? 0.1f : 0.25f));         // ankle sits over the heel
            Surface.Sample(c.x, c.z, out float gy, out bool deck);
            var dust = DustTrails.I;
            if (dust && dust.isActiveAndEnabled)
            {
                float ds = deck ? deckDust : Surface.DustStrength(c.x, c.z);
                if (ds > 0.02f) dust.Footfall(new Vector3(c.x, gy, c.z), ds, Surface.DustColor(c.x, c.z), mech);
            }
            if (deck) return;
            float str = Surface.MarkStrength(c.x, c.z);
            if (str < 0.01f) return;
            float life = mech ? mechLife : Mathf.Max(4f, robotLife + Random.Range(-robotLifeJitter, robotLifeJitter));
            Stamp(c, front, r.footLen, r.footWid, mech ? Kind.MechFoot : Kind.RobotFoot, str, life, leftFoot);
        }

        /// <summary>One footprint (heel at -front, toe at +front): a small lattice of quads on the drawn ground (1-2 quads for a
        /// robot boot, up to 4 x 3 for the mech's foot), so a big print lies over a bump instead of cutting into it.</summary>
        public void Stamp(Vector3 centre, Vector3 front, float length, float width, Kind kind, float strength, float life, bool mirror)
        {
            if (!ready) return;
            front.y = 0f; if (front.sqrMagnitude < 1e-6f) return; front.Normalize();
            var side = new Vector3(front.z, 0f, -front.x);
            float hl = length * 0.5f * (1f + 2f * BermPad), hw = width * 0.5f * (1f + 2f * BermPad);
            var a = centre - front * hl - side * hw; var b = centre - front * hl + side * hw;
            var c = centre + front * hl + side * hw; var d = centre + front * hl - side * hw;
            if (Height(ref a) | Height(ref b) | Height(ref c) | Height(ref d)) return;                       // never on a deck
            int nL = Mathf.Clamp(Mathf.CeilToInt(2f * hl / Mathf.Max(0.1f, maxQuadWidth)), 1, MaxFootLen);
            int nW = Mathf.Clamp(Mathf.CeilToInt(2f * hw / Mathf.Max(0.1f, maxQuadWidth)), 1, MaxFootWide);
            int row = nW + 1;
            for (int l = 0; l <= nL; l++)
                for (int w = 0; w <= nW; w++)
                {
                    int g = l * row + w;
                    if (l == 0 && w == 0) grid[g] = a; else if (l == 0 && w == nW) grid[g] = b;
                    else if (l == nL && w == nW) grid[g] = c; else if (l == nL && w == 0) grid[g] = d;
                    else { var p = centre + front * (hl * (2f * l / nL - 1f)) + side * (hw * (2f * w / nW - 1f)); OnGround(ref p); grid[g] = p; }
                }
            byte sb = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f);
            float ks = (float)kind + Random.value * 0.9f;                                                    // one dissolve seed per print
            for (int l = 0; l < nL; l++)
            {
                float v0 = (float)l / nL, v1 = (float)(l + 1) / nL;
                for (int w = 0; w < nW; w++)
                {
                    float t0 = (float)w / nW, t1 = (float)(w + 1) / nW;
                    float u0 = mirror ? 1f - t0 : t0, u1 = mirror ? 1f - t1 : t1;
                    int g = l * row + w;
                    Quad(grid[g], grid[g + 1], grid[g + row + 1], grid[g + row], new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1),
                         clock, clock, sb, sb, life, ks);
                }
            }
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
                  float birthAB, float birthCD, byte strAB, byte strCD, float life, float kindSeed)
        {
            int k = (cursor % capacity) * 4;
            cursor++;
            life = Mathf.Min(life, lifeCap);                                                                 // ring pressure (LateUpdate)
            float death = Mathf.Max(birthAB, birthCD) + life;
            var cab = new Color32(255, 255, 255, strAB); var ccd = new Color32(255, 255, 255, strCD);
            verts[k] = new MarkVertex { pos = a, col = cab, uv = ua, data = new Vector4(birthAB, life, death, kindSeed) };
            verts[k + 1] = new MarkVertex { pos = b, col = cab, uv = ub, data = new Vector4(birthAB, life, death, kindSeed) };
            verts[k + 2] = new MarkVertex { pos = c, col = ccd, uv = uc, data = new Vector4(birthCD, life, death, kindSeed) };
            verts[k + 3] = new MarkVertex { pos = d, col = ccd, uv = ud, data = new Vector4(birthCD, life, death, kindSeed) };
        }

        // ================================================================================================ binding
        Runner Bind(UnitView v)
        {
            var r = new Runner { v = v };
            var o = v.Obj;
            if (o.Kind == ZKind.Robot) BindRobot(r);
            else if (o.Kind == ZKind.Vehicle)
            {
                if (!BindMech(r)) BindVehicle(r, o.Type == "jeep");
                r.maxSpeed = o.MoveSpeed > 0 ? o.MoveSpeed * (float)ZConst.ROAD_SPEED / 8f : 6f;             // px/s -> m/s on a road (UnitView.cs:342)
            }
            return r;
        }

        void BindRobot(Runner r)
        {
            r.gait = Gait.Robot;
            Transform toeL = null, toeR = null;
            var an = r.v.GetComponentInChildren<Animator>();
            if (an && an.isHuman)
            {
                r.footL = an.GetBoneTransform(HumanBodyBones.LeftFoot); r.footR = an.GetBoneTransform(HumanBodyBones.RightFoot);
                toeL = an.GetBoneTransform(HumanBodyBones.LeftToes); toeR = an.GetBoneTransform(HumanBodyBones.RightToes);
            }
            if (!r.footL || !r.footR)
                foreach (var t in r.v.GetComponentsInChildren<Transform>(true))
                {   // Z_robot / Z_tough: mixamorigLeftFoot, mixamorigLeftToeBase (OpenZWading.cs:73-75 binds the same way)
                    string n = t.name;
                    if (!r.footL && n.EndsWith("LeftFoot")) r.footL = t;
                    else if (!r.footR && n.EndsWith("RightFoot")) r.footR = t;
                    else if (!toeL && n.EndsWith("LeftToeBase")) toeL = t;
                    else if (!toeR && n.EndsWith("RightToeBase")) toeR = t;
                }
            r.skin = r.v.GetComponentInChildren<SkinnedMeshRenderer>();
            float len = 0.4f, spacing = 0.36f;
            if (r.footL && toeL) len = Mathf.Clamp(2f * Vector3.Distance(r.footL.position, toeL.position), 0.26f, 0.7f);
            if (r.footL && r.footR)
            {
                var R = RootFrame(r.v.transform);
                spacing = Mathf.Abs(R.MultiplyPoint3x4(r.footL.position).x - R.MultiplyPoint3x4(r.footR.position).x);
            }
            r.footLen = len * robotFootScale; r.footWid = r.footLen * 0.52f;
            r.halfSpacing = Mathf.Clamp(spacing * 0.5f, 0.08f, 0.45f);
            r.stride = 0.75f; r.amp = 0.08f; r.minHyst = 0.015f;
        }

        bool BindMech(Runner r)
        {
            Transform l = null, rr = null;
            foreach (var t in r.v.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "MechAnkle_L") l = t; else if (t.name == "MechAnkle_R") rr = t;
            }
            if (!l || !rr) return false;
            r.gait = Gait.Mech; r.footL = l; r.footR = rr;
            var R = RootFrame(r.v.transform);
            float spacing = Mathf.Abs(R.MultiplyPoint3x4(l.position).x - R.MultiplyPoint3x4(rr.position).x);
            if (spacing < 0.3f) spacing = 1.83f;                                                            // mech.glb hips: -0.96 / +0.87
            r.footLen = Mathf.Clamp(0.62f * spacing, 0.7f, 1.6f) * mechFootScale;
            r.footWid = Mathf.Clamp(0.42f * spacing, 0.45f, 1.2f) * mechFootScale;
            r.halfSpacing = spacing * 0.5f;
            r.stride = Mathf.PI / 3.2f;                                                                     // MechWalk: phase = travel x 1.6 x 2 -> a step every 0.98 m
            r.amp = 0.2f; r.minHyst = 0.03f;
            return true;
        }

        static Matrix4x4 RootFrame(Transform t) => Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;   // metres, x = lateral

        static bool IsTrackPart(string n) => n.Contains("track") || n.Contains("Track") || n.Contains("Tread") || n.Contains("tread");   // VehicleMotion.cs:44

        void BindVehicle(Runner r, bool wheeled)
        {
            r.gait = wheeled ? Gait.Wheels : Gait.Treads;
            r.segLen = wheeled ? tyreSegment : treadSegment;
            var t = r.v.transform; var R = RootFrame(t);
            // measure the model only (every Z_ vehicle prefab keeps its meshes under "Model"), never overlays parented later
            var model = t.Find("Model"); if (!model) model = t;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            float tcSum = 0f, tsSum = 0f, twSum = 0f, tlMax = 0f; int tn = 0;                               // track / tread meshes
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                Extents(m.bounds, R * mf.transform.localToWorldMatrix, out var lo, out var hi);
                minX = Mathf.Min(minX, lo.x); maxX = Mathf.Max(maxX, hi.x); minZ = Mathf.Min(minZ, lo.z); maxZ = Mathf.Max(maxZ, hi.z);
                var p = mf.transform.parent;
                if (!wheeled && (IsTrackPart(mf.name) || (p && IsTrackPart(p.name))))
                {
                    float cx = (lo.x + hi.x) * 0.5f;
                    tcSum += Mathf.Abs(cx); tsSum += cx; twSum += hi.x - lo.x; tn++;
                    tlMax = Mathf.Max(tlMax, Mathf.Max(Mathf.Abs(lo.z), Mathf.Abs(hi.z)));                  // rear end of the track
                }
            }
            float wcSum = 0f, wsSum = 0f, wwSum = 0f, wlMax = 0f; int wn = 0;                              // wheels (VehicleMotion.cs:35)
            foreach (var w in model.GetComponentsInChildren<Transform>(true))
            {
                if (!(w.name.StartsWith("WheelSpin_") || w.name.StartsWith("TankWheel_"))) continue;
                var wp = R.MultiplyPoint3x4(w.position);
                wcSum += Mathf.Abs(wp.x); wsSum += wp.x; wn++;
                float rad = 0.4f;
                var wm = w.GetComponentInChildren<MeshFilter>();
                if (wm && wm.sharedMesh)
                {
                    Extents(wm.sharedMesh.bounds, R * wm.transform.localToWorldMatrix, out var lo, out var hi);
                    wwSum += hi.x - lo.x; rad = Mathf.Max(hi.y - lo.y, hi.z - lo.z) * 0.5f;
                }
                wlMax = Mathf.Max(wlMax, Mathf.Abs(wp.z) + rad);
            }
            bool any = minX < maxX;
            float halfW = any ? Mathf.Clamp(Mathf.Max(-minX, maxX), 0.8f, 2.2f) : 1.3f;
            float gauge, width, off, rear;
            if (!wheeled && tn > 0) { gauge = tcSum / tn; width = twSum / tn; off = tsSum / tn; rear = tlMax; }
            else if (wn > 0) { gauge = wcSum / wn; width = wwSum > 0f ? wwSum / wn : halfW * (wheeled ? 0.22f : 0.4f); off = wsSum / wn; rear = wlMax; }
            else
            {   // imported APC / crane: one mesh, no rig -> the model bounds
                gauge = halfW * 0.72f; width = halfW * (wheeled ? 0.22f : 0.42f);
                off = any ? (minX + maxX) * 0.5f : 0f; rear = any ? Mathf.Max(-minZ, maxZ) * 0.85f : 2.2f;
            }
            if (wheeled) { width = Mathf.Clamp(width, 0.2f, 0.6f); gauge = Mathf.Clamp(gauge, 0.5f, 1.6f); }
            else { width = Mathf.Clamp(width, 0.32f, 0.9f); gauge = Mathf.Clamp(gauge, 0.65f, 1.7f); }
            r.gauge = Mathf.Max(gauge, width * 0.62f);                                                      // the two bands never overlap
            r.width = width;
            r.across = Mathf.Clamp(Mathf.CeilToInt(width * (1f + 2f * BermPad) / Mathf.Max(0.1f, maxQuadWidth)), 1, MaxAcross);
            r.left.e = new Vector3[MaxAcross + 1]; r.right.e = new Vector3[MaxAcross + 1];
            r.offX = Mathf.Clamp(off, -0.4f, 0.4f);                                                         // Z_heavy's model sits 9 cm off the root
            r.halfLen = Mathf.Clamp(rear > 0.5f ? rear : 2.2f, 1f, 3.5f);
        }

        static void Extents(Bounds b, Matrix4x4 m, out Vector3 lo, out Vector3 hi)
        {
            lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); hi = -lo;
            var c = b.center; var e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
        }

        // ================================================================================================ debug
        /// <summary>One line for the console / test steps.</summary>
        public string Stats()
        {
            int vehicles = 0, feet = 0;
            foreach (var kv in runners) { if (kv.Value.gait == Gait.Wheels || kv.Value.gait == Gait.Treads) vehicles++; else if (kv.Value.gait != Gait.None) feet++; }
            return $"TrackMarks: {Mathf.Min(cursor, capacity)}/{capacity} quads used ({cursor} written, {writeRate:F0}/s, life cap " +
                   $"{(lifeCap < 1000f ? lifeCap.ToString("F1") + " s" : "none")}), {vehicles} vehicles + {feet} walkers tracked, clock {clock:F1} s, " +
                   $"heights {(Surface != null && Surface.RenderHeights ? "rendered TER_*" : "COL_Terrain")}, dust {(DustTrails.I ? DustTrails.I.Live : 0)} puffs";
        }

        [ContextMenu("Log stats + surface under the camera centre")]
        void LogStats()
        {
            Debug.Log(Stats());
            var cam = Camera.main;
            if (cam && Surface != null && game && game.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var p))
                Debug.Log($"TrackMarks surface at {p}: {Surface.Describe(p.x, p.z)}");
        }
    }

    /// <summary>
    /// What the ground under a point is, for prints and dust. Built once per level (TrackMarks.Start):
    /// - Zod tile classes (ZMapData.ClassAt), bilinear between tile centres so a print fades over ~1 tile at a road edge;
    /// - the level's ground paint (TER_Ground's _BaseMap, planar over the terrain extent x -apron..W*2+apron, z apron..-(H*2+apron),
    ///   level_terrain_data.py / paint_l0x_ground.py) blitted once to a 0.5 m grid and read back: saturation = soft ground
    ///   (sand, dirt, flower fields), grey = hard (L17 concrete plates, asphalt); the orientation (u / v flips) is checked
    ///   against the class grid and the best match used; the rendered albedo (Sand shader grading, _BaseColor) = dust colour;
    /// - the walkable surface (ZGame.WalkLayer: COL_Terrain + bridge WalkDecks, ZGame.cs:116-128): a hit that is not
    ///   COL_Terrain is a bridge deck;
    /// - the RENDERED terrain height: the TER_* meshes (TER_Ground, TER_RiverBed, TER_Cliffs, TER_Apron, TER_QuayWalls; 1 m
    ///   grid, blender_level_terrain.py:68-113, read/write enabled) rasterised once onto the 0.5 m grid nodes; the lookup
    ///   splits each half-metre cell along the 1 m quad's fold, so it returns the drawn triangles exactly (RenderY). COL_Terrain
    ///   is a 2-degree limited-dissolve copy (blender_level_terrain.py:170-183) up to ~9 cm off the drawn ground; prints on
    ///   the drawn surface can use a tight depth tolerance, so units standing on a fresh print are not darkened.
    /// </summary>
    public sealed class TrackSurface
    {
        public const float Cell = 0.5f;
        public float sand = 1f, hard = 0.2f, road = 0.2f, rock = 0.3f;

        ZMapData map; ZGame game; Collider terrain; int walkMask;
        float x0, x1, z0, z1, cx, cz; int nx, nz;
        byte[] soft; Color32[] dust;
        float[] rh; int rnx, rnz;                                                                          // rendered heights at grid nodes
        Color32 fallbackDust; byte fallbackSoft;
        public bool PaintOk { get; private set; }
        /// <summary>Heights come from the rendered TER_* meshes (else from the COL_Terrain proxy).</summary>
        public bool RenderHeights { get; private set; }
        public bool City { get; private set; }
        /// <summary>World wind (m/s direction, xz) from the ground material's _WindAngle (Sand.shader), else 28 degrees.</summary>
        public Vector3 Wind { get; private set; }
        public Bounds Bounds => new Bounds(new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 400f, z1 - z0));

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), MainTexId = Shader.PropertyToID("_MainTex"),
            BaseColorId = Shader.PropertyToID("_BaseColor"), SandColorId = Shader.PropertyToID("_SandColor"),
            SandRefId = Shader.PropertyToID("_SandRef"), GradeId = Shader.PropertyToID("_Grade"), WindAngleId = Shader.PropertyToID("_WindAngle");

        public static TrackSurface Build(ZGame g, float apron)
        {
            var s = new TrackSurface { game = g, map = g.World.Map };
            s.City = s.map.planet == "city";
            s.x0 = -apron; s.x1 = s.map.width * 2f + apron; s.z0 = -(s.map.height * 2f) - apron; s.z1 = apron;
            s.nx = Mathf.CeilToInt((s.x1 - s.x0) / Cell); s.nz = Mathf.CeilToInt((s.z1 - s.z0) / Cell);
            s.cx = (s.x1 - s.x0) / s.nx; s.cz = (s.z1 - s.z0) / s.nz;
            var col = GameObject.Find("COL_Terrain");                                                       // ZGame.cs:46
            s.terrain = col ? col.GetComponent<Collider>() : null;
            s.walkMask = ZGame.WalkLayer >= 0 ? 1 << ZGame.WalkLayer : 0;
            s.fallbackDust = s.City ? new Color32(118, 100, 88, 255) : new Color32(205, 160, 104, 255);
            s.fallbackSoft = (byte)(s.City ? 80 : 255);
            float windDeg = 28f;
            try { s.ReadPaint(ref windDeg); }
            catch (System.Exception e) { Debug.LogWarning("TrackSurface: ground paint read-back failed, using the class grid only: " + e.Message); s.PaintOk = false; }
            try { s.BuildRenderHeights(); }
            catch (System.Exception e) { Debug.LogWarning("TrackSurface: rendered terrain heights failed, using COL_Terrain: " + e.Message); s.rh = null; s.RenderHeights = false; }
            float a = windDeg * Mathf.Deg2Rad;
            s.Wind = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));                                           // Sand.shader: dir = (cos a, sin a) over world xz
            return s;
        }

        // ------------------------------------------------------------------------------------------------ queries
        /// <summary>Walkable surface height at (x, z) (one raycast for the deck test); on the terrain the height of the RENDERED
        /// ground when known; deck = a bridge deck, not the terrain.</summary>
        public bool Sample(float x, float z, out float y, out bool deck)
        {
            deck = false;
            var ray = new Ray(new Vector3(x, 200f, z), Vector3.down);
            if (walkMask != 0 && Physics.Raycast(ray, out var hit, 400f, walkMask, QueryTriggerInteraction.Ignore))
            {
                deck = terrain && hit.collider != terrain && !CraterField.IsGroundCollider(hit.collider);
                y = !deck && RenderY(x, z, out float ry) ? ry + CraterField.DentRender(x, z) : hit.point.y + (deck ? 0f : CraterField.GroundAdd(hit.collider, x, z));
                return true;
            }
            if (RenderY(x, z, out float r2)) { y = r2 + CraterField.DentRender(x, z); return true; }
            if (terrain && terrain.Raycast(ray, out var h, 400f)) { y = h.point.y; return true; }
            y = game ? game.GroundY(x, z) : 0f;
            return false;
        }

        /// <summary>Rendered terrain height at (x, z); false on holes (and on > 4 m steps inside a half-metre cell). The TER_
        /// meshes are 1 m quads folded along one diagonal; the 0.5 m nodes hold their corners, edge midpoints and centres, and
        /// the fold always runs corner -> centre, so each half-metre cell is split along the diagonal that joins its 1 m-lattice
        /// corner (even, even node) to the 1 m cell centre (odd, odd node): the lookup is exact, not bilinear
        /// </summary>
        public bool RenderY(float x, float z, out float y)
        {
            y = 0f;
            if (rh == null) return false;
            float fx = (x - x0) / cx, fz = (z - z0) / cz;
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fz);
            if (i < 0 || j < 0 || i >= rnx - 1 || j >= rnz - 1) return false;
            int k = j * rnx + i;
            float a = rh[k], b = rh[k + 1], c = rh[k + rnx], d = rh[k + rnx + 1];                          // (0,0) (1,0) (0,1) (1,1)
            float lo = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)), hi = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
            if (float.IsNegativeInfinity(lo) || hi - lo > 4f) return false;
            float tx = fx - i, tz = fz - j;
            if (((i + j) & 1) == 0)                                                                            // fold a-d
                y = tx >= tz ? a + (b - a) * tx + (d - b) * tz : a + (c - a) * tz + (d - c) * tx;
            else                                                                                               // fold b-c
                y = tx + tz <= 1f ? a + (b - a) * tx + (c - a) * tz : d + (c - d) * (1f - tx) + (b - d) * (1f - tz);
            return true;
        }

        /// <summary>0 = no print (water), 1 = full print (sand / dirt).</summary>
        public float MarkStrength(float x, float z)
        {
            Classes(x, z, out float roadW, out float waterW, out float rockW);
            if (waterW > 0.55f) return 0f;
            float s = Mathf.Lerp(hard, sand, Soft(x, z));
            s = Mathf.Lerp(s, road, roadW);
            s = Mathf.Lerp(s, rock, rockW);
            return s * (1f - Smooth(0.15f, 0.55f, waterW));
        }

        /// <summary>0 on roads, concrete and water, 1 on loose sand / dirt.</summary>
        public float DustStrength(float x, float z)
        {
            Classes(x, z, out float roadW, out float waterW, out float rockW);
            return Soft(x, z) * (1f - roadW) * (1f - 0.6f * rockW) * (1f - Smooth(0.1f, 0.5f, waterW));
        }

        /// <summary>Rendered ground colour at (x, z), sRGB.</summary>
        public Color DustColor(float x, float z)
        {
            if (!PaintOk) return fallbackDust;
            return dust[Index(x, z)];
        }

        public string Describe(float x, float z)
        {
            Classes(x, z, out float roadW, out float waterW, out float rockW);
            Sample(x, z, out float y, out bool deck);
            var tile = map.ClassAt(Mathf.FloorToInt(x * 0.5f), Mathf.FloorToInt(-z * 0.5f));
            float colY = terrain && terrain.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var ch, 400f) ? ch.point.y : float.NaN;
            return $"tile {tile}, road {roadW:F2}, water {waterW:F2}, rock {rockW:F2}, paint soft {Soft(x, z):F2}{(PaintOk ? "" : " (no paint)")}, " +
                   $"deck {deck}, y {y:F3} ({(RenderHeights && RenderY(x, z, out _) ? "rendered" : "collider")}, COL_Terrain {colY:F3}), " +
                   $"print {(deck ? 0f : MarkStrength(x, z)):F2}, dust {DustStrength(x, z):F2}, colour {(Color32)DustColor(x, z)}";
        }

        float Soft(float x, float z) => (PaintOk ? soft[Index(x, z)] : fallbackSoft) / 255f;

        int Index(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt((x - x0) / cx), 0, nx - 1), j = Mathf.Clamp(Mathf.FloorToInt((z - z0) / cz), 0, nz - 1);
            return j * nx + i;
        }

        /// <summary>Road / water / rock weights, bilinear between tile centres (tile tx covers x 2tx..2tx+2, ty covers z -2ty-2..-2ty).</summary>
        public void Classes(float x, float z, out float roadW, out float waterW, out float rockW)
        {
            float fx = x * 0.5f - 0.5f, fy = -z * 0.5f - 0.5f;
            int tx = Mathf.FloorToInt(fx), ty = Mathf.FloorToInt(fy);
            float ax = fx - tx, ay = fy - ty;
            roadW = waterW = rockW = 0f;
            for (int k = 0; k < 4; k++)
            {
                int dx = k & 1, dy = k >> 1;
                float w = (dx == 0 ? 1f - ax : ax) * (dy == 0 ? 1f - ay : ay);
                switch (map.ClassAt(tx + dx, ty + dy))                                                      // outside the map = Impassable (ZMapData.cs:33)
                {
                    case ZClass.Road: roadW += w; break;
                    case ZClass.Water: case ZClass.DeepWater: waterW += w; break;
                    case ZClass.Impassable: rockW += w; break;
                }
            }
        }

        static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        // ------------------------------------------------------------------------------------------------ paint read-back
        void ReadPaint(ref float windDeg)
        {
            Texture tex = null; Material gm = null;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("TER_Ground")) continue;
                var m = r.sharedMaterial; if (!m) continue;
                Texture t = m.HasProperty(BaseMapId) ? m.GetTexture(BaseMapId) : null;
                if (!t && m.HasProperty(MainTexId)) t = m.GetTexture(MainTexId);
                if (!t) continue;
                tex = t; gm = m; break;
            }
            if (gm && gm.HasProperty(WindAngleId)) windDeg = gm.GetFloat(WindAngleId);
            if (!tex) { Debug.LogWarning("TrackSurface: no TER_Ground paint found, prints use the tile classes only"); return; }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rt = RenderTexture.GetTemporary(nx, nz, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);                                                                        // mip-filtered down to 0.5 m cells
            RenderTexture.active = rt;
            var rd = new Texture2D(nx, nz, TextureFormat.RGBA32, false, false);
            rd.ReadPixels(new Rect(0, 0, nx, nz), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = rd.GetPixels32();
            Object.Destroy(rd);

            // orientation check: the right mapping has the least colour spread inside each tile class
            var scores = new double[4];
            for (int f = 0; f < 4; f++) scores[f] = Spread(px, (f & 1) != 0, (f & 2) != 0);
            int best = 0; for (int f = 1; f < 4; f++) if (scores[f] < scores[best]) best = f;
            bool flipU = (best & 1) != 0, flipV = (best & 2) != 0;

            // rendered albedo = paint x _BaseColor, desert sand graded like OpenZ/Sand (Sand.shader:87-93, linear space)
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color baseC = gm.HasProperty(BaseColorId) ? gm.GetColor(BaseColorId) : Color.white;
            Color baseLin = linear ? baseC.linear : baseC;
            bool grade = gm.HasProperty(SandColorId);
            Color sandLin = grade ? (linear ? gm.GetColor(SandColorId).linear : gm.GetColor(SandColorId)) : Color.white;
            float sandRef = grade && gm.HasProperty(SandRefId) ? gm.GetFloat(SandRefId) : 0.62f;
            float gradeAmt = grade && gm.HasProperty(GradeId) ? gm.GetFloat(GradeId) : 0f;

            soft = new byte[nx * nz]; dust = new Color32[nx * nz];
            long softCells = 0, land = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    var p = px[(flipV ? nz - 1 - j : j) * nx + (flipU ? nx - 1 - i : i)];
                    float mx = Mathf.Max(p.r, Mathf.Max(p.g, p.b)) / 255f, mn = Mathf.Min(p.r, Mathf.Min(p.g, p.b)) / 255f;
                    float sat = mx > 0.02f ? (mx - mn) / mx : 0f;
                    byte sb = (byte)Mathf.RoundToInt(Smooth(0.10f, 0.22f, sat) * 255f);
                    soft[j * nx + i] = sb;
                    Color paint = p; if (linear) paint = paint.linear;
                    Color c = paint * baseLin;
                    if (gradeAmt > 0f)
                    {   // Sand.shader:87-93: the sand mask from the paint alone, the grade from the tinted albedo's value
                        Color.RGBToHSV(paint, out float h, out float s, out float pv);
                        float mask = Smooth(0.03f, 0.07f, h) * (1f - Smooth(0.15f, 0.2f, h)) * Smooth(0.18f, 0.32f, s) * Smooth(0.25f, 0.45f, pv);
                        float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                        var graded = sandLin * Mathf.Clamp(v / Mathf.Max(0.05f, sandRef), 0.82f, 1.12f);
                        c = Color.Lerp(c, graded, mask * gradeAmt);
                    }
                    c.a = 1f;
                    dust[j * nx + i] = linear ? (Color32)c.gamma : (Color32)c;
                    if (sb > 127) softCells++;
                    land++;
                }
            PaintOk = true;
            Debug.Log($"TrackSurface {map.file}: paint '{tex.name}' {tex.width}x{tex.height} -> {nx}x{nz} cells of {cx:F2} m in {sw.ElapsedMilliseconds} ms, " +
                      $"orientation {(flipU ? "u-flipped " : "")}{(flipV ? "v-flipped" : best == 0 ? "as planned" : "")} (class spread {scores[0]:F0}/{scores[1]:F0}/{scores[2]:F0}/{scores[3]:F0}), " +
                      $"soft ground {100.0 * softCells / Mathf.Max(1, (int)land):F0} %");
        }

        // ------------------------------------------------------------------------------------------------ rendered heights
        void BuildRenderHeights()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            rnx = nx + 1; rnz = nz + 1;
            var h = new float[rnx * rnz];
            for (int i = 0; i < h.Length; i++) h[i] = float.NegativeInfinity;
            var vs = new List<Vector3>(); var ids = new List<int>();
            int meshes = 0, tris = 0, verts = 0, onLattice = 0; string skipped = "";
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                string n = mr.name;
                if (!n.StartsWith("TER_") || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                var mf = mr.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                if (!m) continue;
                if (!m.isReadable || mr.isPartOfStaticBatch) { skipped += " " + n; continue; }          // not readable / merged: COL for those
                m.GetVertices(vs);
                var M = mr.transform.localToWorldMatrix;
                for (int i = 0; i < vs.Count; i++)
                {
                    var w = vs[i] = M.MultiplyPoint3x4(vs[i]);
                    float gi = (w.x - x0) / (2f * cx), gj = (w.z - z0) / (2f * cz);                         // 1 m lattice = even nodes
                    if (Mathf.Abs(gi - Mathf.Round(gi)) < 0.01f && Mathf.Abs(gj - Mathf.Round(gj)) < 0.01f) onLattice++;
                }
                verts += vs.Count;
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    if (m.GetTopology(sm) != MeshTopology.Triangles) continue;
                    m.GetTriangles(ids, sm);
                    for (int t = 0; t + 2 < ids.Count; t += 3) Raster(h, vs[ids[t]], vs[ids[t + 1]], vs[ids[t + 2]]);
                    tris += ids.Count / 3;
                }
                meshes++;
            }
            int filled = 0; for (int i = 0; i < h.Length; i++) if (!float.IsNegativeInfinity(h[i])) filled++;
            RenderHeights = meshes > 0 && filled > h.Length / 4;
            rh = RenderHeights ? h : null;
            Debug.Log($"TrackSurface {map.file}: rendered terrain heights from {meshes} TER_ meshes ({tris} tris) -> {rnx}x{rnz} nodes, " +
                      $"{100.0 * filled / h.Length:F0} % covered, {100.0 * onLattice / Mathf.Max(1, verts):F1} % of the vertices on the 1 m lattice " +
                      $"(100 = exact lookups) in {sw.ElapsedMilliseconds} ms{(skipped.Length > 0 ? ", skipped (not readable / batched):" + skipped : "")}" +
                      $"{(RenderHeights ? "" : " -> using COL_Terrain")}");
        }

        /// <summary>Writes the triangle's height at every grid node inside its xz footprint (topmost surface wins).</summary>
        void Raster(float[] h, Vector3 a, Vector3 b, Vector3 c)
        {
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-6f) return;                                                                // wall seen edge-on
            float inv = 1f / d;
            int i0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - x0) / cx - 1e-3f));
            int i1 = Mathf.Min(rnx - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - x0) / cx + 1e-3f));
            int j0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - z0) / cz - 1e-3f));
            int j1 = Mathf.Min(rnz - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - z0) / cz + 1e-3f));
            const float eps = -1e-4f;
            for (int j = j0; j <= j1; j++)
            {
                float z = z0 + j * cz;
                for (int i = i0; i <= i1; i++)
                {
                    float x = x0 + i * cx;
                    float w0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) * inv;
                    float w1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) * inv;
                    float w2 = 1f - w0 - w1;
                    if (w0 < eps || w1 < eps || w2 < eps) continue;
                    float y = w0 * a.y + w1 * b.y + w2 * c.y;
                    int k = j * rnx + i;
                    if (y > h[k]) h[k] = y;
                }
            }
        }

        /// <summary>Summed colour variance inside each Zod tile class, sampling every tile centre.</summary>
        double Spread(Color32[] px, bool flipU, bool flipV)
        {
            var sum = new double[5, 3]; var sq = new double[5, 3]; var n = new int[5];
            for (int ty = 0; ty < map.height; ty++)
                for (int tx = 0; tx < map.width; tx++)
                {
                    float x = tx * 2f + 1f, z = -(ty * 2f + 1f);
                    int i = Mathf.Clamp(Mathf.FloorToInt((x - x0) / cx), 0, nx - 1), j = Mathf.Clamp(Mathf.FloorToInt((z - z0) / cz), 0, nz - 1);
                    var p = px[(flipV ? nz - 1 - j : j) * nx + (flipU ? nx - 1 - i : i)];
                    int c = Mathf.Clamp((int)map.ClassAt(tx, ty), 0, 4);
                    n[c]++;
                    sum[c, 0] += p.r; sum[c, 1] += p.g; sum[c, 2] += p.b;
                    sq[c, 0] += p.r * p.r; sq[c, 1] += p.g * p.g; sq[c, 2] += p.b * p.b;
                }
            double total = 0; int count = 0;
            for (int c = 0; c < 5; c++)
            {
                if (n[c] == 0) continue;
                count += n[c];
                for (int k = 0; k < 3; k++) total += sq[c, k] - sum[c, k] * sum[c, k] / n[c];
            }
            return count > 0 ? total / count : double.MaxValue;
        }
    }
}
