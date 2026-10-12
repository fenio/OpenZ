using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// Where a stored gun may stand, for one building: asked spot by spot from the placement rule itself (ZWorld.CannonPlacable),
    /// never from a copy of it. tile = the zone's tiles that lie under at least one allowed ground spot; pads = the allowed fort
    /// tower spots. A 2x2 ground spot is allowed exactly when its four tiles are all marked (every tile of an allowed ground spot
    /// is free of the zone edge, of blocking objects and of bad ground, and a spot made of four such tiles is allowed), so the
    /// picture drawn from this and the rule cannot differ. Plain data, no scene needed (the counted test runs it headless).
    /// </summary>
    public sealed class GunCover
    {
        /// <summary>The building's zone in tiles.</summary>
        public int zx, zy, zw, zh;
        public bool[] tile;
        public readonly List<Vector2Int> pads = new List<Vector2Int>();
        readonly List<ZObject> forts = new List<ZObject>();
        public int validSpots, hash;

        public bool Tile(int tx, int ty)
        {
            int i = tx - zx, j = ty - zy;
            return i >= 0 && j >= 0 && i < zw && j < zh && tile[j * zw + i];
        }

        /// <summary>What the picture says about the 2x2 spot with its top-left tile at (tx, ty).</summary>
        public bool Marked(int tx, int ty)
        {
            for (int k = 0; k < pads.Count; k++) if (pads[k].x == tx && pads[k].y == ty) return true;
            return Tile(tx, ty) && Tile(tx + 1, ty) && Tile(tx, ty + 1) && Tile(tx + 1, ty + 1);
        }

        public static GunCover Compute(ZWorld w, ZObject b, GunCover into = null)
        {
            var z = w != null ? w.ZoneOf(b) : null;
            if (z == null) return null;
            var c = into ?? new GunCover();
            c.zx = z.X >> 4; c.zy = z.Y >> 4; c.zw = z.W >> 4; c.zh = z.H >> 4;
            int n = c.zw * c.zh;
            if (c.tile == null || c.tile.Length != n) c.tile = new bool[n]; else System.Array.Clear(c.tile, 0, n);
            c.pads.Clear(); c.validSpots = 0;
            c.forts.Clear();
            foreach (var o in w.Objects) if (o.IsFort) c.forts.Add(o);
            for (int ty = c.zy; ty < c.zy + c.zh; ty++)
                for (int tx = c.zx; tx < c.zx + c.zw; tx++)
                {
                    if (!w.CannonPlacable(b, tx, ty)) continue;
                    c.validSpots++;
                    // a fort tower spot? Only a spot at a fort's four tower offsets can be one; the rule's own test then decides
                    bool pad = false;
                    for (int f = 0; f < c.forts.Count && !pad; f++)
                        if (TowerMounts.IsTurretSpot(c.forts[f].LocX, c.forts[f].LocY, tx * 16, ty * 16)) pad = w.GunSpotOnFortPad(tx, ty);
                    if (pad) { c.pads.Add(new Vector2Int(tx, ty)); continue; }
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++)
                        {
                            int lx = tx + i - c.zx, ly = ty + j - c.zy;
                            if (lx >= 0 && ly >= 0 && lx < c.zw && ly < c.zh) c.tile[ly * c.zw + lx] = true;
                        }
                }
            int h = 17;
            for (int k = 0; k < n; k++) if (c.tile[k]) h = h * 31 + k;
            for (int k = 0; k < c.pads.Count; k++) h = h * 31 + c.pads[k].x * 4099 + c.pads[k].y;
            c.hash = h;
            return c;
        }
    }

    /// <summary>
    /// While a stored gun is being placed (PlayerInput.Placing) the ground where it may stand is marked: a faint green wash with
    /// a thin green outline, in the green of the placement footprint ("allowed"). The area is GunCover (the placement rule asked
    /// spot by spot); a free fort tower pad gets a thin ring on the pad. Drawn like the game's other ground marks: a mesh draped
    /// on ZGame.GroundY (1 m grid), a few centimetres up, with the OpenZ/Overlay shader (depth tested, no depth write, polygon
    /// offset), so it follows the ground, units and buildings cover it and it cannot fight with the terrain. A height sample
    /// that jumps off the tile's own level (a cliff face overhanging the tile) is flattened to the tile. The outline keeps a
    /// steady width on screen (wider in metres when the camera is far) and has mitred corners: no overlap, no double bright spots.
    /// Kept up to date while the mode is on: the rule is asked again when a blocking object appears, goes or moves and once a
    /// second anyway; the mesh is rebuilt only when the answer changed. Ground heights are sampled once per zone in slices of
    /// about a millisecond per frame, then the area fades in. No new sound: the mode start is heard through ui_cannon_place_arm
    /// (AudioDirector), a refused click through ui_cannon_place_invalid; the refused click also says why in a HUD notice.
    /// View only.
    /// </summary>
    [DefaultExecutionOrder(76)]
    public class OzGunZone : MonoBehaviour
    {
        public static OzGunZone I { get; private set; }
        /// <summary>Adds itself next to PlayerInput in every loaded scene (set false to add it by hand).</summary>
        public static bool AutoAttach = true;
        /// <summary>The placement footprint's "allowed" green (PlacementFootprint).</summary>
        public static readonly Color Green = new Color(0.35f, 1f, 0.45f, 1f);

        public PlayerInput input;

        [Header("Look")]
        [Tooltip("HDR gain of the green (the selection ring uses 1.9)")] public float gain = 1.1f;
        [Tooltip("by night the dark ground makes the same green far louder: the gain is multiplied by this at full night")] public float nightGain = 0.4f;
        static readonly int NightId = Shader.PropertyToID("_OZNight");
        [Tooltip("fine screen lines over the wash, like a projected picture: how much they take off the wash (0 = none)")] [Range(0f, 1f)] public float scan = 0.6f;
        [Tooltip("their period in pixels on a screen 1080 high (scaled with the screen), and their roll in pixels a second")] public float scanPeriod = 5f, scanDrift = 3f;
        [Range(0f, 1f)] public float fillAlpha = 0.13f, lineAlpha = 0.62f, edgeDark = 0.22f;
        [Tooltip("above the sampled ground (m)")] public float fillLift = 0.07f, lineLift = 0.10f;
        [Tooltip("the outline starts this far inside the edge of the area (m)")] public float inset = 0.08f;
        [Tooltip("outline width (m): camera distance x 0.0021, inside these limits")] public Vector2 lineWidth = new Vector2(0.10f, 0.34f);
        public float fadeIn = 0.2f, fadeOut = 0.15f;
        [Tooltip("a height sample further than this from the tile's own level is a cliff face over the tile: flattened (m)")] public float maxStep = 1.2f;
        [Tooltip("fort tower pad ring radius (m)")] public float padRadius = 1.25f;
        [Tooltip("a surface higher than this is a cliff top or rock wall, not zone ground (the rule of ZoneMarkers and FlagView: 1.5 m)")] public float floorMax = 1.5f;
        [Tooltip("the rule is asked again this often even when no blocking object changed (s); the object signature already catches every change the rule can see")] public float refreshSeconds = 1f;

        public int Rebuilds { get; private set; }
        public int Recalcs { get; private set; }
        /// <summary>Marked tiles of the last built mesh whose nine height samples all lie above floorMax (drawn on a high surface). Must be 0.</summary>
        public int HighTiles { get; private set; }
        public bool Shown => built && alpha > 0f;
        public GunCover Cover => cover;

        ZGame game;
        ZObject cur; int curZone = -1;
        GunCover cover;
        float[] H; int hw, hh, sampleNext; bool heightsReady, firstSlice;
        readonly Dictionary<int, (float[] h, float stamp)> cache = new Dictionary<int, (float[], float)>();
        int lastSig; float lastCalc = -10f, alpha, builtW; bool dirty, built;
        Mesh mesh, padMesh; Material mat;
        readonly List<Vector3> vv = new List<Vector3>(); readonly List<Color> cc = new List<Color>(); readonly List<int> tt = new List<int>();
        readonly List<Matrix4x4> padPoses = new List<Matrix4x4>();
        readonly float[] P = new float[9]; readonly float[] d = new float[6]; readonly Color[] dc = new Color[6];

        // ------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Attach(); }
        static void OnScene(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            if (!AutoAttach) return;
            var pi = Object.FindFirstObjectByType<PlayerInput>();
            if (pi && !pi.GetComponent<OzGunZone>()) pi.gameObject.AddComponent<OzGunZone>();
        }

        void Awake()
        {
            I = this;
            if (!input) input = GetComponent<PlayerInput>();
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (padMesh) Destroy(padMesh);
            if (mat) Destroy(mat);
            if (I == this) I = null;
        }

        Material Mat()
        {
            if (mat) return mat;
            Material baseMat = null;
            var so = GetComponent<SelectionOverlay>();
            if (!so) so = Object.FindFirstObjectByType<SelectionOverlay>();
            if (so) baseMat = so.overlayMaterial;                                  // the scene's overlay material keeps the shader in builds
            if (baseMat) mat = new Material(baseMat);
            else { var sh = Shader.Find("OpenZ/Overlay"); if (!sh) return null; mat = new Material(sh); }
            mat.name = "OZ_GunZone";
            mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);              // on the ground, under units and buildings
            // one step before the game's other ground marks (same shader, none writes depth, so among them the draw order is the
            // look): the area always lies UNDER the placement footprint, the selection rings, the route dots and the range ring,
            // on the near and on the far side of the zone alike; still after the water (Transparent-10)
            mat.renderQueue = Mathf.Max((int)RenderQueue.Transparent + 1, mat.renderQueue - 1);
            return mat;
        }

        // ------------------------------------------------------------------ shared helpers (OzGunRange uses them too)
        /// <summary>Distance from the game camera to the ground it looks at (m).</summary>
        public static float CamDistance()
        {
            var cam = Camera.main;
            if (!cam) return 85f;
            float down = Mathf.Max(0.3f, -cam.transform.forward.y);
            return Mathf.Max(1f, cam.transform.position.y) / down;
        }

        /// <summary>A line width in metres that keeps about the same width on screen (0.0021 of the camera distance: about four
        /// pixels at 1080 lines with the game camera's 32 degree view), inside the given limits.</summary>
        public static float ScreenWidth(Vector2 limits) => Mathf.Clamp(CamDistance() * 0.0021f, limits.x, limits.y);

        /// <summary>The words of the HUD notice for a refused spot (null = nothing to say).</summary>
        public static string Words(ZWorld.GunSpot s)
        {
            switch (s)
            {
                case ZWorld.GunSpot.OutsideZone: return "OUTSIDE THE ZONE";
                case ZWorld.GunSpot.Water: return "NOT IN WATER";
                case ZWorld.GunSpot.RoughGround: return "GROUND TOO ROUGH";
                case ZWorld.GunSpot.Blocked: return "SPOT IS BLOCKED";
                default: return null;
            }
        }

        /// <summary>Called by PlayerInput when a placement click was refused (its sound, ui_cannon_place_invalid, has just played):
        /// the reason as a HUD notice.</summary>
        public static void Refused(PlayerInput pi)
        {
            if (!pi || !pi.game || pi.game.World == null || pi.Placing == null) return;
            string text = Words(pi.game.World.GunSpotState(pi.Placing, pi.PlaceTile.x, pi.PlaceTile.y));
            if (text == null) return;
            var hud = OpenZHud.I;
            if (hud != null) hud.Toast("NO GUN HERE: " + text, HudStyle.Amber, 2.5f);
            OzSoundLog.Add("ui", "place_refused", "ui_cannon_place_invalid", text);
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!game) game = input && input.game ? input.game : ZGame.I;
            if (!input || !game || game.World == null) return;
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            var b = input.Placing;
            if (b == null)
            {
                cur = null; curZone = -1;
                if (alpha <= 0f || !built) { alpha = 0f; return; }
                alpha = Mathf.MoveTowards(alpha, 0f, dt / Mathf.Max(0.01f, fadeOut));
                if (alpha > 0f) Draw();
                return;
            }
            var w = game.World;
            if (b != cur || b.ZoneIndex != curZone)
            {
                Begin(w, b, now);
                if (cover != null) OzSoundLog.Add("ui", "gun_zone", "ui_cannon_place_arm", "valid area shown: " + cover.validSpots + " spots, " + cover.pads.Count + " tower pads");
            }
            if (cover == null) return;
            int sig = Signature(w);
            if (sig != lastSig || now - lastCalc > Mathf.Max(0.25f, refreshSeconds))
            {
                lastSig = sig; lastCalc = now;
                int old = cover.hash;
                GunCover.Compute(w, b, cover); Recalcs++;
                if (cover.hash != old) dirty = true;
            }
            if (!heightsReady) { SampleSome(firstSlice ? 2.5 : 1.2, now); firstSlice = false; }
            float lw = ScreenWidth(lineWidth);
            if (heightsReady && (dirty || !built || Mathf.Abs(lw - builtW) > 0.06f * builtW)) Build(lw);
            if (!built) return;
            alpha = Mathf.MoveTowards(alpha, 1f, dt / Mathf.Max(0.01f, fadeIn));
            Draw();
        }

        void Begin(ZWorld w, ZObject b, float now)
        {
            cur = b; curZone = b.ZoneIndex;
            cover = GunCover.Compute(w, b, cover); Recalcs++;
            built = false; dirty = true; alpha = 0f;
            if (cover == null) return;
            lastSig = Signature(w); lastCalc = now;
            hw = 2 * cover.zw + 1; hh = 2 * cover.zh + 1;
            if (cache.TryGetValue(curZone, out var e) && now - e.stamp < 30f && e.h.Length == hw * hh) { H = e.h; heightsReady = true; }
            else { H = new float[hw * hh]; sampleNext = 0; heightsReady = false; firstSlice = true; }
        }

        /// <summary>Everything the rule looks at besides the zone: the objects that can block a spot (robots and vehicles never do).</summary>
        static int Signature(ZWorld w)
        {
            int s = ZWorld.GunGroundRule ? 17 : 19;
            var os = w.Objects;
            for (int i = 0; i < os.Count; i++)
            {
                var o = os[i];
                if (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) continue;
                s = s * 31 + o.Id; s = s * 31 + o.LocX; s = s * 31 + o.LocY; s = s * 31 + o.WidthPix + o.HeightPix * 7;
            }
            return s;
        }

        /// <summary>Ground heights on the zone's 1 m grid, a slice per frame (GroundY is a ray cast).</summary>
        void SampleSome(double budgetMs, float now)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float x0 = cover.zx * 2f, z0 = -cover.zy * 2f;
            while (sampleNext < H.Length)
            {
                int i = sampleNext % hw, j = sampleNext / hw;
                H[sampleNext++] = game.GroundY(x0 + i, z0 - j);
                if ((sampleNext & 31) == 0 && sw.Elapsed.TotalMilliseconds > budgetMs) break;
            }
            if (sampleNext >= H.Length) { heightsReady = true; cache[curZone] = (H, now); }
        }

        void Draw()
        {
            var m = Mat();
            if (!m || !mesh) return;
            float gainNow = gain * Mathf.Lerp(1f, nightGain, Mathf.Clamp01(Shader.GetGlobalFloat(NightId)));
            m.SetColor("_Color", new Color(Green.r * gainNow, Green.g * gainNow, Green.b * gainNow, alpha));
            float px = Mathf.Max(3f, scanPeriod * Screen.height / 1080f);
            m.SetFloat("_Scan", scan); m.SetFloat("_ScanPeriod", px); m.SetFloat("_ScanDrift", scanDrift * Screen.height / 1080f);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, m, 0, null, 0, null, ShadowCastingMode.Off, false);
            if (padPoses.Count > 0 && padMesh)
                for (int i = 0; i < padPoses.Count; i++) Graphics.DrawMesh(padMesh, padPoses[i], m, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        // ------------------------------------------------------------------ mesh
        bool Cov(int tx, int ty) => tx >= 0 && ty >= 0 && tx < cover.zw && ty < cover.zh && cover.tile[ty * cover.zw + tx];

        /// <summary>The tile's own 3 x 3 heights at tile-local (u east, v south) metres; a little outside the tile it carries on straight.</summary>
        float PatchH(float u, float v)
        {
            int iu = u < 1f ? 0 : 1, iv = v < 1f ? 0 : 1;
            float a = u - iu, b = v - iv;
            float top = Mathf.LerpUnclamped(P[iv * 3 + iu], P[iv * 3 + iu + 1], a);
            float bot = Mathf.LerpUnclamped(P[(iv + 1) * 3 + iu], P[(iv + 1) * 3 + iu + 1], a);
            return Mathf.LerpUnclamped(top, bot, b);
        }

        void Quad(int a, int b, int c, int e) { tt.Add(a); tt.Add(b); tt.Add(c); tt.Add(a); tt.Add(c); tt.Add(e); }

        void Profile(float w)
        {
            // across the outline, from the edge of the area inwards: dark soft edge, bright core, dark soft edge
            d[0] = inset; d[1] = inset + 0.20f * w; d[2] = inset + 0.38f * w; d[3] = inset + 0.62f * w; d[4] = inset + 0.80f * w; d[5] = inset + w;
            dc[0] = new Color(0f, 0f, 0f, 0f); dc[1] = new Color(0f, 0f, 0f, edgeDark);
            dc[2] = new Color(1f, 1f, 1f, lineAlpha); dc[3] = dc[2];
            dc[4] = dc[1]; dc[5] = dc[0];
        }

        void Build(float w)
        {
            builtW = w; dirty = false; built = true; Rebuilds++; HighTiles = 0;
            vv.Clear(); cc.Clear(); tt.Clear();
            Profile(w);
            int zw = cover.zw, zh = cover.zh;
            var fill = new Color(0.78f, 0.78f, 0.78f, fillAlpha);
            for (int ty = 0; ty < zh; ty++)
                for (int tx = 0; tx < zw; tx++)
                {
                    if (!cover.tile[ty * zw + tx]) continue;
                    float hc = H[(2 * ty + 1) * hw + 2 * tx + 1];
                    if (hc > floorMax)
                    {   // the centre sample itself landed well above the play floor (a cliff top hanging over the tile): the tile's
                        // own level is its lowest sample, so the tile stays on its ground, under the overhang, not up on the cliff
                        for (int k = 0; k < 9; k++) hc = Mathf.Min(hc, H[(2 * ty + k / 3) * hw + 2 * tx + k % 3]);
                        if (hc > floorMax) HighTiles++;                                              // all nine samples high: the audit counts it
                    }
                    for (int j = 0; j < 3; j++)
                        for (int i = 0; i < 3; i++)
                        {
                            float h = H[(2 * ty + j) * hw + 2 * tx + i];
                            P[j * 3 + i] = Mathf.Abs(h - hc) > maxStep ? hc : h;
                        }
                    float ox = (cover.zx + tx) * 2f, oz = -(cover.zy + ty) * 2f;                    // the tile's north-west corner (world m)
                    int v0 = vv.Count;
                    for (int j = 0; j < 3; j++)
                        for (int i = 0; i < 3; i++) { vv.Add(new Vector3(ox + i, P[j * 3 + i] + fillLift, oz - j)); cc.Add(fill); }
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++) { int q = v0 + j * 3 + i; Quad(q, q + 1, q + 4, q + 3); }
                    Side(tx, ty, ox, oz, 0f, 0f, 1, 0, 0, 1);                                        // north edge, inwards = south
                    Side(tx, ty, ox, oz, 0f, 2f, 1, 0, 0, -1);                                       // south edge
                    Side(tx, ty, ox, oz, 0f, 0f, 0, 1, 1, 0);                                        // west edge
                    Side(tx, ty, ox, oz, 2f, 0f, 0, 1, -1, 0);                                       // east edge
                }
            if (!mesh) { mesh = new Mesh { name = "OZ_GunZone", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic(); }
            mesh.Clear();
            mesh.SetVertices(vv); mesh.SetColors(cc); mesh.SetTriangles(tt, 0);
            mesh.RecalculateBounds();
            BuildPads();
        }

        /// <summary>The outline along one edge of a marked tile whose neighbour beyond that edge is not marked. (au, av) = the edge's
        /// start corner, (du, dv) = along the edge, (nu, nv) = inwards (tile-local metres; also the tile steps). The ends are mitred
        /// against the next edge of the outline: a convex corner turns with this tile, a concave one with the tile diagonally
        /// outside, a straight run carries on; neighbouring edges compute the same end points, so the outline has no gap and no overlap.</summary>
        void Side(int tx, int ty, float ox, float oz, float au, float av, int du, int dv, int nu, int nv)
        {
            if (Cov(tx - nu, ty - nv)) return;
            bool l = Cov(tx - du, ty - dv), ld = Cov(tx - du - nu, ty - dv - nv);
            bool r = Cov(tx + du, ty + dv), rd = Cov(tx + du - nu, ty + dv - nv);
            float mau = nu, mav = nv, mbu = nu, mbv = nv;
            if (!l) { mau += du; mav += dv; } else if (ld) { mau -= du; mav -= dv; }
            if (!r) { mbu -= du; mbv -= dv; } else if (rd) { mbu += du; mbv += dv; }
            int v0 = vv.Count;
            for (int st = 0; st < 3; st++)
            {
                float bu = au + du * st, bv = av + dv * st;                                         // start corner, middle, end corner (1 m apart)
                float mu = st == 0 ? mau : st == 2 ? mbu : nu, mv = st == 0 ? mav : st == 2 ? mbv : nv;
                for (int k = 0; k < 6; k++)
                {
                    float u = bu + mu * d[k], v = bv + mv * d[k];
                    vv.Add(new Vector3(ox + u, PatchH(u, v) + lineLift, oz - v)); cc.Add(dc[k]);
                }
            }
            for (int st = 0; st < 2; st++)
                for (int k = 0; k < 5; k++) { int q = v0 + st * 6 + k; Quad(q, q + 1, q + 7, q + 6); }
        }

        void BuildPads()
        {
            padPoses.Clear();
            if (cover.pads.Count == 0 || !TowerMounts.Enabled) return;
            if (!padMesh) padMesh = PadRing(48);
            foreach (var p in cover.pads)
                foreach (var o in game.World.Objects)
                {
                    if (!o.IsFort || o.Destroyed || !TowerMounts.IsTurretSpot(o.LocX, o.LocY, p.x * 16, p.y * 16)) continue;
                    if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go || !go.activeInHierarchy) continue;
                    var fort = go.transform;
                    int pad = TowerMounts.PadForSpot(fort, p.x * 16 - o.LocX, p.y * 16 - o.LocY);
                    padPoses.Add(Matrix4x4.TRS(TowerMounts.SeatWorld(fort, pad) + fort.up * 0.06f, fort.rotation, Vector3.one * padRadius));
                    break;
                }
        }

        /// <summary>A thin ring of radius 1 in the XZ plane with the outline's cross-section (dark soft edges, bright core).</summary>
        Mesh PadRing(int seg)
        {
            float[] rr = { 0.925f, 0.955f, 0.982f, 1.018f, 1.045f, 1.075f };
            var c = new[] { new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, edgeDark), new Color(1f, 1f, 1f, lineAlpha), new Color(1f, 1f, 1f, lineAlpha), new Color(0f, 0f, 0f, edgeDark), new Color(0f, 0f, 0f, 0f) };
            var v = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg; var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < 6; k++) { v.Add(dir * rr[k]); col.Add(c[k]); }
                if (i < seg) for (int k = 0; k < 5; k++) { int q = i * 6 + k; t.Add(q); t.Add(q + 1); t.Add(q + 7); t.Add(q); t.Add(q + 7); t.Add(q + 6); }
            }
            var m = new Mesh { name = "OZ_GunZonePad" };
            m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ audit (Play mode, for the proof steps)
        /// <summary>For every zone in which the player owns a building: asks the rule for every spot of the zone and two tiles
        /// round it and compares with the picture's data, builds the mesh at once and measures it against the ground. Returns the
        /// report; violations = the number of lines that must be 0. Not while a gun is being placed.</summary>
        public static string Audit(out int violations)
        {
            violations = 0;
            var sb = new StringBuilder("gun zone audit\n");
            var z = I; var g = z ? (z.game ? z.game : ZGame.I) : null;
            if (!z || !g || g.World == null) { violations = 1; return sb.Append("FAIL no game / no OzGunZone in the scene\n").ToString(); }
            if (z.input && z.input.Placing != null) { violations = 1; return sb.Append("FAIL a gun is being placed: end the placement first\n").ToString(); }
            z.game = g;
            var w = g.World; var seen = new HashSet<int>();
            foreach (var b in w.Objects)
            {
                if (b.Kind != ZKind.Building || b.Owner != g.player || b.ZoneIndex < 0 || !seen.Add(b.ZoneIndex)) continue;
                z.cache.Remove(b.ZoneIndex);
                z.Begin(w, b, Time.unscaledTime);
                if (z.cover == null) continue;
                var c = z.cover; int differ = 0, spots = 0, badGround = 0;
                for (int ty = c.zy - 2; ty < c.zy + c.zh + 2; ty++)
                    for (int tx = c.zx - 2; tx < c.zx + c.zw + 2; tx++) { spots++; if (c.Marked(tx, ty) != w.CannonPlacable(b, tx, ty)) differ++; }
                if (ZWorld.GunGroundRule)
                    for (int ty = c.zy; ty < c.zy + c.zh; ty++)
                        for (int tx = c.zx; tx < c.zx + c.zw; tx++)
                        {
                            if (!c.Tile(tx, ty)) continue;
                            var k = w.Map.ClassAt(tx, ty);
                            if (k != ZClass.Ground && k != ZClass.Road) badGround++;
                        }
                while (!z.heightsReady) z.SampleSome(1000.0, Time.unscaledTime);
                z.Build(ScreenWidth(z.lineWidth));
                int below = 0, high = 0, hidden = 0, outside = 0, tall = 0;
                float x0 = c.zx * 2f - 0.01f, x1 = (c.zx + c.zw) * 2f + 0.01f, z1 = -c.zy * 2f + 0.01f, z0 = -(c.zy + c.zh) * 2f - 0.01f;
                for (int i = 0; i < z.vv.Count; i++)
                {
                    var p = z.vv[i]; float gy = g.GroundY(p.x, p.z), dh = p.y - gy;
                    if (gy - p.y > z.maxStep) hidden++;                                              // under a cliff face that hangs over the tile: covered by it
                    else if (dh < 0.01f) below++;
                    else if (dh > 0.6f) high++;
                    if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) outside++;
                }
                for (int i = 0; i + 2 < z.tt.Count; i += 3)
                {
                    float a = z.vv[z.tt[i]].y, b2 = z.vv[z.tt[i + 1]].y, c2 = z.vv[z.tt[i + 2]].y;
                    if (Mathf.Max(a, Mathf.Max(b2, c2)) - Mathf.Min(a, Mathf.Min(b2, c2)) > 2f * z.maxStep + 0.1f) tall++;
                }
                int onTop = z.HighTiles;                                                             // tiles drawn on a surface above floorMax (a cliff top?)
                int bad = differ + badGround + below + high + outside + tall + onTop;
                violations += bad;
                sb.Append(bad == 0 ? "OK   " : "FAIL ").Append("zone ").Append(b.ZoneIndex).Append(" (").Append(b.Type).Append("): ")
                  .Append(spots).Append(" spots asked, picture differs from the rule on ").Append(differ)
                  .Append("; marked tiles on water or impassable ground ").Append(badGround)
                  .Append("; mesh ").Append(z.vv.Count).Append(" vertices: below the ground ").Append(below).Append(", more than 0.6 m above it ").Append(high)
                  .Append(", outside the zone ").Append(outside).Append(", triangles spanning a cliff ").Append(tall)
                  .Append(", tiles drawn above ").Append(z.floorMax.ToString("0.0")).Append(" m (cliff top) ").Append(onTop)
                  .Append(" (under an overhanging cliff, covered by it: ").Append(hidden).Append("); allowed spots ").Append(c.validSpots)
                  .Append(", tower pads ").Append(c.pads.Count).Append(", pad rings drawn ").Append(z.padPoses.Count).Append('\n');
            }
            z.cur = null; z.curZone = -1; z.built = false; z.alpha = 0f;
            if (seen.Count == 0) { violations++; sb.Append("FAIL the player owns no building\n"); }
            return sb.ToString();
        }
    }
}
