using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// What is left on a fort's roof where its big cannon stood, once the cannon is shot off while the fort still stands.
    /// The intact pedestal (a flat dark round pad with two lugs, part of the fort's own mesh) read as an unfinished, untextured
    /// platform with a hole. It is now replaced by ONE modelled wreck, fitted to the pedestal's real foot print:
    ///  - Resources/OpenZ/FortGunMount/OZ_FortGunWreck (model): the pedestal ring cracked into heaved segments and torn away in
    ///    two sectors, the break jagged; a pit through the roof slab (slab edge with thickness, undercut, rubble floor); the
    ///    slab's rebar grid across it, bars bent and snapped; a twisted arc of the cannon's slewing ring with its gear teeth, a
    ///    buckled deck plate and the pivot's stump inside; slab pieces with rebar and burnt faces lying round;
    ///  - its own texture set, painted from the fort's own colours (soot graded from the break to the fort's dark coat, bare
    ///    burnt concrete on every broken face); it wears a copy of the fort's own material with these maps;
    ///  - a soot-and-cracks mark on the roof plate round it: a flat mesh of the same model drawn with OpenZ/ScorchDecal (it
    ///    multiplies what is under it and is depth-offset, so it grades to the roof's own colour and cannot z-fight);
    ///  - fire for 9 s and smoke for 45 s at the model's anchors, embers at night, a glow in the rubble that cools over two
    ///    minutes; a concrete crack and a settling sound when it breaks, a fire loop while it burns.
    /// To seat the model the pedestal's triangles are taken out of this fort's FortBase mesh (a copy of the mesh, cached; the
    /// rule is a foot print round the gun's pivot and a height, counted and logged: 766 triangles on the shipped fort).
    /// View only: the rules do not know it (the cannon stays dead, the fort's health and targeting are untouched). It belongs to
    /// its fort's body: a destroyed fort keeps standing until the bomb lands, and the wreck stays on its roof with it (fire, smoke
    /// and sound too); on the frame the fort collapses (the collapse switches the body's renderers off) it is removed and the roof
    /// mesh is put back, so nothing is left over the ruin and nothing pops back on camera before it.
    /// </summary>
    public class FortGunMount : MonoBehaviour
    {
        /// <summary>Checks: mounts built and removed since the game started; of the last one: triangles cut out of the roof, triangles
        /// of the wreck model, anchors found (of 6), whether the break sound and the fire loop were accepted by the audio director.</summary>
        public static int Built, Removed, LastCut, LastTriangles, LastAnchors;
        public static bool LastCrackSound, LastFireLoop;
        /// <summary>Cost of the last mount, real milliseconds: what Build took, and the longest frame of the CostWindow seconds after it
        /// (the frame of the break itself is in it). Logged once per mount when the window ends.</summary>
        public static float LastBuildMs, LastLongestFrameMs;
        public const float CostWindow = 3f;
        /// <summary>Forts made ready ahead of time by Prewarm, and what the last of them took (real milliseconds).</summary>
        public static int Prewarmed;
        public static float LastPrewarmMs;
        /// <summary>Seconds of fire, of smoke, and of embers at night after the cannon is shot off.</summary>
        public const float FireSeconds = 9f, SmokeSeconds = 45f, EmberSeconds = 120f;
        public const string Res = "OpenZ/FortGunMount/";
        /// <summary>Triangles of the pedestal in the shipped fort mesh (Models/Gates/Fort_Gate.glb, FortBase). Another count = the
        /// fort's mesh changed: build the kit again from the new mesh (the kit's tools), do not adjust this number by hand.</summary>
        public const int ExpectedCut = 766;

        // The cut rule, metres round the gun's pivot (FortBase mesh space x 10.53449; the pivot = FortGunYaw's place in Z_fort.prefab).
        const float MeshToM = 10.53449f;
        static readonly Vector3 Pivot = new Vector3(0.00084548676f, 0.11239612f, -0.058455955f);
        const float CutR = 2.70f, CutLugX = 1.20f, CutLugZ = 2.95f, CutHMax = -0.288f, CutHTop = 0.30f;

        ZObject fort; Vector3 centre; float radius = 2.5f, born, next, nextGlow, bornReal, buildMs, worstFrameMs;
        MeshFilter baseMf; MeshRenderer baseR; Mesh whole, cutMesh; Material mat; int serial, nth; bool crackB, settle, smoulder, loopOff, costLogged; string loopKey;
        Vector3 fireA, fireB, smokeAt; readonly List<Vector3> embers = new List<Vector3>();
        static readonly List<FortGunMount> all = new List<FortGunMount>();
        public static IReadOnlyList<FortGunMount> All => all;
        public Vector3 Centre => centre; public float Radius => radius; public ZObject Fort => fort;

        static readonly Dictionary<Mesh, (Mesh cut, int removed)> cutCache = new Dictionary<Mesh, (Mesh, int)>();
        static Material sDecal;
        static Object[] warmAssets;                                                                        // held, so the model and the maps stay loaded
        static readonly List<Transform> warmed = new List<Transform>();
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BumpMapId = Shader.PropertyToID("_BumpMap"), MaskMapId = Shader.PropertyToID("_MaskMap"),
                            EmissionMapId = Shader.PropertyToID("_EmissionMap"), EmissionMapColorId = Shader.PropertyToID("_EmissionMapColor"),
                            EmissionNightId = Shader.PropertyToID("_EmissionNight"), UseNormalId = Shader.PropertyToID("_UseNormalMap"),
                            UseMaskId = Shader.PropertyToID("_UseMaskMap"), TeamModeId = Shader.PropertyToID("_TeamMode");

        /// <summary>Builds the wrecked mount of this fort's cannon. fortView = the fort's view object (FortBase is found under it).</summary>
        public static FortGunMount Build(ZObject fort, Transform fortView)
        {
            if (fort == null || !fortView) return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            MeshFilter mf = null;
            foreach (var f in fortView.GetComponentsInChildren<MeshFilter>(true)) if (f.name == "FortBase" && f.sharedMesh) { mf = f; break; }
            var baseR = mf ? mf.GetComponent<MeshRenderer>() : null;
            var prefab = Resources.Load<GameObject>(Res + "OZ_FortGunWreck");
            var albedo = Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Albedo");
            var decalTex = Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Decal");
            if (!mf || !baseR || !baseR.sharedMaterial || !prefab || !albedo || !decalTex)
            {
                Debug.LogError($"[OpenZ] fort #{fort.Id}: wrecked mount NOT built (FortBase {(bool)mf}, model {(bool)prefab}, albedo {(bool)albedo}, soot mark {(bool)decalTex}; Resources/{Res}): the roof stays as it was");
                return null;
            }
            var cut = CutPedestal(mf.sharedMesh, out int removed);
            if (!cut) { Debug.LogError($"[OpenZ] fort #{fort.Id}: wrecked mount NOT built: FortBase's mesh is not readable"); return null; }
            if (removed != ExpectedCut)
                Debug.LogWarning($"[OpenZ] fort #{fort.Id}: wrecked mount: {removed} triangles cut out of the roof, expected {ExpectedCut}: the fort's mesh is not the one the wreck was fitted to (look at the seam; build the kit again from this mesh)");

            var go = new GameObject("OZ_FortGunMount_" + fort.Id);
            var m = go.AddComponent<FortGunMount>();
            m.fort = fort; m.baseMf = mf; m.baseR = baseR; m.whole = mf.sharedMesh; m.cutMesh = cut; m.born = Time.time; m.next = Time.time + 0.3f;
            m.bornReal = Time.unscaledTime;
            m.loopKey = "fortmount_" + fort.Id;
            mf.sharedMesh = cut;
            // the wreck lives in FortBase's own space (same importer, same units): no fitting by eye
            var t = mf.transform;
            go.transform.SetPositionAndRotation(t.position, t.rotation); go.transform.localScale = t.lossyScale;
            var model = Instantiate(prefab, go.transform, false); model.name = "Wreck";
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            m.centre = t.TransformPoint(Pivot);

            var normal = Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Normal");
            var mask = Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_MaskMap");
            var emission = Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Emission");
            int tris = 0;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.gameObject.layer = mf.gameObject.layer;
                var rmf = r.GetComponent<MeshFilter>();
                if (r.name.StartsWith("Decal"))
                {   // soot and cracks on the roof plate: multiplied over it, depth-offset (OpenZScorchDecal.shader)
                    var dm = DecalMat(decalTex);
                    if (dm) r.sharedMaterial = dm; else r.enabled = false;
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    continue;
                }
                // the fort's own material with the wreck's maps: lit, shadowed and night-graded exactly like the roof round it
                m.mat = new Material(baseR.sharedMaterial) { name = "OZ_FortGunWreck" };
                m.mat.SetTexture(BaseMapId, albedo);
                m.mat.SetFloat(TeamModeId, 0f);                                                          // no team paint on a wreck (the map has no mask)
                if (normal) m.mat.SetTexture(BumpMapId, normal); m.mat.SetFloat(UseNormalId, normal ? 1f : 0f);
                if (mask) m.mat.SetTexture(MaskMapId, mask); m.mat.SetFloat(UseMaskId, mask ? 1f : 0f);
                m.mat.SetTexture(EmissionMapId, emission ? emission : Texture2D.blackTexture);
                m.mat.SetColor(EmissionMapColorId, Color.black); m.mat.SetFloat(EmissionNightId, 0.35f);   // the glow shows a little by day, fully at night
                r.sharedMaterial = m.mat;
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                if (rmf && rmf.sharedMesh) for (int s = 0; s < rmf.sharedMesh.subMeshCount; s++) tris += (int)(rmf.sharedMesh.GetIndexCount(s) / 3);
            }
            // where the existing fire, smoke and embers go: the model's anchors
            int anchors = 0;
            m.fireA = m.Anchor(model.transform, "Anchor_Fire_A", new Vector3(0.62f, -0.55f, 0.78f), ref anchors);
            m.fireB = m.Anchor(model.transform, "Anchor_Fire_B", new Vector3(-1.15f, -0.2f, -1.45f), ref anchors);
            m.smokeAt = m.Anchor(model.transform, "Anchor_Smoke", new Vector3(-0.1f, -0.25f, 0.1f), ref anchors);
            m.embers.Add(m.Anchor(model.transform, "Anchor_Embers_1", new Vector3(1.6f, -0.2f, 1.35f), ref anchors));
            m.embers.Add(m.Anchor(model.transform, "Anchor_Embers_2", new Vector3(-1.3f, -0.18f, -1.6f), ref anchors));
            m.embers.Add(m.Anchor(model.transform, "Anchor_Embers_3", new Vector3(-0.9f, 0.02f, 1.5f), ref anchors));

            // the break has its sounds: concrete cracking now, again a moment later, the rubble settling; then the fire
            int em = 905000 + fort.Id;
            bool crack = AudioDirector.Has("dst_debris_concrete");
            if (crack) AudioDirector.Fire("dst_debris_concrete", m.centre, em, 0.95f, 0.9f);
            bool loop = AudioDirector.I && AudioDirector.Has("dst_fire_loop") && AudioDirector.I.StartLoop(m.loopKey, "dst_fire_loop", m.fireA, null, 0.75f) > 0;

            LastCut = removed; LastTriangles = tris; LastAnchors = anchors; LastCrackSound = crack; LastFireLoop = loop; Built++;
            all.Add(m);
            m.nth = Built; m.buildMs = LastBuildMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[OpenZ] fort #{fort.Id}: cannon shot off, wrecked mount built (roof: {removed} triangles cut, expected {ExpectedCut}; model {tris} triangles; anchors {anchors}/6; break sound {(crack ? "yes" : "NO")}, fire loop {(loop ? "yes" : "NO")})");
            return m;
        }

        /// <summary>Makes a fort ready ahead of time: loads the wreck's model and its five maps and makes the roof copy without the
        /// pedestal, so that none of it is done on the frame a cannon is shot off. Changes nothing on screen (FortBase keeps its mesh).
        /// Once per fort view; a later call for the same view returns at once. Nothing calls it unless the kit was applied with
        /// --prewarm (FortGunView.LateUpdate then calls it 2 s into the level, after the flag cut has swapped FortBase's mesh).</summary>
        public static void Prewarm(Transform fortView)
        {
            if (!fortView || warmed.Contains(fortView)) return;
            warmed.RemoveAll(t => !t); warmed.Add(fortView);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (warmAssets == null || !warmAssets[0])
                warmAssets = new Object[]
                {
                    Resources.Load<GameObject>(Res + "OZ_FortGunWreck"), Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Albedo"),
                    Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Decal"), Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Normal"),
                    Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_MaskMap"), Resources.Load<Texture2D>(Res + "OZ_FortGunWreck_Emission"),
                };
            int loaded = 0; foreach (var a in warmAssets) if (a) loaded++;
            int removed = -1;
            foreach (var f in fortView.GetComponentsInChildren<MeshFilter>(true))
                if (f.name == "FortBase" && f.sharedMesh)
                {
                    if (!f.sharedMesh.name.EndsWith("_nomount")) CutPedestal(f.sharedMesh, out removed);       // a roof already cut is left alone
                    break;
                }
            Prewarmed++; LastPrewarmMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[OpenZ] wrecked mount made ready for {fortView.name} ({loaded}/6 assets loaded, roof copy: {removed} triangles cut, expected {ExpectedCut}; {LastPrewarmMs:0.0} ms)");
        }

        /// <summary>World place of a named anchor of the model; without it, the same place given in metres round the pivot (FortBase's mesh axes).</summary>
        Vector3 Anchor(Transform model, string name, Vector3 fallbackM, ref int found)
        {
            var a = FindDeep(model, name);
            if (a) { found++; return a.position; }
            return baseMf.transform.TransformPoint(Pivot + fallbackM / MeshToM);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        static Material DecalMat(Texture2D tex)
        {
            if (sDecal) return sDecal;
            var sh = Shader.Find("OpenZ/ScorchDecal");
            if (!sh) { Debug.LogError("[OpenZ] wrecked mount: shader OpenZ/ScorchDecal missing: no soot mark on the roof plate"); return null; }
            sDecal = new Material(sh) { name = "OZ_FortGunWreckDecal", mainTexture = tex };
            sDecal.SetFloat("_Strength", 1f); sDecal.SetFloat("_Opacity", 1f);
            return sDecal;
        }

        /// <summary>A copy of the fort's mesh without the gun pedestal (the round pad, its bevel, wall and two lugs): every triangle
        /// whose centre lies in the pedestal's foot print round the pivot and which has a corner above the roof plate. The copy keeps
        /// the original bounds (TeamPaint's accent band reads them) and is shared by every fort with that mesh.</summary>
        static Mesh CutPedestal(Mesh src, out int removed)
        {
            removed = 0;
            if (!src || !src.isReadable) return null;
            if (cutCache.TryGetValue(src, out var e) && e.cut) { removed = e.removed; return e.cut; }
            var vs = src.vertices; var cut = Instantiate(src); cut.name = src.name + "_nomount";
            var keep = new List<int>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s); keep.Clear();
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = (vs[tris[i]] - Pivot) * MeshToM, b = (vs[tris[i + 1]] - Pivot) * MeshToM, c = (vs[tris[i + 2]] - Pivot) * MeshToM;
                    Vector3 g = (a + b + c) / 3f;
                    bool foot = g.x * g.x + g.z * g.z < CutR * CutR || (Mathf.Abs(g.x) < CutLugX && Mathf.Abs(g.z) < CutLugZ);
                    if (foot && Mathf.Max(a.y, Mathf.Max(b.y, c.y)) > CutHMax && g.y < CutHTop && g.y > -1f) { removed++; continue; }
                    keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                }
                cut.SetTriangles(keep, s, false);
            }
            cut.bounds = src.bounds;
            cutCache[src] = (cut, removed);
            return cut;
        }

        void Update()
        {
            if (fort == null || !baseMf || !baseR || !baseMf.gameObject.activeInHierarchy || (fort.Destroyed && !baseR.enabled)) { Removed++; Destroy(gameObject); return; }   // it goes with its fort's BODY: a destroyed fort stands until the bomb lands (the collapse switches its renderers off), the wreck stays on it till then
            if (!costLogged)
            {   // what the break cost: the longest frame of its first seconds, real time (the first value read here is the frame Build ran in)
                worstFrameMs = Mathf.Max(worstFrameMs, Time.unscaledDeltaTime * 1000f);
                if (Time.unscaledTime - bornReal >= CostWindow)
                {
                    costLogged = true; LastLongestFrameMs = worstFrameMs;
                    Debug.Log($"[OpenZ] fort #{fort.Id}: wrecked mount {nth}: build {buildMs:0.0} ms, longest frame in the {CostWindow:0} s after it {worstFrameMs:0.0} ms");
                }
            }
            float age = Time.time - born;
            int em = 905000 + fort.Id;
            // sounds after the first crack
            if (!crackB && age >= 0.35f) { crackB = true; if (AudioDirector.Has("dst_debris_concrete")) AudioDirector.Fire("dst_debris_concrete", fireB, em + 1000, 0.6f, 1.1f); }
            if (!settle && age >= 0.9f) { settle = true; if (AudioDirector.Has("dst_debris_settle")) AudioDirector.Fire("dst_debris_settle", centre, em, 0.8f, 1f); }
            if (!smoulder && age >= FireSeconds) { smoulder = true; if (AudioDirector.I) AudioDirector.I.SetLoop(loopKey, 0.3f, 0.9f); }   // the fire is out: a low crackle under the smoke
            if (!loopOff && age >= SmokeSeconds) { loopOff = true; if (AudioDirector.I) AudioDirector.I.StopLoop(loopKey, 2.5f); }
            // the glow in the rubble cools
            if (mat && Time.time >= nextGlow && age < EmberSeconds + 1f)
            {
                nextGlow = Time.time + 0.15f;
                float k = age >= EmberSeconds ? 0f : 2.6f * Mathf.Exp(-age / 38f) * (0.82f + 0.18f * Mathf.Sin(Time.time * 7.3f + fort.Id));
                mat.SetColor(EmissionMapColorId, new Color(k, k, k, 1f));
            }
            if (age > Mathf.Max(SmokeSeconds, EmberSeconds) || Time.time < next) return;
            var fx = OpenZFxPlayer.I; if (!fx || !fx.isActiveAndEnabled) { next = Time.time + 1f; return; }
            if (age < SmokeSeconds)
            {
                float k = Mathf.Lerp(0.75f, 0.3f, age / SmokeSeconds);
                bool fire = age < FireSeconds;
                next = Time.time + OzFireDesync.FortBurn(fx, fort.Id, fire ? serial++ : serial, smokeAt, k, fire, fireA, fireB, Mathf.Lerp(0.3f, 0.8f, age / SmokeSeconds));   // item 400: its own beat
            }
            else if (NightGrade.NightK > 0.5f && FxManager.I && embers.Count > 0)
            {   // embers at night, now and then
                FxManager.I.Burn(embers[Random.Range(0, embers.Count)] + Vector3.up * 0.15f, 0.25f);
                next = Time.time + Random.Range(1.2f, 2.6f);
            }
            else next = Time.time + 1f;
        }

        void OnDestroy()
        {
            all.Remove(this);
            if (baseMf && whole && baseMf.sharedMesh == cutMesh) baseMf.sharedMesh = whole;                 // the roof as it was (the fort's own collapse takes over)
            if (AudioDirector.I && loopKey != null) AudioDirector.I.StopLoop(loopKey, 0.3f);
            if (mat) Destroy(mat);
        }
    }
}
