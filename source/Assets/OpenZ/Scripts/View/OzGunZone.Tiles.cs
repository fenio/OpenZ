using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The gun zone's tiles look: the area where a stored gun may stand (GunCover, the placement rule asked spot by spot, as before)
    /// is drawn as the game's real gun cells (GunCells: one cell = one 2 x 2 tile spot the rule allows, the gun's own footprint;
    /// the faint "+" in a cell is where the gun's centre stands when it is put there), under a soft deep-green tint, the cells a
    /// shade lighter than the darker olive channels between them, with thin faint cell borders whose corners are cut at 45 degrees
    /// with rounded joins, a barely visible "+" where cells meet (a gun may stand centred there too), a thin zone outline with
    /// L-shaped brackets on its outer corners (and a second, inner L on the corner cell's own corner), and thin frames round
    /// objects inside the zone. The area's edge is eased (a one-tile stair becomes a soft step, straight edges stay where they
    /// are) and the outline and the brackets fade out next to the objects the placement rule tests (trees, rocks, huts,
    /// buildings, guns), so they never run over trunks and roots. One mesh laid on the DRAWN ground (the ground's
    /// half-metre height lattice that the track marks and the crater patches use, TrackSurface.RenderY, plus the crater bowls,
    /// CraterField.DentRender), folded exactly as that lattice is folded, lifted <see cref="tileLift"/>, refined to
    /// <see cref="craterStep"/> where a crater bowl lies; all marks are drawn per pixel by the shader OpenZ/GunZoneTiles from three small tile textures,
    /// so every line lies on slopes, hills and in craters. It is rebuilt when the rule's answer changes (as before) and when a
    /// crater appears, eases in or goes inside the zone; the objects' texture is filled again when those objects change. A slow
    /// sheen runs over the lines and the cells come in from the building (both off in calm mode). Without the shader or the
    /// drawn heights the plain look (wash and outline) is drawn as before.
    /// View only: the placement rule and the snapping of the gun are untouched.
    /// </summary>
    public partial class OzGunZone
    {
        [Header("Tiles look")]
        [Tooltip("the gun cells look (off = the plain wash and outline)")] public bool tilesLook = true;
        [Tooltip("tint inside a cell (sRGB)")] public Color tileFill = new Color(18f / 255f, 58f / 255f, 48f / 255f, 1f);
        [Tooltip("tint of the channels between the cells and of area in no cell (sRGB): a darker olive")] public Color tileGap = new Color(23f / 255f, 55f / 255f, 19f / 255f, 1f);
        [Tooltip("opacity of the tint in the channels")] [Range(0f, 1f)] public float tileFillAlpha = 0.33f;
        [Tooltip("inside a cell the tint's opacity is this much of the channels': the cells read a shade lighter than the channels between them")] [Range(0f, 1f)] public float cellFillShare = 0.72f;
        [Tooltip("colour of all lines and marks (sRGB)")] public Color tileLine = new Color(215f / 255f, 230f / 255f, 199f / 255f, 1f);
        [Tooltip("ground sizes (m): cell border inset from the cell edge (half the gap between two cells), its 45 degree corner cut, the cell's '+' arm, the joint '+' arm")]
        public Vector4 cellGeometry = new Vector4(0.23f, 0.50f, 0.20f, 0.16f);
        [Tooltip("ground sizes (m): the outline's distance outside the area's edge, the bracket arm (along each edge), the bracket's corner cut")]
        public Vector3 outlineGeometry = new Vector3(0.08f, 2.3f, 0.8f);
        [Tooltip("ground size (m): the inner bracket's arm on the corner cell's border (from the area's corner)")] public float innerBracketArm = 1.4f;
        [Tooltip("ground sizes (m): the joins of the cut corners rounded over this much (smooth maximum): the cells' corners, the brackets' corners")]
        public Vector2 cornerRound = new Vector2(0.10f, 0.10f);
        [Tooltip("ground size (m): the area's edge eased through a smooth box of this half width (straight edges stay, a one-tile stair becomes a soft step; at most 2)")]
        [Range(0.05f, 2f)] public float areaEase = 0.75f;
        [Tooltip("next to the objects the placement rule tests (trees, rocks, huts, buildings, guns): reach (half width, m, at most 2), how much the outline and the brackets fade (0..1), the object share where the fade starts and where it is full")]
        public Vector4 objectFade = new Vector4(1.1f, 0.92f, 0.05f, 0.40f);
        [Tooltip("widths in pixels on a screen 1080 high (scaled with the screen): cell border, cell '+', joint '+', outline")]
        public Vector4 lineWidths = new Vector4(1.2f, 1.0f, 1.0f, 1.5f);
        [Tooltip("widths in pixels at 1080: bracket, frame round an object, the step from the cell's shade to the channel's")] public Vector3 lineWidths2 = new Vector3(2.3f, 1.1f, 1.6f);
        [Tooltip("opacity: cell border, cell '+' (each stroke: brighter where they cross), joint '+' (each stroke), outline")] public Vector4 lineAlphas = new Vector4(0.10f, 0.13f, 0.06f, 0.24f);
        [Tooltip("opacity: bracket, frame round an object")] public Vector2 lineAlphas2 = new Vector2(0.70f, 0.10f);
        [Tooltip("sheen over the lines: strength, period (m), speed (m/s); off in calm mode")] public Vector3 sheen = new Vector3(0.10f, 26f, 4f);
        [Tooltip("the cells come in from the building at this speed (m/s; 0 = at once); off in calm mode")] public float sweepSpeed = 90f;
        [Tooltip("by night: line brightness, tint opacity (x the day values); a cell smaller than this many pixels fades its marks")] public Vector3 nightAndFar = new Vector3(0.45f, 0.6f, 26f);
        [Tooltip("above the drawn ground (m): only a hair, the shader's depth offset does the rest")] public float tileLift = 0.04f;
        [Tooltip("node spacing (m) on a tile a crater bowl touches (else half a metre, the drawn ground's own lattice)")] public float craterStep = 0.125f;

        /// <summary>The cells of the zone shown last (or audited).</summary>
        public GunCells Cells => cells;
        /// <summary>The tiles look draws (its shader is there and it is on).</summary>
        public bool TilesActive => TilesOn;
        /// <summary>Last build: vertices, triangles, tiles of area, tiles of the outline's strip, tiles refined round a crater, whether
        /// the heights came from the drawn ground.</summary>
        public int TileVertices => tv.Count;
        public int TileTriangles => ti.Count / 3;
        public int TileAreaTiles { get; private set; }
        public int TileStripTiles { get; private set; }
        public int TileCraterTiles { get; private set; }
        public bool TileDrawnHeights { get; private set; }

        const int MaxBrackets = 16;
        static readonly int CellsTexId = Shader.PropertyToID("_Cells"), CornersTexId = Shader.PropertyToID("_Corners"), ObjectsTexId = Shader.PropertyToID("_Objects"),
            ObjFadeId = Shader.PropertyToID("_ObjFade"), ZoneId = Shader.PropertyToID("_Zone"),
            FillId = Shader.PropertyToID("_FillColor"), GapId = Shader.PropertyToID("_GapColor"), Geo3Id = Shader.PropertyToID("_Geo3"), LineId = Shader.PropertyToID("_LineColor"), HostId = Shader.PropertyToID("_Host"), GeoId = Shader.PropertyToID("_Geo"),
            Geo2Id = Shader.PropertyToID("_Geo2"), WidthId = Shader.PropertyToID("_Width"), Width2Id = Shader.PropertyToID("_Width2"), Alpha1Id = Shader.PropertyToID("_Alpha1"),
            Alpha2Id = Shader.PropertyToID("_Alpha2"), FxId = Shader.PropertyToID("_Fx"), NightId2 = Shader.PropertyToID("_Night"), BracketsId = Shader.PropertyToID("_Brackets"),
            BracketCountId = Shader.PropertyToID("_BracketCount"), ZTestId = Shader.PropertyToID("_ZTest"), CraterArrayId = Shader.PropertyToID("_OzCraters"),
            CraterCountId = Shader.PropertyToID("_OzCraterCount");
        const string TilesShader = "OpenZ/GunZoneTiles", TilesMaterial = "OpenZ/GunZone/OzGunZoneTiles";

        readonly GunCells cells = new GunCells();
        Material tilesMat; Mesh tilesMesh; Texture2D cellsTex, cornersTex, objectsTex; bool tilesFailed, tilesBuilt;
        int cellsHash = int.MinValue, cellsZone = -2, craterSig, bracketCount, objectsSig = int.MinValue, objectsZone = -2; float shownAt;
        bool[] spots = new bool[0];
        readonly Dictionary<int, int> zonePhase = new Dictionary<int, int>();   // the lattice phase each zone was shown with: kept, so the grid never jumps a tile
        readonly Vector4[] bracketData = new Vector4[MaxBrackets];
        readonly List<Vector3> tv = new List<Vector3>(); readonly List<int> ti = new List<int>(); readonly List<float> tg = new List<float>();
        readonly List<Vector4> craterGlob = new List<Vector4>();
        readonly List<bool> tflat = new List<bool>();
        float[] nodeH = new float[0]; bool[] nodeFlat = new bool[0];

        bool TilesOn => tilesLook && !tilesFailed && TilesMat() != null;

        Material TilesMat()
        {
            if (tilesMat || tilesFailed) return tilesMat;
            var src = Resources.Load<Material>(TilesMaterial);                       // the Resources material keeps the shader in builds
            if (src && src.shader && src.shader.isSupported) tilesMat = new Material(src);
            else
            {
                var sh = Shader.Find(TilesShader);
                if (sh && sh.isSupported) tilesMat = new Material(sh);
            }
            if (!tilesMat) { tilesFailed = true; Debug.LogWarning("[OpenZ] gun zone: shader " + TilesShader + " missing or not supported here: the plain zone look is drawn"); return null; }
            tilesMat.name = "OZ_GunZoneTiles";
            tilesMat.SetFloat(ZTestId, (float)CompareFunction.LessEqual);              // on the ground, under units and buildings
            tilesMat.renderQueue = (int)RenderQueue.Transparent + 49;                  // one step before the other ground marks (the plain zone's place)
            return tilesMat;
        }

        void TilesDestroy()
        {
            if (tilesMesh) Destroy(tilesMesh);
            if (tilesMat) Destroy(tilesMat);
            if (cellsTex) Destroy(cellsTex);
            if (cornersTex) Destroy(cornersTex);
            if (objectsTex) Destroy(objectsTex);
        }

        /// <summary>LateUpdate: must the built mesh be rebuilt for a reason besides the rule's answer? The plain look: the line width
        /// changed with the zoom. The tiles look: a crater appeared, eased or went inside the zone (its lines keep their width by
        /// themselves). And the tiles look's objects texture is filled again when the objects the rule tests changed (the object
        /// signature, lastSig, moved on) but the rule's answer did not (an object round the area, not in it).</summary>
        bool TilesRebuild(float lw)
        {
            if (!TilesOn) return Mathf.Abs(lw - builtW) > 0.06f * builtW;
            if (tilesBuilt && (lastSig != objectsSig || curZone != objectsZone)) BuildObjects();
            return CraterSig() != craterSig;
        }

        /// <summary>A signature of the craters that touch the zone (centre, radius, ease), read from the globals CraterField sets.</summary>
        int CraterSig()
        {
            if (cover == null || CraterField.ActiveCount == 0) return 0;
            int n = Mathf.RoundToInt(Shader.GetGlobalFloat(CraterCountId));
            if (n <= 0) return 0;
            Shader.GetGlobalVectorArray(CraterArrayId, craterGlob);
            float x0 = cover.zx * 2f, x1 = (cover.zx + cover.zw) * 2f, z1 = -cover.zy * 2f, z0 = -(cover.zy + cover.zh) * 2f;
            int s = 23;
            for (int k = 0; k < n && k < craterGlob.Count; k++)
            {
                var c = craterGlob[k]; float r = c.z + 0.1f;
                if (c.x + r < x0 || c.x - r > x1 || c.y + r < z0 || c.y - r > z1) continue;
                s = s * 31 + Mathf.RoundToInt(c.x * 20f); s = s * 31 + Mathf.RoundToInt(c.y * 20f); s = s * 31 + Mathf.RoundToInt(c.z * 20f); s = s * 31 + Mathf.RoundToInt(c.w * 64f);
            }
            return s;
        }

        // ------------------------------------------------------------------ build
        /// <summary>Build hook: the tiles look's mesh, cells and textures (false = the plain look builds).</summary>
        bool BuildTiles(float w)
        {
            if (!TilesOn) return false;
            builtW = w; dirty = false; built = true; Rebuilds++; HighTiles = 0;
            vv.Clear(); cc.Clear(); tt.Clear();                                          // the plain look's lists stay empty
            if (alpha <= 0f) shownAt = Time.unscaledTime;
            bool cellsNew = cover.hash != cellsHash || curZone != cellsZone;
            if (cellsNew) { BuildCells(); cellsHash = cover.hash; cellsZone = curZone; }
            if (cellsNew || lastSig != objectsSig || curZone != objectsZone) BuildObjects();
            craterSig = CraterSig();
            BuildTileMesh();
            BuildPads();
            tilesBuilt = true;
            return true;
        }

        static bool OutlineGround(ZClass c) => c == ZClass.Ground || c == ZClass.Road;

        void BuildCells()
        {
            int zw = cover.zw, zh = cover.zh, n = zw * zh;
            if (spots.Length != n) spots = new bool[n];
            for (int j = 0; j < zh; j++)
                for (int i = 0; i < zw; i++)
                    spots[j * zw + i] = Cov(i, j) && Cov(i + 1, j) && Cov(i, j + 1) && Cov(i + 1, j + 1);   // an allowed ground spot = its four tiles marked (GunCover)
            int prefX = cur != null ? ((cur.LocX >> 4) - cover.zx) & 1 : 1, prefY = cur != null ? ((cur.LocY >> 4) - cover.zy) & 1 : 1;
            cells.Build(zw, zh, spots, cover.tile, prefX, prefY, true, zonePhase.TryGetValue(curZone, out int kept) ? kept : -1);
            zonePhase[curZone] = cells.Phase;
            var map = game.World.Map;
            int zx = cover.zx, zy = cover.zy;
            cells.Encode((i, j) => OutlineGround(map.ClassAt(zx + i, zy + j)), outlineGeometry.x, MaxBrackets);
            if (!cellsTex || cellsTex.width != zw || cellsTex.height != zh)
            {
                if (cellsTex) Destroy(cellsTex);
                cellsTex = new Texture2D(zw, zh, TextureFormat.RGBA32, false, true) { name = "OZ_GunZoneCells", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            if (!cornersTex || cornersTex.width != zw + 1 || cornersTex.height != zh + 1)
            {
                if (cornersTex) Destroy(cornersTex);
                cornersTex = new Texture2D(zw + 1, zh + 1, TextureFormat.RGBA32, false, true) { name = "OZ_GunZoneCorners", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            cellsTex.SetPixelData(cells.cellTexels, 0); cellsTex.Apply(false, false);
            cornersTex.SetPixelData(cells.cornerTexels, 0); cornersTex.Apply(false, false);
            bracketCount = cells.bracketData.Length / 4;
            for (int q = 0; q < MaxBrackets; q++)
                bracketData[q] = q < bracketCount ? new Vector4(cells.bracketData[q * 4], cells.bracketData[q * 4 + 1], cells.bracketData[q * 4 + 2], cells.bracketData[q * 4 + 3]) : Vector4.zero;
        }

        /// <summary>The objects the placement rule tests, per tile on and round the zone (GunCells.EncodeObjects), into the shader's
        /// third texture: the outline and the brackets fade next to them. Filled after the cells and whenever those objects changed.</summary>
        void BuildObjects()
        {
            int zw = cover.zw, zh = cover.zh;
            cells.EncodeObjects(game.World.Objects, cover.zx, cover.zy);
            if (!objectsTex || objectsTex.width != zw || objectsTex.height != zh)
            {
                if (objectsTex) Destroy(objectsTex);
                objectsTex = new Texture2D(zw, zh, TextureFormat.RGBA32, false, true) { name = "OZ_GunZoneObjects", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            objectsTex.SetPixelData(cells.objectTexels, 0); objectsTex.Apply(false, false);
            objectsSig = lastSig; objectsZone = curZone;
        }

        /// <summary>The drawn ground at (x, z): the ground's height lattice (the one track marks and crater patches sit on) + the
        /// crater bowls; false = no drawn height there (then the walk surface, GroundY).</summary>
        bool DrawnY(float x, float z, out float y)
        {
            var s = TrackMarks.I ? TrackMarks.I.Surface : null;
            if (s != null && s.RenderHeights && s.RenderY(x, z, out y)) { y += CraterField.DentRender(x, z); return true; }
            y = game.GroundY(x, z);
            return false;
        }

        bool CraterNear(float x0, float x1, float z0, float z1)
        {
            if (CraterField.ActiveCount == 0) return false;
            int n = Mathf.RoundToInt(Shader.GetGlobalFloat(CraterCountId));
            if (n <= 0) return false;
            if (craterGlob.Count < n) Shader.GetGlobalVectorArray(CraterArrayId, craterGlob);
            for (int k = 0; k < n && k < craterGlob.Count; k++)
            {
                var c = craterGlob[k]; float r = c.z / Mathf.Cos(Mathf.PI / 64f) + 0.1f;   // the bowl's full radius (the global holds the clip circle)
                float dx = Mathf.Max(0f, Mathf.Max(x0 - c.x, c.x - x1)), dz = Mathf.Max(0f, Mathf.Max(z0 - c.y, c.y - z1));
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        void BuildTileMesh()
        {
            tv.Clear(); ti.Clear(); tg.Clear(); tflat.Clear();
            TileAreaTiles = TileStripTiles = TileCraterTiles = 0; TileDrawnHeights = true;
            int zw = cover.zw, zh = cover.zh;
            if (CraterField.ActiveCount > 0) Shader.GetGlobalVectorArray(CraterArrayId, craterGlob); else craterGlob.Clear();
            var map = game.World.Map;
            for (int j = 0; j < zh; j++)
                for (int i = 0; i < zw; i++)
                {
                    bool area = Cov(i, j);
                    if (!area)
                    {   // the outer strip: a tile next to the area whose ground may carry the outline, an object's tile inside the area
                        // too (the eased edge rounds the tint into its corners; the shader draws no outline there)
                        if (!OutlineGround(map.ClassAt(cover.zx + i, cover.zy + j))) continue;
                        bool next = false;
                        for (int dj = -1; dj <= 1 && !next; dj++) for (int di = -1; di <= 1 && !next; di++) if (Cov(i + di, j + dj)) next = true;
                        if (!next) continue;
                        TileStripTiles++;
                    }
                    else TileAreaTiles++;
                    float ox = (cover.zx + i) * 2f, oz = -(cover.zy + j) * 2f;              // the tile's north-west corner (world m)
                    bool fine = CraterNear(ox, ox + 2f, oz - 2f, oz);
                    if (fine) TileCraterTiles++;
                    int per = fine ? Mathf.Clamp(Mathf.RoundToInt(2f / Mathf.Max(0.0625f, craterStep)), 4, 32) & ~3 : 4;   // quads per tile side (a multiple of 4: every half-metre cell splits evenly)
                    float step = 2f / per; int nn = per + 1;
                    if (nodeH.Length < nn * nn) { nodeH = new float[nn * nn]; nodeFlat = new bool[nn * nn]; }
                    bool drawnAll = true;
                    for (int b = 0; b < nn; b++)
                        for (int a = 0; a < nn; a++)
                        {
                            bool ok = DrawnY(ox + a * step, oz - b * step, out float h);
                            nodeH[b * nn + a] = h; nodeFlat[b * nn + a] = false;
                            if (!ok) drawnAll = false;
                        }
                    if (!drawnAll) TileDrawnHeights = false;
                    // a sample far off the tile's own level is a cliff face over the tile: flattened to the tile (as the plain look)
                    int mid = (nn / 2) * nn + nn / 2;
                    float hc = nodeH[mid];
                    if (hc > floorMax)
                    {
                        for (int q = 0; q < nn * nn; q++) hc = Mathf.Min(hc, nodeH[q]);
                        if (hc > floorMax && area) HighTiles++;
                    }
                    for (int q = 0; q < nn * nn; q++) if (Mathf.Abs(nodeH[q] - hc) > maxStep) { nodeH[q] = hc; nodeFlat[q] = true; }
                    float lift = drawnAll ? tileLift : fillLift;                            // on the walk surface (no drawn heights) as the plain look
                    int v0 = tv.Count;
                    for (int q = 0; q < nn * nn; q++)
                    {
                        tv.Add(new Vector3(ox + (q % nn) * step, nodeH[q] + lift, oz - (q / nn) * step));
                        tg.Add(nodeH[q]); tflat.Add(nodeFlat[q]);
                    }
                    for (int b = 0; b + 1 < nn; b++)
                        for (int a = 0; a + 1 < nn; a++)
                        {
                            int nw = v0 + b * nn + a, ne = nw + 1, sw = nw + nn, se = sw + 1;
                            // the fold of the half-metre cell this quad lies in, as TrackSurface.RenderY folds it (south-west node to the
                            // north-east one when the half-metre indices of the cell's south-west corner add up even)
                            float xs = ox + Mathf.Floor(a * step / 0.5f + 1e-3f) * 0.5f, zs = oz - Mathf.Floor(b * step / 0.5f + 1e-3f) * 0.5f - 0.5f;
                            bool swne = ((Mathf.RoundToInt(xs * 2f) + Mathf.RoundToInt(zs * 2f)) & 1) == 0;
                            if (swne) { ti.Add(nw); ti.Add(ne); ti.Add(sw); ti.Add(ne); ti.Add(se); ti.Add(sw); }
                            else { ti.Add(nw); ti.Add(ne); ti.Add(se); ti.Add(nw); ti.Add(se); ti.Add(sw); }
                        }
                }
            if (!tilesMesh) { tilesMesh = new Mesh { name = "OZ_GunZoneTiles", indexFormat = IndexFormat.UInt32 }; tilesMesh.MarkDynamic(); }
            tilesMesh.Clear();
            tilesMesh.SetVertices(tv); tilesMesh.SetTriangles(ti, 0);
            tilesMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ draw
        /// <summary>Draw hook: the tiles look (false = the plain look draws).</summary>
        bool DrawTiles()
        {
            if (!TilesOn) return false;
            if (!tilesBuilt || !tilesMesh || cover == null) return true;
            var m = tilesMat;
            bool calm = OzMotion.Reduced;
            float ps = Mathf.Max(0.5f, Screen.height / 1080f);
            m.SetTexture(CellsTexId, cellsTex); m.SetTexture(CornersTexId, cornersTex); m.SetTexture(ObjectsTexId, objectsTex);
            m.SetVector(ZoneId, new Vector4(cover.zx * 2f, -cover.zy * 2f, cover.zw, cover.zh));
            m.SetColor(FillId, tileFill); m.SetColor(GapId, tileGap); m.SetColor(LineId, tileLine);
            Vector2 host = cur != null ? new Vector2((float)(cur.LocX + cur.WidthPix * 0.5) / 8f - cover.zx * 2f, (float)(cur.LocY + cur.HeightPix * 0.5) / 8f - cover.zy * 2f) : Vector2.zero;
            m.SetVector(HostId, new Vector4(host.x, host.y, Time.unscaledTime - shownAt, calm ? 0f : sweepSpeed));
            m.SetVector(GeoId, cellGeometry);
            m.SetVector(Geo2Id, new Vector4(outlineGeometry.x, outlineGeometry.y, outlineGeometry.z, ps));
            m.SetVector(Geo3Id, new Vector4(innerBracketArm, cornerRound.x, cornerRound.y, Mathf.Clamp(areaEase, 0.05f, 2f)));
            m.SetVector(WidthId, lineWidths);
            m.SetVector(Width2Id, new Vector4(lineWidths2.x, lineWidths2.z, lineWidths2.y, cellFillShare));
            m.SetVector(Alpha1Id, new Vector4(tileFillAlpha, lineAlphas.x, lineAlphas.y, lineAlphas.z));
            m.SetVector(Alpha2Id, new Vector4(lineAlphas.w, lineAlphas2.x, lineAlphas2.y, 0f));
            m.SetVector(ObjFadeId, new Vector4(Mathf.Clamp(objectFade.x, 0.05f, 2f), objectFade.y, objectFade.z, objectFade.w));
            m.SetVector(FxId, new Vector4(alpha, calm ? 0f : sheen.x, Mathf.Max(1f, sheen.y), sheen.z));
            m.SetVector(NightId2, new Vector4(1f, nightAndFar.x, nightAndFar.y, nightAndFar.z));
            m.SetVectorArray(BracketsId, bracketData); m.SetFloat(BracketCountId, bracketCount);
            Graphics.DrawMesh(tilesMesh, Matrix4x4.identity, m, 0, null, 0, null, ShadowCastingMode.Off, false);
            var pm = Mat();
            if (pm && padPoses.Count > 0 && padMesh)
            {   // a free fort tower pad: the ring in the line colour (its own soft dark edges keep it readable on the pale roof)
                pm.SetColor("_Color", new Color(tileLine.r, tileLine.g, tileLine.b, alpha * 0.9f));
                pm.SetFloat("_Scan", 0f);
                for (int i = 0; i < padPoses.Count; i++) Graphics.DrawMesh(padMesh, padPoses[i], pm, 0, null, 0, null, ShadowCastingMode.Off, false);
            }
            return true;
        }

        // ------------------------------------------------------------------ audit
        /// <summary>Audit hook (Play mode, after the zone was built): the cells against the rule, the mesh against the drawn ground
        /// at every vertex and at every edge middle and triangle centre (wanted 2 to 6 cm above it, never under it), the shader.
        /// Appends one line; returns the violations.</summary>
        int TilesAudit(StringBuilder sb, ZWorld w, ZObject b, GunCover c)
        {
            if (!tilesLook) { sb.Append("NOTE tiles look switched off on the component: the plain look is drawn\n"); return 0; }
            if (!TilesOn) { sb.Append("FAIL tiles look: shader ").Append(TilesShader).Append(" not found or not supported: the plain look is drawn\n"); return 1; }
            int cellBad = cells.Verify();
            int objBad = cells.VerifyObjects(w.Objects, c.zx, c.zy);                    // the objects texture against the objects the rule tests
            foreach (int k in cells.cells) if (!w.CannonPlacable(b, c.zx + k % c.zw, c.zy + k / c.zw)) cellBad++;
            foreach (int k in cells.joints) if (!w.CannonPlacable(b, c.zx + k % (c.zw + 1) - 1, c.zy + k / (c.zw + 1) - 1)) cellBad++;
            float lo = float.MaxValue, hi = float.MinValue, lo2 = float.MaxValue, hi2 = float.MinValue; int flat = 0, samples = 0;
            for (int i = 0; i < tv.Count; i++)
            {
                if (tflat[i]) { flat++; continue; }
                DrawnY(tv[i].x, tv[i].z, out float y);
                float gap = tv[i].y - y; lo = Mathf.Min(lo, gap); hi = Mathf.Max(hi, gap);
            }
            for (int q = 0; q + 2 < ti.Count; q += 3)
            {
                int a = ti[q], bb = ti[q + 1], cq = ti[q + 2];
                if (tflat[a] || tflat[bb] || tflat[cq]) continue;
                Vector3 pa = tv[a], pb = tv[bb], pc = tv[cq];
                for (int s = 0; s < 4; s++)
                {
                    Vector3 p = s == 0 ? (pa + pb) * 0.5f : s == 1 ? (pb + pc) * 0.5f : s == 2 ? (pa + pc) * 0.5f : (pa + pb + pc) / 3f;
                    DrawnY(p.x, p.z, out float y);
                    float gap = p.y - y; lo2 = Mathf.Min(lo2, gap); hi2 = Mathf.Max(hi2, gap); samples++;
                }
            }
            int empty = tv.Count == 0 ? 1 : 0;
            if (empty == 1) lo = hi = lo2 = hi2 = 0f;
            float minOk = TileDrawnHeights ? 0.02f : 0f, maxOk = TileDrawnHeights ? 0.06f : 0.6f;
            int gapBad = (lo < minOk - 1e-4f || hi > maxOk + 1e-4f ? 1 : 0) + (lo2 < minOk - 1e-4f || hi2 > maxOk + 1e-4f ? 1 : 0);
            int bad = cellBad + gapBad + empty + objBad;
            sb.Append(bad == 0 ? "OK   " : "FAIL ").Append("tiles look zone ").Append(b.ZoneIndex).Append(": cells ").Append(cells.cells.Count).Append(" (phase ").Append(cells.phaseX).Append(',').Append(cells.phaseY).Append(", lattice ").Append(cells.lattice)
              .Append(", one tile off ").Append(cells.shifted).Append(", area tiles in no cell ").Append(cells.loose).Append("), joints ").Append(cells.joints.Count).Append(", brackets ").Append(bracketCount)
              .Append("; cells or joints the rule refuses or overlapping ").Append(cellBad)
              .Append("; object tiles on and round the zone ").Append(cells.objectTiles).Append(" (outline and brackets fade next to them; bits wrong against the objects the rule tests ").Append(objBad).Append(')')
              .Append("; mesh ").Append(tv.Count).Append(" vertices, ").Append(ti.Count / 3).Append(" triangles over ").Append(TileAreaTiles).Append(" area + ").Append(TileStripTiles).Append(" outline strip tiles (")
              .Append(TileCraterTiles).Append(" refined round craters); heights ").Append(TileDrawnHeights ? "of the drawn ground (TrackSurface lattice + crater bowls)" : "partly of the walk surface (no drawn heights there)")
              .Append("; above the drawn ground at every vertex ").Append((lo * 100f).ToString("0.00")).Append(" .. ").Append((hi * 100f).ToString("0.00"))
              .Append(" cm, at every edge middle and triangle centre (").Append(samples).Append(") ").Append((lo2 * 100f).ToString("0.00")).Append(" .. ").Append((hi2 * 100f).ToString("0.00"))
              .Append(" cm (wanted ").Append((minOk * 100f).ToString("0")).Append(" .. ").Append((maxOk * 100f).ToString("0")).Append(" cm); flattened under an overhang ").Append(flat)
              .Append("; craters in the zone ").Append(CraterSig() != 0 ? "yes" : "none").Append("; shader ").Append(TilesShader).Append(" OK\n");
            return bad;
        }
    }
}
