using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// <list type="bullet">
    /// <item><b>Ruin, not a silhouette</b>: the old collapse squashed the whole model to 24 % height and charred it 90 % (a black
    /// fort-shaped slab). Now the model is swapped for its RUIN (BuildingRuins: broken walls on a jagged cut, corner towers
    /// taller, double-sided), lightly scorched (TeamPaint-style charring 45 %, never black), with a rubble pile of chunks settled
    /// in the footprint, fire and a smoke column. Without a ruin mesh the model sinks half into the ground instead of squashing.</item>
    /// <item><b>Big parts</b>: the cut-off upper model (wall sections, tower tops, roof) flies as 4-12 heavy rigid parts,
    /// thrown up by the blast, tumbling, crashing down with dust, a heavy crash sound and a camera kick (map 13).</item>
    /// <item><b>Chunks game-style</b> (the project rules): the baked rounded low-poly chunks (OpenZFracture) are no longer replaced by the
    /// Unity cube with a quarter of the building texture on every face (noisy grey square chips): they keep their rounded
    /// shape, flat-shaded, one clean colour each from the building's own palette (light walls, some trim, a few scorched, one in
    /// eight in the owner's team accent), sizes varied, thrown high, bounce, settle, pop out.</item>
    /// <item><b>No hitch</b>: everything is prepared at level load (OpenZEnding prewarm): the fracture set is instantiated
    /// inactive with its colliders, bodies and colours, the palette GPU read-back and the debris materials are done, the ruin and
    /// parts are built. The death only activates and launches (the old path loaded, instantiated, added ~60 colliders +
    /// continuous bodies and did a ReadPixels GPU stall in the death frame).</item>
    /// </list>
    /// Visual only: the sim keeps its ruin. Restore (auto-repair, crane) clears everything and prepares a fresh set.
    /// </summary>
    public partial class BuildingBreaker
    {
        public static bool RuinV1 = true, RuinAllBuildings = true;
        public static float RuinCharred = 0.45f, BigPartLifeMin = 14f, BigPartLifeMax = 20f;

        bool v1, prepared, sunk; float fireSpan = 14f, smokeUntil, smokeSpread = 3f; Vector3 smokeCenter;
        GameObject holder, ruinGo; MeshRenderer mainR;
        readonly List<Rigidbody> bigParts = new List<Rigidbody>();
        readonly List<Piece> rubble = new List<Piece>();
        Vector3 sinkFromPos; Quaternion sinkFromRot;
        static readonly Dictionary<string, GameObject> fracturePrefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<Material, Material> chunkMats = new Dictionary<Material, Material>();
        static Transform poolRoot;
        static MaterialPropertyBlock mpbV1;
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorIdV1 = Shader.PropertyToID("_BaseColor"),
                            AccentLoIdV1 = Shader.PropertyToID("_AccentLo"), AccentHiIdV1 = Shader.PropertyToID("_AccentHi");

        public bool IsPrepared => prepared;
        /// <summary>The debris chunk materials made so far (OpenZEnding's warm render draws each once at level load).</summary>
        public static IEnumerable<Material> WarmMaterials() { foreach (var m in chunkMats.Values) if (m) yield return m; }

        static GameObject FracturePrefab(string key)
        {
            if (fracturePrefabs.TryGetValue(key, out var pf)) return pf;
            pf = Resources.Load<GameObject>("OpenZ/Fractures/F_" + key);
            fracturePrefabs[key] = pf;
            return pf;
        }

        static Transform PoolRoot()
        {
            if (poolRoot) return poolRoot;
            poolRoot = new GameObject("OpenZ_BuildingDebrisPool").transform;
            return poolRoot;
        }

        /// <summary>Level-load work for this building (OpenZEnding prewarm, one building per frame). Safe to call twice.</summary>
        public void Prepare()
        {
            if (prepared || collapsed || obj == null) return;
            var pf = FracturePrefab(Key(obj.Type)); if (!pf) return;
            var t = transform;
            // the intact renderers as they are drawn now (the palette samples their textures once per building type)
            var live = new List<Renderer>(); foreach (var r in GetComponentsInChildren<Renderer>()) if (r.enabled && !BuildingRuins.IsGunPart(r.transform, t)) live.Add(r);
            var palette = Palette(live, obj.Type);                                                 // the ReadPixels GPU read-back happens here, at load
            holder = new GameObject(name + "_debris"); holder.transform.SetParent(PoolRoot(), false);
            // fracture cells: instantiated under an inactive parent, so nothing wakes up or simulates before the death
            var sleeper = new GameObject("sleeper"); sleeper.SetActive(false); sleeper.transform.SetParent(holder.transform, false);
            fracture = Instantiate(pf, sleeper.transform);
            fracture.name = name + "_fracture";
            fracture.SetActive(false);
            fracture.transform.SetParent(holder.transform, false);
            Destroy(sleeper);
            fracture.transform.SetPositionAndRotation(t.position, t.rotation);
            fracture.transform.localScale = t.lossyScale;
            var tp = fracture.GetComponent<TeamPaint>(); if (tp) { tp.enabled = false; Destroy(tp); }
            int layer = GetDebrisLayer(), i = 0; bool fort = obj.IsFort;
            pieces.Clear(); rubble.Clear();
            bool useLib = LibReady(); int heapsUsed = 0; if (useLib) ResetLibStats();
            foreach (Transform c in fracture.transform)
            {
                var go = c.gameObject; go.layer = layer;
                var mr = go.GetComponent<MeshRenderer>(); if (!mr) continue;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                mr.shadowCastingMode = ShadowCastingMode.On;
                var lib = useLib ? LibCell(go, i, fort, ref heapsUsed) : null;
                if (lib == null) ChunkLook(go, i, palette, fort);
                var mf = go.GetComponent<MeshFilter>(); var mb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                var mc = go.AddComponent<BoxCollider>(); mc.center = mb.center; mc.size = lib != null ? mb.size * 0.85f : mb.size; mc.sharedMaterial = Bouncy();
                if (lib != null && lib.cls == "HEAP") mc.enabled = false;                                 // heaps: visual rubble only
                var rb = go.AddComponent<Rigidbody>();
                var ls = go.transform.lossyScale; var sz = Vector3.Scale(mb.size, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));   // inactive: no renderer bounds yet
                rb.mass = Mathf.Clamp(sz.x * sz.y * sz.z * 5f, 0.8f, 160f);
                rb.linearDamping = 0.28f; rb.angularDamping = 0.95f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;               // was ContinuousDynamic on every cell
                rb.maxAngularVelocity = 14f;
                var p = go.AddComponent<Piece>(); p.owner = this; p.rb = rb; p.life = Lifetime(obj.Id, i); p.lib = lib;
                bool isRubble = i % (fort ? 4 : 6) == 0;                                                  // settled in the footprint from the start
                if (isRubble) { rb.isKinematic = true; p.enabled = false; rubble.Add(p); }
                else pieces.Add(p);
                i++;
            }
            BuildRuinObjects();
            if (useLib) LogChunkStats();
            prepared = true;
        }

        /// <summary>The ruin model and the big parts (inactive until the collapse), placed exactly on the main model renderer.</summary>
        void BuildRuinObjects()
        {
            mainR = BuildingRuins.MainRenderer(transform);
            var mf = mainR ? mainR.GetComponent<MeshFilter>() : null;
            var set = mf ? BuildingRuins.Get(Key(obj.Type), mf.sharedMesh) : null;
            if (set == null || !set.ruin) return;
            var mt = mainR.transform; var mats = mainR.sharedMaterials;
            ruinGo = new GameObject(name + "_ruin");
            ruinGo.transform.SetParent(holder.transform, false);
            ruinGo.transform.SetPositionAndRotation(mt.position, mt.rotation); ruinGo.transform.localScale = mt.lossyScale;
            ruinGo.layer = mainR.gameObject.layer;
            ruinGo.AddComponent<MeshFilter>().sharedMesh = set.ruin;
            var rr = ruinGo.AddComponent<MeshRenderer>(); rr.sharedMaterials = mats;
            rr.shadowCastingMode = mainR.shadowCastingMode; rr.receiveShadows = mainR.receiveShadows;
            ruinGo.SetActive(false);
            AttachShell(set, mt, mats.Length > 0 ? mats[0] : null);                              // the ruin's inside: wall thickness and a floor (BuildingBreaker.Shell.cs)
            if (ConcreteParts && (obj.IsFort || ConcreteAllBuildings) && LibReady()) { BuildLibraryChunks(set, mt); return; }
            int layer = GetDebrisLayer(); var s = mt.lossyScale;
            foreach (var pm in set.parts)
            {
                if (!pm) continue;
                var c = pm.bounds.center;
                var root = new GameObject(pm.name); root.layer = layer;
                root.transform.SetParent(holder.transform, false);
                root.transform.SetPositionAndRotation(mt.TransformPoint(c), mt.rotation);
                var vis = new GameObject("mesh"); vis.layer = layer;
                vis.transform.SetParent(root.transform, false);
                vis.transform.localPosition = -Vector3.Scale(c, s); vis.transform.localScale = s;
                vis.AddComponent<MeshFilter>().sharedMesh = pm;
                var r = vis.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On; r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                var size = Vector3.Scale(pm.bounds.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                bool concrete = ConcreteParts && (obj.IsFort || ConcreteAllBuildings);
                if (concrete) size = MakeConcretePart(root, vis, size, obj.Id * 131 + bigParts.Count * 17 + 5, mats.Length > 0 ? mats[0] : null);
                var bc = root.AddComponent<BoxCollider>(); bc.size = Vector3.Max(size * (concrete ? 0.92f : 0.82f), Vector3.one * 0.3f); bc.sharedMaterial = Concrete();
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = Mathf.Clamp(size.x * size.y * size.z * 9f, 150f, 3000f);
                rb.linearDamping = 0.05f; rb.angularDamping = 2.2f; rb.maxAngularVelocity = 3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var bp = root.AddComponent<BigPart>(); bp.owner = this; bp.rb = rb; bp.concrete = concrete;
                root.SetActive(false);
                bigParts.Add(rb);
            }
        }

        /// <summary>endings_v1 chunk look: the baked rounded chunk keeps its shape (never the Unity cube), one clean flat colour
        /// from the building's own palette, varied size; one in eight in the owner's team accent (set at the death).</summary>
        void ChunkLook(GameObject go, int i, List<Color> pal, bool fort)
        {
            var mr = go.GetComponent<MeshRenderer>(); var t = go.transform;
            float f1 = (i * 0.7548776f) % 1f, f2 = (i * 0.5698403f + 0.31f) % 1f;
            float s = 0.6f + 0.45f * f1;
            if (i % 5 == 1) s *= 0.7f;                                                                  // some small bits between them
            if (fort && i % 7 == 3) s *= 1.35f;                                                         // a few big boulders (was 1.55)
            t.localScale = Vector3.Scale(t.localScale, new Vector3(s, s * (0.82f + 0.3f * f2), s * (0.9f + 0.2f * f1)));
            t.localRotation *= Quaternion.Euler(f2 * 360f, f1 * 360f, (f1 + f2) * 180f);
            // colour: the light wall half most of the time, trim / dark quarter every 4th, a few scorched
            int n = pal.Count, q = Mathf.Max(1, n / 4);
            var c = i % 4 == 1 ? pal[(i * 37 + 11) % q] : pal[Mathf.Min(n - 1, n / 2 + (i * 37 + 11) % Mathf.Max(1, n - n / 2))];
            c *= 0.86f;
            float scorch = i % 6 == 5 ? 0.42f : 0.06f + 0.1f * f2;
            c = Color.Lerp(c, new Color(0.2f, 0.17f, 0.15f), scorch); c.a = 1f;
            var src = mr.sharedMaterial;
            if (src)
            {
                if (!chunkMats.TryGetValue(src, out var dm))
                {
                    dm = new Material(src) { name = src.name + "_chunk" };
                    if (dm.HasProperty("_TeamMode")) dm.SetFloat("_TeamMode", 0f);                     // flat palette colours: no hue band
                    if (dm.HasProperty("_Accent")) dm.SetFloat("_Accent", 0f);
                    if (dm.HasProperty("_FlatShade")) dm.SetFloat("_FlatShade", 1f);                   // low-poly facets
                    if (dm.HasProperty("_Smoothness")) dm.SetFloat("_Smoothness", 0.28f);
                    chunkMats[src] = dm;
                }
                mr.sharedMaterial = dm;
            }
            mpbV1 ??= new MaterialPropertyBlock();
            mpbV1.Clear();
            mpbV1.SetTexture(BaseMapId, Texture2D.whiteTexture);
            mpbV1.SetColor(BaseColorIdV1, c);
            mr.SetPropertyBlock(mpbV1);
        }

        /// <summary>endings_v1 collapse. false = not handled (no fracture set): the original path runs.</summary>
        bool CollapseV1(Vector3 blast, float power)
        {
            if (!RuinV1 || obj == null || (!RuinAllBuildings && !obj.IsFort)) return false;
            if (!prepared) Prepare();                                                               // not prewarmed (late building): prepare now
            if (!prepared || !fracture) return false;
            collapsed = true; v1 = true;
            var t = transform; bool fort = obj.IsFort;
            intact.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>()) if (r.enabled && !BuildingRuins.IsGunPart(r.transform, t)) { intact.Add(r); r.enabled = false; }
            var accent = TeamColors.Of(obj.Owner); bool owned = obj.Owner != Team.Neutral;
            fracture.SetActive(true);
            var bounds = new Bounds(t.position, Vector3.zero);
            foreach (Transform c in fracture.transform) { var r = c.GetComponent<Renderer>(); if (r) bounds.Encapsulate(r.bounds); }
            float h = Mathf.Max(1f, bounds.size.y), size = Mathf.Max(bounds.size.x, bounds.size.z);
            var center = new Vector3(blast.x, t.position.y + h * 0.15f, blast.z);
            var foot = new Bounds(t.position, new Vector3(size * 0.8f, 100f, size * 0.8f));
            var game = ZGame.I;
            int i = 0;
            foreach (var p in pieces)
            {
                if (!p) continue;
                var go = p.gameObject;
                if (p.lib != null) { if (p.lib.band) ConcreteChunkLib.SetTrim(go.GetComponent<MeshRenderer>(), p.lib, owned ? accent : TeamColors.Neutral, new System.Random(obj.Id * 31 + i)); }
                else if (owned && i % 8 == 3)
                {   // team accent chunk (owner colour, a touch scorched)
                    var mr = go.GetComponent<MeshRenderer>(); mpbV1 ??= new MaterialPropertyBlock();
                    mr.GetPropertyBlock(mpbV1); mpbV1.SetColor(BaseColorIdV1, Color.Lerp(accent, Color.black, 0.18f)); mr.SetPropertyBlock(mpbV1);
                }
                var rb = p.rb; p.footprint = foot;
                float up01 = Mathf.Clamp01((go.transform.position.y - t.position.y) / h);
                var away = go.transform.position - center; away.y = 0;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
                float kick = power * (0.6f + 0.4f * size / 10f);
                if (fort)
                {
                    kick = Mathf.Min(kick, 1.3f);
                    rb.linearVelocity = away * Random.Range(1.5f, 4f) * kick * (0.4f + 0.6f * up01) + Vector3.up * Random.Range(4f, 8f) * kick * (0.4f + 0.6f * up01);
                }
                else rb.linearVelocity = away * Random.Range(2f, 6.5f) * kick * (0.5f + up01) + Vector3.up * Random.Range(6f, 12.5f) * kick * (0.45f + up01);
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(2f, 7f);
                i++;
            }
            // the rubble pile: chunks resting in the footprint, half sunk, from the first frame (hidden by the blast)
            var rnd = new System.Random(obj.Id * 7919 + 3);
            foreach (var p in rubble)
            {
                if (!p) continue;
                var tr = p.transform;
                float x = Mathf.Lerp(bounds.min.x, bounds.max.x, 0.14f + 0.72f * (float)rnd.NextDouble());
                float z = Mathf.Lerp(bounds.min.z, bounds.max.z, 0.14f + 0.72f * (float)rnd.NextDouble());
                float gy = game ? game.GroundY(x, z) : t.position.y;
                if (p.lib != null)
                {
                    float yaw = (float)rnd.NextDouble() * 360f, ly = Mathf.Abs(tr.lossyScale.y);
                    float sink = p.lib.cls == "HEAP" ? 0.03f : 0.25f * (p.lib.size_xyz != null && p.lib.size_xyz.Length > 1 ? p.lib.size_xyz[1] : 0.3f) * ly;
                    tr.SetPositionAndRotation(new Vector3(x, gy - p.lib.rest_bottom_y * ly - sink, z), Quaternion.Euler(0f, yaw, 0f));
                    continue;
                }
                tr.localScale *= 1.0f + 0.4f * (float)rnd.NextDouble();
                var mf = tr.GetComponent<MeshFilter>(); float half = mf && mf.sharedMesh ? mf.sharedMesh.bounds.extents.y * Mathf.Abs(tr.lossyScale.y) : 0.5f;
                tr.SetPositionAndRotation(new Vector3(x, gy + half * 0.35f, z), Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f));
            }
            // the big parts: heavy, thrown up and out, tumbling slowly
            LaunchBigParts(center, power, fort);
            // fire, dust, sound (as before)
            var fx = FxManager.I; var au = CombatAudio.I;
            if (fx)
            {
                fx.DemolitionDust(t.position, bounds.size.x, bounds.size.z, Mathf.Clamp(size / 10f, 0.7f, 1.8f));
                StartCoroutine(Blasts(bounds, Mathf.Clamp(Mathf.RoundToInt(size), 5, 14)));
            }
            bool small = obj.Type == "radar";
            au?.Play("collapse", t.position, 1f, 0, obj.Id, fort ? new[] { "v3-collapse-large-a", "v3-collapse-large-b", "building-collapse-20260909" }
                                                          : small ? new[] { "v3-collapse-small-a", "v3-collapse-medium-a" } : new[] { "v3-collapse-medium-a", "v3-collapse-medium-b", "building-collapse-20260909" });
            if (AudioDirector.Has("dst_structure_groan")) AudioDirector.Fire("dst_structure_groan", t.position + Vector3.up * 2f, obj.Id);
            smokeCenter = bounds.center; smokeSpread = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.5f;
            StartCoroutine(SettleV1(t, h, bounds));
            fireSpan = fort ? 28f : 14f; fireUntil = Time.time + fireSpan; smokeUntil = Time.time + (fort ? 45f : 16f);
            RuinFireBegin(bounds);                                                                  // ground fires and smoke in the ruin for a while (BuildingBreaker.RuinFire.cs)
            if (fort) StartCoroutine(CookOff(bounds));
            if (fort) OzFortInferno.Spawn(bounds.center, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.4f);
            return true;
        }

        void LaunchBigParts(Vector3 center, float power, bool fort)
        {
            if (bigParts.Count == 0) return;
            RocksBigParts(center, fort);                                                                           // items 331 + 332: the big ones meet the rocks (BuildingBreaker.Rocks.cs)
            float k = Mathf.Clamp(power, 0.8f, fort ? 1.1f : 1.8f);
            foreach (var rb in bigParts)
            {
                if (!rb) continue;
                var go = rb.gameObject; go.SetActive(true);
                var bp = go.GetComponent<BigPart>(); if (bp) bp.Arm(Random.Range(BigPartLifeMin, BigPartLifeMax), fort);
                if (bp && bp.lib != null) { LaunchLibChunk(rb, bp, center, k); continue; }
                if (!bp || !bp.concrete) PaintLike(go.GetComponentInChildren<MeshRenderer>(), RuinCharred * 0.6f);   // the building's own paint, scorched (concrete blocks keep their own tone)
                var away = go.transform.position - center; away.y = 0;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
                float up01 = Mathf.Clamp01((go.transform.position.y - transform.position.y) / 8f);
                rb.linearVelocity = away * Random.Range(1.4f, 3.6f) * k + Vector3.up * Random.Range(3.5f, 6.5f) * k * (0.6f + 0.6f * up01);
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(0.3f, 1.1f);
            }
        }

        /// <summary>Under the dust (0.9 s): the ruin replaces the model (or the model sinks when there is no ruin mesh).</summary>
        IEnumerator SettleV1(Transform t, float h, Bounds b)
        {
            yield return new WaitForSeconds(0.9f);
            if (!collapsed) yield break;
            if (ruinGo)
            {
                ruinGo.SetActive(true);
                PaintLike(ruinGo.GetComponent<MeshRenderer>(), RuinCharred);
            }
            else
            {   // no ruin mesh: the model sinks half into the ground, tilted a little, lightly charred (never the black squashed slab)
                sunk = true; sinkFromPos = t.position; sinkFromRot = t.rotation;
                t.position -= Vector3.up * h * 0.5f;
                t.rotation = Quaternion.AngleAxis(3.5f, Vector3.Cross(Vector3.up, Random.onUnitSphere).normalized) * t.rotation;
                var tp = GetComponent<TeamPaint>(); if (tp) tp.Charred = RuinCharred;
                foreach (var r in intact) if (r) r.enabled = true;
            }
            FxManager.I?.DemolitionDust(t.position, b.size.x * 0.8f, b.size.z * 0.8f, 0.6f, 0.35f, 1.4f);
            yield return new WaitForSeconds(1.2f);
            CombatAudio.I?.Play("settle", t.position, 0.8f, 0, obj.Id, "building-settle-20260909");
        }

        /// <summary>The model's own look (team colour, panels) from its intact renderer, scorched by `charred`, no accent band
        /// (the band belongs to the missing top). Keeps TeamPaint's per-material blocks.</summary>
        void PaintLike(MeshRenderer r, float charred)
        {
            if (!r || !mainR) return;
            mpbV1 ??= new MaterialPropertyBlock();
            var mats = r.sharedMaterials; float k = 1f - 0.78f * charred;
            for (int i = 0; i < mats.Length; i++)
            {
                mainR.GetPropertyBlock(mpbV1, i);
                mpbV1.SetFloat(AccentLoIdV1, 1e6f); mpbV1.SetFloat(AccentHiIdV1, -1e6f);
                if (mats[i] && mats[i].HasProperty(BaseColorIdV1)) mpbV1.SetColor(BaseColorIdV1, mats[i].GetColor(BaseColorIdV1) * new Color(k, k, k, 1f));
                r.SetPropertyBlock(mpbV1, i);
            }
        }

        /// <summary>Fort: ammunition cooking off in the ruin for a few seconds after the blast (Z's side explosions, later ones).</summary>
        IEnumerator CookOff(Bounds b)
        {
            yield return new WaitForSeconds(2.2f);
            for (int i = 0; i < 6 && collapsed; i++)
            {
                var p = new Vector3(Random.Range(b.min.x, b.max.x), 0f, Random.Range(b.min.z, b.max.z));
                p.y = (ZGame.I ? ZGame.I.GroundY(p.x, p.z) : b.min.y) + Random.Range(0.8f, 2.4f);
                var fxp = OpenZFxPlayer.I;
                if (fxp && fxp.isActiveAndEnabled) fxp.Blast(p, 0f, i % 2 == 0 ? FxBlastKind.Barrel : FxBlastKind.Rocket, 7100 + i);
                else FxManager.I?.Explosion(p, 1.1f);
                if (AudioDirector.Has("exp_fort_side_booms")) AudioDirector.Fire("exp_fort_side_booms", p, -1, 0.8f);
                else CombatAudio.I?.Play("fortboom", p, 0.7f, 0.1f, -1, "explosion-v1-20260909", "explosion-v2-20260909");
                yield return new WaitForSeconds(Random.Range(0.45f, 1.1f));
            }
        }

        /// <summary>Smoke column over the ruin (fort 45 s, others 16 s) on top of BuildingBreaker's fire on the pieces.</summary>
        float nextSmoke;
        void LateUpdate()
        {
            if (!v1 || !collapsed || Time.time > smokeUntil || Time.time < nextSmoke) return;
            nextSmoke = Time.time + 0.3f;
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled) return;
            float k = Mathf.Clamp01((smokeUntil - Time.time) / 12f);
            var c = smokeCenter; float spread = smokeSpread;
            var gy = ZGame.I ? ZGame.I.GroundY(c.x, c.z) : c.y;
            float a = Time.time * 1.7f + obj.Id;
            var p = new Vector3(c.x + Mathf.Cos(a) * spread * Random.value, gy + 2.2f, c.z + Mathf.Sin(a) * spread * Random.value);
            fxp.WreckSmoke(p, (obj.IsFort ? 2.6f : 1.6f) * (0.5f + 0.5f * k), Time.time < fireUntil);
        }

        void RestoreV1()
        {
            collapsed = false; v1 = false;
            StopAllCoroutines();
            if (sunk) { transform.SetPositionAndRotation(sinkFromPos, sinkFromRot); sunk = false; }
            foreach (var r in intact) if (r) r.enabled = true;
            var tp = GetComponent<TeamPaint>(); if (tp) tp.Charred = 0f;
            if (ruinGo) ruinGo.SetActive(false);
            foreach (var pc in pieces) if (pc && pc.gameObject.activeInHierarchy) PopOut.Begin(pc.transform, PopOut.Style.Debris, null, false, false);
            foreach (var pc in rubble) if (pc && pc.gameObject.activeInHierarchy) PopOut.Begin(pc.transform, PopOut.Style.Debris, null, false, false);
            foreach (var rb in bigParts) if (rb && rb.gameObject.activeInHierarchy) PopOut.Begin(rb.transform, PopOut.Style.Debris, null, false, false);
            if (holder) Destroy(holder, PopOut.Duration + 0.05f);
            holder = null; fracture = null; ruinGo = null; pieces.Clear(); rubble.Clear(); bigParts.Clear();
            prepared = false; fireUntil = 0; smokeUntil = 0;
            FxManager.I?.DemolitionDust(transform.position, 6f, 6f, 0.6f);
            StartCoroutine(Reprepare());
        }

        IEnumerator Reprepare()
        {
            yield return new WaitForSeconds(2.5f);                                                    // after the pop: a fresh set for the next death
            if (!collapsed) Prepare();
        }

        void OnDestroy() { if (holder) Destroy(holder); }

        /// <summary>A big model part: first hard landings crash (sound, dust, camera kick); after its life it freezes as part of the
        /// fort's ruin, or sinks into the ground (other buildings, which may be rebuilt).</summary>
        public class BigPart : MonoBehaviour
        {
            public BuildingBreaker owner; public Rigidbody rb; public bool concrete;
            public ConcreteChunkLib.Entry lib; public Vector3 partCenter;
            float age, life = 16f, contactAt = -10f; int crashes; bool keep, done;
            public void Arm(float lifeSeconds, bool stays) { life = lifeSeconds; keep = stays; age = 0f; crashes = 0; done = false; }
            bool RockPop() { if (!OzRockBody.OnRockNow(gameObject) || !OzRockBody.PopIfOnRock(gameObject, () => gameObject.SetActive(false))) return false; done = true; return true; }   // items 331 + 332 (asked every sinking frame: nothing made unless it pops)
            void Start() { OzWaterLanding.Watch(transform, "building part", rb); }                  // b27: the shared water landing rule (the part is switched on at the collapse)

            void OnCollisionEnter(Collision c)
            {
                float speed = c.relativeVelocity.magnitude;
                if (age < 0.2f || speed < 2.5f || crashes >= 2 || age - contactAt < 0.4f) return;
                contactAt = age; crashes++;
                var p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
                if (OzWaterLanding.UnderSurface(p)) return;                                         // b27: it touched the bed under a drawn water surface: no crash, dust or shake there (its splash is made at the surface)
                if (lib != null) { LibCrash(p, speed); return; }
                var fx = FxManager.I; if (fx) { fx.DemolitionDust(p, 3f, 3f, Mathf.Clamp(speed * 0.08f, 0.4f, 1.1f)); fx.Dust(p, 1.2f); }
                var fxp = OpenZFxPlayer.I; if (fxp && fxp.Shake != null) fxp.Shake.Impact(p, crashes == 1 ? 1.1f : 0.6f);
                float vol = Mathf.Clamp01(0.45f + speed * 0.05f);
                if (crashes == 1 && AudioDirector.Has("dst_collapse_large")) AudioDirector.Fire("dst_collapse_large", p, GetInstanceID(), vol);
                else if (AudioDirector.Has("dst_debris_concrete")) AudioDirector.Fire("dst_debris_concrete", p, GetInstanceID(), vol);
                else if (AudioDirector.Has("dst_debris_metal")) AudioDirector.Fire("dst_debris_metal", p, GetInstanceID(), vol);
                else CombatAudio.I?.Play("debris", p, vol, 0, GetInstanceID(), "v3-collapse-large-a", "final-metal-land-a");
            }

            /// <summary>
            /// concrete thud + dust (max 8 / s), S / XS = dust and a quiet small concrete knock through the global debris budget; no shake.</summary>
            void LibCrash(Vector3 p, float speed)
            {
                var fx = FxManager.I; int id = GetInstanceID();
                float vol = Mathf.Clamp01(0.45f + speed * 0.05f);
                if (lib.cls == "L" && crashes == 1 && owner && owner.HeavyBudget(true))
                {
                    if (fx) fx.DemolitionDust(p, 3f, 3f, Mathf.Clamp(speed * 0.08f, 0.4f, 1.1f));
                    if (AudioDirector.Has("dst_collapse_large")) AudioDirector.Fire("dst_collapse_large", p, id, vol);
                    else AudioDirector.Fire("dst_debris_concrete", p, id, vol);
                    LibCrashes[0]++;
                }
                else if (lib.Heavy && owner && owner.HeavyBudget(false))
                {
                    if (fx) fx.Dust(p, 1.0f);
                    AudioDirector.Fire("dst_debris_concrete", p, id, vol * (lib.cls == "L" ? 1f : 0.85f));
                    LibCrashes[1]++;
                }
                else if (!lib.Heavy)
                {
                    if (fx) fx.Dust(p, 0.5f);
                    if (owner && owner.TrySound()) { AudioDirector.Fire("dst_debris_concrete", p, id, Mathf.Clamp(0.18f + speed * 0.025f, 0.18f, 0.4f) * (lib.cls == "XS" ? 0.8f : 1f)); LibCrashes[2]++; }   // quieter for small chunks
                }
            }

            void Update()
            {
                if (done) return;
                age += Time.deltaTime;
                if (age > 5f && rb && !rb.isKinematic && rb.IsSleeping()) rb.isKinematic = true;          // settled: no more physics cost
                if (age < life) return;
                if (keep) { if (rb) rb.isKinematic = true; done = true; return; }                     // the fort's ruin keeps its parts
                if (rb) rb.isKinematic = true;
                if (!done && RockPop()) return;                                                                      // items 331 + 332: on a rock it pops away, never sinks through it
                float t = age - life;
                transform.position += Vector3.down * 0.6f * Time.deltaTime;                             // sinks ~1.8 m over 3 s
                if (t > 3f) { gameObject.SetActive(false); done = true; }
            }
        }
    }
}
