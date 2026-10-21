using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Counted check and owner pictures of the water landing rule (OzWaterLanding). Play only, session only: nothing is saved,
    /// the computer players are paused while it runs and put back, the camera too.
    /// <para><b>Count</b> (<c>OzWaterLandingProof.Run(false)</c>, menu OpenZ/V1.1/Count Pieces Falling Into Water): first it
    /// reads the level's water meshes once more, on its own, and walks the level metre by metre: wherever the drawn water lies
    /// clearly over the walk surface the rule must call it open water, at the height it is drawn (the ravine streams of the
    /// Jungle, the widened river of the Desert). Then it looks for a place with open water on a water tile (at least 2 m of
    /// water all round, 0.3 m deep; one 0.65 m deep or more when the level has it), free land beside it, a dry place 12 m from any water and, when the level has one, a
    /// shallow place (0.12 to 0.45 m deep; drawn water over a LAND tile is taken first, and of that a place with no entry in
    /// the level's water table). It throws one of every piece kind into the water and the same piece onto the dry place, one
    /// at a time with the camera on the place, and reads the rule's own record (OzWaterLanding.Entries), the sound log and
    /// the piece itself: one landing per piece, droplets, rings, the splash sound played, exactly one splash sound in the
    /// sound log, the rest height against the surface (under it by the rule's share or on the bed, never on top, never under
    /// the bed, its top still showing, never a piece that lies flatter than the rule's thin limit) or, for a small or a thin
    /// piece, gone under in the rule's time; the tilt; burning pieces: fire out and the hiss; on dry ground: no landing, not
    /// held. Into the shallow place go a building part, a rock chunk, rubble chips, a detail part, a wheel (one splash, not
    /// sunk, on the bed) and a second tank's turret.
    /// Real paths: a tank is killed and its turret's landing point (the game's own debris point) is moved onto the water, a torn
    /// jeep part (WreckPieces.Single), a thrown wheel (RollingWheel.Throw), a detail part (the detail hub's chunks), rubble chips
    /// (FxManager.Breakup), robot chips (RobotPopDeath.Chips), a crater clod (CraterField), a body with a collision relay, and at
    /// the end the standing bridge nearest to the place, with a jeep put on it, is destroyed (the game rebuilds its bridges by its
    /// own rule): the jeep's wreck falls in, the bridge's own chunks come down, and the spacing and the cap of the splash sounds
    /// are checked on that shower. Bodies built here carry the game's own components (RockChunkLife, BuildingBreaker.Piece /
    /// BigPart) so they reach the rule the way the game's pieces do. A full run is <see cref="FullCount"/> lines; a part that
    /// cannot run on a level (no shallow place, no bridge, no chip in the water) is named with its lines and left out of n. The
    /// last line: "PASS n/n" or "FAIL x of n".</para>
    /// <para><b>Pictures</b> (<c>Run(true)</c>, menu OpenZ/V1.1/Pictures Turret And Wreck Piece In Water): full screen, the
    /// normal game camera at its normal height, no crop: a killed tank's burnt turret and a torn wreck piece in the water of
    /// this level, each one second after it lands and at rest; and for the one who checks, each at rest once more from the
    /// camera's lowest height.</para>
    /// <see cref="Root"/>
    /// </summary>
    public sealed class OzWaterLandingProof : MonoBehaviour
    {
        public static string Root = OzRelease.Milestones("b27_debris_water");
        public static string Status = "idle", Report = "not run";
        /// <summary>Lines of a run in which every part can run.</summary>
        public const int FullCount = 146;
        static readonly StringBuilder log = new StringBuilder();

        int ok, bad, skipped; string dir; ZGame g; ZWorld W; RtsCamera rts; float h0 = -1f; Vector3 f0;
        Vector3 S, D, Sh; bool hasShallow, shLand, shNoTable; float clearS, depthS;
        readonly List<Vector2Int> banks = new List<Vector2Int>();
        Mesh mesh; Material mat; float side = 1f; List<ZBot> bots; UnitView jeep; ZObject jeepObj; int force0 = -1;
        readonly List<GameObject> mine = new List<GameObject>();

        public static string Run(bool pictures)
        {
            var game = ZGame.I; if (!Application.isPlaying || !game || game.World == null) return "Play mode in a level first.";
            if (Status == "running") return "a run is still going";
            log.Length = 0; Status = "running"; Report = "running";
            var h = new GameObject("OzWaterLandingProof").AddComponent<OzWaterLandingProof>();
            h.StartCoroutine(pictures ? h.Pictures() : h.Count());
            return (pictures ? "pictures" : "count") + " started on " + game.level + " (OzWaterLandingProof.Status / Report)";
        }

        // ================================================================================================ small helpers
        void Line(bool pass, string what) { if (pass) ok++; else bad++; log.AppendLine((pass ? "PASS  " : "FAIL  ") + what); Status = what; }
        void Note(string s) { log.AppendLine("      " + s); }
        void Skip(string what, int lines) { skipped += lines; log.AppendLine("SKIP  " + what + " (" + lines + (lines == 1 ? " line left out)" : " lines left out)")); }
        void Fail(string what, int lines) { for (int i = 0; i < lines; i++) { bad++; } log.AppendLine("FAIL  " + what + " (" + lines + " lines)"); }
        void Cam(Vector3 p) { if (rts) rts.focus = new Vector3(p.x, 0f, p.z); }
        static void Kill(ZWorld w, ZObject o) { o.SetHealth(0); w.UpdateObjectHealth(o, -1); }
        static IEnumerator Until(System.Func<bool> done, float max) { float t = 0f; while (t < max && !done()) { t += Time.deltaTime; yield return null; } }

        static bool WB(Transform t, out Bounds b)
        {
            b = new Bounds(t ? t.position : Vector3.zero, Vector3.zero); bool any = false;
            if (!t) return false;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || (r.sharedMaterial && r.sharedMaterial.renderQueue >= 2450)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        static bool Drawn(GameObject go)
        {
            if (!go || !go.activeInHierarchy) return false;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.enabled && go.transform.lossyScale.sqrMagnitude > 0.01f) return true;
            return false;
        }

        /// <summary>The landings after serial s0 of this piece (or, piece == 0, of this kind): the first one and how many.</summary>
        static bool Find(int s0, int piece, string kind, out OzWaterLanding.Entry first, out int n)
        {
            first = default; n = 0;
            foreach (var e in OzWaterLanding.Entries)
            {
                if (e.serial <= s0) continue;
                if (piece != 0 ? e.piece != piece : e.kind != kind) continue;
                if (n++ == 0) first = e;
            }
            return n > 0;
        }

        static bool IsSplash(OzSoundLog.Entry e) =>
            e.key == "wtr_debris_plop" || e.key == "dst_debris_splash" || e.key == "wtr_vehicle_plunge" || e.key == "dst_ragdoll_splash" || e.key == "turret_splash" ||
            (e.clip != null && (e.clip.StartsWith("sd_debris_splash") || e.clip.StartsWith("sd_debris_plop") || e.clip.StartsWith("sd_vehicle_plunge") || e.clip.StartsWith("sd_body_splash")));

        /// <summary>Splash sounds that really played between two (unscaled) times, by the sound log; the rule's own note lines are not counted.</summary>
        static int SplashPlays(float t0, float t1, out string seen)
        {
            int n = 0; var sb = new StringBuilder();
            foreach (var e in OzSoundLog.Since(t0))
            {
                if (e.t > t1 || e.src == "WATR" || e.outcome == null || !e.outcome.StartsWith("PLAY") || !IsSplash(e)) continue;
                n++; sb.Append(e.src).Append(' ').Append(e.key).Append(' ');
            }
            seen = sb.ToString(); return n;
        }

        static int Plays(float t0, float t1, string keyA, string keyB = null)
        {
            int n = 0;
            foreach (var e in OzSoundLog.Since(t0)) if (e.t <= t1 && e.src != "WATR" && e.outcome != null && e.outcome.StartsWith("PLAY") && (e.key == keyA || (keyB != null && e.key == keyB))) n++;
            return n;
        }

        /// <summary>Every line of the sound log with this key between two times, whatever became of it (the rule's own lines left out).</summary>
        static int Logged(float t0, float t1, string key)
        {
            int n = 0;
            foreach (var e in OzSoundLog.Since(t0)) if (e.t <= t1 && e.src != "WATR" && e.key == key) n++;
            return n;
        }

        static Transform Named(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }

        static GameObject RootById(int transformId)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) if (go.transform.GetInstanceID() == transformId) return go;
            return null;
        }

        // ================================================================================================ the drawn water, read once more
        /// <summary>Every triangle of the level's WAT_ meshes, read here on its own (not through the rule's grid) and sorted into
        /// 4 m squares, so the scan can ask "is water drawn over this point, and how high" without the rule.</summary>
        sealed class WaterRead
        {
            const float B = 4f;
            readonly List<Vector3> tri = new List<Vector3>();
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            public bool ok = true; public int Triangles => tri.Count / 3;
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

            public WaterRead()
            {
                foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!r.name.StartsWith("WAT_") || !r.gameObject.activeInHierarchy) continue;
                    var mf = r.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                    if (!m) continue;
                    if (!m.isReadable) { ok = false; continue; }
                    var v = m.vertices; var t = m.triangles; var M = r.transform.localToWorldMatrix;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = M.MultiplyPoint3x4(v[t[i]]), b = M.MultiplyPoint3x4(v[t[i + 1]]), c = M.MultiplyPoint3x4(v[t[i + 2]]);
                        int k = tri.Count; tri.Add(a); tri.Add(b); tri.Add(c);
                        int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / B), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / B);
                        int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / B), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / B);
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                            {
                                long key = Key(x, z);
                                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                                list.Add(k);
                            }
                    }
                }
                if (tri.Count == 0) ok = false;
            }

            /// <summary>Water is drawn over (x, z); y = the highest water triangle there (the same inside test as the game's raster).</summary>
            public bool At(float x, float z, out float y)
            {
                y = float.NegativeInfinity; bool any = false;
                if (!cells.TryGetValue(Key(Mathf.FloorToInt(x / B), Mathf.FloorToInt(z / B)), out var list)) return false;
                for (int n = 0; n < list.Count; n++)
                {
                    int k = list[n];
                    Vector2 a = new Vector2(tri[k].x, tri[k].z), b = new Vector2(tri[k + 1].x, tri[k + 1].z), c = new Vector2(tri[k + 2].x, tri[k + 2].z);
                    float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (Mathf.Abs(area) < 1e-6f) continue;
                    float w0 = (b.x - x) * (c.y - z) - (b.y - z) * (c.x - x), w1 = (c.x - x) * (a.y - z) - (c.y - z) * (a.x - x), w2 = (a.x - x) * (b.y - z) - (a.y - z) * (b.x - x);
                    if (!(area > 0f ? (w0 >= 0f && w1 >= 0f && w2 >= 0f) : (w0 <= 0f && w1 <= 0f && w2 <= 0f))) continue;
                    float h = (w0 * tri[k].y + w1 * tri[k + 1].y + w2 * tri[k + 2].y) / area;
                    if (h > y) y = h;
                    any = true;
                }
                return any;
            }
        }

        /// <summary>One line: the level walked metre by metre, each point taken at the middle of its quarter-metre cell of the
        /// rule's water grid (AnticSpot.WaterCellMiddle), so this reading and the rule look at exactly the same point. Where the
        /// drawn water (read here on its own, straight from the meshes) lies 0.09 m or more over the walk surface the rule must
        /// call the place open water, at the height that is drawn (0.01 m); where it lies 0.07 m or less over it, or no water is
        /// drawn, the rule must not (the rule's own limit is 0.08 m).</summary>
        IEnumerator Scan()
        {
            const string label = "the level walked metre by metre";
            var fxp = OpenZFxPlayer.I;
            var read = new WaterRead();
            if (!fxp || !read.ok) { Skip(label + ": a water mesh of this level cannot be read here (the rule then asks the water tiles)", 1); yield break; }
            float mw = W.Map.width * 2f, mh = W.Map.height * 2f;
            int under = 0, open = 0, land = 0, missed = 0, phantom = 0, off = 0, col = 0; string first = null;
            for (float x = 0.5f; x < mw; x += 1f)
            {
                for (float z = -0.5f; z > -mh; z -= 1f)
                {
                    var p = AnticSpot.WaterCellMiddle(fxp, new Vector3(x, 0f, z));
                    bool drawn = read.At(p.x, p.z, out float my);
                    bool rule = OzWaterLanding.OpenWaterAt(p, out float ry);
                    if (!drawn)
                    {
                        if (rule) { phantom++; first ??= $"open water with no water drawn at {p.x:F2}, {p.z:F2}"; }
                        continue;
                    }
                    under++;
                    float over = my - g.GroundY(p.x, p.z);
                    if (over >= OzWaterLanding.MinDepth + 0.01f)
                    {
                        open++; if (!OpenZFxPlayer.IsWaterTile(p)) land++;
                        if (!rule) { missed++; first ??= $"drawn water at {p.x:F2}, {p.z:F2} (surface {my:F2}, {over:F2} m over the walk surface) is dry land for the rule"; }
                        else if (Mathf.Abs(ry - my) > 0.01f) { off++; first ??= $"at {p.x:F2}, {p.z:F2} the water is drawn at {my:F2}, the rule says {ry:F2}"; }
                    }
                    else if (over <= OzWaterLanding.MinDepth - 0.01f && rule) { phantom++; first ??= $"open water at {p.x:F2}, {p.z:F2} where the drawn water is only {over:F2} m over the walk surface"; }
                }
                if (++col % 16 == 0) yield return null;
            }
            Line(missed == 0 && phantom == 0 && off == 0 && open > 0,
                 $"{label} ({read.Triangles} water triangles read): {under} places under drawn water, {open} of them open water ({land} over land tiles); " +
                 $"dry land for the rule: {missed} (must be 0), surface height off by more than 0.01 m: {off} (must be 0), open water where it is not: {phantom} (must be 0)" + (first != null ? "; first: " + first : ""));
        }

        // ================================================================================================ places
        bool Land(int tx, int ty) { var c = W.Map.ClassAt(tx, ty); return c == ZClass.Ground || c == ZClass.Road; }

        bool FreeTile(int tx, int ty)
        {
            for (int y = ty - 1; y <= ty + 2; y++) for (int x = tx - 1; x <= tx + 2; x++) if (x < 2 || y < 2 || x >= W.Map.width - 2 || y >= W.Map.height - 2 || !Land(x, y)) return false;
            int x0 = (tx - 2) * 16, x1 = (tx + 4) * 16, y0 = (ty - 2) * 16, y1 = (ty + 4) * 16;
            foreach (var o in W.Objects) if (o != null && !o.Destroyed && o.CenterX + o.WidthPix / 2 > x0 && o.CenterX - o.WidthPix / 2 < x1 && o.CenterY + o.HeightPix / 2 > y0 && o.CenterY - o.HeightPix / 2 < y1) return false;
            return !WaterWithin(Centre(tx, ty), 3f);
        }

        static Vector3 Centre(int tx, int ty) => new Vector3(tx * 2f + 2f, 0f, -(ty * 2f + 2f));           // the middle of a vehicle standing on tile tx, ty (2 x 2 tiles)

        static bool WaterWithin(Vector3 p, float r)
        {
            if (OzWaterLanding.OpenWaterAt(p, out _)) return true;
            for (int k = 0; k < 8; k++) { var d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward; for (float s = 1f; s <= r; s += 1f) if (OzWaterLanding.OpenWaterAt(p + d * s, out _)) return true; }
            return false;
        }

        static float ClearOf(Vector3 p, float upTo)
        {
            float best = upTo;
            for (int k = 0; k < 8; k++) { var d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward; for (float s = 0.5f; s <= best; s += 0.5f) if (!OzWaterLanding.OpenWaterAt(p + d * s, out _)) { best = s - 0.5f; break; } }
            return best;
        }

        /// <summary>The level has a water table and it holds no wet tile at p nor one metre east, west, north or south of it.</summary>
        bool NoTable(Vector3 p)
        {
            if (!OzWaterLevel.Has(g.level)) return false;
            return float.IsNaN(OzWaterLevel.At(g.level, p.x, p.z, float.NaN)) && float.IsNaN(OzWaterLevel.At(g.level, p.x + 1f, p.z, float.NaN)) && float.IsNaN(OzWaterLevel.At(g.level, p.x - 1f, p.z, float.NaN)) &&
                   float.IsNaN(OzWaterLevel.At(g.level, p.x, p.z + 1f, float.NaN)) && float.IsNaN(OzWaterLevel.At(g.level, p.x, p.z - 1f, float.NaN));
        }

        /// <summary>A free land tile 5 to 18 m from p, away from the places of the other test vehicles (nearest first).</summary>
        bool FreeNear(Vector3 p, out Vector2Int tile)
        {
            tile = default; float bd = float.MaxValue; bool any = false;
            int ptx = Mathf.FloorToInt(p.x * 0.5f), pty = Mathf.FloorToInt(-p.z * 0.5f);
            for (int ty = pty - 9; ty <= pty + 9; ty++)
                for (int tx = ptx - 9; tx <= ptx + 9; tx++)
                {
                    if (tx < 3 || ty < 3 || tx >= W.Map.width - 4 || ty >= W.Map.height - 4) continue;
                    var c = Centre(tx, ty); float d = new Vector2(c.x - p.x, c.z - p.z).magnitude;
                    if (d < 5f || d > 18f || d >= bd || !FreeTile(tx, ty)) continue;
                    bool own = false; foreach (var b in banks) if (Mathf.Abs(b.x - tx) + Mathf.Abs(b.y - ty) < 5) own = true;
                    if (own) continue;
                    bd = d; tile = new Vector2Int(tx, ty); any = true;
                }
            return any;
        }

        /// <summary>wantClear = the water width the place should have round it (the count takes the widest up to 6 m, the pictures a river-like 2.5 m).</summary>
        bool FindPlaces(float wantClear)
        {
            var wet = new List<Vector3>(); var shallow = new List<Vector3>(); var stream = new List<Vector3>();
            float mw = W.Map.width * 2f, mh = W.Map.height * 2f;
            for (float x = 4.5f; x < mw - 4f; x += 1f)
                for (float z = -4.5f; z > -(mh - 4f); z -= 1f)
                {
                    var p = new Vector3(x, 0f, z);
                    if (!OzWaterLanding.OpenWaterAt(p, out float wy)) continue;
                    float depth = wy - g.GroundY(x, z); bool tile = OpenZFxPlayer.IsWaterTile(p);
                    if (depth >= 0.3f && tile) wet.Add(new Vector3(x, wy, z));
                    else if (depth >= 0.12f && depth <= 0.45f) { shallow.Add(new Vector3(x, wy, z)); if (!tile) stream.Add(new Vector3(x, wy, z)); }
                }
            Note($"open water: {wet.Count} one-metre places 0.3 m deep or more on water tiles, {shallow.Count} shallow ones (0.12 to 0.45 m), {stream.Count} of those over land tiles");
            if (wet.Count == 0) return false;
            var from = rts ? rts.focus : Vector3.zero; float bestScore = float.MinValue; bool any = false;
            int step = Mathf.Max(1, wet.Count / 240);
            for (int i = 0; i < wet.Count; i += step)
            {
                var p = wet[i]; float c = ClearOf(p, 6f); if (c < 1.5f) continue;
                float d = new Vector2(p.x - from.x, p.z - from.z).magnitude;
                float score = -Mathf.Abs(c - wantClear) * 10f - d * 0.02f;
                if (p.y - g.GroundY(p.x, p.z) < OzWaterLanding.SinkDepth + 0.05f) score -= 60f;                 // a place deep enough for the wheel to sink away, when the level has one
                if (score > bestScore) { bestScore = score; S = p; clearS = c; any = true; }
            }
            if (!any) return false;
            depthS = S.y - g.GroundY(S.x, S.z);
            // free land beside it (for the tank, the jeep and a second tank), nearest first
            int stx = Mathf.FloorToInt(S.x * 0.5f), sty = Mathf.FloorToInt(-S.z * 0.5f);
            var cand = new List<(float d, Vector2Int t)>();
            for (int ty = sty - 9; ty <= sty + 9; ty++)
                for (int tx = stx - 9; tx <= stx + 9; tx++)
                {
                    if (tx < 3 || ty < 3 || tx >= W.Map.width - 4 || ty >= W.Map.height - 4) continue;
                    var c = Centre(tx, ty); float d = new Vector2(c.x - S.x, c.z - S.z).magnitude;
                    if (d < 5f || d > 18f || !FreeTile(tx, ty)) continue;
                    cand.Add((d, new Vector2Int(tx, ty)));
                }
            cand.Sort((a, b) => a.d.CompareTo(b.d));
            banks.Clear();
            foreach (var c in cand)
            {
                bool far = true; foreach (var b in banks) if (Mathf.Abs(b.x - c.t.x) + Mathf.Abs(b.y - c.t.y) < 5) far = false;
                if (far) banks.Add(c.t);
                if (banks.Count == 3) break;
            }
            // a dry place: land, nothing on it, no water within 8 m, nearest to the water place
            float bd = float.MaxValue; bool dry = false;
            for (int ty = sty - 16; ty <= sty + 16; ty++)
                for (int tx = stx - 16; tx <= stx + 16; tx++)
                {
                    if (tx < 3 || ty < 3 || tx >= W.Map.width - 4 || ty >= W.Map.height - 4 || !FreeTile(tx, ty)) continue;
                    var c = Centre(tx, ty); float d = new Vector2(c.x - S.x, c.z - S.z).magnitude;
                    if (d < 12f || d >= bd || WaterWithin(c, 8f)) continue;
                    bool own = false; foreach (var b in banks) if (Mathf.Abs(b.x - tx) + Mathf.Abs(b.y - ty) < 5) own = true;
                    if (own) continue;
                    bd = d; D = new Vector3(c.x, g.GroundY(c.x, c.z), c.z); dry = true;
                }
            // the shallow place: drawn water over a land tile first (of that, a place the water table does not know), nearest to the water place
            hasShallow = shLand = shNoTable = false;
            for (int pass = 0; pass < 2 && !hasShallow; pass++)
            {
                var list = pass == 0 ? stream : shallow; float bs = float.MaxValue;
                int sstep = Mathf.Max(1, list.Count / 160);
                for (int i = 0; i < list.Count; i += sstep)
                {
                    var p = list[i]; bool noTable = pass == 0 && NoTable(p);
                    // a flat place first (the wheel is dropped here and must stay): the bed's height 0.7 m round it differs by under 5 cm
                    float g0 = g.GroundY(p.x, p.z), tilt = 0f;
                    for (int q = 0; q < 8; q++) { float a = q * Mathf.PI / 4f; tilt = Mathf.Max(tilt, Mathf.Abs(g.GroundY(p.x + Mathf.Cos(a) * 0.7f, p.z + Mathf.Sin(a) * 0.7f) - g0)); }
                    float d = new Vector2(p.x - S.x, p.z - S.z).sqrMagnitude - (noTable ? 1e7f : 0f) + Mathf.Max(0f, tilt - 0.05f) * 2e5f;
                    if (d >= bs || ClearOf(p, 1.5f) < 1f) continue;
                    bs = d; Sh = p; hasShallow = true; shLand = pass == 0; shNoTable = noTable;
                }
            }
            Note($"water place {S.x:F1}, {S.z:F1}: surface {S.y:F2}, {depthS:F2} m deep, {clearS:F1} m of open water all round; land places beside it: {banks.Count}; dry place: {(dry ? D.x.ToString("F1") + ", " + D.z.ToString("F1") : "none")}; " +
                 $"shallow place: {(hasShallow ? Sh.x.ToString("F1") + ", " + Sh.z.ToString("F1") + " (surface " + Sh.y.ToString("F2") + ", " + (Sh.y - g.GroundY(Sh.x, Sh.z)).ToString("F2") + " m deep, " + (shLand ? "drawn water over a land tile" + (shNoTable ? ", not in the water table" : "") : "on a water tile") + ")" : "none on this level")}");
            return dry && banks.Count >= 1;
        }

        // ================================================================================================ set up / put back
        bool Begin(float wantClear)
        {
            g = ZGame.I; W = g.World; rts = FindFirstObjectByType<RtsCamera>();
            dir = Path.Combine(Root, g.level); Directory.CreateDirectory(dir);
            if (rts) { h0 = rts.height; f0 = rts.focus; if (rts.height < 55f || rts.height > 70f) rts.SetHeightImmediate(62f); }
            bots = new List<ZBot>(W.Bots); W.Bots.Clear();
            var rp = FindFirstObjectByType<RockPillar>();
            if (rp && rp.chunks != null && rp.chunks.Length > 0 && rp.chunkMaterial)
            {
                mat = rp.chunkMaterial;
                foreach (var m in rp.chunks) if (m && (!mesh || m.bounds.size.magnitude > mesh.bounds.size.magnitude)) mesh = m;
            }
            if (!mesh)
            {   // a level without rock pillars: the game's own rounded wreck block
                mesh = WreckPieces.CoreMesh();
                foreach (var v in g.Views.Values) { var r = v ? v.GetComponentInChildren<MeshRenderer>() : null; if (r && r.sharedMaterial) { mat = WreckPieces.CoreMat(r.sharedMaterial); break; } }
            }
            if (!mesh || !mat) { log.AppendLine("no mesh / material for the test bodies"); return false; }
            side = Mathf.Max(0.05f, Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z)));
            log.AppendLine($"{g.level}, screen {Screen.width} x {Screen.height}, camera height {(rts ? rts.height : 0f):F0}, game speed {g.gameSpeed:F2}, rule on: {OzWaterLanding.Enabled}, test body mesh {mesh.name} ({side:F2} m)");
            return FindPlaces(wantClear);
        }

        void End(string file)
        {
            if (force0 >= 0) { WreckFx.Force = force0; force0 = -1; }
            if (jeepObj != null && !jeepObj.Destroyed) Kill(W, jeepObj);
            foreach (var m in mine) if (m) Destroy(m);
            if (bots != null) W.Bots.AddRange(bots);
            if (rts && h0 > 0f) { rts.focus = f0; rts.SetHeightImmediate(h0); }
            try { File.WriteAllText(Path.Combine(dir ?? Root, file), log.ToString()); } catch { }
            Debug.Log("[OpenZ V1.1] b27 debris into water\n" + log);
            Status = "DONE";
            Destroy(gameObject);
        }

        // ================================================================================================ bodies
        /// <summary>A test body of the level's own rock mesh, its largest side as given. A body that is not Small never comes out
        /// thin by the chance of the mesh's shape (a side under 0.36 m is stretched to that): the thin case has its own body, the slab.</summary>
        GameObject Body(string name, Vector3 at, float largestSide, float mass)
        {
            var go = new GameObject(name) { layer = BuildingBreaker.DebrisLayer };
            var bs = mesh.bounds.size; float k = largestSide / side; var sc = Vector3.one * k;
            if (largestSide >= OzWaterLanding.SmallMax)
                for (int ax = 0; ax < 3; ax++) if (bs[ax] * k < 0.36f) sc[ax] = 0.36f / Mathf.Max(0.01f, bs[ax]);
            go.transform.SetPositionAndRotation(at, Random.rotation); go.transform.localScale = sc;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = mass; rb.linearDamping = 0.05f; rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            mine.Add(go);
            return go;
        }

        static void Aim(Rigidbody rb, Vector3 from, Vector3 to, float seconds)
        {
            rb.transform.position = from; rb.position = from;
            rb.linearVelocity = (to - from) / seconds - 0.5f * Physics.gravity * seconds;
            rb.angularVelocity = Random.insideUnitSphere * 3f;
        }

        GameObject RockChunk(Vector3 at) { var go = Body("RockChunk", at, 0.9f, 60f); go.AddComponent<RockChunkLife>().lifeMin = 16f; return go; }
        GameObject LooseStone(Vector3 at) { var go = Body("RockChunk", at, 0.28f, 12f); go.AddComponent<RockChunkLife>().lifeMin = 16f; return go; }
        GameObject BurningDrum(Vector3 at) { var go = Body("BaseChunk", at, 0.8f, 40f); go.AddComponent<RockChunkLife>().lifeMin = 16f; go.AddComponent<BasePropFlamingDebris>(); return go; }
        GameObject BuildingPiece(Vector3 at) { var go = Body("OZ_TestBuildingPiece", at, 0.8f, 40f); var p = go.AddComponent<BuildingBreaker.Piece>(); p.rb = go.GetComponent<Rigidbody>(); p.life = 16f; return go; }
        GameObject BuildingPart(Vector3 at) => BuildingPart(at, 2.1f);
        GameObject BuildingPart(Vector3 at, float size) { var go = Body("OZ_TestBuildingPart", at, size, 600f * Mathf.Pow(size / 2.1f, 3f)); var p = go.AddComponent<BuildingBreaker.BigPart>(); p.rb = go.GetComponent<Rigidbody>(); p.Arm(18f, false); return go; }
        GameObject WatchedBody(Vector3 at) { var go = Body("OZ_TestWatchedBody", at, 1.1f, 70f); OzWaterLanding.Watch(go.transform, "watched body", go.GetComponent<Rigidbody>()); return go; }

        /// <summary>A flat slab 1.0 x 0.7 x 0.13 m (the size of the bridges' deck slab) with a building piece's own component: the thin case.</summary>
        GameObject Slab(Vector3 at)
        {
            var go = Body("OZ_TestSlab", at, 1f, 60f);
            var bs = mesh.bounds.size;
            go.transform.localScale = new Vector3(1.0f / Mathf.Max(0.01f, bs.x), 0.13f / Mathf.Max(0.01f, bs.y), 0.7f / Mathf.Max(0.01f, bs.z));
            var p = go.AddComponent<BuildingBreaker.Piece>(); p.rb = go.GetComponent<Rigidbody>(); p.life = 16f;
            return go;
        }

        GameObject BridgeChunk(Vector3 at)
        {
            var set = g.Prefab($"Z_bridge_{(g.level == "L17" ? "city" : g.level == "L16" ? "jungle" : "desert")}_chunks");
            if (!set || set.transform.childCount == 0) return null;
            var go = Instantiate(set.transform.GetChild(0).gameObject, at, Random.rotation);
            go.layer = BuildingBreaker.DebrisLayer;
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf && mf.sharedMesh) { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true; }
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 60f; rb.linearDamping = 0.28f; rb.angularDamping = 0.95f;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (g.level == "L17") AudioDirector.AttachImpacts(go, "dst_debris_metal", "dst_debris_splash");      // as RulesView.BridgeChunks: the relay must stay silent for a piece the rule holds
            go.AddComponent<RockChunkLife>().lifeMin = 16f;
            mine.Add(go);
            return go;
        }

        static readonly string[] JeepParts = { "Seat_passenger", "Seat_driver", "SpareCover_owner", "SteeringWheel", "Antenna" };
        int partN;

        GameObject TornPart(Vector3 at) => TornPart(at, false);

        /// <summary>A real part torn off the test jeep (WreckPieces.Single): the piece object. thick = only a part that will rest on
        /// the water (not a thin one, not a small one: for the "at rest" picture).</summary>
        GameObject TornPart(Vector3 at, bool thick)
        {
            if (!jeep) return null;
            for (int i = partN; i < JeepParts.Length; i++)
            {
                var t = Named(jeep.transform, JeepParts[i]); if (!t || !t.IsChildOf(jeep.transform)) continue;
                bool any = false; foreach (var r in t.GetComponentsInChildren<Renderer>()) if (r.enabled) any = true;
                if (!any) continue;
                if (thick && (!WB(t, out var tb) || Mathf.Min(tb.size.x, Mathf.Min(tb.size.y, tb.size.z)) < OzWaterLanding.ThinMax + 0.03f || Mathf.Max(tb.size.x, Mathf.Max(tb.size.y, tb.size.z)) < OzWaterLanding.SmallMax + 0.05f)) continue;
                if (!WreckPieces.Single(t, t.position + Vector3.down, 1f, 9100 + i)) continue;
                if (!thick) partN = i + 1;
                var go = t.parent.gameObject; mine.Add(go);
                return go;
            }
            return null;
        }

        IEnumerator SpawnJeep()
        {
            if (banks.Count == 0) yield break;
            var tile = banks[Mathf.Min(1, banks.Count - 1)];
            jeepObj = W.CreateObject("vehicle", "jeep", tile.x * 16, tile.y * 16, Team.Neutral);
            if (jeepObj == null) yield break;
            g.SpawnUnitView(jeepObj);
            yield return new WaitForSeconds(0.8f);
            g.Views.TryGetValue(jeepObj.Id, out jeep);
        }

        // ================================================================================================ one held piece: 7 lines wet, 2 dry
        /// <summary>mustSink = this piece is thin by its make: it must go under, never rest.</summary>
        IEnumerator Held(string label, System.Func<Vector3, GameObject> make, Vector3 target, bool dry, bool burning = false, bool mustSink = false)
        {
            Cam(target);
            yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial;
            var from = target + new Vector3(2.2f, 3.2f, 1.1f);
            var go = make(from);
            if (!go) { Skip(label + (dry ? " on dry ground" : "") + ": the piece could not be made on this level", dry ? 2 : burning ? 3 : 7); yield break; }
            var rb = go.GetComponent<Rigidbody>(); if (rb) Aim(rb, from, target + Vector3.up * 0.3f, 0.9f);
            var wp = go.GetComponent<WreckPieces>(); if (burning && wp) wp.OzIgnite(8f);
            var drum = go.GetComponent<BasePropFlamingDebris>();
            int id = go.transform.GetInstanceID();
            yield return Until(() => !go || Find(s0, id, null, out _, out _), dry ? 2.5f : 4f);
            yield return new WaitForSeconds(dry ? 2.2f : 3.6f);
            bool has = Find(s0, id, null, out var e, out int n);
            if (dry)
            {
                Line(!has, $"{label} on dry ground: no water landing ({n})");
                Line(go && !OzWaterLanding.InWater(go.transform), $"{label} on dry ground: not held by the rule");
                if (go && WB(go.transform, out var db)) Note($"lies with its bottom {db.min.y - g.GroundY(db.center.x, db.center.z):F2} m over the ground under its middle");
                yield break;
            }
            if (burning && wp)
            {   // the burning torn part: 3 lines
                Line(n == 1, $"{label}: one water landing ({n})");
                Line(has && e.doused && !wp.OzBurning, $"{label}: its fire is out (rule: {has && e.doused}, piece still burning: {wp.OzBurning})");
                Line(has && Plays(e.utime - 0.1f, e.utime + 0.6f, "dtl_steam_hiss", "water_hiss") >= 1, $"{label}: the hiss is in the sound log");
                yield break;
            }
            Line(n == 1, $"{label}: one water landing ({n})");
            if (!has) { Fail(label + ": no landing recorded, the other lines cannot be read", 6); yield break; }
            Line(e.fx, $"{label} ({e.size}, {e.speed:F1} m/s): droplets drawn");
            int need = e.size == OzWaterSize.Small ? 1 : e.size == OzWaterSize.Medium ? 2 : 3;
            Line(e.rings >= need, $"{label}: {e.rings} rings (at least {need})");
            Line(e.sound != null && e.sound.StartsWith("PLAY"), $"{label}: splash sound: {e.sound}");
            int plays = SplashPlays(e.utime - 0.15f, e.utime + 0.6f, out string seen);
            Line(plays == 1, $"{label}: splash sounds in the sound log: {plays} (must be 1): {seen}");
            if (drum)
            {   // the burning drum: fire out and the hiss instead of the rest lines
                Line(e.doused && !drum.enabled, $"{label}: its fire is out (rule: {e.doused}, flame trail still on: {drum.enabled})");
                Line(Plays(e.utime - 0.1f, e.utime + 0.6f, "dtl_steam_hiss", "water_hiss") >= 1, $"{label}: the hiss is in the sound log");
                yield break;
            }
            Find(s0, id, null, out e, out _);
            if (e.size == OzWaterSize.Small || e.sinks)
            {   // a small piece, or a thin one (it would lie flat on the water): under and gone, never at rest on the surface
                string why = e.size == OzWaterSize.Small ? "small" : $"thin: {e.flat:F2} m lying flat, limit {OzWaterLanding.ThinMax:F2} m";
                Line(e.end == 2 && !Drawn(go), $"{label} ({why}): went under and is gone, never at rest on the surface (end {e.end}, still drawn: {Drawn(go)})");
                float took = e.endTime - e.time;
                Line(e.end == 2 && Mathf.Abs(took - OzWaterLanding.SinkSeconds) <= 0.3f, $"{label}: gone {took:F2} s after it touched the water (rule: {OzWaterLanding.SinkSeconds:F1} s, 0.3 s either way)");
                yield break;
            }
            if (mustSink) { Fail($"{label}: a piece {e.flat:F2} m high lying flat must go under (limit {OzWaterLanding.ThinMax:F2} m), the rule let it rest on the water", 2); yield break; }
            bool alive = WB(go ? go.transform : null, out var b);
            if (alive && OzWaterLanding.LowHigh(go.transform, out float trueLow, out float trueHigh)) b.SetMinMax(new Vector3(b.min.x, trueLow, b.min.z), new Vector3(b.max.x, trueHigh, b.max.z));   // its real lowest and highest point, as the rule measures them
            float under = alive ? e.waterY - b.min.y : -9f, want = e.under;
            bool onBed = e.under < e.share * e.height - 0.005f, thick = e.flat < 0f || e.flat >= OzWaterLanding.ThinMax - 0.006f;
            bool rest = alive && thick && e.end == 1 && Mathf.Abs(under - want) <= 0.05f && under >= 0.05f && b.max.y > e.waterY - 0.02f && b.min.y >= e.bedY - OzWaterLanding.MudSink - 0.05f;
            Line(rest, $"{label}: at rest its bottom is {under:F2} m under the surface (rule: {want:F2} m = {(onBed ? "on the bed" : (e.share * 100f).ToString("F0") + " % of its " + e.height.ToString("F2") + " m")}), " +
                       $"its top {(alive ? b.max.y - e.waterY : 0f):F2} m over it, the water {e.waterY - e.bedY:F2} m deep, {(e.flat < 0f ? "its own pose" : e.flat.ToString("F2") + " m high lying flat (never under " + OzWaterLanding.ThinMax.ToString("F2") + ")")}, end {e.end}");
            bool isTurret = e.kind == "turret";
            Line(e.tiltDeg < 0f || (e.tiltDeg >= (isTurret ? OzWaterLanding.TurretTiltMin : OzWaterLanding.TiltMin * OzWaterLanding.TiltLargeK) - 0.01f && e.tiltDeg <= (isTurret ? OzWaterLanding.TurretTiltMax : OzWaterLanding.TiltMax) + 0.01f),
                 $"{label}: tilt {e.tiltDeg:F1} degrees off its flat rest{(isTurret ? $" (a turret: {OzWaterLanding.TurretTiltMin:0} to {OzWaterLanding.TurretTiltMax:0})" : "")}");
        }

        // ================================================================================================ the count
        IEnumerator Count()
        {
            yield return null;
            if (!Begin(6f)) { log.AppendLine("FAIL: no place for the check on this level (see the lines above)"); Report = "FAIL: no place for the check"; End("count.txt"); yield break; }
            yield return Scan();
            yield return SpawnJeep();
            Note(jeep ? "test jeep placed (its parts and its wheels are thrown)" : "no test jeep could be placed: the torn part and the wheels are left out");

            // ---- bodies with the game's own components, into the water and onto the dry place
            var kinds = new (string label, System.Func<Vector3, GameObject> make)[]
            {
                ("rock chunk", RockChunk), ("loose stone", LooseStone), ("building piece", BuildingPiece), ("building part", BuildingPart),
                ("watched body (the way palm parts, sandbags and the radar dish come in)", WatchedBody), ("bridge chunk", BridgeChunk), ("torn vehicle part", TornPart),
            };
            foreach (var k in kinds) { yield return Held(k.label, k.make, S, false); yield return Held(k.label, k.make, D, true); }
            yield return Held("thin slab 1.0 x 0.7 x 0.13 m", Slab, S, false, false, true);
            yield return Held("burning fuel drum", BurningDrum, S, false);
            yield return Held("burning torn vehicle part", TornPart, S, false, true);

            yield return Wheel(false); yield return Wheel(true);
            yield return DetailPart(S, "");
            yield return RubbleChips(S, "");
            yield return RobotChips();
            yield return RelayBody();
            yield return CraterClod();

            // ---- the shallow place: a ford of the Desert, a ravine stream of the Jungle (drawn water over a land tile when the level has that)
            const int shallowLines = 26;
            if (!hasShallow) Skip("the shallow place (building part 2, rock chunk 7, rubble chips 2, detail part 6, wheel 1, turret 8): no water 0.12 to 0.45 m deep on this level", shallowLines);
            else
            {
                string where = shLand ? " in the stream place (drawn water over a land tile)" : " in the shallow place";
                yield return Shallow();
                yield return Held("rock chunk" + where, RockChunk, Sh, false);
                yield return RubbleChips(Sh, where);
                yield return DetailPart(Sh, where);
                yield return WheelShallow();
            }

            force0 = WreckFx.Force; WreckFx.Force = 2;                                                   // the test tanks stay plain wrecks (no parts of theirs fly into the water while the turret is counted)
            yield return Turret("blown-off tank turret", S, banks[0], false);
            yield return Turret("blown-off tank turret on dry ground", D, banks[banks.Count - 1], true);
            if (hasShallow)
            {
                if (!FreeNear(Sh, out var tile)) tile = banks[0];
                yield return Turret("blown-off tank turret" + (shLand ? " in the stream place (drawn water over a land tile)" : " in the shallow place"), Sh, tile, false);
            }
            WreckFx.Force = force0; force0 = -1;
            yield return BridgeShower();

            int n = ok + bad;
            string verdict = bad == 0 ? $"PASS {ok}/{n}" : $"FAIL {bad} of {n}";
            log.AppendLine();
            log.AppendLine($"rule totals this session: {OzWaterLanding.Landings} landings, {OzWaterLanding.SoundsPlayed} splash sounds played, {OzWaterLanding.SoundsHeld} held back, {OzWaterLanding.Doused} fires out, " +
                           $"{OzWaterLanding.Rested} at rest, {OzWaterLanding.Sunk} gone under, {OzWaterLanding.LetGo} taken back by their own code, {OzWaterLanding.Laps} lap rings");
            Report = $"[OpenZ V1.1] b27 debris into water: {verdict} on {g.level}" + (skipped > 0 ? $" ({skipped} of {FullCount} lines left out, see SKIP)" : $" (full run = {FullCount})");
            log.AppendLine(Report);
            End("count.txt");
        }

        Transform NextWheel()
        {
            // The donor jeep stands and lives; a thrown wheel is destroyed when it sinks or pops. In a match a wheel is thrown only when
            // its vehicle dies, and a dead vehicle's view does not spin its wheels any more. So the donor's wheel spin ends before its
            // first wheel is taken (else it would turn a wheel that is gone, an error line a frame).
            if (jeep) { var spin = jeep.GetComponent<VehicleMotion>(); if (spin) DestroyImmediate(spin); }
            if (jeep) foreach (var mr in jeep.GetComponentsInChildren<MeshRenderer>()) if (mr.enabled && WheeledBreakup.IsWheelName(mr.name) && mr.GetComponent<MeshFilter>()) return mr.transform;
            return null;
        }

        IEnumerator Wheel(bool dry)
        {
            string label = "thrown wheel" + (dry ? " on dry ground" : "");
            var w = NextWheel();
            if (!w) { Skip(label + ": no wheel to throw", dry ? 2 : 6); yield break; }
            var target = dry ? D : S; Cam(target);
            yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial, wid = 9200 + (dry ? 1 : 0), sinks0 = RollingWheel.Sinks;
            RollingWheel.BurnTyre(w);
            var rw = RollingWheel.Throw(w, jeep.transform.position, jeep.transform.forward, 1f, wid, 4242u + (uint)wid);
            if (!rw) { Skip(label + ": the wheel could not be thrown", dry ? 2 : 6); yield break; }
            var from = target + new Vector3(2.2f, 3.2f, 1.1f);
            Aim(rw.GetComponent<Rigidbody>(), from, target + Vector3.up * 0.3f, 0.9f);
            yield return Until(() => Find(s0, wid, null, out _, out _), dry ? 2.5f : 4f);
            yield return new WaitForSeconds(dry ? 2f : 4.2f);
            bool has = Find(s0, wid, null, out var e, out int n);
            if (dry) { Line(!has, $"{label}: no water landing ({n})"); Line(rw, $"{label}: it is still there (rolls, falls flat and pops as before)"); yield break; }
            Line(n == 1, $"{label}: one water landing ({n})");
            if (!has) { Fail(label + ": no landing recorded", 5); yield break; }
            Line(e.fx, $"{label} ({e.size}): droplets drawn");
            Line(e.rings >= (e.size == OzWaterSize.Small ? 1 : 2), $"{label}: {e.rings} rings");
            Line(e.sound != null && e.sound.StartsWith("PLAY"), $"{label}: splash sound: {e.sound}");
            int plays = SplashPlays(e.utime - 0.15f, e.utime + 0.6f, out string seen);
            Line(plays == 1, $"{label}: splash sounds in the sound log: {plays} (must be 1): {seen}");
            if (depthS >= OzWaterLanding.SinkDepth)
                Line(!rw && RollingWheel.Sinks == sinks0 + 1, $"{label} (water {depthS:F2} m deep, it sinks from {OzWaterLanding.SinkDepth:F1} m): it sank away by its own code (gone: {!rw}, sinks counted: {RollingWheel.Sinks - sinks0})");
            else
                Line(RollingWheel.Sinks == sinks0, $"{label} (water only {depthS:F2} m deep, it sinks from {OzWaterLanding.SinkDepth:F1} m): it did not sink (sinks counted: {RollingWheel.Sinks - sinks0}); it rolls on and falls flat on the bed");
        }

        /// <summary>One line: a wheel dropped into water shallower than the rule's sink depth splashes once, does not sink and ends lying on the bed.</summary>
        IEnumerator WheelShallow()
        {
            const string label = "thrown wheel in the shallow place";
            var w = NextWheel();
            if (!w) { Skip(label + ": no wheel to throw", 1); yield break; }
            float depth = Sh.y - g.GroundY(Sh.x, Sh.z);
            Cam(Sh); yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial, wid = 9202, sinks0 = RollingWheel.Sinks;
            RollingWheel.BurnTyre(w);
            var rw = RollingWheel.Throw(w, jeep.transform.position, jeep.transform.forward, 1f, wid, 4242u + (uint)wid);
            if (!rw) { Skip(label + ": the wheel could not be thrown", 1); yield break; }
            var from = Sh + new Vector3(0.5f, 2.6f, 0.3f);
            var rb = rw.GetComponent<Rigidbody>(); Aim(rb, from, Sh + Vector3.up * 0.3f, 0.75f); rb.angularVelocity = Vector3.zero;   // nearly straight down: it should stay in the shallow place
            var lastP = from; float t = 0f;
            while (t < 6.5f) { if (rw) lastP = rw.transform.position; t += Time.deltaTime; yield return null; }
            Find(s0, wid, null, out _, out int n);
            bool sank = !rw && RollingWheel.Sinks > sinks0;
            if (sank && OzWaterLanding.OpenWaterAt(lastP, out float wy2) && wy2 - g.GroundY(lastP.x, lastP.z) >= OzWaterLanding.SinkDepth - 0.05f)
            { Skip($"{label}: it rolled on into water {wy2 - g.GroundY(lastP.x, lastP.z):F2} m deep at {lastP.x:F1}, {lastP.z:F1} and sank there, as the rule says for deep water (run the check again)", 1); yield break; }
            // its lowest point against the bed UNDER THAT POINT (the wheel's own rim points; the box's lowest corner against the bed under
            // the box's middle reads a slope as "in the bed"), the old figure beside it
            float gap = 0f, boxGap = 0f; bool onBed = false;
            if (rw) { gap = -rw.InGround(); onBed = gap >= -0.03f; if (WB(rw.transform, out var b)) boxGap = b.min.y - g.GroundY(b.center.x, b.center.z); }
            Line(n == 1 && !sank && onBed, $"{label} ({depth:F2} m deep, it sinks only from {OzWaterLanding.SinkDepth:F1} m): {n} splash (must be 1), sunk: {sank} (must be False), it lies ON the bed: its lowest rim point is {(-gap):F2} m in the bed under that point (at most 0.03): {onBed}; the box's lowest point against the bed under the box's middle {boxGap:F2} m; wheels lifted onto the ground at rest in this session {RollingWheel.Seated} (most {RollingWheel.SeatedMost:F2} m)");
        }

        bool Redirect(ZObject o, Vector3 to)
        {
            var px = ZGame.WorldToPx(to); bool any = false;
            foreach (var m in W.QueuedMissiles) if (m.debris && m.attackerId == o.Id) { m.x = px.x; m.y = px.y; any = true; }
            foreach (var m in W.PendingMissiles) if (m.debris && m.attackerId == o.Id) { m.x = px.x; m.y = px.y; any = true; }
            return any;
        }

        /// <summary>A real tank dies on `tile`; the landing point of its turret (the game's debris point) is moved to the target.</summary>
        IEnumerator Turret(string label, Vector3 target, Vector2Int tile, bool dry)
        {
            int lines = dry ? 2 : 8;
            var o = W.CreateObject("vehicle", "light", tile.x * 16, tile.y * 16, Team.Neutral);
            if (o == null) { Skip(label + ": no tank could be placed", lines); yield break; }
            g.SpawnUnitView(o);
            Cam(target);
            yield return new WaitForSeconds(1f);
            int s0 = OzWaterLanding.LastSerial, sp0 = LegacyMatch.TurretLandSplashes, bl0 = LegacyMatch.TurretLandBlasts;
            Kill(W, o);
            if (!Redirect(o, target)) { Fail(label + ": the turret's debris point was not found (the tank did not die as a tank?)", lines); yield break; }
            yield return Until(() => LegacyMatch.TurretLandSplashes + LegacyMatch.TurretLandBlasts > sp0 + bl0, 9f);
            yield return new WaitForSeconds(dry ? 1.5f : 3.8f);
            bool has = Find(s0, 0, "turret", out var e, out int n);
            if (dry)
            {
                Line(!has, $"{label}: no water landing ({n})");
                Line(LegacyMatch.TurretLandBlasts == bl0 + 1 && LegacyMatch.TurretLandSplashes == sp0, $"{label}: its landing blast as before (blasts +{LegacyMatch.TurretLandBlasts - bl0}, water landings +{LegacyMatch.TurretLandSplashes - sp0})");
                yield break;
            }
            Line(n == 1, $"{label}: one water landing ({n})");
            if (!has) { Fail(label + ": no landing recorded (did the turret fly? blasts +" + (LegacyMatch.TurretLandBlasts - bl0) + ")", 7); yield break; }
            Line(e.fx, $"{label} ({e.size}): droplets drawn");
            Line(e.rings >= 3, $"{label}: {e.rings} rings (at least 3)");
            Line(e.sound == "PLAY wtr_vehicle_plunge", $"{label}: splash sound: {e.sound}");
            int plays = SplashPlays(e.utime - 0.4f, e.utime + 0.8f, out string seen);
            Line(plays == 1, $"{label}: splash sounds in the sound log round the landing: {plays} (must be 1: no second plunge, no old turret splash): {seen}");
            Line(LegacyMatch.TurretLandSplashes == sp0 + 1 && LegacyMatch.TurretLandBlasts == bl0, $"{label}: no fireball and no scorch on the water (water landings +{LegacyMatch.TurretLandSplashes - sp0}, blasts +{LegacyMatch.TurretLandBlasts - bl0})");
            var go = RootById(e.piece); bool alive = WB(go ? go.transform : null, out var b);
            if (alive && OzWaterLanding.LowHigh(go.transform, out float trueLow, out float trueHigh)) b.SetMinMax(new Vector3(b.min.x, trueLow, b.min.z), new Vector3(b.max.x, trueHigh, b.max.z));   // its real lowest and highest point, as the rule measures them
            float under = alive ? e.waterY - b.min.y : -9f;
            Line(alive && Mathf.Abs(under - e.under) <= 0.06f && under >= 0.05f, $"{label}: at rest its bottom is {under:F2} m under the surface (rule: {e.under:F2} m of its {e.height:F2} m, water {e.waterY - e.bedY:F2} m deep), end {e.end}");
            Line(alive && b.max.y > e.waterY + 0.1f, $"{label}: its top shows {(alive ? b.max.y - e.waterY : 0f):F2} m over the surface");
        }

        IEnumerator DetailPart(Vector3 target, string where)
        {
            string label = "detail part (the detail hub's thrown chunks)" + where;
            var hub = DetailPropHub.I;
            if (!hub) { Skip(label + ": this level has no detail hub", 6); yield break; }
            Cam(target); yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial;
            var go = new GameObject("OZ_TestDetailPart");
            go.transform.localScale = Vector3.one * (0.6f / side);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat; mine.Add(go);
            var from = target + new Vector3(2.2f, 3.2f, 1.1f); go.transform.position = from;
            const float T = 0.9f;
            hub.Chunks.Throw(go.transform, (target - from) / T + Vector3.up * (0.5f * 9.81f * T), new Vector3(200f, 120f, 160f), 12f);
            int id = go.transform.GetInstanceID();
            yield return Until(() => Find(s0, id, null, out _, out _), 4f);
            yield return new WaitForSeconds(4.6f);
            bool has = Find(s0, id, null, out var e, out int n);
            Line(n == 1, $"{label}: one water landing ({n})");
            if (!has) { Fail(label + ": no landing recorded", 5); yield break; }
            Line(e.fx, $"{label} ({e.size}): droplets drawn");
            Line(e.rings >= (e.size == OzWaterSize.Small ? 1 : 2), $"{label}: {e.rings} rings");
            Line(e.sound != null && e.sound.StartsWith("PLAY"), $"{label}: splash sound: {e.sound}");
            int plays = SplashPlays(e.utime - 0.15f, e.utime + 0.6f, out string seen);
            Line(plays == 1, $"{label}: splash sounds in the sound log: {plays} (must be 1): {seen}");
            Line(!go || !go.activeSelf, $"{label}: it sank and was put away by its own code");
        }

        IEnumerator RubbleChips(Vector3 target, string where)
        {
            string label = "rubble chips (FxManager.Breakup, 6 chips)" + where;
            if (!FxManager.I) { Skip(label + ": no FxManager", 2); yield break; }
            Cam(target); yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial;
            FxManager.I.Breakup(target + Vector3.up * 0.8f, "metal", 1.2f, 6, 0.9f);
            yield return new WaitForSeconds(3.2f);
            int n = 0, played = 0, fx = 0;
            foreach (var e in OzWaterLanding.Entries) if (e.serial > s0 && e.kind == "rubble chip") { n++; if (e.fx) fx++; if (e.sound != null && e.sound.StartsWith("PLAY")) played++; }
            Line(n >= 1 && fx >= 1 && fx <= OzWaterLanding.SmallFxPerHalfSecond, $"{label}: {n} chips came down on the water, {fx} small splashes drawn (at most {OzWaterLanding.SmallFxPerHalfSecond} in half a second)");
            Line(played >= 1 && played <= OzWaterLanding.SoundMax, $"{label}: {played} plop sounds played (1 to {OzWaterLanding.SoundMax}), the others held back");
        }

        IEnumerator RobotChips()
        {
            const string label = "robot bits (RobotPopDeath.Chips)";
            Cam(S); yield return new WaitForSeconds(0.4f);
            for (int batch = 0; batch < 3; batch++)
            {
                int s0 = OzWaterLanding.LastSerial;
                RobotPopDeath.Chips(S + Vector3.up * 1f, batch == 1 ? Vector3.right : Vector3.forward, new Color(0.5f, 0.5f, 0.5f), 5, 1.2f, (uint)(777 + batch), false);
                yield return new WaitForSeconds(3.4f);
                int n = 0, played = 0; var ids = new HashSet<int>();
                foreach (var e in OzWaterLanding.Entries) if (e.serial > s0 && e.kind == "robot part") { n++; ids.Add(e.piece); if (e.sound != null && e.sound.StartsWith("PLAY")) played++; }
                if (n == 0) continue;
                Line(ids.Count == n, $"{label}: {n} bits came down on the water, each once ({ids.Count} different bits)");
                Line(played >= 1, $"{label}: {played} plop sounds played");
                yield break;
            }
            Skip(label + ": none of 15 bits came down on the water (it is narrow here)", 2);
        }

        IEnumerator RelayBody()
        {
            const string label = "body with a collision relay (the way a robot's body comes in)";
            Cam(S); yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial; float u0 = Time.unscaledTime;
            var go = Body("OZ_TestRelayBody", S + Vector3.up * 2.2f, 0.8f, 40f);
            AudioDirector.AttachImpacts(go, "dst_ragdoll_impact", "dst_ragdoll_splash");
            go.GetComponent<Rigidbody>().linearVelocity = Vector3.down * 2f;
            yield return new WaitForSeconds(3f);
            bool has = Find(s0, 0, "body (relay)", out var e, out int n);
            Line(n == 1, $"{label}: one water landing ({n})");
            Line(has && e.fx && e.rings >= 1, $"{label}: droplets and {(has ? e.rings : 0)} rings at the surface over the contact");
            Line(has && e.sound == "PLAY dst_ragdoll_splash", $"{label}: its own splash sound: {(has ? e.sound : "-")}");
            Line(Plays(u0, Time.unscaledTime, "dst_ragdoll_impact") == 0, $"{label}: no dry impact sound under water");
            if (go) Destroy(go);
        }

        /// <summary>One line: a clod of the crater kit thrown at the water makes one small landing of the rule and no patter of earth.</summary>
        IEnumerator CraterClod()
        {
            const string label = "crater clod";
            Cam(S); yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial; float u0 = Time.unscaledTime;
            if (!CraterField.OzThrowClod(S + new Vector3(1.2f, 1.6f, 0.6f), S, 0.6f)) { Skip(label + ": no crater clods in this session (deformable ground is switched off in the options, or its kit is not loaded)", 1); yield break; }
            yield return new WaitForSeconds(2.2f);
            int n = 0, fx = 0; string snd = "-";
            foreach (var e in OzWaterLanding.Entries) if (e.serial > s0 && e.kind == "crater clod") { n++; if (e.fx) fx++; snd = e.sound; }
            int patter = Logged(u0, Time.unscaledTime, "dst_crater_clods");
            Line(n == 1 && fx == 1 && snd != null && snd.StartsWith("PLAY") && patter == 0, $"{label} thrown at the water: {n} landing (must be 1), small splash drawn: {fx == 1}, sound: {snd}, patter of earth in the sound log: {patter} (must be 0)");
        }

        IEnumerator Shallow()
        {
            float size = Mathf.Clamp(ClearOf(Sh, 3f) * 1.4f, 1.2f, 2.1f);                                 // a part that fits the water here (a ravine stream is 2 to 3 m wide: a 2 m part would lie across its banks)
            string label = $"building part ({size:F1} m) in the shallow place";
            Cam(Sh); yield return new WaitForSeconds(0.4f);
            int s0 = OzWaterLanding.LastSerial;
            var from = Sh + new Vector3(1.6f, 3.2f, 0.8f);
            var go = BuildingPart(from, size); Aim(go.GetComponent<Rigidbody>(), from, Sh + Vector3.up * 0.3f, 0.9f);
            int id = go.transform.GetInstanceID();
            yield return Until(() => Find(s0, id, null, out _, out _), 4f);
            yield return new WaitForSeconds(3.8f);
            bool has = Find(s0, id, null, out var e, out _);
            if (!has) { Fail(label + ": no landing recorded (it came down beside the water?)", 2); yield break; }
            bool alive = WB(go ? go.transform : null, out var b);
            if (alive && OzWaterLanding.LowHigh(go.transform, out float trueLow, out float trueHigh)) b.SetMinMax(new Vector3(b.min.x, trueLow, b.min.z), new Vector3(b.max.x, trueHigh, b.max.z));   // its real lowest and highest point, as the rule measures them
            float depth = e.waterY - e.bedY, under = alive ? e.waterY - b.min.y : -9f, bed = depth + OzWaterLanding.MudSink;
            bool shallower = e.share * e.height > bed;
            Line(alive && Mathf.Abs(under - (shallower ? bed : e.share * e.height)) <= 0.05f && under >= 0.05f,
                 $"{label}: water {depth:F2} m deep, the part {e.height:F2} m high: its bottom {under:F2} m under the surface = {(shallower ? "on the bed (" + OzWaterLanding.MudSink.ToString("F2") + " m into it)" : "its share")}, never on top");
            Line(alive && b.max.y > e.waterY + 0.1f && b.min.y >= e.bedY - OzWaterLanding.MudSink - 0.05f, $"{label}: its top {(alive ? b.max.y - e.waterY : 0f):F2} m over the surface, not through the bed");
        }

        /// <summary>The real thing: a bridge with a jeep on it is destroyed (the game rebuilds its bridges by its own rule).</summary>
        IEnumerator BridgeShower()
        {
            const string label = "bridge shower";
            ZObject br = null; float bd = float.MaxValue;
            foreach (var o in W.Objects)
            {
                if (o == null || !o.IsBridge || o.Destroyed) continue;
                var c = g.CenterWorld(o); float d = new Vector2(c.x - S.x, c.z - S.z).sqrMagnitude;
                if (d < bd) { bd = d; br = o; }
            }
            if (br == null) { Skip(label + ": no standing bridge on this level", 6); yield break; }
            var bc = g.CenterWorld(br); Cam(bc);
            var j = W.CreateObject("vehicle", "jeep", br.CenterX - 16, br.CenterY - 16, Team.Neutral);
            if (j != null) g.SpawnUnitView(j);
            yield return new WaitForSeconds(1.2f);
            UnitView jv = null; if (j != null) g.Views.TryGetValue(j.Id, out jv);
            int jid = jv ? jv.transform.GetInstanceID() : 0;                                            // the rule records a wreck hull under its view's transform
            // the two counts of splash sounds below span the same frames, both by time: from the frame of the kill on. (Before, the
            // sound log was counted by time and the rule by number: a landing made earlier in the very frame the count began, by a
            // body's contact or a thrown chip, had that frame's time but an older number, which gave "one more in the log than by
            // the rule" on a long frame.)
            int s0 = OzWaterLanding.LastSerial;
            yield return null;
            float u0 = Time.unscaledTime;
            Kill(W, br);
            yield return new WaitForSeconds(10f);
            int hull = 0, chunks = 0, rulePlays = 0, wood = 0; var ids = new HashSet<int>(); OzWaterLanding.Entry he = default;
            var played = new List<OzWaterLanding.Entry>();
            foreach (var e in OzWaterLanding.Entries)
            {
                if (e.serial <= s0) continue;
                if (jid != 0 && e.piece == jid) { if (hull++ == 0) he = e; }                              // this jeep's wreck, not another vehicle that died in a ford meanwhile
                if (e.kind == "bridge chunk") { chunks++; ids.Add(e.piece); if (e.held && !e.sinks && Mathf.Abs(e.share - OzWaterLanding.ShareFloats) < 0.001f) wood++; }
                if (e.utime >= u0 && e.sound != null && e.sound.StartsWith("PLAY")) { rulePlays++; played.Add(e); }
            }
            if (j == null || !j.Destroyed || jid == 0) Skip(label + ": the jeep " + (j == null ? "could not be placed on the bridge" : jid == 0 ? "has no view" : "did not die with the bridge") + ": its wreck's two lines", 2);
            else
            {
                Line(hull == 1, $"{label}: the jeep's wreck fell into the water once ({hull})");
                Line(hull >= 1 && he.under >= 0.05f && he.fx, $"{label}: the wreck: droplets drawn, {he.rings} rings, sound {he.sound}, it settles {he.under:F2} m under the surface ({he.height:F2} m high, water {he.waterY - he.bedY:F2} m deep)");
            }
            if (!g.Prefab($"Z_bridge_{(g.level == "L17" ? "city" : g.level == "L16" ? "jungle" : "desert")}_chunks"))
                Skip(label + ": this level's bridge has no chunk set among the game's prefabs (it throws no chunks): the two chunk lines", 2);
            else
            {
                Line(chunks >= 3, $"{label}: {chunks} of the bridge's chunks came down on the water (at least 3)");
                Line(ids.Count == chunks, $"{label}: each of them once ({ids.Count} different chunks)");
            }
            // spacing and cap on the sounds the rule played
            int gapBad = 0, capBad = 0;
            for (int i = 0; i < played.Count; i++)
            {
                if (i > 0 && played[i].utime - played[i - 1].utime < OzWaterLanding.SoundGap - 0.001f && (int)played[i].size <= (int)played[i - 1].size) gapBad++;
                if (played[i].size == OzWaterSize.Large) continue;
                int before = 0; for (int k = 0; k < i; k++) if (played[i].utime - played[k].utime <= OzWaterLanding.SoundWindow) before++;
                if (before >= OzWaterLanding.SoundMax) capBad++;
            }
            Line(gapBad == 0 && capBad == 0, $"{label}: {rulePlays} splash sounds played for the whole shower, none stacked (closer than {OzWaterLanding.SoundGap:F2} s: {gapBad}, over {OzWaterLanding.SoundMax} in {OzWaterLanding.SoundWindow:F0} s: {capBad})");
            int logPlays = SplashPlays(u0, Time.unscaledTime, out string heardKeys);
            var ruleKeys = new StringBuilder(); foreach (var e in played) ruleKeys.Append(e.sound.Substring(5)).Append(' ');
            Line(logPlays == rulePlays, $"{label}: every splash sound in the sound log is one of the rule's ({logPlays} in the log, {rulePlays} by the rule)" + (logPlays == rulePlays ? "" : $"; in the log: {heardKeys}; by the rule: {ruleKeys}"));
            if (g.level == "L16") Line(wood >= 1, $"{label}: the jungle bridge is planks: {wood} of its {chunks} chunks on the water are held as wood and rest {OzWaterLanding.ShareFloats:0.00} of their height under (at least 1; a small chip still goes under)");
        }

        // ================================================================================================ the owner's pictures
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ProofCapture.Frame();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            log.AppendLine("picture " + Path.Combine(dir, name + ".png").Replace('\\', '/'));
        }

        /// <summary>The checker's second look: the same place from the camera's lowest height (not a picture for the owner).</summary>
        IEnumerator LowLook(string name)
        {
            if (!rts) yield break;
            float h = rts.height; rts.SetHeightImmediate(26f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot(name);
            rts.SetHeightImmediate(h);
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator Pictures()
        {
            yield return null;
            if (!Begin(2.5f)) { log.AppendLine("no place for the pictures on this level (see the lines above)"); Report = "no place for the pictures"; End("pictures.txt"); yield break; }
            string lv = g.level;
            Cam(S);
            yield return new WaitForSeconds(1f);
            yield return Shot(lv + "_0_water_before");
            // ---- the burnt turret of a killed tank
            force0 = WreckFx.Force; WreckFx.Force = 2;                                                   // a plain wreck: it does not break or cook off, so its turret is not pulled under before the "at rest" picture (End puts it back)
            var o = W.CreateObject("vehicle", "light", banks[0].x * 16, banks[0].y * 16, Team.Neutral);
            if (o != null)
            {
                g.SpawnUnitView(o);
                yield return new WaitForSeconds(1.2f);
                int s0 = OzWaterLanding.LastSerial;
                Kill(W, o);
                bool moved = Redirect(o, S);
                yield return Until(() => Find(s0, 0, "turret", out _, out _), 9f);
                if (Find(s0, 0, "turret", out var e, out _))
                {
                    yield return new WaitForSeconds(1f);
                    yield return Shot(lv + "_1_turret_1s_after_landing");
                    yield return new WaitForSeconds(3f);
                    yield return Shot(lv + "_2_turret_at_rest");
                    yield return LowLook(lv + "_look_low_turret_at_rest");
                    Find(s0, 0, "turret", out e, out _);
                    log.AppendLine($"turret: {e.size}, {e.rings} rings, sound {e.sound}, {e.under:F2} m of its {e.height:F2} m under the surface, water {e.waterY - e.bedY:F2} m deep, end {e.end} (1 = at rest)");
                }
                else log.AppendLine("the turret did not come down on the water (debris point moved: " + moved + ")");
            }
            else log.AppendLine("no tank could be placed");
            // ---- a torn wreck piece (one that rests: not a thin plate, not a small bit)
            yield return SpawnJeep();
            var aside = S + new Vector3(clearS > 3f ? 2.2f : 0.9f, 0f, clearS > 3f ? -1.4f : 0.9f);
            if (!OzWaterLanding.OpenWaterAt(aside, out _)) aside = S;
            int s1 = OzWaterLanding.LastSerial;
            var from = aside + new Vector3(2.2f, 3.2f, 1.1f);
            var piece = TornPart(from, true);
            if (!piece) { piece = TornPart(from, false); if (piece) log.AppendLine("no part of the test jeep is thick enough to rest on the water: a thin one is thrown (it goes under; the at-rest picture shows the water after it)"); }
            if (piece)
            {
                var wp = piece.GetComponent<WreckPieces>(); if (wp) wp.OzIgnite(8f);
                Aim(piece.GetComponent<Rigidbody>(), from, aside + Vector3.up * 0.3f, 0.9f);
                int id = piece.transform.GetInstanceID();
                yield return Until(() => Find(s1, id, null, out _, out _), 4f);
                if (Find(s1, id, null, out var e, out _))
                {
                    yield return new WaitForSeconds(1f);
                    yield return Shot(lv + "_3_wreck_piece_1s_after_landing");
                    yield return new WaitForSeconds(3f);
                    yield return Shot(lv + "_4_wreck_piece_at_rest");
                    yield return LowLook(lv + "_look_low_wreck_piece_at_rest");
                    Find(s1, id, null, out e, out _);
                    log.AppendLine($"wreck piece: {e.size}, {e.rings} rings, sound {e.sound}, fire out {e.doused}, {e.under:F2} m of its {e.height:F2} m under the surface, {(e.sinks ? "it went under" : "it rests")}, end {e.end} (1 = at rest)");
                }
                else log.AppendLine("the wreck piece did not come down on the water");
            }
            else log.AppendLine("no part could be torn off the test jeep");
            Report = "pictures of " + lv + " in " + dir.Replace('\\', '/');
            log.AppendLine(Report);
            End("pictures.txt");
        }
    }
}
