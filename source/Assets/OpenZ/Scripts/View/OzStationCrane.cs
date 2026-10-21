using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The repair station's crane gets out of the playable shuttle's way (rule DV118, view only): the shuttle lands ON the station's deck
    /// (the rules' deck point and heading, ZObject.ShuttleDeckPoint / ZWorld.ShuttleDeckHeadingDeg), and the station's crane GANTRY (the
    /// beam with its hoses and its trolley, the hook and its cable hanging from it, the east post it stands on) rolls 2.2 m north along the
    /// deck like a gantry crane on its rails, so that nothing is over the ship.
    ///  * When: ZWorld.ShuttleCraneAway(station) is true (a shuttle called it: hovering over the deck, coming down, standing on it, being
    ///    repaired, climbing off). It rolls away from the call over exactly ZWorld.ShuttleCraneMoveSec on the rules' clock (the rules start
    ///    the descent only then), eased at both ends; when the call ends it rolls back the same way. Its sounds: the crane hydraulic and the
    ///    hoist chain as it starts, the chain again as it stops.
    ///  * How: the station's body mesh (the one BuildingAnimView draws) is split in two while its crane is away: the gantry's triangles
    ///    (<see cref="InGantry"/>, by their middle in the station's frame) become their own part that moves, the rest stays; the hoist
    ///    (BuildingAnimView's swing with the hook and cable) rides on the moving part and its hook stays up while the shuttle is repaired;
    ///    the trolley's two lamps follow it. Once it is back the body mesh is put back whole: every other vehicle's repair and the
    ///    station's look are as before.
    ///  * The deck: <see cref="DeckY"/> = the deck's own height under a point (the game's ground has no deck), so the shuttle stands on
    ///    its four pads on the deck, not in it.
    /// The station frame: metres from the model's middle, x east, z north, y up from its base (research/U4_death_repair_audio.md 4.2; the
    /// model sits in the station's root with no turn, so frame = (-x, y, -z) of the root's own space). The zone and the slide were measured
    /// off the editor against the ship's real parts (tools/c_view_deck_fit.py: change one, change the other).
    /// </summary>
    public static partial class OzStationCrane
    {
        public const float SlideM = 5.0f;                                                                // K256e: was 2.2 (FIX_LIST M8: at least 5 m, fully off the deck's north end)
        /// <summary>K256e: past SettleFrom of its way (off the deck) the gantry settles onto the ground north of the station (its east post's
        /// foot on the ground there, at most SettleMaxM); where that place is taken (a building, hut, rock or crate, a tree crown) it rolls
        /// only to its rail's end (RailEndM) and lifts LiftM instead (the beam 1.5 m over the ship's dome).</summary>
        public static float SettleFrom = 0.68f, SettleMaxM = 1.2f, RailEndM = 0.9f, LiftM = 4.6f;   // K342b: LiftM was 3.6 (the lifted east post went 0.72 m into the landed ship's right pod; 4.6 keeps it 0.1 m clear)
        /// <summary>Proof: how the last split parked its gantry.</summary>
        public static string ParkLog = "-";
        /// <summary>Proof: splits made, moves begun away / back, frames a station's crane was not clear while a shuttle was under 7 m over its deck.</summary>
        public static int Splits, Aways, Backs, SplitFailures, BlockedFrames;
        public static string LastLog = "-";

        /// <summary>The gantry's triangles by their middle (station frame): the beam and the hoses under it, the beam over the west tower,
        /// the east post.</summary>
        public static bool InGantry(Vector3 c)
        {
            if (c.z < 0.7f || c.z > 2.9f) return false;
            if (c.z >= 0.8f && c.y >= 4.1f && c.x >= -3.0f && c.x <= 2.9f) return true;
            if (c.y >= 4.6f && c.x >= -4.8f) return true;
            return c.x >= 2.85f && c.y >= 0.95f;
        }

        sealed class St
        {
            public int id; public GameObject go; public Transform root; public MeshFilter mf; public MeshRenderer mr;
            public Mesh whole, rest, part; public GameObject gantry; public MeshRenderer gr; public Transform swing, swingParent;
            public float k; public bool away, moving, failed; public double moveFrom = -1; public float kFrom; public int dir;   // dir +1 away, -1 back
            public float[] deck; public float deckX0, deckZ0; public int deckN; public Vector3 restLocal;
            public float slide = SlideM, settle, lift; public string park = "";                         // K256e: where it parks (Park)
        }
        static readonly Dictionary<int, St> st = new Dictionary<int, St>();
        static int frame = -1; static ZGame lastGame; static MaterialPropertyBlock mpb;

        /// <summary>Once a frame (the buildings' view BuildingAnimView.Update, and any shuttle's LateUpdate): each station whose crane a
        /// shuttle wants away rolls it, and back. The buildings' view steps it every frame, so the crane also rolls back and the station
        /// is whole again when no shuttle is drawn any more (one shot down on the deck or over it).</summary>
        public static void Tick(ZGame g)
        {
            if (frame == Time.frameCount || g == null || g.World == null) return;
            frame = Time.frameCount;
            if (lastGame != g) { st.Clear(); lastGame = g; }
            var w = g.World;
            // the stations a shuttle calls now (ShuttleCraneStation), with the earliest call time
            for (int i = 0; i < OzPlayableShuttle.Count; i++)
            {
                var sv = OzPlayableShuttle.At(i); var o = sv ? sv.Obj : null;
                if (o == null || o.Destroyed || o.ShuttleCraneStation < 0) continue;
                if (!st.ContainsKey(o.ShuttleCraneStation)) st[o.ShuttleCraneStation] = new St { id = o.ShuttleCraneStation };
            }
            foreach (var s in st.Values) Step(g, w, s);
            RamsTick(g);                                                                                 // K342: the crane's hydraulic rams on every repair station, posed after the gantries (View/OzStationCrane.Rams.cs)
        }

        static void Step(ZGame g, ZWorld w, St s)
        {
            var b = w.Get(s.id);
            bool want = b != null && !b.Destroyed && w.ShuttleCraneAway(s.id);
            if (b == null || b.Destroyed || !g.StaticViews.TryGetValue(s.id, out var go) || !go || !go.activeInHierarchy) { Unsplit(s); s.k = 0f; s.away = false; return; }
            double call = -1;
            if (want)
                for (int i = 0; i < OzPlayableShuttle.Count; i++)
                {
                    var sv = OzPlayableShuttle.At(i); var o = sv ? sv.Obj : null;
                    if (o == null || o.Destroyed || o.ShuttleCraneStation != s.id || o.ShuttleCraneCallAt < 0) continue;
                    if (call < 0 || o.ShuttleCraneCallAt < call) call = o.ShuttleCraneCallAt;
                }
            if (want && !s.away)
            {   // the call: away over the rules' move time from the call (from where it is now, if it was on its way back)
                s.away = true; s.dir = 1; s.kFrom = s.k; s.moveFrom = call >= 0 ? call : w.Time; s.moving = true; Aways++;
                if (!Split(g, s, go)) { s.failed = true; return; }
                Sound(s, true);
            }
            else if (!want && s.away)
            {
                s.away = false; s.dir = -1; s.kFrom = s.k; s.moveFrom = w.Time; s.moving = true; Backs++;
                Sound(s, true);
            }
            if (s.moving)
            {
                float T = (float)ZWorld.ShuttleCraneMoveSec * (s.dir > 0 ? Mathf.Max(0.05f, 1f - s.kFrom) : Mathf.Max(0.05f, s.kFrom));
                float u = Mathf.Clamp01((float)(w.Time - s.moveFrom) / Mathf.Max(0.05f, T));
                float e = u * u * u * (u * (u * 6f - 15f) + 10f);                                         // eased at both ends (speed and push 0)
                s.k = s.dir > 0 ? Mathf.Lerp(s.kFrom, 1f, e) : Mathf.Lerp(s.kFrom, 0f, e);
                if (u >= 1f) { s.moving = false; Sound(s, false); }
            }
            if (s.gantry)
            {   // where it is, and the body's paint (team colour, scorch) on it too
                s.gantry.transform.localPosition = s.restLocal + Offset(s, s.k);
                mpb ??= new MaterialPropertyBlock();
                if (s.gr && s.mr && s.part) for (int k = 0; k < s.part.subMeshCount; k++) { s.mr.GetPropertyBlock(mpb, k); s.gr.SetPropertyBlock(mpb, k); }
            }
            if (!s.away && !s.moving && s.k <= 0f) Unsplit(s);
        }

        /// <summary>The gantry's local offset (in the body's own space) for its slide k (0..1).</summary>
        static Vector3 Offset(St s, float k)
        {
            if (!s.mf || !s.root) return Vector3.zero;
            var world = s.root.TransformVector(Move(s, k));                                              // K256e: north (the root's -z), settled or lifted
            return s.mf.transform.InverseTransformVector(world);
        }

        static MeshFilter Body(GameObject go)
        {
            MeshFilter best = null; int n = 0;
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!f.sharedMesh || f.name.StartsWith("anim_") || f.name.StartsWith("OZ_")) continue;
                var r = f.GetComponent<MeshRenderer>(); if (!r || !r.sharedMaterial) continue;
                if (f.sharedMesh.vertexCount > n) { n = f.sharedMesh.vertexCount; best = f; }
            }
            return best;
        }

        static Vector3 Frame(St s, Matrix4x4 m, Vector3 v) { var p = s.root.InverseTransformPoint(m.MultiplyPoint3x4(v)); return new Vector3(-p.x, p.y, -p.z); }

        static bool Split(ZGame g, St s, GameObject go)
        {
            if (s.gantry) return true;
            s.go = go; s.root = go.transform; s.mf = Body(go); s.mr = s.mf ? s.mf.GetComponent<MeshRenderer>() : null;
            if (!s.mf || !s.mr || !s.mf.sharedMesh || !s.mf.sharedMesh.isReadable) { SplitFailures++; LastLog = "station #" + s.id + ": no readable body mesh: the crane cannot move"; Debug.LogError("[OpenZ] " + LastLog); return false; }
            var mesh = s.mf.sharedMesh; s.whole = mesh;
            var vs = mesh.vertices; var m = s.mf.transform.localToWorldMatrix;
            var F = new Vector3[vs.Length]; for (int i = 0; i < vs.Length; i++) F[i] = Frame(s, m, vs[i]);
            int subs = mesh.subMeshCount, nG = 0, nAll = 0;
            var restT = new List<int>[subs]; var partT = new List<int>[subs];
            var FP = F; bool clean = CleanSplit(s, mesh, F, ref FP, restT, partT, ref nG, ref nAll);   // K342b: the clean cut with closed caps (View/OzStationCrane.CleanSplit.cs); the old split below only if it does not fit
            for (int k = 0; !clean && k < subs; k++)
            {
                var t = mesh.GetTriangles(k); restT[k] = new List<int>(t.Length); partT[k] = new List<int>();
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var c = (F[t[i]] + F[t[i + 1]] + F[t[i + 2]]) / 3f; nAll++;
                    var l = InGantry(c) ? partT[k] : restT[k]; if (l == partT[k]) nG++;
                    l.Add(t[i]); l.Add(t[i + 1]); l.Add(t[i + 2]);
                }
            }
            // the measured station: about 7400 of its 20600 triangles are the gantry; anything far off = another model (refused, the log says so)
            if (nG < 0.2f * nAll || nG > 0.5f * nAll) { SplitFailures++; LastLog = $"station #{s.id}: {nG} of {nAll} triangles in the gantry's zone (another model?): the crane cannot move"; Debug.LogError("[OpenZ] " + LastLog); return false; }
            Mesh Make(List<int>[] T, string name)
            {
                var r = Object.Instantiate(mesh); r.name = name;
                r.subMeshCount = subs; for (int k = 0; k < subs; k++) r.SetTriangles(T[k], k, false);
                r.RecalculateBounds(); return r;
            }
            if (!clean) { s.rest = Make(restT, mesh.name + "_without_crane"); s.part = Make(partT, mesh.name + "_crane"); }   // K342b: the clean cut made its own
            BuildDeck(s, F, mesh);
            Park(g, s, FP, partT);                                                                       // K256e: where the gantry parks (K342b: the cut's points)
            s.mf.sharedMesh = s.rest;
            var gg = new GameObject("OZ_StationCrane") { layer = s.mf.gameObject.layer };
            gg.transform.SetParent(s.mf.transform, false); s.restLocal = Vector3.zero;
            gg.AddComponent<MeshFilter>().sharedMesh = s.part;
            var gr = gg.AddComponent<MeshRenderer>(); gr.sharedMaterials = s.mr.sharedMaterials; gr.shadowCastingMode = s.mr.shadowCastingMode; gr.receiveShadows = s.mr.receiveShadows;
            mpb ??= new MaterialPropertyBlock(); for (int k = 0; k < subs; k++) { s.mr.GetPropertyBlock(mpb, k); gr.SetPropertyBlock(mpb, k); }
            s.gantry = gg; s.gr = gr;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "HoistSwing") { s.swing = t; s.swingParent = t.parent; t.SetParent(gg.transform, true); break; }
            Splits++; LastLog = $"station #{s.id}: crane split ({nG} of {nAll} triangles){(s.swing ? ", the hoist rides on it" : ", no hoist found")}";
            Debug.Log("[OpenZ] shuttle (DV118): " + LastLog);
            return true;
        }

        static void Unsplit(St s)
        {
            if (!s.gantry) return;
            if (s.swing) s.swing.SetParent(s.swingParent ? s.swingParent : (s.mf ? s.mf.transform : null), true);
            if (s.mf && s.mf.sharedMesh == s.rest && s.whole) s.mf.sharedMesh = s.whole;
            Object.Destroy(s.gantry); s.gantry = null; s.swing = null;
            if (s.rest) Object.Destroy(s.rest); if (s.part) Object.Destroy(s.part); s.rest = s.part = null;
        }

        static void Sound(St s, bool start)
        {
            var at = s.gantry ? s.gantry.GetComponent<Renderer>().bounds.center : (s.root ? s.root.position + Vector3.up * 5f : Vector3.zero);
            if (start && AudioDirector.Has("shuttle_station_crane")) AudioDirector.Fire("shuttle_station_crane", at, s.id, 1f);
            // K256e: the hoist's own louder row at the shuttle (at least 0.12); the hook's reel-up is BuildingAnimView's, so here only the
            // gantry's stop clank of the chain
            string hoist = AudioDirector.Has("shuttle_crane_hoist") ? "shuttle_crane_hoist" : "bld_repair_hoist";
            if (!start && AudioDirector.Has(hoist)) AudioDirector.Fire(hoist, at, s.id, 0.8f, 0.85f);
        }

        /// <summary>K256e: the gantry's offset in the root's own space for its slide k (0..1): north by its slide, settled onto the ground
        /// once off the deck (from SettleFrom of its way), or lifted (the place north taken).</summary>
        static Vector3 Move(St s, float k)
        {
            float u = Mathf.Clamp01((k - SettleFrom) / Mathf.Max(0.01f, 1f - SettleFrom)), sk = u * u * (3f - 2f * u), lk = k * k * (3f - 2f * k);
            return new Vector3(0f, s.lift * lk - s.settle * sk, -s.slide * k);
        }

        /// <summary>K256e: where the gantry parks: SlideM north, its east post's foot settled onto the ground there; or, where that place is
        /// taken (any other static object's drawing, or a plant crown of the level), to the rail's end and lifted.</summary>
        static void Park(ZGame g, St s, Vector3[] F, List<int>[] partT)
        {
            s.slide = SlideM; s.settle = 0f; s.lift = 0f;
            float x0 = 99f, x1 = -99f, z0 = 99f, z1 = -99f, y1 = -99f, postY = 99f; Vector3 post = Vector3.zero;
            foreach (var T in partT)
                for (int i = 0; i < T.Count; i++)
                {
                    var p = F[T[i]];
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); y1 = Mathf.Max(y1, p.y);
                    if (p.x >= 2.85f && p.y < postY) { postY = p.y; post = p; }
                }
            Vector3 W(Vector3 fr) => s.root.TransformPoint(new Vector3(-fr.x, fr.y, -fr.z));
            var box = new Bounds(W(new Vector3(x0, 0.3f, z0 + SlideM)), Vector3.zero);
            foreach (var c in new[] { new Vector3(x0, 0.3f, z0 + SlideM), new Vector3(x1, 0.3f, z0 + SlideM), new Vector3(x0, y1, z1 + SlideM), new Vector3(x1, y1, z1 + SlideM) }) box.Encapsulate(W(c));
            string taken = null;
            if (g != null)
                foreach (var kv in g.StaticViews)
                {
                    if (kv.Key == s.id || !kv.Value || !kv.Value.activeInHierarchy) continue;
                    foreach (var r in kv.Value.GetComponentsInChildren<Renderer>()) if (r.enabled && r.bounds.Intersects(box)) { taken = kv.Value.name; break; }
                    if (taken != null) break;
                }
            var fw = OzFloraWind.I;
            if (taken == null && fw)
                foreach (var c in fw.Crowns)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(box.min.x - c.x, c.x - box.max.x)), dz = Mathf.Max(0f, Mathf.Max(box.min.z - c.z, c.z - box.max.z));
                    if (dx * dx + dz * dz < c.w * c.w) { taken = "a plant crown"; break; }
                }
            if (taken == null)
            {
                var pw = W(post + new Vector3(0f, 0f, SlideM));
                float gy = g != null ? g.GroundY(pw.x, pw.z) : pw.y - postY;
                s.settle = Mathf.Clamp(pw.y - gy, -0.5f, SettleMaxM);
                s.park = $"parked {SlideM:F1} m north, fully off the deck (its south edge {z0 + SlideM:F2} m, the deck ends at 4.10), settled {s.settle:F2} m onto the ground";
            }
            else
            {
                s.slide = RailEndM; s.lift = LiftM;
                s.park = $"the place north is taken ({taken}): {RailEndM:F1} m to the rail's end and lifted {LiftM:F1} m";
            }
            ParkLog = "station #" + s.id + ": " + s.park;
            Debug.Log("[OpenZ] shuttle (K256e): " + ParkLog);
        }

        // ------------------------------------------------------------------ the deck's height (0.2 m cells of the deck-level triangles)
        const float DeckCell = 0.2f;
        static void BuildDeck(St s, Vector3[] F, Mesh mesh)
        {
            s.deckX0 = -5f; s.deckZ0 = -4.2f; s.deckN = 52; s.deck = new float[s.deckN * s.deckN];
            for (int i = 0; i < s.deck.Length; i++) s.deck[i] = -99f;
            for (int k = 0; k < mesh.subMeshCount; k++)
            {
                var t = mesh.GetTriangles(k);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = F[t[i]], b = F[t[i + 1]], c = F[t[i + 2]];
                    if (a.y > 1.35f || b.y > 1.35f || c.y > 1.35f) continue;
                    float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    for (int ix = Mathf.Max(0, (int)((x0 - s.deckX0) / DeckCell)); ix <= Mathf.Min(s.deckN - 1, (int)((x1 - s.deckX0) / DeckCell)); ix++)
                        for (int iz = Mathf.Max(0, (int)((z0 - s.deckZ0) / DeckCell)); iz <= Mathf.Min(s.deckN - 1, (int)((z1 - s.deckZ0) / DeckCell)); iz++)
                        {
                            float px = s.deckX0 + (ix + 0.5f) * DeckCell, pz = s.deckZ0 + (iz + 0.5f) * DeckCell;
                            if (!Bary(a, b, c, px, pz, out float y)) continue;
                            int q = iz * s.deckN + ix; if (y > s.deck[q]) s.deck[q] = y;
                        }
                }
            }
        }

        static bool Bary(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float y)
        {
            y = 0f; float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Mathf.Abs(d) < 1e-8f) return false;
            float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d, w = 1f - u - v;
            if (u < -0.02f || v < -0.02f || w < -0.02f) return false;
            y = u * a.y + v * b.y + w * c.y; return true;
        }

        /// <summary>The station's deck height (world y) under a world point; false when the point is not over its deck (then the ground).</summary>
        public static bool DeckY(int stationId, Vector3 world, out float y)
        {
            y = 0f;
            if (!st.TryGetValue(stationId, out var s) || s.deck == null || !s.root) return false;
            var p = s.root.InverseTransformPoint(world); float fx = -p.x, fz = -p.z;
            int ix = (int)((fx - s.deckX0) / DeckCell), iz = (int)((fz - s.deckZ0) / DeckCell);
            if (ix < 0 || iz < 0 || ix >= s.deckN || iz >= s.deckN) return false;
            float best = -99f;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int jx = ix + dx, jz = iz + dz; if (jx < 0 || jz < 0 || jx >= s.deckN || jz >= s.deckN) continue;
                    best = Mathf.Max(best, s.deck[jz * s.deckN + jx]);
                }
            if (best < 0.3f) return false;
            y = s.root.TransformPoint(new Vector3(0f, best, 0f)).y; return true;
        }

        /// <summary>The crane is (being) moved away for a shuttle on this station: the hoist's hook stays up.</summary>
        public static bool Away(int stationId) => st.TryGetValue(stationId, out var s) && (s.away || s.k > 0f);
        /// <summary>The crane's slide now (0 at rest .. 1 away).</summary>
        public static float SlideK(int stationId) => st.TryGetValue(stationId, out var s) ? s.k : 0f;
        public static bool Split(int stationId) => st.TryGetValue(stationId, out var s) && s.gantry;

        /// <summary>BuildingAnimView: a lamp on the station's trolley rides with the crane (a point of the gantry's zone, moved by its slide).</summary>
        public static Vector3 Follow(int stationId, Vector3 world)
        {
            if (!st.TryGetValue(stationId, out var s) || !s.gantry || s.k <= 0f || !s.root) return world;
            var p = s.root.InverseTransformPoint(world); var c = new Vector3(-p.x, p.y, -p.z);
            if (!InGantry(c) && !(c.z >= 0.6f && c.z <= 3.0f && c.y >= 4.0f)) return world;
            return world + s.root.TransformVector(Move(s, s.k));                                          // K256e: the gantry's own move
        }

        // ------------------------------------------------------------------ the proof's test (the Unity side of tools/c_view_deck_fit.py)
        /// <summary>Proof: how many of the drawn ship's SH_ vertices are inside the station as it is drawn now (its parts as 0.1 m columns
        /// in the station's frame: a column standing there, a part overhead, the deck itself), the worst depth (m) and where. -1 = no station.</summary>
        public static int ShipHits(int stationId, Transform ship, out float worst, out string where)
        {
            worst = 0f; where = "";
            if (!st.TryGetValue(stationId, out var s) || !s.root || !s.mf || !ship) return -1;
            const float C = 0.1f; const int N = 110; const float X0 = -5.5f, Z0 = -5.5f;
            var H = new float[N * N]; var Lo = new float[N * N]; for (int i = 0; i < H.Length; i++) { H[i] = -1f; Lo[i] = 99f; }
            void Add(Mesh m, Matrix4x4 toWorld)
            {
                var vs = m.vertices; var F = new Vector3[vs.Length];
                for (int i = 0; i < vs.Length; i++) { var p = s.root.InverseTransformPoint(toWorld.MultiplyPoint3x4(vs[i])); F[i] = new Vector3(-p.x, p.y, -p.z); }
                for (int k = 0; k < m.subMeshCount; k++)
                {
                    var t = m.GetTriangles(k);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = F[t[i]], b = F[t[i + 1]], c = F[t[i + 2]];
                        if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;               // the zero-area bounds keepers
                        int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((b - a).magnitude, Mathf.Max((c - a).magnitude, (c - b).magnitude)) / 0.05f), 1, 60);
                        for (int u = 0; u <= n; u++)
                            for (int v = 0; v <= n - u; v++)
                            {
                                float fu = (float)u / n, fv = (float)v / n; var q = a + (b - a) * fu + (c - a) * fv;
                                int ix = (int)((q.x - X0) / C), iz = (int)((q.z - Z0) / C); if (ix < 0 || iz < 0 || ix >= N || iz >= N) continue;
                                int j = iz * N + ix; if (q.y > H[j]) H[j] = q.y; if (q.y > 1.3f && q.y < Lo[j]) Lo[j] = q.y;
                            }
                    }
                }
            }
            Add(s.mf.sharedMesh, s.mf.transform.localToWorldMatrix);
            if (s.gantry) { var gf = s.gantry.GetComponent<MeshFilter>(); if (gf && gf.sharedMesh) Add(gf.sharedMesh, s.gantry.transform.localToWorldMatrix); }
            if (s.swing) foreach (var f in s.swing.GetComponentsInChildren<MeshFilter>()) if (f.sharedMesh && f.sharedMesh.isReadable) Add(f.sharedMesh, f.transform.localToWorldMatrix);
            int hits = 0;
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.name.StartsWith("SH_")) continue;
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = s.root.InverseTransformPoint(m.MultiplyPoint3x4(v)); var q = new Vector3(-p.x, p.y, -p.z);
                    int ix = (int)((q.x - X0) / C), iz = (int)((q.z - Z0) / C); if (ix < 0 || iz < 0 || ix >= N || iz >= N) continue;
                    int j = iz * N + ix; float h = H[j], lo = Lo[j], d = 0f;
                    if (h > 1.3f && lo < 2.2f && q.y < h - 0.05f) d = h - q.y;                                  // a column standing there
                    else if (h > 1.3f && lo >= 2.2f && q.y >= lo - 0.05f && q.y <= h) d = q.y - lo + 0.05f;   // a part overhead
                    else if (h > 0f && h <= 1.3f && q.y < h - 0.08f) d = h - q.y;                              // into the deck
                    if (d <= 0f) continue;
                    hits++; if (d > worst) { worst = d; where = $"{r.name} at ({q.x:F2}, {q.y:F2}, {q.z:F2}) in the station's frame, {d:F2} m in"; }
                }
            }
            return hits;
        }

        /// <summary>The world-space bounds of the moving gantry now (the proof measures the ship against it); false = not split.</summary>
        public static bool GantryBounds(int stationId, out Bounds b)
        {
            b = default; if (!st.TryGetValue(stationId, out var s) || !s.gantry) return false;
            b = s.gantry.GetComponent<Renderer>().bounds; return true;
        }
    }
}
