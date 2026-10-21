using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's sky clearance (rule DV118, view only: the rules' 12 m and lift stay as they are). The rules know no
    /// height of anything, so the flying ship is drawn high enough to clear what stands under its way: the fort (walls, tower pads,
    /// cannons, the big gun, the flag pole: FortGunView.FortTopAt), the pyramids, rock pillars, huts, crates, buildings, lamp posts and
    /// props (their renderers' tops, in a 4 m bucket grid built at the level start a few objects a frame, so no frame hitches; a
    /// destroyed or hidden thing drops out by itself; built again the same way when the level's plants are hooked later), the trees and
    /// palms (foot + their real height, never their widened drawing boxes) and the terrain itself (cliffs).
    /// <see cref="Query"/> = the highest hard top and plant top within a radius of a point, sampled over the ship's footprint.
    /// <see cref="Wanted"/> = the root height the ship needs at its place and along its way (looking ahead along its velocity at 0, 0.5,
    /// 1.0, ... 3.0 s): hard tops + <see cref="HardGap"/>, plants + <see cref="PlantGap"/> (their crowns bend in its downwash).
    /// K256e: each gap carries the 0.7 m hover bob (OzPlayableShuttle.Flight.cs), and over crowns it keeps 4 m at the bob's lowest.
    /// </summary>
    public sealed class OzShuttleSkyTop
    {
        /// <summary>The ship's plan radius (m: the rear pads reach 4.07 m), the air kept round it while it flies, the gaps under its pads
        /// (K256e: each + the 0.7 m bob: 1.5 / 3.0 -> 2.2 / 3.7; plants 4.0 + 0.7, so its pads stay 4 m over the crowns at the bob's lowest).</summary>
        public static float Footprint = 4.1f, Air = 2.2f, HardGap = 3.7f, PlantGap = 4.7f;
        /// <summary>The hover bob's amplitude the gaps above carry (m; OzPlayableShuttle.bobM).</summary>
        public const float BobCarried = 0.7f;
        public const float Bucket = 4f;
        public static int Builds, HardEntries, PlantEntries;

        struct Hard { public Renderer r; public float top; public Bounds b; }
        struct Plant { public OzFloraWind.Plant p; public float top, x, z, rad; }
        Dictionary<long, List<Hard>> hard = new Dictionary<long, List<Hard>>();
        Dictionary<long, List<Plant>> plants = new Dictionary<long, List<Plant>>();
        readonly HashSet<Renderer> plantRs = new HashSet<Renderer>();
        readonly ZGame game; readonly Transform self;
        bool built; float builtAt = -100f; int plantsSeen = -1;
        // the build spread over frames: the sources, the next one, the grids being filled (swapped in when done)
        readonly List<GameObject> srcs = new List<GameObject>(); int srcAt = -1, plantAt; int nextPlants;
        Dictionary<long, List<Hard>> hardNew; Dictionary<long, List<Plant>> plantsNew; readonly HashSet<Renderer> plantRsNew = new HashSet<Renderer>();
        /// <summary>Objects (renderer roots) and plants taken into the grid per frame while it is built.</summary>
        public static int PerFrame = 24, PlantsPerFrame = 160, StepBuilds;

        public OzShuttleSkyTop(ZGame g, Transform ship) { game = g; self = ship; }

        static long Key(int bx, int bz) => ((long)bx << 32) ^ (uint)bz;
        static int B(float v) => Mathf.FloorToInt(v / Bucket);

        /// <summary>The grid (again when the plant list changed: the level's plants are hooked a little after the start).</summary>
        public void Build()
        {
            var fw = OzFloraWind.I; int np = fw != null ? fw.All.Count : 0;
            if (built && np == plantsSeen) return;
            built = true; builtAt = Time.unscaledTime; plantsSeen = np; Builds++;
            hard.Clear(); plants.Clear(); plantRs.Clear(); HardEntries = PlantEntries = 0;
            if (fw != null) foreach (var p in fw.All) if (p != null && p.R) plantRs.Add(p.R);      // plants count by their real height, never their widened boxes
            if (game != null)
            {
                foreach (var kv in game.StaticViews) AddHard(kv.Value);
            }
            foreach (var py in L16Pyramid.All) if (py) AddHard(py.gameObject);
            foreach (var dp in DetailProp.All) if (dp && !dp.Broken) AddHard(dp.gameObject);
            if (fw != null)
                foreach (var p in fw.All)
                {
                    if (p == null || !p.R) continue;
                    var t = p.R.transform; float sc = Mathf.Abs(t.lossyScale.y), sr = Mathf.Abs(t.lossyScale.x);
                    float top = t.position.y + p.Height * sc, rad = Mathf.Max(0.5f, p.Radius * sr);
                    var e = new Plant { p = p, top = top, x = t.position.x, z = t.position.z, rad = rad };
                    for (int bx = B(e.x - rad); bx <= B(e.x + rad); bx++)
                        for (int bz = B(e.z - rad); bz <= B(e.z + rad); bz++)
                        {
                            var k = Key(bx, bz); if (!plants.TryGetValue(k, out var l)) plants[k] = l = new List<Plant>();
                            l.Add(e); PlantEntries++;
                        }
                }
        }

        void AddHard(GameObject go) => AddHard(go, hard, plantRs);
        void AddHard(GameObject go, Dictionary<long, List<Hard>> into, HashSet<Renderer> skip)
        {
            if (!go) return;
            var hardSave = hard; hard = into;
            try { AddHardInto(go, skip); } finally { hard = hardSave; }
        }
        void AddHardInto(GameObject go, HashSet<Renderer> plantRs)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (self && r.transform.IsChildOf(self)) continue;
                if (OzPlayableShuttle.OfRenderer(r)) continue;                                       // another shuttle is no hard top (it moves)
                if (plantRs.Contains(r)) continue;
                var b = r.bounds; if (b.size.y < 0.3f) continue;
                var e = new Hard { r = r, top = b.max.y, b = b };
                for (int bx = B(b.min.x); bx <= B(b.max.x); bx++)
                    for (int bz = B(b.min.z); bz <= B(b.max.z); bz++)
                    {
                        var k = Key(bx, bz); if (!hard.TryGetValue(k, out var l)) hard[k] = l = new List<Hard>();
                        l.Add(e); HardEntries++;
                    }
            }
        }

        /// <summary>Starts a build spread over the next frames (<see cref="BuildStep"/>); the grid in use stays until the new one is done.</summary>
        public void BeginBuild()
        {
            srcs.Clear(); plantRsNew.Clear(); srcAt = 0; plantAt = 0;
            hardNew = new Dictionary<long, List<Hard>>(); plantsNew = new Dictionary<long, List<Plant>>();
            var fw = OzFloraWind.I; nextPlants = fw != null ? fw.All.Count : 0;
            if (fw != null) foreach (var p in fw.All) if (p != null && p.R) plantRsNew.Add(p.R);
            if (game != null) foreach (var kv in game.StaticViews) if (kv.Value) srcs.Add(kv.Value);
            foreach (var py in L16Pyramid.All) if (py) srcs.Add(py.gameObject);
            foreach (var dp in DetailProp.All) if (dp && !dp.Broken) srcs.Add(dp.gameObject);
        }

        /// <summary>Once a frame (OzPlayableShuttle's LateUpdate): a few more objects and plants into the grid being built; done = swapped in.</summary>
        public void BuildStep()
        {
            if (srcAt < 0) return;
            int end = Mathf.Min(srcs.Count, srcAt + PerFrame);
            for (; srcAt < end; srcAt++) AddHard(srcs[srcAt], hardNew, plantRsNew);
            if (srcAt < srcs.Count) return;
            var fw = OzFloraWind.I;
            if (fw != null)
            {
                int pend = Mathf.Min(fw.All.Count, plantAt + PlantsPerFrame);
                for (; plantAt < pend; plantAt++) AddPlant(fw.All[plantAt], plantsNew);
                if (plantAt < fw.All.Count) return;
            }
            hard = hardNew; plants = plantsNew; plantRs.Clear(); foreach (var r in plantRsNew) plantRs.Add(r);
            hardNew = null; plantsNew = null; srcAt = -1; srcs.Clear();
            built = true; builtAt = Time.unscaledTime; plantsSeen = nextPlants; Builds++; StepBuilds++;
            HardEntries = 0; foreach (var l in hard.Values) HardEntries += l.Count; PlantEntries = 0; foreach (var l in plants.Values) PlantEntries += l.Count;
        }

        void AddPlant(OzFloraWind.Plant p, Dictionary<long, List<Plant>> into)
        {
            if (p == null || !p.R) return;
            var t = p.R.transform; float sc = Mathf.Abs(t.lossyScale.y), sr = Mathf.Abs(t.lossyScale.x);
            float top = t.position.y + p.Height * sc, rad = Mathf.Max(0.5f, p.Radius * sr);
            var e = new Plant { p = p, top = top, x = t.position.x, z = t.position.z, rad = rad };
            for (int bx = B(e.x - rad); bx <= B(e.x + rad); bx++)
                for (int bz = B(e.z - rad); bz <= B(e.z + rad); bz++)
                {
                    var k = Key(bx, bz); if (!into.TryGetValue(k, out var l)) into[k] = l = new List<Plant>();
                    l.Add(e);
                }
        }

        static bool Live(Renderer r) => r && r.enabled && r.gameObject.activeInHierarchy;

        static float DiscBox(float x, float z, Bounds b)
        {   // squared flat distance from a point to a box
            float dx = Mathf.Max(b.min.x - x, 0f, x - b.max.x), dz = Mathf.Max(b.min.z - z, 0f, z - b.max.z);
            return dx * dx + dz * dz;
        }

        /// <summary>The highest hard top (terrain, fort, things) and plant top within `r` of (x, z); float.MinValue = none.</summary>
        public void Query(float x, float z, float r, bool withPlants, out float hardTop, out float plantTop)
        {
            if (!built) Build();                                                                     // never built yet (no frames to spread it over): now
            else if (Time.unscaledTime - builtAt > 20f) { builtAt = Time.unscaledTime; if (srcAt < 0 && OzFloraWind.I != null && OzFloraWind.I.All.Count != plantsSeen) BeginBuild(); }
            hardTop = float.MinValue; plantTop = float.MinValue;
            float r2 = r * r;
            for (int bx = B(x - r); bx <= B(x + r); bx++)
                for (int bz = B(z - r); bz <= B(z + r); bz++)
                {
                    var k = Key(bx, bz);
                    if (hard.TryGetValue(k, out var hl))
                        foreach (var h in hl) { if (h.top <= hardTop || !Live(h.r) || DiscBox(x, z, h.b) > r2) continue; hardTop = h.top; }
                    if (withPlants && plants.TryGetValue(k, out var pl))
                        foreach (var p in pl)
                        {
                            if (p.top <= plantTop || p.p.Gone || !p.p.R || !p.p.R.enabled) continue;
                            float dx = p.x - x, dz = p.z - z, rr = r + p.rad; if (dx * dx + dz * dz > rr * rr) continue;
                            plantTop = p.top;
                        }
                }
            // the terrain under the footprint and the fort (a 5 x 5 grid over the disc: the flag pole is thin)
            if (game != null)
            {
                for (int i = -2; i <= 2; i++)
                    for (int j = -2; j <= 2; j++)
                    {
                        float px = x + i * r * 0.5f, pz = z + j * r * 0.5f;
                        if ((i * i + j * j) * 0.25f > 1.05f) continue;
                        var p = new Vector3(px, 0f, pz);
                        hardTop = Mathf.Max(hardTop, game.GroundY(px, pz));
                        float ft = FortGunView.FortTopAt(p, new Vector3(1e6f, 0f, 1e6f));
                        if (ft > hardTop) hardTop = ft;
                    }
            }
        }

        /// <summary>The root height the flying ship needs at `c` going with `vel` (m/s): max over the look-ahead of hard tops + HardGap
        /// and plants + PlantGap (radius Footprint + Air); float.MinValue = nothing to clear.</summary>
        public float Wanted(Vector3 c, Vector3 vel, bool withPlants, out float hardTop, out float plantTop)
        {
            hardTop = plantTop = float.MinValue; float r = Footprint + Air;
            var hv = new Vector3(vel.x, 0f, vel.z);
            for (int i = 0; i <= 6; i++)
            {
                float t = i * 0.5f; if (i > 0 && hv.sqrMagnitude < 0.04f) break;
                var p = c + hv * t;
                Query(p.x, p.z, r, withPlants, out float h, out float pt);
                hardTop = Mathf.Max(hardTop, h); plantTop = Mathf.Max(plantTop, pt);
            }
            float w = float.MinValue;
            if (hardTop > float.MinValue) w = Mathf.Max(w, hardTop + HardGap);
            if (plantTop > float.MinValue) w = Mathf.Max(w, plantTop + PlantGap);
            return w;
        }
    }
}
