using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// VX64 / VX96 sandbag emplacements under Environment/Sandbags, one DEC_Sandbag_* object per wall segment. Decor only, never
    /// on a walkable Zod tile, so the sim does not know them.
    /// (inactive), each a LODGroup of the imported SB2_Seg_* meshes. The v1 generator (Intact + Rubble only) still works.
    ///   * blast (FxManager.Exploded, PropBreakables' reach 1.2 + 2.4 x strength): within the reach the wall breaks (Intact or
    ///     Damaged -> Rubble: slumped bags + spilled sand stay), between the reach and partialReach x the reach the top bags are
    ///     knocked off (Intact -> Damaged); a Damaged wall caught again by a partial blast breaks. The imported bags SB2_Bag_A/B/C
    ///     fly as physics chunks, sand puffs and grit burst out along the wall. Sounds on the break frame: dst_sandbag_burst
    ///     (once per blast) + dst_sandbag_pour (the sand trickle, once per blast; sfx_batch_v1).
    ///   * shots (OpenZFxPlayer.ShotTraced: rifle / MG / sniper / bolt rounds as drawn): a round whose path enters a standing
    ///     wall thuds into it when the drawn round gets there: imp_bullet_sandbag (sfx_batch_v1; until it is applied, the table's
    ///     imp_bullet_sand) + a small sand puff at the entry point. Shots never break a wall (Zod: sandbags are scenery).
    ///   * thrown bags thud when they land (AudioImpactRelay, the same burlap thud; a plop in water);
    ///     thrown bags that come to rest on a non-walkable tile stay (the "loose bags" next to the broken wall); bags on a walkable
    ///     tile, on something that is not the ground, or past the cap pop out like the other debris, so robots never wade through them.
    /// </summary>
    public class SandbagBreakables : MonoBehaviour
    {
        [Tooltip("v1 generator only: its one generated bag (v2 uses bagMeshes)")] public Mesh bagMesh;
        [Tooltip("v2: the imported bags thrown by a break: SB2_Bag_A (full), SB2_Bag_B (damp), SB2_Bag_C (burst, sand dribble)")] public Mesh[] bagMeshes;
        [Tooltip("Material of the thrown bags (v2: the level's M_SandbagsV2_Lxx atlas material; v1: DV3_BatchedPalette)")] public Material material;
        [Tooltip("v1 generator only: canvas colour baked into its bag")] public Color canvas = new Color32(178, 158, 124, 255);
        [Range(0, 12)] public int chunksPerSegment = 5;
        [Tooltip("Most thrown bags alive at once, flying + resting (the oldest pops out first)")] public int maxChunks = 96;
        [Tooltip("Seconds until a thrown bag settles for good (stays or pops)")] public Vector2 chunkLife = new Vector2(4.5f, 7f);
        [Tooltip("A blast between the break reach and this many times the reach knocks the top bags off (Intact -> Damaged)")] public float partialReach = 1.6f;

        // GateBattery (proof) reads `segs`, `broken` and `b` by reflection: keep the names
        class Seg { public GameObject go, intact, damaged, rubble; public Bounds b, bDamaged; public int bags; public bool broken, dmg; public float nextThud; }
        class Chunk { public Transform t; public Rigidbody rb; public float die, born; public bool rest; }
        struct Thud { public float at; public Vector3 pos; }
        readonly List<Seg> segs = new List<Seg>();
        readonly List<Chunk> chunks = new List<Chunk>();
        readonly List<Thud> thuds = new List<Thud>();
        static PhysicsMaterial sandMat;
        static readonly string[] BurstClips = { "sd_sandbag_burst_a", "sd_sandbag_burst_b" };
        static readonly string[] FallbackClips = { "v3-collapse-small-a", "v3-collapse-small-b" };
        static int burstFrame = -1, pourFrame = -1;                                              // one burst + one pour per blast frame
        static bool warnedPour;
        float thudTokens = 6f, thudAt;
        ZGame game;
        public static int Breaks, Partials, ShotThuds, BagsStayed, BagsPopped;
        /// <summary>The burlap thud (shot hits, landing bags): sfx_batch_v1's imp_bullet_sandbag, else the table's sand hit.</summary>
        static string ThudId => AudioDirector.Has("imp_bullet_sandbag") ? "imp_bullet_sandbag" : "imp_bullet_sand";

        void Start()
        {
            int withDamaged = 0;
            foreach (Transform t in transform)
            {
                if (!t.name.StartsWith("DEC_Sandbag_") || !t.gameObject.activeInHierarchy) continue;
                var intact = t.Find("Intact"); var damaged = t.Find("Damaged"); var rubble = t.Find("Rubble");
                if (!intact || !MeshBounds(intact, out var b)) continue;
                int bags;
                var mf = intact.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh && bagMesh && bagMesh.vertexCount > 0 && (bagMeshes == null || bagMeshes.Length == 0))
                    bags = Mathf.Max(1, mf.sharedMesh.vertexCount / bagMesh.vertexCount);                   // v1: merged copies of one bag
                else
                {   // v2: 2 / 3 / 4 bags in the bottom row (0.74 m pitch), 2 rows (3 at the fort gates)
                    int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(b.size.x, b.size.z) / 0.74f), 2, 4);
                    bags = b.size.y > 0.75f ? n * 3 - 3 : n * 2 - 1;
                }
                var bd = b;
                if (damaged && MeshBounds(damaged, out var db)) { bd = db; withDamaged++; }
                if (damaged) damaged.gameObject.SetActive(false);
                if (rubble) rubble.gameObject.SetActive(false);
                intact.gameObject.SetActive(true);
                segs.Add(new Seg { go = t.gameObject, intact = intact.gameObject, damaged = damaged ? damaged.gameObject : null, rubble = rubble ? rubble.gameObject : null, b = b, bDamaged = bd, bags = bags });
            }
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            FxManager.Exploded += OnExplosion;
            OpenZFxPlayer.ShotTraced += OnShot;
            Breaks = Partials = ShotThuds = BagsStayed = BagsPopped = 0;
            Debug.Log($"[OpenZ] sandbags: {segs.Count} breakable segments ({withDamaged} with a damaged stage), thrown bags {(bagMeshes != null && bagMeshes.Length > 0 ? bagMeshes.Length + " imported meshes" : "v1 bag")}, " +
                      $"shot thud {(AudioDirector.Has("imp_bullet_sandbag") ? "imp_bullet_sandbag" : "imp_bullet_sand (imp_bullet_sandbag not in the table yet: apply sfx_batch_v1)")}, " +
                      $"pour {(AudioDirector.Has("dst_sandbag_pour") ? "dst_sandbag_pour" : "MISSING (apply sfx_batch_v1)")}");
        }

        void OnDestroy() { FxManager.Exploded -= OnExplosion; OpenZFxPlayer.ShotTraced -= OnShot; }

        /// <summary>World bounds of every mesh under t from the mesh data (works on inactive objects, unlike Renderer.bounds).</summary>
        static bool MeshBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any;
        }

        // ------------------------------------------------------------------------------------------------ blasts
        void OnExplosion(Vector3 at, float strength)
        {
            float reach = 1.2f + 2.4f * strength;                     // = PropBreakables
            float outer = reach * Mathf.Max(1f, partialReach);
            foreach (var s in segs)
            {
                if (s.broken || !s.go) continue;
                if (at.y > s.b.max.y + 3f || at.y < s.b.min.y - 4f) continue;     // a blast on a plateau vs a wall below its rim
                var c = s.b.ClosestPoint(at); c.y = at.y;
                float d2 = (c - at).sqrMagnitude;
                if (d2 <= reach * reach) Break(s, at, strength, true);
                else if (d2 <= outer * outer && s.damaged) Break(s, at, strength, s.dmg);   // first partial: knocked; second: broken
            }
        }

        void Break(Seg s, Vector3 blast, float strength, bool full)
        {
            var bb = s.dmg ? s.bDamaged : s.b;
            if (full) { s.broken = true; Breaks++; } else { s.dmg = true; Partials++; }
            Debug.Log($"[OpenZ] sandbags: {s.go.name} -> {(full ? (s.rubble ? "Rubble" : "broken") : "Damaged")} (blast {Vector3.Distance(new Vector3(bb.center.x, blast.y, bb.center.z), blast):F1} m from its centre, strength {strength:F2}{(full && s.dmg ? ", was damaged" : "")})");
            s.intact.SetActive(false);
            if (s.damaged) s.damaged.SetActive(!full);
            if (s.rubble) s.rubble.SetActive(full);
            var c = bb.center; float len = Mathf.Max(bb.size.x, bb.size.z);
            var along = bb.size.x >= bb.size.z ? Vector3.right : Vector3.forward;
            float floor = bb.min.y, top = bb.max.y;
            // thrown bags: a full break throws most of the wall, a partial one the top bags it knocked off
            int n = full ? Mathf.Min(chunksPerSegment, Mathf.Max(1, s.bags)) : (s.bags >= 7 ? 2 : 1);
            if (full && s.dmg) n = Mathf.Max(1, n - 2);                // the damaged wall already lost its top bags (s.dmg = it was hit before)
            float kick = Mathf.Clamp(0.6f + 0.5f * strength, 0.6f, 1.8f) * (full ? 1f : 0.75f);
            for (int k = 0; k < n; k++)
            {
                float f = n == 1 ? Random.Range(-0.25f, 0.25f) : k / (float)(n - 1) - 0.5f;
                var p = c + along * f * len * 0.8f;
                p.y = full ? floor + 0.15f + 0.1f * (k % 2) + 0.2f : top - 0.12f;
                var away = p - blast; away.y = 0;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
                SpawnChunk(p, away * Random.Range(1.8f, 4.5f) * kick + Vector3.up * Random.Range(3.5f, 6.5f) * kick, k);
            }
            // sand: puffs along the wall, grit, a low cloud (a partial hit: puffs at the top only)
            var fx = FxManager.I;
            if (fx)
            {
                int puffs = full ? Mathf.Clamp(Mathf.RoundToInt(len / 0.9f), 2, 4) : 2;
                for (int k = 0; k < puffs; k++)
                {
                    float f = puffs == 1 ? 0 : k / (float)(puffs - 1) - 0.5f;
                    fx.Dust(new Vector3(c.x, full ? floor + 0.25f : top - 0.1f, c.z) + along * f * len * (full ? 0.8f : 0.5f), full ? 0.75f : 0.45f);
                }
                if (full)
                {
                    fx.Breakup(new Vector3(c.x, floor + 0.35f, c.z), "stone", Mathf.Max(1f, len), Mathf.Clamp(Mathf.RoundToInt(len * 4), 6, 14), 0.45f);
                    fx.DemolitionDust(new Vector3(c.x, floor, c.z), len, 0.8f, 0.3f);
                }
                else fx.Breakup(new Vector3(c.x, top - 0.1f, c.z), "stone", Mathf.Max(0.6f, len * 0.5f), 5, 0.35f);
            }
            // sound on the break frame: the burst (bags tearing) + the sand pour, each once per blast even when it hits several walls
            if (AudioDirector.Has("dst_sandbag_burst"))
            {
                if (burstFrame != Time.frameCount) AudioDirector.Fire("dst_sandbag_burst", c, -1, full ? 1f : 0.8f);
                burstFrame = Time.frameCount;
            }
            else { var au = CombatAudio.I; if (au) au.Play("prop", c, full ? 0.8f : 0.65f, 0.05f, -1, au.HasClip(BurstClips[0]) ? BurstClips : FallbackClips); }
            if (AudioDirector.Has("dst_sandbag_pour"))
            {
                if (pourFrame != Time.frameCount) AudioDirector.Fire("dst_sandbag_pour", c, -1, full ? 1f : 0.75f);
                pourFrame = Time.frameCount;
            }
            else if (!warnedPour)
            {
                warnedPour = true;
                Debug.LogWarning("[OpenZ] sandbags: dst_sandbag_pour is not in the sound table (sfx batch not imported): the sand pour is silent");
            }
        }

        void SpawnChunk(Vector3 p, Vector3 v, int k)
        {
            Mesh mesh = null; bool v2 = bagMeshes != null && bagMeshes.Length > 0;
            if (v2) mesh = bagMeshes[k % bagMeshes.Length];
            if (!mesh) mesh = bagMesh;
            if (!mesh || !material) return;
            while (chunks.Count >= maxChunks) { Retire(chunks[0]); chunks.RemoveAt(0); }
            var go = new GameObject("SandbagChunk");
            go.layer = BuildingBreaker.DebrisLayer;
            var t = go.transform; t.position = p; t.rotation = Random.rotation;
            // v1: some bags burst half-empty (squashed); v2: SB2_Bag_C is the modelled burst bag
            t.localScale = v2 ? Vector3.one * Random.Range(0.92f, 1f) : (k % 3 == 2 ? new Vector3(1.05f, 0.55f, 0.8f) : Vector3.one * Random.Range(0.85f, 1f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = material;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;   // fast chunks blur like the building debris
            var bc = go.AddComponent<BoxCollider>(); bc.size = mesh.bounds.size * 0.92f; bc.center = mesh.bounds.center;
            bc.sharedMaterial = sandMat ??= new PhysicsMaterial("Sandbag") { bounciness = 0.08f, dynamicFriction = 0.9f, staticFriction = 1f, bounceCombine = PhysicsMaterialCombine.Minimum };
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 18f; rb.linearDamping = 0.2f; rb.angularDamping = 1.6f;          // heavy, floppy: tumbles, then lies still
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = v; rb.angularVelocity = Random.insideUnitSphere * Random.Range(3f, 8f);
            // the landing: a dull burlap thud on the contact frame (AudioImpactRelay: volume from the impact speed, per-body and
            // per-event budgets), a plop when it lands in water
            AudioDirector.AttachImpacts(go, ThudId, AudioDirector.Has("wtr_debris_plop") ? "wtr_debris_plop" : "dst_debris_splash");
            OzWaterLanding.Watch(t, "sandbag", rb);                                             // b27: the shared water landing rule (the relay above stays silent for a bag the rule holds)
            chunks.Add(new Chunk { t = t, rb = rb, born = Time.time, die = Time.time + Random.Range(chunkLife.x, chunkLife.y) });
        }

        void Retire(Chunk c)
        {
            if (!c.t) return;
            if (c.rb) { c.rb.isKinematic = true; c.rb.interpolation = RigidbodyInterpolation.None; }
            if (!PopOut.IsPopping(c.t)) PopOut.Begin(c.t, PopOut.Style.Debris, null, true, false);
            BagsPopped++;
        }

        /// <summary>A bag has settled: it stays where it lies when that is the ground of a non-walkable tile, else it pops out.
        /// Before its pop time (final = false) a bag that has to go lies still (kinematic, like the v1 debris) and pops at its
        /// 4.5-7 s life; a bag still sliding at its pop time never stays (it would freeze mid-roll).</summary>
        bool Settle(Chunk c, bool final)
        {
            if (!c.t) return false;
            var p = c.t.position;
            bool ground = false;
            int mask = ~(1 << BuildingBreaker.DebrisLayer) & ~OzRockSolid.Mask;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var h, 1.6f, mask, QueryTriggerInteraction.Ignore))
                for (var q = h.collider.transform; q; q = q.parent) { if (q.name == "COL_Terrain" || q.name == "Environment") { ground = true; break; } if (q.name == "ZodObjects") break; }
            bool walk = true;
            var w = game ? game.World : null;
            if (w != null) { var px = ZGame.WorldToPx(p); walk = w.Path.TilePassable(px.x >> 4, px.y >> 4, true); }
            bool moving = c.rb && !c.rb.isKinematic && c.rb.linearVelocity.sqrMagnitude > 0.04f;
            if (!ground || walk || moving)
            {
                if (!final && c.rb) { c.rb.isKinematic = true; return true; }   // lies still until its pop time
                Retire(c); return false;
            }
            if (c.rb) Destroy(c.rb);
            var col = c.t.GetComponent<Collider>(); if (col) Destroy(col);
            var mr = c.t.GetComponent<MeshRenderer>(); if (mr) mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            c.rest = true; c.rb = null; BagsStayed++;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ shots
        void OnShot(Vector3 from, Vector3 to, float life, FxWeapon w)
        {
            if (w != FxWeapon.Rifle && w != FxWeapon.MachineGun && w != FxWeapon.Sniper && w != FxWeapon.Bolt) return;
            var d = to - from; float len = d.magnitude;
            if (len < 0.1f || segs.Count == 0) return;
            var dir = d / len; float best = len; Seg hit = null;
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s.broken || !s.go) continue;
                var b = s.dmg ? s.bDamaged : s.b; b.Expand(new Vector3(0.06f, 0.04f, 0.06f));
                if (b.IntersectRay(new Ray(from, dir), out float t) && t <= best && !b.Contains(from)) { best = t; hit = s; }
            }
            if (hit == null) return;
            float now = Time.time;
            if (now < hit.nextThud) return;
            thudTokens = Mathf.Min(6f, thudTokens + (now - thudAt) * 6f); thudAt = now;            // at most ~6 thuds a second map-wide
            if (thudTokens < 1f) return;
            thudTokens -= 1f; hit.nextThud = now + 0.07f;
            thuds.Add(new Thud { at = now + Mathf.Max(0f, life) * (best / len), pos = from + dir * best });   // the drawn round's head gets there
        }

        void Update()
        {
            float now = Time.time;
            for (int i = thuds.Count - 1; i >= 0; i--)
            {
                if (now < thuds[i].at) continue;
                var p = thuds[i].pos; thuds.RemoveAt(i);
                AudioDirector.Fire(ThudId, p, -1, Random.Range(0.85f, 1f));
                if (FxManager.I) FxManager.I.Dust(p, 0.2f);
                ShotThuds++;
            }
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                var c = chunks[i];
                if (!c.t) { chunks.RemoveAt(i); continue; }
                if (c.rest) continue;
                bool still = c.rb && !c.rb.isKinematic && now > c.born + 2.5f && c.rb.linearVelocity.sqrMagnitude < 0.01f && c.rb.angularVelocity.sqrMagnitude < 0.04f;
                if (now >= c.die || still) { if (!Settle(c, now >= c.die)) chunks.RemoveAt(i); }
            }
        }
    }
}
