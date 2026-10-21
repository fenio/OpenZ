using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's landing mark on the ground while it is up (rule DV118, view only): a soft, faint dark patch lying on the drawn
    /// surface straight under the ship, the hint of where it can come down (not its sun shadow: in the air the ship casts none,
    /// View/OzShuttleRealShadow.cs). The selection ring is drawn only while the ship stands on the ground (View/OzShuttleShadowRing.cs).
    /// <list type="bullet">
    /// <item>
    /// The mark no longer lies on a lattice of SAMPLED heights (the ground, the water tiles, a fort,
    /// the deck: everything else drawn stood over it and hid it, the water redrew the bed over it, its one plane jumped at every edge).
    /// It is projected straight down onto WHATEVER IS DRAWN (OpenZ/ShuttleMarkDecal): each pixel of its footprint reads the scene depth,
    /// so it lies on the drawn ground, rims and cliffs, roofs, forts, the station's deck / tower / beam / rams, crowns and bridges as they
    /// are drawn this frame; over drawn water it lies on the water's surface (View/OzWaterSurface.cs) and is drawn after the water. At a
    /// roof or cliff edge each part slides on with the ship: no plane, no step, no pop; never inside a building, never on a wall (faded
    /// over 40 .. 50 degrees). Its size and softness follow the drawn surface under its middle, eased (critically damped,
    /// <see cref="RefOmega"/>): never a step. Nothing above the ship's lowest drawn point is darkened (its own hull); it fades out over
    /// <see cref="TopFadeM"/> under that point, and at a touchdown the top stays <see cref="LandLiftM"/> over the surface (no cut at landing). Every rule of
    /// strength, colour, centre and cross-fade below is K256g's, unchanged. Without the scene depth (a camera without the depth texture)
    /// or without the shader it falls back to K256g's lattice (logged once); <see cref="UseDecal"/> = false does the same.</item>
    /// <item>WHEN (K256g): crossed with the ship's own shadow on the ship's ground share (OzPlayableShuttle.Touches.cs: 0 in the air .. 1
    /// standing, eased over 0.5 s): the mark is drawn x (1 - k) and the ship's own shadow x k (smoothstepped), so at a touchdown the mark
    /// eases out while the real shadow eases in, and the other way round at a take-off; never two full shadows.</item>
    /// <item><see cref="MarkDay"/>
    /// middle while it cruises slowly or hovers, <see cref="MarkNear"/> near a landing (coming down, low over the ground, or while the right
    /// button aims a move for it at a place it can land), x <see cref="MarkFastK"/> at full speed (it fades as the ship flies fast); x
    /// <see cref="MarkNightK"/> at night; eased (<see cref="MarkEaseSec"/>), never a jump. It no longer breathes with the hover bob.</item>
    /// <item>SHAPE: an ellipse 3.5 m along the ship and 3.3 m across it, turned with the ship's eased heading, a little larger the higher it
    /// is (the bob left out); fully dark out to half its radius near the ground, to 0.3 at 12 m (it softens with height).</item>
    /// <item>
    /// had moved 0.25 m (a 5-7 px step at the 70 m camera every 2-3 frames at cruise) and its size breathed with the bob. Now the grid is a
    /// fixed lattice in the world (<see cref="CellM"/> cells, its heights sampled once per cell and kept while the cell stays under the
    /// mark), and the ellipse is placed every frame by its uv from a smoothed centre (the ship's ground point after its final pose, the
    /// drawn velocity fed forward, <see cref="CentreTau"/>): it glides with the ship, no step, no breathing. The alpha a lattice point gets
    /// from the plane rule below eases (<see cref="AlphaEaseSec"/>), so a refit never pops.</item>
    /// <item>WHERE IT CANNOT LAND: red (0.24, 0.03, 0.025, a darkening red, never a glow; at most 0.55 and x(1 - 0.6 night); both tints
    /// x(1 - 0.88 night)) only as aiming feedback: while the right button aims a move for this ship (selected, in the air) at a place it
    /// cannot land (SHUTTLE ORDERS = LANDABLE SPOTS ONLY: a place where it can never land, OzShuttleOrderAim; ANYWHERE: the old test with the
    /// units, OzPlayableShuttle.LandableAt), for <see cref="RedHoldSec"/> after such an order was taken out of a click
    /// (ZObject.ShuttleNoLandOrders), and for <see cref="RedHoldSec"/> from a refused landing of the rules (it hovers: a unit on the spot,
    /// STOP over water, or ANYWHERE); never while it patrols. Into red over 0.25 s, back over 0.4 s; it bounces x1.0 -> x1.15 -> x1.0 every
    /// 0.7 s and pulses 0.25 .. 0.55 (CALM SCREEN: 0.4 of that).</item>
    /// <item>NEVER ON A WALL: drawn on the lattice laid on the drawn surface (the rendered ground, the water surface over open water, a
    /// fort's roof, the repair station's deck while it comes down onto it), lifted 3 cm, but only within 0.6 m of ONE plane: seeded by the
    /// surface under the hull's middle when that is flat enough, else by the flat band most of the mark lies on, then fitted through the
    /// flat points near it (a gentle hillside keeps the whole mark). A point more than 0.4 .. 0.6 m off that plane, or steeper than 35 .. 40
    /// degrees, has alpha 0 (soft edges), and so has a point whose fine slope (0.15 m round it) is over 50 degrees (40 .. 50 soft: the
    /// foot of a bank, a wall or a roof edge; run 27D). Over open water it is drawn after the water (queue 2995), else in the shader's own queue. The
    /// lattice moves on by whole cells (only the new cells are sampled) and is sampled again every second (a crater, a fallen fort). The
    /// shader OpenZ/SoftGroundBlob (kept in builds by Resources/OpenZ/FX/M_OzSoftGroundBlob).</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleShadow                                                  // + OzShuttleShadow.Jitter.cs (the proofs' jitter count, frame-rate aware)
    {
        /// <summary>The landed selection ring's outer radius (m, its glow's outer edge; the band is the light tank's in metres:
        /// View/OzShuttleShadowRing.cs): inside the ship's selection frame (the twin: ring / frame <= 0.87 at the pulse peak, the light tank 0.92).</summary>
        public static float RingM = 3.0f;
        /// <summary>Proof: ring draws left out while airborne (OzShuttleShadowRing), frames drawn, red frames, lattice layouts; visible
        /// lattice points off the plane (by more than 0.6 m) or on a steep wall (a fine test, over 50 degrees) in the last layout of any shuttle.</summary>
        public static int RingSkips, Frames, RedFrames, Rebuilds, WallVerts, WallVertsMax;
        public static float AlongM = 3.5f, AcrossM = 3.3f, Reach = 1.25f;
        public static Color Dark = new Color(0.03f, 0.035f, 0.03f), Red = new Color(0.24f, 0.03f, 0.025f);
        /// <summary>The tints x(1 - NightDim x night): the shader is unlit, so at night both marks would lift the dark ground (a grey or red
        /// glow); the twin: at 0.88 the dark mark darkens every night ground and the red one stays within 2.5 / 255 of the darkest (night water),
        /// still reading red (r - g up by +5 .. +13).</summary>
        public static float NightDim = 0.88f;
        /// <summary>The red: seconds held from a refusal, in, out; the bounce's period and size; its strength and pulse; the most.</summary>
        public static float RedHoldSec = 2.0f, RedInSec = 0.25f, RedOutSec = 0.4f, BouncePeriod = 0.7f, BounceScale = 0.15f, RedStrength = 0.40f, RedPulse = 0.15f, RedMax = 0.55f;
        /// <summary>The plane rule: the band (m: full alpha within PlaneSoftM, none beyond PlaneBandM) and the slope (the surface normal's
        /// y: none under SlopeNy = 40 degrees, full over SlopeSoftNy = 35 degrees).</summary>
        public static float PlaneBandM = 0.6f, PlaneSoftM = 0.4f, SlopeNy = 0.77f, SlopeSoftNy = 0.82f;
        /// <summary>The hover bob's full swing (m; the mark leaves it out since K256g: no breathing).</summary>
        public static float BobRefM = 0.7f;
        /// <summary>K256g, the mark's strength at its middle by day: cruising slowly or hovering; near a landing (coming down, low, a landing
        /// spot aimed); the factor at full speed; the night factor; the ease (s for the whole near-to-far change).</summary>
        public static float MarkDay = 0.315f, MarkNear = 0.34f, MarkFastK = 0.90f, MarkNightK = 1.0f, MarkEaseSec = 0.35f;   // the readable mark (OzShuttleShadow.Visible.cs): 18 / 20 / 22 % darker by day
        /// <summary>K256g: from this low (full) to this high (none) (m of the pads over the ground); "fast" from this share of
        /// its top speed (none) to this one (full).</summary>
        public static float NearFullM = 3f, NearNoneM = 9f, FastFrom = 0.35f, FastFull = 0.9f;
        /// <summary>K256g: the lattice's cell (m); the centre's smoothing (s); the plane alpha's ease (s); a full new sampling every (s).</summary>
        public static float CellM = 0.5f, CentreTau = 0.04f, AlphaEaseSec = 0.25f, RefreshSec = 1.0f;
        /// <summary>Proof: hide every shuttle shadow this frame (a picture with and without it); count the fine wall test at every layout
        /// (four more surface samples a drawn point: proofs only).</summary>
        public static bool ProofHide, ProofWalls;
        /// <summary>Proof (K256g): while it flies, the frame-to-frame change of the mark's drawn velocity (m per frame at 60 fps: 0 = it
        /// glides at an even speed) and the same of the ship's own ground point: the worst and the sum (rms = sqrt(sum2 / n)); the cells
        /// sampled (new or refreshed).</summary>
        public static float JitterMaxM, JitterSum2, ShipJitterMaxM, ShipJitterSum2; public static int JitterFrames, CellsSampled;
        const int Half = 13, N = 2 * Half + 1;
        /// <summary>K256g2: the mark projected onto what is drawn (false: K256g's lattice); the two passes' queues (the drawn surface at
        /// the old mark's queue, the water surface after the water); the eased surface height's spring (1/s, critically damped); the box's
        /// top over the ship's lowest drawn point, its bottom under the lowest ground of the footprint (m), its margin round the ellipse.</summary>
        public static bool UseDecal = true;
        public static int QueueGround = 2860, QueueWater = 2995;
        public static float RefOmega = 6f, TopLiftM = 0.02f, BottomM = 2.5f, BoxPad = 1.06f;
        /// <summary>At a touchdown the box's top never sinks under the eased surface + LandLiftM (the drawn deck / ground relief round the
        /// pads stays marked to the end; the pads and the legs' lowest part may take the mark, as under a real hull), and the mark fades out
        /// over TopFadeM under the top instead of a hard cut (a part that the top passes goes soft, never in one frame).</summary>
        public static float LandLiftM = 0.45f, TopFadeM = 0.3f;
        /// <summary>Proof (K256g2): frames drawn as the decal / as the lattice; switches to the lattice (and why, the last); the worst
        /// change of the eased surface height and of the ellipse's radius in one frame (m).</summary>
        public static int DecalFrames, LatticeFrames, Fallbacks, KickLowFrames; public static string FallbackWhy = "";
        public static float RefStepMaxM, RadiusStepMaxM;

        public Vector3 Centre { get; private set; }
        public Color TintNow { get; private set; }
        public float ScaleNow { get; private set; } = 1f;
        public float StrengthNow { get; private set; }
        public float Bounce { get; private set; }
        public bool Shown { get; private set; }
        /// <summary>0 = the dark mark .. 1 = the red "cannot land" mark (eased).</summary>
        public float RedNow { get; private set; }
        /// <summary>Seconds since the last refused landing or order (large = none yet); the right-button aim over a blocked place.</summary>
        public float RefusedAgo => t - refusedAt;
        public bool CursorBlocked { get; private set; }
        /// <summary>K256g: the right button aims a move for this ship at a place it can land (the mark at its "near" strength).</summary>
        public bool Aimed { get; private set; }
        /// <summary>K256g: the ship's ground share (0 in the air .. 1 standing), set by the ship before each Step; the mark is drawn x (1 - its smoothstep).</summary>
        public float GroundShare { get; set; }
        /// <summary>K256g: the mark's weight in the cross-fade with the ship's own shadow now (1 - smoothstep(GroundShare)) and its
        /// strength before that weight (the day / near / fast / night value, eased).</summary>
        public float MarkWeight { get; private set; }
        public float MarkStrength { get; private set; }
        /// <summary>What the plane lies on ("ground", "water", "roof", "deck", "none": nothing flat under it) and its height.</summary>
        public string PlaneKind { get; private set; } = "none";
        public float PlaneY { get; private set; }
        /// <summary>Lattice points drawn (alpha over 0.05) in the last layout.</summary>
        public int DrawnVerts { get; private set; }
        /// <summary>K256g2: drawn as the decal this frame; the eased drawn surface under the middle (m, world y); the ship's lowest drawn
        /// point (the box's top); the ellipse's growth and softness core now.</summary>
        public bool DecalOn { get; private set; }
        public float RefY => refY;
        public float LowY { get; private set; }
        public float GrowNow { get; private set; } = 1f;
        public float CoreNow { get; private set; } = 0.5f;
        // K256g2: the decal
        GameObject dgo; MeshRenderer dr; Mesh dmesh; Material dmatG, dmatW; bool decalOk, haveRef, downMode; float refY, refV, lastRadius = -1f, downLow; Vector3 downAt, downVel;
        readonly MaterialPropertyBlock dmpb = new MaterialPropertyBlock(); MeshRenderer[] hull; float hullAt = -9f;
        static readonly int GrowId = Shader.PropertyToID("_Grow"), BoxId = Shader.PropertyToID("_MarkBox"), AxesId = Shader.PropertyToID("_MarkAxes"), WaterPassId = Shader.PropertyToID("_WaterPass"),
                            WaterTexId = Shader.PropertyToID("_WaterTex"), WaterSTId = Shader.PropertyToID("_WaterST"), WaterSizeId = Shader.PropertyToID("_WaterSize"),
                            TopFadeId = Shader.PropertyToID("_TopFade");

        readonly Transform root; GameObject go; MeshFilter mf; MeshRenderer mr; Mesh mesh; Material mat; readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        // the mesh, in window order (i along x, j along z)
        readonly Vector3[] verts = new Vector3[N * N]; readonly Vector2[] uvs = new Vector2[N * N]; readonly Color[] cols = new Color[N * N];
        readonly float[] hs = new float[N * N], ny = new float[N * N], px = new float[N * N], pz = new float[N * N], aT = new float[N * N]; readonly byte[] kind = new byte[N * N];
        readonly int[] slotOf = new int[N * N]; readonly float[] rr = new float[N * N]; readonly bool[] sq = new bool[N * N];
        // the lattice cache (toroidal: the cell (ix, iz) lives in slot (ix mod N, iz mod N)): its key, height, kind and eased alpha
        readonly int[] ckx = new int[N * N], ckz = new int[N * N]; readonly float[] ch = new float[N * N], ca = new float[N * N], cf = new float[N * N]; readonly byte[] ck = new byte[N * N];
        int ox = int.MinValue, oz = int.MinValue, lastDeck = -2; float builtT = -9f, red, t, refusedAt = -1e9f, cursorT, cursorHold, markNow = -1f; bool lastNoLand, cursorRaw, aimRaw, haveC, haveJ, fortNear = true;
        int lastHovers = int.MinValue, lastNoLandOrders = int.MinValue; Vector2 cs, cv, lastC, lastShipP; Vector2 lastStepC, lastStepS; float lastDt; bool haveV;
        OzPlayableShuttle ship; bool shipLooked;
        static readonly Vector3 FarAway = new Vector3(1e6f, 0f, 1e6f);                                     // no tower pad left out of the fort's top
        static readonly int TintId = Shader.PropertyToID("_Tint"), StrengthId = Shader.PropertyToID("_Strength"), CoreId = Shader.PropertyToID("_Core"), ScaleId = Shader.PropertyToID("_Scale");
        const byte KGround = 0, KWater = 1, KRoof = 2, KDeck = 3;
        static readonly string[] KindName = { "ground", "water", "roof", "deck" };

        public OzShuttleShadow(Transform shipRoot)
        {
            root = shipRoot;
            BuildDecal();                                                                                   // K256g2: the mark on whatever is drawn
            var m = Resources.Load<Material>("OpenZ/FX/M_OzSoftGroundBlob");
            var sh = m && m.shader ? m.shader : Shader.Find("OpenZ/SoftGroundBlob");
            if (!sh || !sh.isSupported) { Debug.LogError("[OpenZ] shuttle shadow: shader OpenZ/SoftGroundBlob missing (no ground mark in flight)"); return; }
            mat = new Material(sh) { name = "OZ_ShuttleShadow (runtime)" };
            go = new GameObject("OZ_FxShuttleShadow"); mf = go.AddComponent<MeshFilter>(); mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mesh = new Mesh { name = "OZ_ShuttleShadowGrid" }; mesh.MarkDynamic();
            var tri = new int[(N - 1) * (N - 1) * 6]; int k = 0;
            for (int j = 0; j < N - 1; j++) for (int i = 0; i < N - 1; i++) { int a = j * N + i; tri[k++] = a; tri[k++] = a + N; tri[k++] = a + 1; tri[k++] = a + 1; tri[k++] = a + N; tri[k++] = a + N + 1; }
            for (int q = 0; q < N * N; q++) { ckx[q] = int.MinValue; ca[q] = -1f; }
            mesh.vertices = verts; mesh.uv = uvs; mesh.colors = cols; mesh.triangles = tri;
            mf.sharedMesh = mesh; mr.enabled = false;
        }

        public void Dispose()
        {
            if (go) Object.Destroy(go); if (mesh) Object.Destroy(mesh); if (mat) Object.Destroy(mat);
            if (dgo) Object.Destroy(dgo); if (dmesh) Object.Destroy(dmesh); if (dmatG) Object.Destroy(dmatG); if (dmatW) Object.Destroy(dmatW);
            mr = null; dr = null; Shown = false; DecalOn = false;
        }

        /// <summary>K256g2: the decal's box (two submeshes of one unit box: the drawn surface pass, the water pass), its two materials.</summary>
        void BuildDecal()
        {
            var m = Resources.Load<Material>("OpenZ/FX/M_OzShuttleMarkDecal");
            var sh = m && m.shader ? m.shader : Shader.Find("OpenZ/ShuttleMarkDecal");
            if (!sh || !sh.isSupported)
            {
                FallbackWhy = "the shader OpenZ/ShuttleMarkDecal is missing or not supported"; Fallbacks++;
                Debug.LogWarning("[OpenZ] shuttle mark: " + FallbackWhy + ": the mark is K256g's lattice on the sampled ground"); return;
            }
            dmatG = m ? new Material(m) : new Material(sh); dmatG.name = "OZ_ShuttleMarkDecal ground (runtime)"; dmatG.SetFloat(WaterPassId, 0f); dmatG.renderQueue = QueueGround;
            dmatW = new Material(dmatG) { name = "OZ_ShuttleMarkDecal water (runtime)" }; dmatW.SetFloat(WaterPassId, 1f); dmatW.renderQueue = QueueWater;
            var wt = OzWaterSurface.Texture;
            foreach (var mm in new[] { dmatG, dmatW }) { if (wt) mm.SetTexture(WaterTexId, wt); mm.SetVector(WaterSTId, OzWaterSurface.ST); mm.SetVector(WaterSizeId, OzWaterSurface.Size); }
            dgo = new GameObject("OZ_FxShuttleMarkDecal"); dgo.AddComponent<MeshFilter>().sharedMesh = dmesh = BoxMesh();
            dr = dgo.AddComponent<MeshRenderer>(); dr.sharedMaterials = new[] { dmatG, dmatW };
            dr.shadowCastingMode = ShadowCastingMode.Off; dr.receiveShadows = false; dr.lightProbeUsage = LightProbeUsage.Off; dr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            dr.allowOcclusionWhenDynamic = false; dr.enabled = false;
            if (root) hull = ModelRenderers(root);
            decalOk = true;
        }

        /// <summary>A unit box (outward faces, Unity's clockwise fronts), the same 12 triangles twice (submesh 0 and 1).</summary>
        static Mesh BoxMesh()
        {
            var vs = new Vector3[24]; var tri = new int[36];
            Vector3[] n = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            Vector3[] u = { Vector3.right, Vector3.forward, Vector3.forward, Vector3.up, Vector3.up, Vector3.right };
            Vector3[] w = { Vector3.forward, Vector3.right, Vector3.up, Vector3.forward, Vector3.right, Vector3.up };
            for (int f = 0; f < 6; f++)
            {
                int b = f * 4;
                vs[b] = 0.5f * (n[f] - u[f] - w[f]); vs[b + 1] = 0.5f * (n[f] - u[f] + w[f]); vs[b + 2] = 0.5f * (n[f] + u[f] + w[f]); vs[b + 3] = 0.5f * (n[f] + u[f] - w[f]);
                int k = f * 6; tri[k] = b; tri[k + 1] = b + 1; tri[k + 2] = b + 2; tri[k + 3] = b; tri[k + 4] = b + 2; tri[k + 5] = b + 3;
            }
            var mesh = new Mesh { name = "OZ_ShuttleMarkBox", vertices = vs, subMeshCount = 2 };
            mesh.SetTriangles(tri, 0); mesh.SetTriangles(tri, 1); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The ship's model parts (the renderers that cast a shadow when it stands: no beams, flames or glows), read once.</summary>
        static MeshRenderer[] ModelRenderers(Transform r)
        {
            var list = new System.Collections.Generic.List<MeshRenderer>();
            foreach (var x in r.GetComponentsInChildren<MeshRenderer>(true))
                if (x && x.shadowCastingMode != ShadowCastingMode.Off && x.shadowCastingMode != ShadowCastingMode.ShadowsOnly) list.Add(x);
            return list.ToArray();
        }

        /// <summary>The lowest point of the ship as drawn now (its model parts' bounds; the root without them).</summary>
        float HullLowY()
        {
            if ((hull == null || hull.Length == 0) && root && t - hullAt > 5f) { hullAt = t; hull = ModelRenderers(root); }
            float lo = float.MaxValue;
            if (hull != null) foreach (var r in hull) if (r && r.enabled && r.gameObject.activeInHierarchy) lo = Mathf.Min(lo, r.bounds.min.y);
            return lo < float.MaxValue ? lo : (root ? root.position.y : 0f);
        }

        /// <summary>
        /// OzPlayableShuttle.KickPivot only at the end of its LateUpdate (KickflipDraw), after this step, so the level pose's bounds would
        /// leave the rolled pods' and legs' lower ends (up to about 0.8 m lower) inside the box, on the mark. The flip's own pose
        /// (OzPlayableShuttle.KickPose: the same rotation and offset) is put on every model part's mesh box (8 corners): the lower of the two.</summary>
        float KickLowY(float levelLow)
        {
            if (!ship || !root || hull == null) return levelLow;
            var k = ship.KickNow;
            if (!k.Active) return levelLow;
            var d = Quaternion.Euler(-(float)k.PitchUpDeg, 0f, 0f) * Quaternion.Euler(0f, 0f, (float)k.RollDeg);
            var lr = root.rotation; var q = lr * d; var piv = OzPlayableShuttle.KickPivot;
            var p = root.position + lr * piv - q * piv + Vector3.up * (float)k.HopM;
            if (float.IsNaN(p.y) || float.IsNaN(q.x)) return levelLow;                                          // never a broken pose (as KickPose)
            var flipped = Matrix4x4.TRS(p, q, root.lossyScale) * root.worldToLocalMatrix; float lo = levelLow;     // level world -> the flipped picture's world
            foreach (var r in hull)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || !r.TryGetComponent<MeshFilter>(out var f) || !f.sharedMesh) continue;
                var mb = f.sharedMesh.bounds; var M = flipped * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    float y = M.MultiplyPoint3x4(c).y;
                    if (y < lo) lo = y;
                }
            }
            KickLowFrames++;
            return lo;
        }

        static float depthLook = -9f; static bool depthOk = true;
        /// <summary>The scene depth is there for the decal (the pipeline's depth texture, the main camera's own setting), asked once a second.</summary>
        static bool DepthTextureOn()
        {
            if (Time.unscaledTime - depthLook < 1f) return depthOk;
            depthLook = Time.unscaledTime;
            var a = UniversalRenderPipeline.asset; bool ok = a != null && a.supportsCameraDepthTexture;
            var cam = Camera.main;
            if (ok && cam && cam.TryGetComponent<UniversalAdditionalCameraData>(out var cd)) ok = cd.requiresDepthTexture;
            if (!ok && depthOk) { Fallbacks++; FallbackWhy = "the camera draws no depth texture"; Debug.LogWarning("[OpenZ] shuttle mark: " + FallbackWhy + ": the mark is K256g's lattice on the sampled ground"); }
            depthOk = ok; return ok;
        }

        /// <summary>K256g2: the ship is shot down (its dying drift, or the hand-over after the drawn kill: OzShuttleShadowHandOver): no
        /// red, no aim; the mark's middle follows `at` with `vel` (m per real second), its box tops out at lowY (NaN: the ship's own lowest
        /// drawn point). The caller sets <see cref="GroundShare"/> first (the mark is drawn x (1 - its smoothstep)).</summary>
        public void StepDown(float dt, Vector3 at, Vector3 vel, float lowY)
        {
            downMode = true; downAt = at; downVel = vel; downLow = lowY;
            float g = GroundAt(at.x, at.z), low = float.IsNaN(lowY) ? HullLowY() : lowY;
            Step(dt, Mathf.Max(0f, at.y - g), Mathf.Max(0f, low - g), false, true);
            downMode = false;
        }

        /// <summary>Critically damped: x eases to target, never overshooting from rest (exact for any frame time).</summary>
        static void CriticalStep(ref float x, ref float v, float target, float w, float dt)
        {
            if (dt <= 0f) return;
            float x0 = x - target, e = Mathf.Exp(-w * dt), b = v + w * x0;
            x = target + (x0 + b * dt) * e; v = (v - w * b * dt) * e;
        }

        /// <summary>The drawn surface under the middle for the size and softness (K256g's surface with every fort, the drawn water).</summary>
        float RefSurface(float x, float z, out byte k)
        {
            bool fn = fortNear; fortNear = true;
            float y = SurfaceAt(x, z, out k);
            fortNear = fn;
            if (OzWaterSurface.At(x, z, out float wy) && wy > y) { y = wy; k = KWater; }
            return y;
        }

        /// <summary>K256g2, one frame of the decal: the eased surface under the middle, the box, the shader's numbers.</summary>
        void DrawDecal(Color tint, float strength, float yaw, float shapeY, float lowPoint, float dt)
        {
            float target = RefSurface(cs.x, cs.y, out byte k);
            float before = refY;
            if (!haveRef) { refY = target; refV = 0f; haveRef = true; before = refY; }
            else CriticalStep(ref refY, ref refV, target, RefOmega, dt);
            RefStepMaxM = Mathf.Max(RefStepMaxM, Mathf.Abs(refY - before));
            PlaneY = refY; PlaneKind = KindName[k];
            float h = Mathf.Max(0f, shapeY - refY), grow = 1f + 0.02f * h, core = Mathf.Lerp(0.5f, CoreFar, Mathf.Clamp01(h / 12f));
            GrowNow = grow; CoreNow = core;
            float ra = AlongM * grow * ScaleNow, rc = AcrossM * grow * ScaleNow, R = Mathf.Max(ra, rc);
            if (lastRadius >= 0f) RadiusStepMaxM = Mathf.Max(RadiusStepMaxM, Mathf.Abs(AlongM * grow - lastRadius));
            lastRadius = AlongM * grow;
            float bottom = refY;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) bottom = Mathf.Min(bottom, GroundAt(cs.x + i * R, cs.y + j * R));
            bottom -= BottomM;
            float top = Mathf.Max(Mathf.Max(lowPoint + TopLiftM, refY + LandLiftM), bottom + 0.05f);  // finish fix: never a cut through the landing surface
            LowY = lowPoint;
            var tr = dgo.transform;
            tr.SetPositionAndRotation(new Vector3(cs.x, 0.5f * (top + bottom), cs.y), Quaternion.Euler(0f, yaw, 0f));
            tr.localScale = new Vector3(2f * rc * BoxPad, top - bottom, 2f * ra * BoxPad);
            float yr = yaw * Mathf.Deg2Rad;
            dmpb.SetFloat(TopFadeId, TopFadeM);
            dmpb.SetColor(TintId, tint); dmpb.SetFloat(StrengthId, strength); dmpb.SetFloat(CoreId, core); dmpb.SetFloat(ScaleId, ScaleNow); dmpb.SetFloat(GrowId, grow);
            dmpb.SetVector(BoxId, new Vector4(cs.x, top, cs.y, bottom)); dmpb.SetVector(AxesId, new Vector4(Mathf.Sin(yr), Mathf.Cos(yr), AlongM, AcrossM));
            dr.SetPropertyBlock(dmpb);
            NightWaterPass(strength);                                                                       // K420: the water pass reads at night too (View/OzShuttleShadow.NightWater.cs)
            DecalFrames++;
            if (ProofWalls) DecalWalls(yaw, grow);
        }

        /// <summary>Proof (K256g2, ProofWalls): the decal's own rule on the sampled surface at the old 17 x 17 grid's points: drawn = alpha
        /// over 0.05 (the wall fade on the fine slope, 0.15 m round the point); a wall = drawn on a slope over 50 degrees (0 by the rule).</summary>
        void DecalWalls(float yaw, float grow)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f); var fwd = rot * Vector3.forward; var right = rot * Vector3.right;
            int drawn = 0, walls = 0, all = 0;
            for (int j = -8; j <= 8; j++)
                for (int i = -8; i <= 8; i++)
                {
                    float a = i / 8f * Reach, c = j / 8f * Reach; all++;
                    var p = new Vector2(cs.x, cs.y) + new Vector2(fwd.x, fwd.z) * (a * AlongM * grow) + new Vector2(right.x, right.z) * (c * AcrossM * grow);
                    float ny0 = FineNy(p.x, p.y), wall = Mathf.InverseLerp(0.643f, 0.766f, ny0);
                    if (Mathf.Sqrt(a * a + c * c) >= 1f || wall <= 0.05f) continue;
                    drawn++; if (ny0 < 0.643f) walls++;
                }
            WallVerts = walls; if (walls > WallVertsMax) WallVertsMax = walls; DrawnVerts = Mathf.RoundToInt(289f * drawn / Mathf.Max(1, all));
        }

        static float GroundAt(float x, float z)
        {
            var tm = TrackMarks.I;
            if (tm && tm.Surface != null && tm.Surface.RenderY(x, z, out float y)) return y;
            var g = ZGame.I; return g ? g.GroundY(x, z) : 0f;
        }

        /// <summary>The drawn surface under (x, z): the rendered ground, the water surface over open water, a live fort's roof / walls /
        /// pads, the station's deck while this ship comes down onto it (the highest of them), and what it is.</summary>
        float SurfaceAt(float x, float z, out byte k)
        {
            float y = GroundAt(x, z); k = KGround;
            var p = new Vector3(x, y, z);
            var fx = OpenZFxPlayer.I;
            if (fx && fx.isActiveAndEnabled && OpenZFxPlayer.IsWaterTile(p))
            {
                float wy = fx.WaterYAt(p);
                if (!float.IsNegativeInfinity(wy) && wy > y) { y = wy; k = KWater; }
            }
            if (fortNear)
            {
                float ft = FortGunView.FortTopAt(p, FarAway);
                if (ft > y + 0.3f) { y = ft; k = KRoof; }
            }
            if (ship && ship.DeckStation >= 0 && OzStationCrane.DeckY(ship.DeckStation, p, out float dy) && dy > y + 0.05f) { y = dy; k = KDeck; }
            return y;
        }

        /// <summary>One frame (the ship's LateUpdate, after its pose). heightM = the ship's root over the ground (m), feetM = its pads over the
        /// ground (m), noLand = hovering where it cannot land, alive = drawn at all. The ship sets <see cref="GroundShare"/> first.</summary>
        public void Step(float dt, float heightM, float feetM, bool noLand, bool alive)
        {
            if (!mr && !dr) return;
            t += dt;
            if (!shipLooked) { shipLooked = true; ship = root ? root.GetComponent<OzPlayableShuttle>() : null; }
            var o = ship ? ship.Obj : null;
            // the refusal moments: the rules counted another hover where it cannot land, its hover flag came on, or (K256g) a move order was
            // taken out of a click because it could never land there
            int hv = o != null ? o.ShuttleHovers : 0, nl = o != null ? o.ShuttleNoLandOrders : 0;
            bool orderRefused = lastNoLandOrders != int.MinValue && nl > lastNoLandOrders;
            if ((noLand && !lastNoLand) || (lastHovers != int.MinValue && hv > lastHovers) || orderRefused) refusedAt = t;
            if (orderRefused) orderRefusedAt = t;
            lastNoLand = noLand; lastHovers = hv; lastNoLandOrders = nl;
            bool patrol = o != null && o.Patrolling;
            CursorBlocked = alive && !downMode && CursorOverBlocked(o, dt);
            if (downMode) Aimed = false;
            bool wantRed = alive && !patrol && !downMode && ((noLand && t - refusedAt < RedHoldSec) || (t - orderRefusedAt < RedHoldSec) || CursorBlocked);
            red = Mathf.MoveTowards(red, wantRed ? 1f : 0f, dt / (wantRed ? RedInSec : RedOutSec));
            float rk = red * red * (3f - 2f * red); RedNow = rk;
            // the cross-fade with the ship's own shadow (its ground share, eased by the ship)
            float gk = Mathf.Clamp01(GroundShare), air = 1f - gk * gk * (3f - 2f * gk);
            MarkWeight = air;
            Shown = alive && air > 0.001f && !ProofHide;
            bool useDecal = decalOk && dr && UseDecal && DepthTextureOn();                                  // K256g2: the decal, else K256g's lattice
            if (!useDecal && !mr) Shown = false;
            DecalOn = Shown && useDecal;
            if (mr && mr.enabled != (Shown && !useDecal)) mr.enabled = Shown && !useDecal;
            if (dr && dr.enabled != DecalOn) dr.enabled = DecalOn;
            if (!Shown) { StrengthNow = 0f; Bounce = 0f; haveJ = false; if (!alive || air <= 0.001f) { haveC = false; haveRef = false; lastRadius = -1f; } return; }
            Frames++;
            if (rk > 0.5f) RedFrames++;
            // the bounce of the red: x1.0 -> x1.15 -> x1.0 every 0.7 s (a hop: up eased out, down eased in)
            float u = Mathf.Repeat((noLand ? t - refusedAt : t) / BouncePeriod, 1f), b = Mathf.Sin(Mathf.PI * u);
            float calm = OzMotion.Reduced ? 0.4f : 1f;
            Bounce = b * rk;
            ScaleNow = 1f + BounceScale * calm * Bounce;                                                    // K256g: no breathing with the bob
            // the height without the bob (no size pulse)
            float h = Mathf.Max(0f, heightM - (ship ? ship.BobNow : 0f));
            float grow = 1f + 0.02f * h;
            float night = Mathf.Clamp01(NightGrade.NightK);
            // K256g: the mark's strength: faint while it cruises, a little stronger near a landing or while a landing spot is aimed, fainter fast
            float vmax = o != null ? Mathf.Max(1f, (float)o.RealMoveSpeed) : 1f, spd = o != null ? (float)o.ShuttleSpeedPx : 0f;
            if (float.IsNaN(spd)) spd = 0f;
            float fastK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FastFrom * vmax, FastFull * vmax, spd));
            float nearK = o != null && !downMode && o.ShuttleState == ZShuttleState.Descend ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NearFullM, NearNoneM, Mathf.Max(0f, feetM - (ship ? ship.BobNow : 0f))));
            if (Aimed) { nearK = 1f; fastK = 0f; }
            float want = WantFor(nearK, fastK, SelectedStep(o, dt), night);                                // + a little stronger while selected (OzShuttleShadow.Visible.cs)
            markNow = markNow < 0f ? want : Mathf.MoveTowards(markNow, want, dt * MarkEaseSpan() / Mathf.Max(0.05f, MarkEaseSec));
            MarkStrength = markNow;
            float redA = Mathf.Clamp(RedStrength + RedPulse * calm * (2f * b - 1f), 0f, RedMax) * (1f - 0.6f * night);
            redA = Mathf.Max(redA, markNow);                                                                 // the red turns red, never lighter than the dark mark
            float strength = Mathf.Lerp(markNow, redA, rk) * air;
            StrengthNow = strength;
            var tint = NightTint(rk, night); TintNow = tint;   // K419a: unlit: never brighter than the ground; the dark one black at night (its share of the ground's light), the red x(1 - NightDim) as before (OzShuttleShadow.NightShare.cs)
            // the centre: the ship's ground point after its final pose, smoothed with its drawn velocity fed forward (no lag at an even speed)
            var rp = downMode ? downAt : root.position; var target = new Vector2(rp.x, rp.z);
            var vel = downMode ? new Vector2(downVel.x, downVel.z) : ship ? new Vector2(ship.Velocity.x, ship.Velocity.z) : Vector2.zero;   // m per real second (the game speed in it)
            float fdt = Mathf.Clamp(Time.unscaledDeltaTime, 1e-4f, 0.25f);                                 // the real frame time: no lag after a slow frame
            var gm = ZGame.I;
            if (!haveC || (target - cs).sqrMagnitude > 16f || (gm && gm.paused)) { cs = target; cv = vel; haveC = true; haveJ = false; }   // paused: it stands where the ship stands
            else
            {
                cv = vel; cs += cv * fdt;
                cs += (target - cs) * (1f - Mathf.Exp(-fdt / Mathf.Max(0.005f, CentreTau)));
            }
            if (downMode) { haveJ = false; haveV = false; } else JitterNote(target, fdt, o);
            float yaw = ship ? ship.YawDeg : root.eulerAngles.y;
            if (useDecal)
            {   // K256g2: projected onto whatever is drawn (no lattice, no plane, no cache)
                float low = downMode ? (float.IsNaN(downLow) ? HullLowY() : downLow) : KickLowY(HullLowY());
                float shapeY = downMode ? low : rp.y - (ship ? ship.BobNow : 0f);
                DrawDecal(tint, strength, yaw, shapeY, low, fdt);
                Centre = new Vector3(cs.x, PlaneY, cs.y);
                return;
            }
            LatticeFrames++;
            if (lastRadius >= 0f) { lastRadius = -1f; haveRef = false; }
            // the lattice under the mark: whole cells; only new cells sampled; everything again every second or when the deck comes or goes
            int cx = Mathf.FloorToInt(cs.x / CellM), cz = Mathf.FloorToInt(cs.y / CellM);
            int deck = ship ? ship.DeckStation : -1;
            bool refresh = t - builtT > RefreshSec || deck != lastDeck;
            if (cx - Half != ox || cz - Half != oz || refresh) Layout(cx - Half, cz - Half, refresh, yaw, grow);
            lastDeck = deck;
            Centre = new Vector3(cs.x, PlaneY, cs.y);
            // every frame: the ellipse's uv from the smoothed centre and the eased heading; the eased alpha of every lattice point
            float yr = yaw * Mathf.Deg2Rad, fx = Mathf.Sin(yr), fz = Mathf.Cos(yr), ia = 1f / (AlongM * grow), ic = 1f / (AcrossM * grow);
            float ease = dt / Mathf.Max(0.02f, AlphaEaseSec);
            for (int k = 0; k < N * N; k++)
            {
                float dx = px[k] - cs.x, dz = pz[k] - cs.y;
                uvs[k] = new Vector2((dx * fx + dz * fz) * ia, (dx * fz - dz * fx) * ic);                  // along the nose, across to its right
                int s = slotOf[k];
                ca[s] = ca[s] < 0f ? aT[k] : Mathf.MoveTowards(ca[s], aT[k], ease);
                cols[k] = new Color(1f, 1f, 1f, ca[s]);
            }
            mesh.uv = uvs; mesh.colors = cols;
            mpb.SetColor(TintId, tint); mpb.SetFloat(StrengthId, strength); mpb.SetFloat(CoreId, Mathf.Lerp(0.5f, CoreFar, Mathf.Clamp01(h / 12f))); mpb.SetFloat(ScaleId, ScaleNow);
            mr.SetPropertyBlock(mpb);
        }
        float orderRefusedAt = -1e9f;

        /// <summary>Proof (K256g): the frame-to-frame change of the drawn velocity of the mark and of the ship's own ground point while it
        /// flies (in m per frame at 60 fps), and their worst and summed squares; frames slower than 0.03 s and the pair after them are left out.</summary>
        void JitterNote(Vector2 shipP, float dt, ZObject o)
        {
            bool flying = o != null && o.ShuttleAirborne && (o.ShuttleState == ZShuttleState.Cruise || o.ShuttleState == ZShuttleState.Hover) && dt > 1e-4f;
            if (!flying || dt > JitterHitchSec(dt)) { haveJ = false; haveV = false; return; }            // a slow frame (a hitch of the editor) is no pair; the limit follows the frame rate (OzShuttleShadow.Jitter.cs)
            if (haveJ && lastDt > 1e-4f)
            {
                Vector2 vm = (cs - lastC) / dt, vs = (shipP - lastShipP) / dt;
                if (haveV)
                {
                    float jm = (vm - lastStepC).magnitude * JitterFrameSec(), js = (vs - lastStepS).magnitude * JitterFrameSec();   // per drawn frame in whole 60 fps frames (1/60 at 60 fps, as before; OzShuttleShadow.Jitter.cs)
                    JitterMaxM = Mathf.Max(JitterMaxM, jm); JitterSum2 += jm * jm; ShipJitterMaxM = Mathf.Max(ShipJitterMaxM, js); ShipJitterSum2 += js * js; JitterFrames++;
                }
                lastStepC = vm; lastStepS = vs; haveV = true;
            }
            else haveV = false;
            lastC = cs; lastShipP = shipP; lastDt = dt; haveJ = true;
        }

        /// <summary>The right button aims a move for this ship (selected and in the air, the pointer on the map, not on the panels, not on a
        /// unit), tested 10 times a second: over a place it cannot land (OzShuttleOrderAim.AimBlocked: never, with LANDABLE SPOTS ONLY; not
        /// now, with ANYWHERE), held 0.15 s before it counts (no flicker over mixed ground); else <see cref="Aimed"/> (a landing spot aimed).</summary>
        bool CursorOverBlocked(ZObject o, float dt)
        {
            var mouse = Mouse.current; var g = ZGame.I;
            if (o == null || !o.ShuttleAirborne || mouse == null || !mouse.rightButton.isPressed || g == null || g.World == null) { cursorHold = 0f; cursorRaw = false; aimRaw = false; Aimed = false; return false; }
            var inp = FindInput();
            var hud = OpenZHud.I;
            if (!inp || !inp.Selection.Contains(o) || inp.Hover != null || (hud && hud.PointerOverUi)) { cursorHold = 0f; cursorRaw = false; aimRaw = false; Aimed = false; return false; }
            if ((cursorT -= dt) <= 0f)
            {
                cursorT = 0.1f;
                var cam = inp.cam ? inp.cam.GetComponent<Camera>() : Camera.main;
                Vector3 gp = default; bool hit = false;
                if (cam) hit = g.GroundPoint(cam.ScreenPointToRay(mouse.position.ReadValue()), out gp);
                cursorRaw = hit && OzShuttleOrderAim.AimBlocked(g.World, o, gp);
                aimRaw = hit && !cursorRaw;
            }
            cursorHold = cursorRaw ? cursorHold + dt : 0f;
            Aimed = aimRaw;
            return cursorHold >= 0.15f;
        }
        static PlayerInput input; static float inputLook = -9f;
        static PlayerInput FindInput()
        {
            if (!input && Time.unscaledTime - inputLook > 1f) { inputLook = Time.unscaledTime; input = Object.FindFirstObjectByType<PlayerInput>(); }
            return input;
        }

        static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        /// <summary>The fine slope at (x, z) (the surface normal's y from 0.15 m either side: run 27D), sampled once with the cell.</summary>
        float FineNy(float x, float z)
        {
            float e = 0.15f, hx0 = SurfaceAt(x - e, z, out _), hx1 = SurfaceAt(x + e, z, out _), hz0 = SurfaceAt(x, z - e, out _), hz1 = SurfaceAt(x, z + e, out _);
            float gx = (hx1 - hx0) / (2f * e), gz = (hz1 - hz0) / (2f * e);
            return 1f / Mathf.Sqrt(1f + gx * gx + gz * gz);
        }

        /// <summary>The lattice window from cell (nox, noz): the cells new to it sampled (all of them with refresh), then the slope, the
        /// plane and the alpha every point aims for (eased per frame in Step).</summary>
        void Layout(int nox, int noz, bool refresh, float yaw, float grow)
        {
            Rebuilds++; ox = nox; oz = noz; if (refresh) builtT = t;
            float c0x = cs.x, c0z = cs.y;
            var rot = Quaternion.Euler(0f, yaw, 0f); var fwd = rot * Vector3.forward; var right = rot * Vector3.right;
            // a fort under any part of the window? (its top is looked up per point only then; 5 x 5 samples 3 m apart first)
            fortNear = false;
            for (int j = 0; j < N && !fortNear; j += 6)
                for (int i = 0; i < N && !fortNear; i += 6)
                    if (FortGunView.FortTopAt(new Vector3((nox + i) * CellM, 0f, (noz + j) * CellM), FarAway) > float.MinValue) fortNear = true;
            // 1. the drawn surface under every lattice point (a cell kept while it stays under the mark)
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i, ix = nox + i, iz = noz + j, s = Mod(ix, N) + Mod(iz, N) * N;
                    float x = ix * CellM, z = iz * CellM;
                    if (refresh || ckx[s] != ix || ckz[s] != iz)
                    {
                        bool fresh = ckx[s] != ix || ckz[s] != iz;
                        ch[s] = SurfaceAt(x, z, out ck[s]); cf[s] = FineNy(x, z); ckx[s] = ix; ckz[s] = iz; CellsSampled++;
                        if (fresh) ca[s] = -1f;                                                             // a new cell takes its alpha at once (it comes in at the window's edge, outside the ellipse)
                    }
                    slotOf[k] = s; px[k] = x; pz[k] = z; hs[k] = ch[s]; kind[k] = ck[s];
                }
            // 2. the slope: the steeper of the two neighbours' steps along each lattice line (a point beside a drop counts as steep)
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i; float h0 = hs[k];
                    float ga = Mathf.Max(i > 0 ? Mathf.Abs(h0 - hs[k - 1]) : 0f, i < N - 1 ? Mathf.Abs(hs[k + 1] - h0) : 0f) / CellM;
                    float gb = Mathf.Max(j > 0 ? Mathf.Abs(h0 - hs[k - N]) : 0f, j < N - 1 ? Mathf.Abs(hs[k + N] - h0) : 0f) / CellM;
                    ny[k] = 1f / Mathf.Sqrt(1f + ga * ga + gb * gb);
                }
            // each point's place in the ellipse (1 = its rim at the radii), and whether it lies in the old 17 x 17 grid's square (|along|,
            // |across| <= Reach: DrawnVerts is counted there and given as a share of that grid's 289 points, as the older proofs read it)
            float ia = 1f / (AlongM * grow), ic = 1f / (AcrossM * grow); int inSq = 0;
            for (int k = 0; k < N * N; k++)
            {
                float dx = px[k] - c0x, dz = pz[k] - c0z, a = (dx * fwd.x + dz * fwd.z) * ia, c = (dx * right.x + dz * right.z) * ic;
                rr[k] = Mathf.Sqrt(a * a + c * c); sq[k] = Mathf.Abs(a) <= Reach && Mathf.Abs(c) <= Reach; if (sq[k]) inSq++;
            }
            float R(int k) => rr[k];
            // 3. the plane: seeded by the surface under the hull's middle when it is flat enough, else by the flat band most of the mark
            //    lies on (the water, the ravine floor, the lip); then fitted (least squares, twice) through the flat points within 0.4 m of
            //    it, so a gentle hillside keeps the whole mark while a bank, a wall or a roof edge stays out (tilt at most 30 degrees)
            int mi = Mathf.Clamp(Mathf.FloorToInt(c0x / CellM) - nox, 0, N - 1), mj = Mathf.Clamp(Mathf.FloorToInt(c0z / CellM) - noz, 0, N - 1);
            int mid = mj * N + mi; float seedH = hs[mid]; byte pk = kind[mid]; bool found = ny[mid] >= SlopeNy;
            if (!found)
            {
                float bestW = 0f;
                for (int k = 0; k < N * N; k++)
                {
                    if (((k / N) & 1) != 0 || ((k % N) & 1) != 0 || ny[k] < SlopeNy) continue;                 // every second point is a candidate
                    float r = R(k); if (r > 1f) continue;
                    float w = 0f, sum = 0f;
                    for (int q = 0; q < N * N; q++)
                    {
                        if (ny[q] < SlopeNy || Mathf.Abs(hs[q] - hs[k]) > PlaneSoftM) continue;
                        float rq = R(q); if (rq > 1f) continue;
                        float wq = 1.25f - rq; w += wq; sum += wq * hs[q];
                    }
                    if (w > bestW) { bestW = w; seedH = sum / w; pk = kind[k]; found = true; }
                }
            }
            float pa = 0f, pb = 0f, pc = seedH;                                                              // plane: h = pa (x - c.x) + pb (z - c.z) + pc
            if (found)
                for (int it = 0; it < 2; it++)
                {
                    double sw = 0, sx = 0, sz = 0, sh = 0, sxx = 0, szz = 0, sxz = 0, sxh = 0, szh = 0;
                    for (int q = 0; q < N * N; q++)
                    {
                        float m = R(q); if (ny[q] < SlopeNy || m > 1.1f) continue;
                        float x = px[q] - c0x, z = pz[q] - c0z;
                        if (Mathf.Abs(hs[q] - (pa * x + pb * z + pc)) > PlaneSoftM) continue;
                        double w = 1.25 - m;
                        sw += w; sx += w * x; sz += w * z; sh += w * hs[q]; sxx += w * x * x; szz += w * z * z; sxz += w * x * z; sxh += w * x * hs[q]; szh += w * z * hs[q];
                    }
                    if (sw <= 0) break;
                    double det = sxx * (szz * sw - sz * sz) - sxz * (sxz * sw - sz * sx) + sx * (sxz * sz - szz * sx);
                    if (System.Math.Abs(det) < 1e-6) { pa = pb = 0f; pc = (float)(sh / sw); continue; }
                    double a1 = (sxh * (szz * sw - sz * sz) - sxz * (szh * sw - sz * sh) + sx * (szh * sz - szz * sh)) / det;
                    double b1 = (sxx * (szh * sw - sz * sh) - sxh * (sxz * sw - sz * sx) + sx * (sxz * sh - szh * sx)) / det;
                    double c1 = (sxx * (szz * sh - sz * szh) - sxz * (sxz * sh - sx * szh) + sxh * (sxz * sz - szz * sx)) / det;
                    if (a1 * a1 + b1 * b1 > 0.3333) { pa = pb = 0f; pc = (float)(sh / sw); }                    // steeper than 30 degrees: level
                    else { pa = (float)a1; pb = (float)b1; pc = (float)c1; }
                }
            PlaneY = pc; PlaneKind = found ? KindName[pk] : "none";
            int qWant = found && pk == KWater ? 2995 : -1;                                                   // over open water: after the water
            if (mat.renderQueue != (qWant < 0 ? mat.shader.renderQueue : qWant)) mat.renderQueue = qWant;
            // 4. the alpha each point aims for: within the plane's band and not steep, and (run 27D) not steep on the fine slope either (0.15 m
            //    round the point, kept with the cell: a point at the foot of a bank, a wall or a roof edge fades out: none over 50 degrees, full
            //    under 40); the fine wall count (a visible point on a slope over 50 degrees)
            int walls = 0, drawn = 0;
            for (int k = 0; k < N * N; k++)
            {
                float dev = Mathf.Abs(hs[k] - (pa * (px[k] - c0x) + pb * (pz[k] - c0z) + pc));
                float a = found ? (1f - Mathf.InverseLerp(PlaneSoftM, PlaneBandM, dev)) * Mathf.InverseLerp(SlopeNy, SlopeSoftNy, ny[k]) : 0f;
                float nyFine = cf[slotOf[k]];
                if (a > 0.05f) a *= Mathf.InverseLerp(0.643f, SlopeNy, nyFine);
                verts[k] = new Vector3(px[k], hs[k] + 0.03f, pz[k]); aT[k] = a;
                if (a <= 0.05f || !sq[k]) continue;
                drawn++;
                if (dev > PlaneBandM) { walls++; continue; }
                if (!ProofWalls) continue;
                if (nyFine < 0.643f) walls++;                                                                    // steeper than 50 degrees
            }
            WallVerts = walls; if (walls > WallVertsMax) WallVertsMax = walls; DrawnVerts = inSq > 0 ? Mathf.RoundToInt(289f * drawn / inSq) : 0;
            mesh.vertices = verts; mesh.RecalculateBounds();
        }
    }
}
