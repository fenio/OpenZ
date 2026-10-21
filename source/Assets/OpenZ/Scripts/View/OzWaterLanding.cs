using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>Size class of a piece that comes down on water: by its largest side.</summary>
    public enum OzWaterSize { Small = 0, Medium = 1, Large = 2 }

    /// <summary>
    /// The one rule for every piece that falls into water (debris, vehicle parts, blown-off turrets, wreck hulls, building and
    /// bridge chunks, rocks, palm parts, sandbags, robot bits). View only: the rules of the game never read it.
    /// <para><b>Where water is.</b> Open water = under a drawn water surface (the level's WAT_ meshes, the grid the fishing spots
    /// use) with the walk surface (bed, bank or a bridge deck) at least <see cref="MinDepth"/> under it. Not the map's water
    /// tiles: on the Desert the drawn river is wider than its tiles, the ravine streams of the Jungle run over land tiles, and
    /// a bridge deck over a water tile is not water. The height of the surface is the height of the drawn water itself at that
    /// place (kept per cell of that grid), so every pool, stream and step has its own; the water table and the one flat height
    /// are asked only when a water mesh cannot be read.</para>
    /// <para><b>What happens.</b> Once per piece, on the frame its lowest point reaches the surface: droplets and spreading
    /// rings of its size in the game's own splash look (the m12_splash / m12_ripple channels, the level's water tint), one splash
    /// sound of its size through the game's sound events (they play on camera only), with a spacing and a cap so a shower of
    /// pieces never stacks. A burning or smoking piece goes out with a hiss and steam. Then:
    /// a SMALL piece (largest side under <see cref="SmallMax"/>) goes under and is gone after its ripple; so does a THIN one
    /// (a slab, a plate, a plank, a door: under <see cref="ThinMax"/> high when it lies flat), with the splash and the sound of
    /// its size: nothing lies flat on top of the water;
    /// any other MEDIUM or LARGE piece dips, bobs once and settles partly under the surface (<see cref="ShareMedium"/> /
    /// <see cref="ShareLarge"/> of its resting height), lying on its largest face, a little tilted; where the water is
    /// shallower than that it rests on the bed (<see cref="MudSink"/> into it). At rest a faint ring now and then where it
    /// breaks the surface. When the piece's own code ends its life (sinks, pops, fades), the rule lets go.</para>
    /// <para><b>Two ways in.</b> <see cref="Watch"/>: a body is handed over when it is made and the rule finds the moment
    /// itself. <see cref="Enter"/> / <see cref="Splash(Vector3, OzWaterSize, string, float, bool, int, string)"/>: code that
    /// moves a piece by hand calls the rule when its piece reaches the water.</para>
    /// </summary>
    [DefaultExecutionOrder(230)]
    public sealed class OzWaterLanding : MonoBehaviour
    {
        // ================================================================================================ numbers
        /// <summary>Test aid, never saved: false = the rule makes no splash and holds no piece (where water is stays the same).</summary>
        public static bool Enabled = true;
        /// <summary>Largest side (m): under SmallMax = Small, under MediumMax = Medium, else Large.</summary>
        public const float SmallMax = 0.45f, MediumMax = 1.6f;
        /// <summary>A Medium or Large piece that would lie flatter than this (its thinnest side, m) does not rest on the water: it
        /// goes under like a Small one. A part modelled exactly this thick is held (the test leaves half a centimetre).</summary>
        public const float ThinMax = 0.30f;
        /// <summary>A thin piece goes under nearly level: this share of the tilt a resting piece gets (a 1.4 m plank tilted 9
        /// degrees would stand 0.29 m high and show over shallow water).</summary>
        public const float ThinTiltK = 0.3f;
        /// <summary>Water at least this deep takes a piece that sinks by its own code (the rolling wheel) out of sight; in
        /// shallower water it only splashes and stays on the bed.</summary>
        public const float SinkDepth = 0.6f;
        /// <summary>Water shallower than this over the walk surface is a wet film, a bank or a deck: the dry rules.</summary>
        public const float MinDepth = 0.08f;
        /// <summary>Share of the piece's resting height that ends under the surface, and how far it settles into a shallow bed.</summary>
        public const float ShareMedium = 0.33f, ShareLarge = 0.33f, ShareFloats = 0.50f, MudSink = 0.06f;
        /// <summary>Tilt off its flat rest (degrees; a Large piece x TiltLargeK).</summary>
        public const float TiltMin = 4f, TiltMax = 9f, TiltLargeK = 0.75f;
        /// <summary>The blown-off turret lies with a small fixed tilt of its own (degrees, per piece from its id), rolled about its
        /// barrel's line so the barrel stays level and the whole underside stays in the water.</summary>
        public const float TurretTiltMin = 6f, TurretTiltMax = 14f;
        /// <summary>The bob: a damped spring round the rest height (cycles a second, damping ratio, seconds until it is still).</summary>
        public const float BobHzMedium = 0.83f, BobHzLarge = 0.64f, BobDampMedium = 0.30f, BobDampLarge = 0.36f, BobSecondsMedium = 1.8f, BobSecondsLarge = 2.4f;
        /// <summary>Share of the entry speed the piece keeps under water (capped), and how far it rocks while it bobs (degrees).</summary>
        public const float Carry = 0.35f, MaxPlungeSpeed = 3.2f, RockDeg = 3f;
        /// <summary>A Small or a thin piece is under and gone after this long.</summary>
        public const float SinkSeconds = 1.1f;
        /// <summary>Splash sounds: never two within SoundGap (a bigger piece may follow a smaller one), at most SoundMax in any
        /// SoundWindow seconds (a Large piece is never dropped by the cap), a landing within MergeRadius of one that sounded
        /// MergeSeconds ago is covered by it.</summary>
        public const float SoundGap = 0.12f, SoundWindow = 1f, MergeRadius = 2.5f, MergeSeconds = 0.4f;
        public const int SoundMax = 4;
        /// <summary>Small splashes drawn in any half second (a burst of chips draws a few, not twenty).</summary>
        public const int SmallFxPerHalfSecond = 6;
        /// <summary>The faint rings at rest: one every LapMin..LapMax seconds for at most MaxLapping resting pieces, on screen only.</summary>
        public const float LapMin = 1.8f, LapMax = 3.2f;
        public const int MaxLapping = 10;
        /// <summary>A piece that falls in at this speed or faster goes out if it burns (one that only stands in a ford burns on).</summary>
        public const float DouseSpeed = 1.5f;

        // ================================================================================================ record (the counted check reads it)
        /// <summary>One water landing as the rule made it.</summary>
        public struct Entry
        {
            public int serial, piece; public float time, utime; public string kind; public OzWaterSize size; public Vector3 at; public float speed;
            /// <summary>Droplets drawn; rings drawn; what became of the sound ("PLAY id", "HELD spacing / cap / merged", "DROP sound rules", "none").</summary>
            public bool fx; public int rings; public string sound;
            /// <summary>Its fire went out; no splash (it already lay there); came through a collision relay; the rule holds the piece.</summary>
            public bool doused, quiet, relay, held;
            /// <summary>Held pieces: the surface, the bed, the resting height, how far its bottom is under the surface, the share, the tilt (-1 = its own pose).</summary>
            public float waterY, bedY, height, under, share, tiltDeg;
            /// <summary>Its height lying flat (its thinnest side; -1 = it keeps its own pose). sinks = it goes under and is gone
            /// (a Small piece, or a thin one: flat under ThinMax).</summary>
            public float flat; public bool sinks;
            /// <summary>0 = still settling / not held, 1 = at rest, 2 = went under and is gone, 3 = its own code took it back. endTime = when (Time.time).</summary>
            public byte end; public float endTime;
        }
        public static readonly List<Entry> Entries = new List<Entry>();
        public static int Landings, SoundsPlayed, SoundsHeld, Doused, Rested, Sunk, LetGo, Laps, Watching;
        static int serial;
        /// <summary>The serial of the newest landing (the counted check looks at the landings after it).</summary>
        public static int LastSerial => serial;
        static readonly Dictionary<string, int> perKind = new Dictionary<string, int>();
        /// <summary>Landings of one kind so far this session.</summary>
        public static int Count(string kind) => kind != null && perKind.TryGetValue(kind, out var n) ? n : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Entries.Clear(); perKind.Clear(); soundTimes.Clear(); heard.Clear(); smallFx.Clear(); relayRoots.Clear();
            Landings = SoundsPlayed = SoundsHeld = Doused = Rested = Sunk = LetGo = Laps = Watching = serial = 0; inst = null; lastSoundT = -10f; lastSoundSize = -1;
        }

        // ================================================================================================ the one object
        static OzWaterLanding inst;
        bool warmed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded; }
        static void OnLoaded(Scene s, LoadSceneMode m) { if (!inst && (ZGame.I || FindFirstObjectByType<ZGame>())) inst = new GameObject("OzWaterLanding").AddComponent<OzWaterLanding>(); }
        static OzWaterLanding I
        {
            get
            {
                if (inst) return inst;
                if (!Application.isPlaying || !ZGame.I) return null;
                inst = new GameObject("OzWaterLanding").AddComponent<OzWaterLanding>();
                return inst;
            }
        }

        sealed class Rec
        {
            public Transform t; public Rigidbody rb; public Renderer[] rs; public string kind; public int id;
            public byte state;                                           // 0 in the air, 1 bobbing, 2 at rest, 3 going under
            public bool drop, keepPose, floats, seen, falling, pooled;
            public float born, still, dryUntil, reach = -1f, vy, downT = -10f;
            public Vector3 prev, vel, last, downV, shift;
            public int entry = -1; public OzWaterSize size;
            public float waterY, restY, floorY, y, w, damp, clock, turn, lapAt;
            public Quaternion rot0, rot1; public Vector3 axis, drift, baseScale, xz;
        }
        readonly List<Rec> recs = new List<Rec>();
        readonly Dictionary<int, Rec> byId = new Dictionary<int, Rec>();
        readonly HashSet<int> wetOnce = new HashSet<int>();

        void Awake() { if (inst && inst != this) { Destroy(gameObject); return; } inst = this; }
        void OnDestroy() { if (inst == this) inst = null; }

        // ================================================================================================ where water is
        /// <summary>Open water under p (see the class text). waterY = the surface there.</summary>
        public static bool OpenWaterAt(Vector3 p, out float waterY)
        {
            waterY = 0f;
            var g = ZGame.I; if (!g || !Surface(p, out waterY)) return false;
            return g.GroundY(p.x, p.z) <= waterY - MinDepth;
        }

        /// <summary>The same with the walk surface height the caller already has (no ray).</summary>
        public static bool OpenWaterAt(Vector3 p, float groundY, out float waterY) => Surface(p, out waterY) && groundY <= waterY - MinDepth;

        /// <summary>For a caller that asks every frame: false at once while p is more than `above` over the surface.
        /// bedY = the walk surface under p (only when p is that near the surface).</summary>
        public static bool OpenWaterNear(Vector3 p, float above, out float waterY, out float bedY)
        {
            bedY = 0f;
            var g = ZGame.I;
            if (!g || !Surface(p, out waterY)) { waterY = 0f; return false; }
            if (p.y > waterY + above) return false;
            bedY = g.GroundY(p.x, p.z);
            return bedY <= waterY - MinDepth;
        }

        /// <summary>p lies under a drawn water surface, at least MinDepth down (the bed of open water, a piece that lies in it).
        /// What touches something there makes no dust, no clang and no scorch: its splash was made at the surface.</summary>
        public static bool UnderSurface(Vector3 p) => Enabled && Surface(p, out float wy) && p.y < wy - MinDepth + 0.02f;

        /// <summary>A drawn water surface over p and its height: the height of the drawn water itself at p, on every level
        /// (AnticSpot keeps it per cell). Only when the level's water mesh cannot be read (then the water tiles answer where water
        /// is) the height comes from the water table or the one flat height.</summary>
        static bool Surface(Vector3 p, out float waterY)
        {
            waterY = 0f;
            var fxp = OpenZFxPlayer.I; var g = ZGame.I;
            if (!g || !fxp || !fxp.isActiveAndEnabled || float.IsNegativeInfinity(fxp.WaterY)) return false;
            if (!AnticSpot.WaterMeshAt(fxp, p, out float y)) return false;
            waterY = float.IsNaN(y) ? LevelAt(g.level, p, fxp.WaterY) : y;
            return !float.IsNaN(waterY);
        }

        /// <summary>Open water at q by the cached ground grid (no ray): for looking along a line of points round a landing.</summary>
        static bool OpenFast(OpenZFxPlayer fxp, Vector3 q) =>
            AnticSpot.WaterMeshAt(fxp, q, out float y) && fxp.GroundAt(q) <= (float.IsNaN(y) ? fxp.WaterYAt(q) : y) - MinDepth;

        /// <summary>Fallback only (no readable water mesh): one flat height on a level without a table; on a level with one the
        /// tile's own, or the nearest wet tile's within a metre; NaN = none.</summary>
        static float LevelAt(string level, Vector3 p, float flat)
        {
            if (!OzWaterLevel.Has(level)) return flat;
            float v = OzWaterLevel.At(level, p.x, p.z, float.NaN);
            if (!float.IsNaN(v)) return v;
            v = OzWaterLevel.At(level, p.x + 1f, p.z, float.NaN); if (!float.IsNaN(v)) return v;
            v = OzWaterLevel.At(level, p.x - 1f, p.z, float.NaN); if (!float.IsNaN(v)) return v;
            v = OzWaterLevel.At(level, p.x, p.z + 1f, float.NaN); if (!float.IsNaN(v)) return v;
            return OzWaterLevel.At(level, p.x, p.z - 1f, float.NaN);
        }

        /// <summary>Distance from `at` to the edge of the open water (the waterline on the bank, a quay wall, a deck; not the edge of
        /// the water mesh, which lies under the bank), looked for up to `upTo` m (8 directions, quarter-metre steps).
        /// away = the direction from the nearest edge back into the water (zero when no edge is that near).</summary>
        static float Clear(OpenZFxPlayer fxp, Vector3 at, float upTo, out Vector3 away)
        {
            float best = upTo; away = Vector3.zero;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float s = 0.25f; s < best; s += 0.25f)
                    if (!OpenFast(fxp, at + d * s)) { best = s - 0.25f; away = -d; break; }
            }
            return Mathf.Max(0f, best);
        }

        // ================================================================================================ sizes
        public static OzWaterSize SizeOf(float largestSide) => largestSide < SmallMax ? OzWaterSize.Small : largestSide < MediumMax ? OzWaterSize.Medium : OzWaterSize.Large;

        /// <summary>Size class of a piece from what it draws.</summary>
        public static OzWaterSize SizeOf(Transform piece)
        {
            if (!piece || !Bounds(piece.GetComponentsInChildren<Renderer>(), piece, out var b)) return OzWaterSize.Small;
            return SizeOf(Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        }

        static bool Counts(Renderer r)
        {
            if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return false;
            var m = r.sharedMaterial; if (m && m.renderQueue >= 2450) return false;                       // light cones, glows, decals
            var n = r.name; return n.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("label", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>World box of the piece's own drawn parts (a part that has left the piece, like a thrown turret, is not counted).</summary>
        static bool Bounds(Renderer[] rs, Transform root, out Bounds b)
        {
            b = new Bounds(root ? root.position : Vector3.zero, Vector3.zero); bool any = false;
            if (rs == null) return false;
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i]; if (!Counts(r) || !r.gameObject.activeInHierarchy || !r.transform.IsChildOf(root)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>The piece's box in its own turn (centre and size in metres, seen from its pivot with its rotation taken out).</summary>
        static void Box(Transform root, Renderer[] rs, Bounds world, out Vector3 centre, out Vector3 size)
        {
            var inv = Quaternion.Inverse(root.rotation); var pos = root.position;
            var box = new Bounds(); bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i]; if (!Counts(r) || !r.gameObject.activeInHierarchy || !r.transform.IsChildOf(root)) continue;
                if (!r.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds; var M = mf.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    var q = inv * (M.MultiplyPoint3x4(c) - pos);
                    if (!any) { box = new Bounds(q, Vector3.zero); any = true; } else box.Encapsulate(q);
                }
            }
            if (!any) { centre = inv * (world.center - pos); size = world.size; return; }                   // no mesh to read: the world box as it lies
            centre = box.center; size = box.size;
        }

        static readonly List<Vector3> extentPts = new List<Vector3>(4096);
        /// <summary>Proof counters: landings whose resting height was read from the points of the piece's meshes / from the boxes of its meshes.</summary>
        public static int ExtentFromPoints { get; private set; }
        public static int ExtentFromBoxes { get; private set; }

        /// <summary>The piece's real lowest and highest point when its root is turned to q, as heights over its pivot: from the points of
        /// its meshes where they can be read, else from the corners of each mesh's own box. This is its thickness in the pose it
        /// rests in: the share that ends under the surface is taken from it, and its lowest point is what must be under the water.</summary>
        static bool ExtentY(Transform root, Renderer[] rs, Quaternion q, out float low, out float high)
        {
            low = float.MaxValue; high = float.MinValue; bool any = false, boxes = false;
            if (!root || rs == null) return false;
            var turn = Matrix4x4.Rotate(q * Quaternion.Inverse(root.rotation)); var pos = root.position;
            var row = new Vector3(turn.m10, turn.m11, turn.m12);                                            // world offset from the pivot -> height in the pose q
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i]; if (!Counts(r) || !r.gameObject.activeInHierarchy || !r.transform.IsChildOf(root)) continue;
                if (!r.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
                var mesh = mf.sharedMesh; var M = mf.transform.localToWorldMatrix;
                var a = new Vector3(row.x * M.m00 + row.y * M.m10 + row.z * M.m20, row.x * M.m01 + row.y * M.m11 + row.z * M.m21, row.x * M.m02 + row.y * M.m12 + row.z * M.m22);
                float c = row.x * (M.m03 - pos.x) + row.y * (M.m13 - pos.y) + row.z * (M.m23 - pos.z);
                if (mesh.isReadable && mesh.vertexCount > 0 && mesh.vertexCount <= 65000)
                {
                    mesh.GetVertices(extentPts);
                    for (int k = 0; k < extentPts.Count; k++) { var v = extentPts[k]; float y = a.x * v.x + a.y * v.y + a.z * v.z + c; if (y < low) low = y; if (y > high) high = y; }
                }
                else
                {
                    boxes = true; var mb = mesh.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        var v = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                        float y = a.x * v.x + a.y * v.y + a.z * v.z + c; if (y < low) low = y; if (y > high) high = y;
                    }
                }
                any = true;
            }
            if (any) { if (boxes) ExtentFromBoxes++; else ExtentFromPoints++; }
            return any;
        }

        /// <summary>The piece's real lowest and highest point (world heights) as it lies now, measured as the rule measures it (the checks).</summary>
        public static bool LowHigh(Transform piece, out float lowY, out float highY)
        {
            lowY = highY = 0f; if (!piece) return false;
            int p0 = ExtentFromPoints, b0 = ExtentFromBoxes;
            bool ok = ExtentY(piece, piece.GetComponentsInChildren<Renderer>(), piece.rotation, out float lo, out float hi);
            ExtentFromPoints = p0; ExtentFromBoxes = b0;                                                    // a look by a check is not a landing
            if (!ok) return false;
            lowY = piece.position.y + lo; highY = piece.position.y + hi; return true;
        }

        static float HeightOf(Quaternion q, Vector3 size) =>
            Mathf.Abs((q * Vector3.right).y) * size.x + Mathf.Abs((q * Vector3.up).y) * size.y + Mathf.Abs((q * Vector3.forward).y) * size.z;

        // ================================================================================================ way in 1: watched bodies
        /// <summary>Hands a piece to the rule when it is made. The rule looks at it every frame while it flies and takes it over on
        /// the frame it reaches open water. drop = a piece that is not simulated and is left in the air over open water falls
        /// (a wreck whose bridge went). keepPose = it rests as it lies (a wreck standing on its wheels, a palm crown), else on its
        /// largest face. floats = wood: a thin piece of this kind rests on the water like a thick one instead of going under, and it
        /// rests half under (ShareFloats); the palm trunk and the Jungle bridge's planks pass it (a felled jungle tree floats by its
        /// own fall, View/OzTreeFall.cs).</summary>
        public static void Watch(Transform piece, string kind, Rigidbody rb = null, bool drop = false, bool keepPose = false, bool floats = false)
        {
            if (!Enabled || !piece) return;
            var m = I; if (!m) return;
            int id = piece.GetInstanceID();
            if (m.byId.ContainsKey(id)) return;
            var r = new Rec { t = piece, rb = rb, kind = kind ?? "piece", id = id, drop = drop, keepPose = keepPose, floats = floats, born = Time.time, prev = piece.position, last = piece.position,
                              rs = piece.GetComponentsInChildren<Renderer>(true) };
            m.recs.Add(r); m.byId[id] = r; Watching++;
        }

        /// <summary>The rule knows this piece (watched now, or it went into the water earlier).</summary>
        public static bool Watched(Transform piece)
        {
            if (!piece || !inst) return false;
            int id = piece.GetInstanceID();
            return inst.byId.ContainsKey(id) || inst.wetOnce.Contains(id);
        }

        /// <summary>This watched piece has reached the water (it stays true when the rule has let it go).</summary>
        public static bool InWater(Transform piece)
        {
            if (!piece || !inst) return false;
            int id = piece.GetInstanceID();
            return inst.wetOnce.Contains(id) || (inst.byId.TryGetValue(id, out var r) && r.state != 0);
        }

        // ================================================================================================ way in 2: pieces moved by hand
        /// <summary>A piece that its own code moves has reached p = its position: when that is open water the rule makes the splash,
        /// the rings and the sound and, for a Medium or Large piece, takes it over (true: the caller stops moving it). A Small
        /// piece, and a thin one that does not keep its pose, is left to the caller's own sinking (false). False also when this is
        /// not open water (the caller's dry landing). keepPose = it rests in the rotation it has now.</summary>
        public static bool Enter(Transform piece, string kind, Vector3 velocity, bool burning = false, bool keepPose = false)
        {
            if (!Enabled || !piece) return false;
            var m = I; if (!m) return false;
            var rs = piece.GetComponentsInChildren<Renderer>();
            if (!Bounds(rs, piece, out var b)) return false;
            var over = b.center;                                                                            // its middle, or its pivot when the middle hangs over the bank
            if (!OpenWaterAt(over, out float wy)) { over = piece.position; if (!OpenWaterAt(over, out wy)) return false; }
            int id = piece.GetInstanceID();
            if (m.byId.TryGetValue(id, out var old)) m.Forget(old, 3);                                      // a pooled piece thrown again
            var r = new Rec { t = piece, kind = kind ?? "piece", id = id, keepPose = keepPose, born = Time.time, prev = piece.position, last = piece.position, rs = rs, pooled = true };
            float bed = Mathf.Min(ZGame.I.GroundY(over.x, over.z), wy - MinDepth);
            m.EnterWater(r, b, wy, bed, velocity, false, burning);
            if (r.state == 0) return false;                                                                 // Small or thin: only the splash
            m.recs.Add(r); m.byId[id] = r;
            return true;
        }

        /// <summary>A piece that keeps its own way of sinking has reached the water at `at`: the splash, the rings and the sound of
        /// its size, once. sound = another sound event for it (a robot body keeps its body splash).</summary>
        public static void Splash(Vector3 at, OzWaterSize size, string kind, float speed = 4f, bool burning = false, int emitter = -1, string sound = null)
        {
            if (!Enabled || !I) return;
            if (Surface(at, out float wy)) at.y = wy;
            Fire(at, size, kind ?? "piece", speed, burning, emitter, sound, false, false, false);
        }

        /// <summary>The same with the size taken from what the piece draws.</summary>
        public static void Splash(Vector3 at, Transform piece, string kind, float speed = 4f, bool burning = false) =>
            Splash(at, SizeOf(piece), kind, speed, burning, piece ? piece.GetInstanceID() : -1, null);

        /// <summary>AudioImpactRelay: a body touched something. True = that was under water (the relay plays no impact for it):
        /// a piece the rule watches makes its own splash at the surface (it has, or it will this frame when a fast piece touched a
        /// shallow bed before the rule saw it cross the surface); any other body (a robot's ragdoll) gets the splash at the surface
        /// over the contact, with its own sound event. False = the contact is not under a drawn water surface (a bridge deck, a
        /// bank): the relay plays its normal impact.</summary>
        public static bool RelaySplash(Transform body, Vector3 contact, string soundEvent)
        {
            if (!Enabled || !I)
            {   // the rule switched off: the splash sound on a water tile, as before
                if (!OpenZFxPlayer.IsWaterTile(contact)) return false;
                if (soundEvent != null) AudioDirector.Fire(soundEvent, contact, body ? body.GetInstanceID() : -1);
                return true;
            }
            if (Watched(body)) return InWater(body) || UnderSurface(contact);
            if (!Surface(contact, out float wy) || contact.y > wy + 0.05f) return false;
            int root = body ? body.root.GetInstanceID() : 0; float now = Time.time;                         // a ragdoll is many bodies: one splash for the whole of it
            if (relayRoots.TryGetValue(root, out var last) && now - last < 3f) return true;
            if (relayRoots.Count > 256) relayRoots.Clear();
            relayRoots[root] = now;
            var at = new Vector3(contact.x, wy, contact.z);
            var size = OzWaterSize.Medium;
            if (body && body.TryGetComponent<Collider>(out var col)) { var s = col.bounds.size; size = SizeOf(Mathf.Max(s.x, Mathf.Max(s.y, s.z))); }
            Fire(at, size, "body (relay)", 4f, false, body ? body.GetInstanceID() : -1, soundEvent, false, true, false);
            return true;
        }

        // ================================================================================================ the splash, the rings, the sound
        static readonly Dictionary<int, float> relayRoots = new Dictionary<int, float>();
        static readonly List<float> soundTimes = new List<float>(), smallFx = new List<float>();
        static readonly List<Vector4> heard = new List<Vector4>();                                           // x, z, time, size
        static float lastSoundT = -10f; static int lastSoundSize = -1;

        static Color Tint() => OpenZFxCatalog.RippleColorFor(ZGame.I ? ZGame.I.level : null);

        /// <summary>The game's splash droplets (the recipe of OpenZFxCatalog.M12Splash on its own channel, as OpenZFxPlayer.Droplets
        /// emits them) at the height the rule gives them: the drawn surface, also where the map's tile says something else (a
        /// stream over a land tile, a step in a stream).</summary>
        static void Drops(OpenZFxPlayer fxp, Vector3 at, float s, int count)
        {
            var L = OpenZFxCatalog.M12Splash; var tint = Tint(); count = Mathf.Clamp(count, 1, 12);
            for (int j = 0; j < count; j++)
            {
                float a = j * L.angleStep, ca = Mathf.Cos(a), sa = Mathf.Sin(a), pr = L.posRadial.At(j);
                float rad = L.radial.At(j) * (L.radialTimesStrength > 0f ? s : 1f), scale = L.scale.At(j) * (L.scaleTimesStrength ? s : 1f);
                fxp.EmitChannel(L.channel, at + new Vector3(ca * pr, L.posUp.At(j), sa * pr), new Vector3(ca * rad, L.up.At(j), sa * rad), L.life.At(j), scale * L.sizePerScale, tint, L.alpha);
            }
        }

        /// <summary>One ring of the game's ripple look, ended at the nearest edge of the open water (+ 0.4 m under the wet band).</summary>
        static int Ring(OpenZFxPlayer fxp, Vector3 at, float s, float lifeMul, float alpha)
        {
            float maxR = Clear(fxp, at, 4.65f * s, out _) + 0.4f;
            if (4.65f * s > maxR) s = Mathf.Max(0.1f, maxR / 4.65f);
            fxp.EmitChannel("m12_ripple", new Vector3(at.x, at.y + OpenZFxCatalog.RippleLift, at.z), Vector3.zero, OpenZFxCatalog.RippleLife * lifeMul, 2f * OpenZFxCatalog.RippleScale * s, Tint(), alpha);
            return 1;
        }

        static int Fire(Vector3 at, OzWaterSize size, string kind, float speed, bool burning, int emitter, string soundEvent, bool quiet, bool relay, bool held)
        {
            float now = Time.unscaledTime;
            var e = new Entry { serial = ++serial, piece = emitter, time = Time.time, utime = now, kind = kind, size = size, at = at, speed = speed, quiet = quiet, relay = relay, held = held, sound = "none", tiltDeg = -1f, flat = -1f };
            var fxp = OpenZFxPlayer.I; bool live = fxp && fxp.isActiveAndEnabled, on = WreckFx.OnScreen(at, 0.2f);
            float k = Mathf.Clamp(0.7f + speed * 0.05f, 0.7f, 1.15f);
            // ---- droplets and rings
            if (live && on)
            {
                if (quiet) e.rings += Ring(fxp, at, size == OzWaterSize.Large ? 0.5f : 0.3f, 1.4f, 0.6f);     // it lay there already: one soft ring, no spray
                else if (size == OzWaterSize.Small)
                {
                    while (smallFx.Count > 0 && now - smallFx[0] > 0.5f) smallFx.RemoveAt(0);
                    if (smallFx.Count < SmallFxPerHalfSecond)
                    {
                        smallFx.Add(now);
                        Drops(fxp, at, 0.22f * k, 4); e.rings += Ring(fxp, at, 0.16f, 1f, 1f); e.fx = true;
                    }
                }
                else if (size == OzWaterSize.Medium)
                {
                    Drops(fxp, at, 0.45f * k, 12); e.fx = true;
                    e.rings += Ring(fxp, at, 0.45f, 1f, 1f); e.rings += Ring(fxp, at, 0.3f, 1.6f, 0.7f);
                }
                else
                {   // the blown-off turret's splash as it was (1.3, a second crown, a 0.9 ring), with one more wide slow ring
                    Drops(fxp, at, 1.3f * k, 12); Drops(fxp, at + Vector3.up * 0.2f, 1.1f * k, 12); e.fx = true;
                    e.rings += Ring(fxp, at, 1.3f, 1f, 1f); e.rings += Ring(fxp, at, 0.9f, 1.3f, 0.85f); e.rings += Ring(fxp, at, 1.5f, 2.2f, 0.55f);
                }
            }
            // ---- the sound: the event of its size, on camera only (the events' own rule), spaced and capped
            if (!quiet)
            {
                string id = soundEvent ?? (size == OzWaterSize.Small ? "wtr_debris_plop" : size == OzWaterSize.Medium ? "dst_debris_splash" : "wtr_vehicle_plunge");
                float vol = size == OzWaterSize.Small ? 0.7f : size == OzWaterSize.Medium ? Mathf.Lerp(0.75f, 0.95f, Mathf.InverseLerp(2f, 9f, speed)) : 0.9f;
                while (soundTimes.Count > 0 && now - soundTimes[0] > SoundWindow) soundTimes.RemoveAt(0);
                for (int i = heard.Count - 1; i >= 0; i--) if (now - heard[i].z > MergeSeconds) heard.RemoveAt(i);
                string why = null;
                if (now - lastSoundT < SoundGap && (int)size <= lastSoundSize) why = "HELD spacing";
                if (why == null && size != OzWaterSize.Large && soundTimes.Count >= SoundMax) why = "HELD cap";
                if (why == null)
                    foreach (var h in heard)
                        if ((int)size <= (int)h.w && (h.x - at.x) * (h.x - at.x) + (h.y - at.z) * (h.y - at.z) < MergeRadius * MergeRadius) { why = "HELD merged"; break; }
                if (why != null) { e.sound = why; SoundsHeld++; OzSoundLog.Add("WATR", id, null, why + " (" + kind + ")", 0f, at); }
                else
                {
                    bool played = PlaySound(id, size, at, emitter, vol);
                    e.sound = played ? "PLAY " + id : "DROP sound rules";
                    if (played) { SoundsPlayed++; lastSoundT = now; lastSoundSize = (int)size; soundTimes.Add(now); heard.Add(new Vector4(at.x, at.z, now, (int)size)); }
                    OzSoundLog.Add("WATR", id, null, (played ? "splash " : "not played ") + size + " (" + kind + ")", played ? vol : 0f, at);
                }
            }
            // ---- a burning piece goes out: steam and a hiss
            if (burning)
            {
                e.doused = true; Doused++;
                if (live && on)
                    for (int i = 0; i < (size == OzWaterSize.Small ? 2 : size == OzWaterSize.Medium ? 4 : 6); i++)
                    {
                        var j = Random.insideUnitSphere * (size == OzWaterSize.Large ? 0.8f : 0.35f); j.y = Mathf.Abs(j.y) * 0.4f;
                        fxp.EmitChannel("m12_smoke", at + j + Vector3.up * 0.3f, new Vector3(0.1f + j.x * 0.4f, Random.Range(1.2f, 1.9f), 0.05f + j.z * 0.4f), Random.Range(1.1f, 1.7f),
                            Random.Range(0.9f, 1.4f) * (size == OzWaterSize.Large ? 1.5f : 1f), new Color(0.78f, 0.78f, 0.76f), 0.35f);
                    }
                if (AudioDirector.Has("dtl_steam_hiss")) AudioDirector.Fire("dtl_steam_hiss", at, emitter, 1.3f, 1.1f);
                else if (CombatAudio.I) CombatAudio.I.PlayPitched("water_hiss", at, 0.5f, 0.3f, emitter, 1.1f, "sd_robot_death_fire_sizzle");
                OzSoundLog.Add("WATR", "dtl_steam_hiss", null, "fire out (" + kind + ")", 0.5f, at);
            }
            Landings++;
            perKind[kind] = (perKind.TryGetValue(kind, out var n) ? n : 0) + 1;
            if (Entries.Count >= 600) { Entries.RemoveRange(0, 300); if (inst) foreach (var r in inst.recs) r.entry -= 300; }
            Entries.Add(e);
            return Entries.Count - 1;
        }

        static bool PlaySound(string id, OzWaterSize size, Vector3 at, int emitter, float vol)
        {
            if (AudioDirector.I && AudioDirector.Has(id)) return AudioDirector.I.Play(id, at, emitter, vol, 1f);
            var ca = CombatAudio.I; if (!ca) return false;                                                    // no sound table in this scene: the clips themselves
            if (size == OzWaterSize.Small) ca.PlayPitched("water_plop", at, vol, 0.05f, emitter, 1f, "sd_debris_plop_small_a", "sd_debris_plop_small_b", "sd_debris_plop_small_c");
            else if (size == OzWaterSize.Medium) ca.PlayPitched("water_splash", at, vol, 0.05f, emitter, 1f, "sd_debris_splash_a", "sd_debris_splash_b");
            else ca.PlayPitched("water_plunge", at, vol, 0.05f, emitter, 0.9f, "sd_debris_splash_a", "sd_debris_splash_b");
            return true;
        }

        /// <summary>Puts the piece's own fire and smoke out. True = it was burning or smoking.</summary>
        static bool Douse(Transform t)
        {
            bool was = false;
            if (t.TryGetComponent<WreckPieces>(out var wp)) was |= wp.OzDouse();
            if (t.TryGetComponent<BasePropFlamingDebris>(out var fd) && fd.enabled) { fd.enabled = false; was = true; }
            if (t.TryGetComponent<UnitView>(out var uv)) was |= uv.OzDouseWreck();
            return was;
        }

        // ================================================================================================ the piece in the water
        void EnterWater(Rec r, Bounds b, float wy, float bed, Vector3 v, bool quiet, bool burning)
        {
            float sp = v.magnitude;
            Box(r.t, r.rs, b, out var oc, out var os);
            r.size = SizeOf(Mathf.Max(os.x, Mathf.Max(os.y, os.z)));
            // a piece that would lie flat on the water (a slab, a plate, a plank, a door: its thinnest side under ThinMax) does not
            // rest there: it makes the splash and the sound of its size and goes under like a Small one
            float thinSide = Mathf.Min(os.x, Mathf.Min(os.y, os.z));
            bool thin = r.size != OzWaterSize.Small && !r.keepPose && !r.floats && thinSide < ThinMax - 0.005f;
            bool holds = r.size != OzWaterSize.Small && !thin;
            if (!holds || (!quiet && sp >= DouseSpeed)) burning |= Douse(r.t); else burning = false;
            var at = new Vector3(b.center.x, wy, b.center.z);
            r.entry = Fire(at, r.size, r.kind, sp, burning, r.id, null, quiet, false, holds || !r.pooled);
            if (!r.pooled) wetOnce.Add(r.id);
            var ent = Entries[r.entry]; ent.waterY = wy; ent.bedY = bed; ent.flat = r.keepPose ? -1f : thinSide; ent.sinks = !holds; Entries[r.entry] = ent;
            if (!holds && r.pooled) return;                                                                 // a Small or thin piece its own code sinks
            if (r.rb)
            {
                if (!r.rb.isKinematic) { r.rb.linearVelocity = Vector3.zero; r.rb.angularVelocity = Vector3.zero; }
                r.rb.isKinematic = true; r.rb.interpolation = RigidbodyInterpolation.None;
            }
            r.waterY = wy; r.clock = 0f; r.xz = r.t.position; r.y = r.t.position.y; r.baseScale = r.t.localScale;
            r.rot0 = r.rot1 = r.t.rotation; r.turn = 0f;
            uint s = RobotDeathRule.Hash(r.id, 27, 51u) | 1u; float u1 = WreckFx.Rnd(ref s), u2 = WreckFx.Rnd(ref s);
            r.axis = Quaternion.Euler(0f, u2 * 360f, 0f) * Vector3.right;
            float tilt = -1f;
            if (!r.keepPose && r.size != OzWaterSize.Small)
            {   // on its largest face (its thinnest side up or down, whichever is nearer), then a little off the level
                int ax = os.x <= os.y && os.x <= os.z ? 0 : (os.y <= os.z ? 1 : 2);
                var a = r.rot0 * (ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward);
                var flat = Quaternion.FromToRotation(a, a.y >= 0f ? Vector3.up : Vector3.down) * r.rot0;
                tilt = Mathf.Lerp(TiltMin, TiltMax, u1) * (r.size == OzWaterSize.Large ? TiltLargeK : 1f) * (thin ? ThinTiltK : 1f);
                if (r.kind == "turret")
                {   // not perfectly flat: 6 to 14 degrees, to the side its id gives, about the line of its barrel (its longest side)
                    tilt = Mathf.Lerp(TurretTiltMin, TurretTiltMax, u1);
                    var along = flat * (os.x >= os.y && os.x >= os.z ? Vector3.right : os.y >= os.z ? Vector3.up : Vector3.forward); along.y = 0f;
                    if (along.sqrMagnitude > 1e-4f) r.axis = along.normalized * (u2 < 0.5f ? -1f : 1f);
                }
                r.rot1 = Quaternion.AngleAxis(tilt, r.axis) * flat;
            }
            bool large = r.size == OzWaterSize.Large;
            float h, off;                                                                                   // its bottom = pivot + off
            if (ExtentY(r.t, r.rs, r.rot1, out float lowY, out float highY)) { h = highY - lowY; off = lowY; }   // its thickness in the pose it rests in: from its real lowest point up
            else { h = HeightOf(r.rot1, os); off = (r.rot1 * oc).y - h * 0.5f; }
            if (!holds)
            {   // goes under and is gone after its ripple. A Small piece as it is, by its own height + 0.2 m. A thin one lies down
                // flat as it goes (it never stands on edge in the bed): its top ends 0.2 m under the surface, or it ends on a bed
                // that is nearer than that
                foreach (var c in r.t.GetComponentsInChildren<Collider>()) c.enabled = false;
                if (thin) { r.turn = 0.5f; r.restY = Mathf.Min(r.y, Mathf.Max(wy - 0.2f - h - off, bed - MudSink - off)); }
                else r.restY = r.y - (Mathf.Max(0.25f, b.size.y) + 0.2f);
                r.state = 3; r.last = r.t.position;
                return;
            }
            float share = r.floats ? ShareFloats : large ? ShareLarge : ShareMedium, under = Mathf.Max(0.05f, Mathf.Min(share * h, wy - bed + MudSink));   // never on top of the water
            r.restY = wy - under - off; r.floorY = bed - MudSink - off;
            r.w = 2f * Mathf.PI * (large ? BobHzLarge : BobHzMedium); r.damp = large ? BobDampLarge : BobDampMedium;
            r.vy = quiet ? 0f : -Mathf.Min(MaxPlungeSpeed, Carry * Mathf.Abs(v.y));
            r.drift = quiet ? Vector3.zero : new Vector3(v.x, 0f, v.z) * 0.15f; r.turn = large ? 0.8f : 0.55f;
            r.shift = Vector3.zero;
            if (!r.keepPose)
            {   // lying flat it needs room: when the bank or a quay wall is nearer than half its length it settles that much further out
                var fxp = OpenZFxPlayer.I;
                float half = 0.5f * Mathf.Sqrt(os.x * os.x + os.y * os.y + os.z * os.z - thinSide * thinSide);   // half the diagonal of its largest face
                float clear = Clear(fxp, at, half + 0.25f, out var away);
                if (clear < half && away != Vector3.zero)
                {
                    var to = away * Mathf.Min(1.5f, half - clear + 0.1f);
                    if (OpenWaterAt(at + to, out float wy2) && Mathf.Abs(wy2 - wy) < 0.05f) { r.shift = to; r.drift = Vector3.zero; }
                }
            }
            r.state = 1;
            ent = Entries[r.entry]; ent.height = h; ent.under = under; ent.share = share; ent.tiltDeg = tilt; Entries[r.entry] = ent;
            r.last = r.t.position;
        }

        void Place(Rec r, Vector3 p, Quaternion q) { r.t.SetPositionAndRotation(p, q); r.last = r.t.position; }

        void End(Rec r, byte how)
        {
            if (r.entry < 0 || r.entry >= Entries.Count) return;
            var e = Entries[r.entry]; if (e.end != 0) return;
            e.end = how; e.endTime = Time.time; Entries[r.entry] = e;
            if (how == 1) Rested++; else if (how == 2) Sunk++; else LetGo++;
        }

        void Forget(Rec r, byte how)
        {
            if (how != 0) End(r, how);
            recs.Remove(r); byId.Remove(r.id);
        }

        void LateUpdate()
        {
            var g = ZGame.I; var fxp = OpenZFxPlayer.I;
            if (!g || !fxp || !fxp.isActiveAndEnabled || float.IsNegativeInfinity(fxp.WaterY)) return;
            if (!warmed) { warmed = true; AnticSpot.WaterMeshAt(fxp, transform.position, out _); }       // the grid of the drawn water is laid out in the level's first frames, not at the first blast
            float dt = g.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f), now = Time.time;
            int lapping = 0;
            for (int i = recs.Count - 1; i >= 0; i--)
            {
                var r = recs[i];
                if (!r.t || !r.t.gameObject.activeInHierarchy) { Forget(r, 3); continue; }
                var p = r.t.position;
                if (r.state != 0 && (p - r.last).sqrMagnitude > 1e-6f) { Forget(r, 3); continue; }           // its own code moves it again (it sinks, pops or fades): let go
                switch (r.state)
                {
                    case 0: if (!Air(r, p, dt, now, g, fxp)) Forget(r, 0); break;
                    case 1: Bob(r, dt, now, fxp); break;
                    case 2: if (lapping < MaxLapping) { lapping++; Lap(r, now, fxp); } break;
                    default: if (!Sink(r, dt)) Forget(r, 2); break;
                }
            }
        }

        /// <summary>A watched piece that has not reached water yet. False = stop watching it.</summary>
        bool Air(Rec r, Vector3 p, float dt, float now, ZGame g, OpenZFxPlayer fxp)
        {
            bool simulated = r.rb && !r.rb.isKinematic;
            if (dt > 0f) r.vel = r.seen ? (p - r.prev) / dt : Vector3.zero;
            r.prev = p; r.seen = true;
            var v = simulated ? r.rb.linearVelocity : r.falling ? new Vector3(0f, r.vy, 0f) : r.vel;
            if (v.y <= -0.3f) { r.downT = now; r.downV = v; }
            if (!r.drop && !simulated && now - r.born > 1f) return false;                                   // its own code froze it on dry ground
            if (!r.drop && now - r.born > 45f) return false;
            if (now < r.dryUntil) return true;
            if (!Surface(p, out float wy)) { r.falling = false; return true; }                              // no drawn water over it
            if (r.reach >= 0f && p.y - r.reach > wy + 0.5f && !r.drop) return true;                         // well over the surface
            if (!Bounds(r.rs, r.t, out var b)) return r.drop ? now - r.born < 60f : false;                  // nothing left of it to look at
            if (r.reach < 0f) r.reach = (b.center - p).magnitude + b.extents.magnitude;
            if (b.min.y > wy)
            {
                if (r.drop && !simulated) Fall(r, p, b, wy, dt, g);
                return true;
            }
            float bed = g.GroundY(b.center.x, b.center.z);
            if (bed > wy - MinDepth) { r.dryUntil = now + 0.25f; r.falling = false; return true; }           // a deck, a bank, a wet film: the dry rules
            bool down = now - r.downT < 0.3f;                                                               // it fell just now (a shallow bed may have stopped it between two frames)
            if (!down)
            {   // thrown up through the surface or lying in it: wait until it comes down or has lain still a moment
                if (v.sqrMagnitude < 0.35f * 0.35f) r.still += dt; else r.still = 0f;
                if (r.still < 0.25f) return true;
            }
            EnterWater(r, b, wy, bed, down ? r.downV : v, !down, false);
            return true;
        }

        /// <summary>A piece nothing simulates, left in the air over open water (a wreck whose bridge went): it falls.</summary>
        void Fall(Rec r, Vector3 p, Bounds b, float wy, float dt, ZGame g)
        {
            if (r.falling && (p - r.last).sqrMagnitude > 1e-6f) { r.drop = false; r.falling = false; return; }   // its own code moves it: leave it
            if (!r.falling)
            {
                float support = g.GroundY(b.center.x, b.center.z);
                if (b.min.y - support < 0.3f || support > wy - MinDepth) { r.dryUntil = Time.time + 0.25f; return; }   // it stands on something (asked again in a quarter second)
                r.falling = true; r.vy = 0f;
            }
            r.vy -= 9.81f * dt;
            r.t.position = p + Vector3.up * (r.vy * dt); r.last = r.t.position; r.prev = r.last;
        }

        void Bob(Rec r, float dt, float now, OpenZFxPlayer fxp)
        {
            bool large = r.size == OzWaterSize.Large;
            float T = large ? BobSecondsLarge : BobSecondsMedium;
            r.clock += dt;
            r.vy += (-r.w * r.w * (r.y - r.restY) - 2f * r.damp * r.w * r.vy) * dt;
            r.y += r.vy * dt;
            if (r.y < r.floorY) { r.y = r.floorY; if (r.vy < 0f) r.vy = -r.vy * 0.2f; }                      // it touches the bed
            float k = Mathf.Clamp01(r.clock / T), ease = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, k));
            r.xz += r.drift * dt; r.drift *= Mathf.Exp(-4f * dt);
            float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r.clock / r.turn));
            var xz = r.xz + r.shift * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r.clock / (r.turn * 1.6f)));
            float rock = RockDeg * (large ? 0.66f : 1f) * Mathf.Exp(-r.damp * r.w * r.clock) * Mathf.Sin(r.w * r.clock) * (1f - ease);
            Place(r, new Vector3(xz.x, Mathf.Lerp(r.y, Mathf.Max(r.restY, r.floorY), ease), xz.z), Quaternion.AngleAxis(rock, r.axis) * Quaternion.Slerp(r.rot0, r.rot1, turn));
            if (k < 1f) return;
            Place(r, new Vector3(r.xz.x + r.shift.x, Mathf.Max(r.restY, r.floorY), r.xz.z + r.shift.z), r.rot1);
            r.state = 2; r.lapAt = now + Random.Range(LapMin, LapMax) * 0.5f; End(r, 1);
            if (WreckFx.OnScreen(r.last, 0.1f) && Bounds(r.rs, r.t, out var b))
            {   // the faint ring where it breaks the surface
                float foot = Mathf.Max(b.size.x, b.size.z);
                fxp.EmitChannel("m12_ripple", new Vector3(b.center.x, r.waterY + OpenZFxCatalog.RippleLift, b.center.z), Vector3.zero, 2.2f, Mathf.Clamp(foot * 0.3f, 0.2f, 0.8f), Tint(), 0.5f);
            }
        }

        void Lap(Rec r, float now, OpenZFxPlayer fxp)
        {
            if (now < r.lapAt) return;
            r.lapAt = now + Random.Range(LapMin, LapMax);
            if (!WreckFx.OnScreen(r.last, 0.1f) || !Bounds(r.rs, r.t, out var b) || b.max.y < r.waterY) return;
            float u = Random.value * 2f - 1f, sg = Random.value < 0.5f ? -1f : 1f;
            var p = Random.value < 0.5f ? new Vector3(b.center.x + sg * (b.extents.x + 0.05f), 0f, b.center.z + u * b.extents.z)
                                        : new Vector3(b.center.x + u * b.extents.x, 0f, b.center.z + sg * (b.extents.z + 0.05f));
            p.y = r.waterY + OpenZFxCatalog.RippleLift;
            fxp.EmitChannel("m12_ripple", p, Vector3.zero, 1.8f, Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.22f, 0.14f, 0.6f), Tint(), 0.45f);
            Laps++;
        }

        /// <summary>A Small or a thin piece goes under (a thin one turns flat on the way). False = it is gone.</summary>
        bool Sink(Rec r, float dt)
        {
            r.clock += dt;
            float k = Mathf.Clamp01(r.clock / SinkSeconds);
            var q = r.turn > 0f ? Quaternion.Slerp(r.rot0, r.rot1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r.clock / r.turn))) : r.t.rotation;
            Place(r, new Vector3(r.xz.x, Mathf.Lerp(r.y, r.restY, Mathf.SmoothStep(0f, 1f, k)), r.xz.z), q);
            if (k > 0.65f) r.t.localScale = r.baseScale * Mathf.Max(0.001f, 1f - (k - 0.65f) / 0.35f);
            if (k < 1f) return true;
            foreach (var x in r.rs) if (x && x.transform.IsChildOf(r.t)) x.enabled = false;                  // its own code removes the object when its time is up
            return false;
        }
    }
}
