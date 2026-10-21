using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16: the two robots' shuttle beside the red fort, placed into the OPEN level 16 scene (the scene is never rebuilt; this
    /// menu replaces only its own two objects and can be run again; an object the first version of this kit left in the scene
    /// is replaced the same way).
    /// In z its about 1/3 of fort size. add also military search light near it
    /// and some blinking neon bloom lights on it as well and some light smoke from his exausts ... in future versions we might
    ///  * THE SHIP IS ONE PREFAB, Assets/OpenZ/Models/L16/Shuttle/L16_Shuttle.prefab, made here: its root on the ground under the
    ///    ship's middle, the dome looking along the root's forward, no place in it; the model's parts under Model, separate and
    ///    named (SH_Hull, SH_Engine_L / R, SH_Leg_FL / FR / BL / BR; the lights SH_Steady, SH_LampsA / B, SH_EngineGlow,
    ///    SH_NeonRun0 / 1 / 2, SH_NeonBlink; one material); RUIN_Wreck (the rear body and the two rear legs) and the six wreck
    ///    pieces that fly; on the root the components OzShuttleLights and OzShuttleExhaust (what a unit version keeps), TeamPaint,
    ///    and DetailProp + L16Shuttle (what makes it the decoration of this level).
    ///  * The scene holds one instance of it, named as the rules name it (ZWorld.L16ShuttleUid), under Environment, at
    ///    <see cref="PlaceX"/>, <see cref="PlaceZ"/>, the dome looking to <see cref="Heading"/>.
    ///    IT STANDS ON THE LEVEL'S GROUND: the ground under the middle of each of its four pads is read (the model's markers
    ///    MK_Pad_*), a plane is fitted through those four points and the ship is tilted to it, then lowered a little into the
    ///    grass (less when the ground under the pads is twisted, so that no pad stands more than 4.5 cm in the ground).
    ///  * THE ARMY SEARCHLIGHT, an object of its own beside it (<see cref="SearchlightName"/>): the kit's trailer
    ///    (L16_FieldSearchlight.fbx) with DetailProp and OzFieldSearchlight; the lamp head (the game's own searchlight drum and
    ///    yoke) is put on at the start of Play, so in the scene view the trailer stands without its head.
    /// The hut of the map the ship replaces (hut_61_117 under ZodObjects) is kept and switched off; plants standing under the ship
    /// or the trailer are switched off and remembered. Nothing here closes a tile: the rules close the ship's square at map load.
    /// </summary>
    public static class OpenZL16Shuttle
    {
        internal const string Dir = "Assets/OpenZ/Models/L16/Shuttle", Tex = "Assets/OpenZ/Textures/L16/Shuttle";
        internal const string Model = Dir + "/L16_Shuttle.fbx", MatPath = Dir + "/M_L16_Shuttle.mat", PrefabPath = Dir + "/L16_Shuttle.prefab";
        internal const string LampModel = Dir + "/L16_FieldSearchlight.fbx", LampMatPath = Dir + "/M_L16_FieldSearchlight.mat", LampColours = "L16_FieldSearchlight_Colours.png";
        public const string SearchlightName = "L16_Shuttle_Searchlight";
        /// <summary>The army searchlight beside the ship (false: removed from the level, the owner's item 173: the build makes none and
        /// takes an old one away).</summary>
        public static bool WithSearchlight = false;
        /// <summary>The pivot (m) and where the dome looks (degrees clockwise from north): south-south-east, towards the camera's
        /// tilted ship stays inside its 4 x 4 tiles (59 to 62, 115 to 118) and 4.85 m clear of the fort's east face.</summary>
        public const float PlaceX = 122.30f, PlaceZ = -234.40f, Heading = 150f;
        /// <summary>The ship's long side as a share of the fort's side, the fort's side the model was scaled for and the long side that gives (m).</summary>
        public const float ShareOfFort = 1f / 3f, FortSide = 20.0f, LongSide = 6.666f;
        /// <summary>The searchlight: 7.4 m from the ship's pivot towards the south-east (heading 135), 4.0 m from the hull.</summary>
        public const float LampX = 127.53f, LampZ = -239.63f;
        /// <summary>The seat: into the grass by SinkMax, less on twisted ground so that the deepest pad stands Deepest in the ground, never less than SinkMin.</summary>
        public const float SinkMax = 0.02f, SinkMin = 0.005f, Deepest = 0.045f;
        /// <summary>The yellow box of the owner's FIRST place picture on the ground (m): x from, x to, z from (south), z to (north). The ship at a third of the fort no longer fits it; kept for the note only.</summary>
        public static readonly float[] OwnerBox = { 118.9f, 124.1f, -236.5f, -228.2f };
        internal static readonly string[] Body = { "SH_Hull", "SH_Engine_L", "SH_Engine_R", "SH_Leg_FL", "SH_Leg_FR", "SH_Leg_BL", "SH_Leg_BR" };
        internal static readonly string[] Lights = { "SH_Steady", "SH_LampsA", "SH_LampsB", "SH_EngineGlow", "SH_NeonRun0", "SH_NeonRun1", "SH_NeonRun2", "SH_NeonBlink" };
        internal static readonly string[] Ship = Body.Concat(Lights).ToArray();
        internal static readonly string[] Flying = { "WR_Dome_L", "WR_Dome_R", "WR_Engine_L", "WR_Engine_R", "WR_Leg_FL", "WR_Leg_FR" };
        internal static readonly string[] Staying = { "WR_Body", "WR_Leg_BL", "WR_Leg_BR" };
        internal static readonly string[] Pads = { "MK_Pad_FL", "MK_Pad_FR", "MK_Pad_BL", "MK_Pad_BR" };
        internal static readonly string[] Markers = { "MK_Front", "MK_Top", "MK_Smoke", "MK_PoolA", "MK_PoolB", "MK_Exhaust_L", "MK_Exhaust_R", "MK_Vent_L", "MK_Vent_R" };
        internal static string HutName => "hut_" + ZWorld.L16ShuttleHutTile[0] + "_" + ZWorld.L16ShuttleHutTile[1];

        static void ImportModel(string path)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path); if (mi == null) return;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None; mi.importBlendShapes = false;
            mi.bakeAxisConversion = true; mi.isReadable = true; mi.meshCompression = ModelImporterMeshCompression.Off; mi.addCollider = false;      // readable: this menu and the checks measure the ship by its points
            mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.CalculateMikk;      // the normal picture was made for this tangent frame
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.SaveAndReimport();
        }

        static void ImportTex(string file, bool srgb, bool normal, bool alpha, bool fields = false)
        {
            string path = Tex + "/" + file;
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) { Debug.LogWarning("[OpenZ L16] missing texture " + path); return; }
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = srgb; ti.mipmapEnabled = !fields;
            ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = fields ? FilterMode.Point : FilterMode.Trilinear; ti.anisoLevel = fields ? 0 : 8; ti.maxTextureSize = fields ? 64 : 2048;
            ti.textureCompression = fields ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; ti.alphaIsTransparency = false;
            ti.SaveAndReimport();
        }

        static readonly (string, float)[] TeamLitBase =
        {
            ("_UseVertexColor", 0f), ("_Triplanar", 0f), ("_Stylize", 0f), ("_UnitLook", 0f), ("_PaintFromAlpha", 0f), ("_Degreen", 0f), ("_SatScale", 1f), ("_ValScale", 1f),
            ("_MaskArea", 0f), ("_MaskDilate", 0f), ("_FlatShade", 0f), ("_NightClass", 3f), ("_SeeThru", 0f), ("_Rust", 0f), ("_Accent", 1f), ("_AccentFrom", -0.1f), ("_AccentTo", 1.1f),
        };

        /// <summary>The ship's one material: the game's team shader set up as the fort's own (team mask in the colour picture's alpha,
        /// normal picture, metal / smoothness picture, emission picture). The emission tint is black here: only the light parts
        /// glow, each through its renderer (OzShuttleLights), so the hull and the wreck never do.</summary>
        internal static Material ShipMaterial(System.Text.StringBuilder sb)
        {
            ImportTex("L16_Shuttle_Albedo.png", true, false, true); ImportTex("L16_Shuttle_Emission.png", true, false, false);
            ImportTex("L16_Shuttle_MaskMap.png", false, false, true); ImportTex("L16_Shuttle_Normal.png", false, true, false); ImportTex("L16_Shuttle_TeamMask.png", false, false, false);
            var sh = Shader.Find("OpenZ/TeamLit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, MatPath); }
            m.shader = sh;
            Texture2D T(string f) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/" + f); if (!t) sb.Append(" [picture missing: " + f + "]"); return t; }
            m.SetTexture("_BaseMap", T("L16_Shuttle_Albedo.png")); m.SetTexture("_BumpMap", T("L16_Shuttle_Normal.png"));
            m.SetTexture("_MaskMap", T("L16_Shuttle_MaskMap.png")); m.SetTexture("_EmissionMap", T("L16_Shuttle_Emission.png"));
            m.SetColor("_BaseColor", Color.white); m.SetColor("_EmissionColor", Color.black); m.SetColor("_EmissionMapColor", Color.black);
            m.SetColor("_NeutralPaint", new Color(0.8f, 0.79f, 0.76f, 1f));
            var f = new (string, float)[]
            {
                ("_UseNormalMap", 1f), ("_BumpScale", 1f), ("_UseMaskMap", 1f), ("_EmissionNight", 0f),          // 0 = the script sets day and night levels itself
                ("_TeamMode", 3f), ("_TeamRef", 0.3f), ("_ShadeMin", 0.85f), ("_ShadeMax", 1.6f), ("_TeamTone", 0.15f), ("_TeamLift", 0f), ("_Smoothness", 0.3f), ("_Metallic", 0.2f),
            };
            foreach (var (k, v) in TeamLitBase.Concat(f)) if (m.HasProperty(k)) m.SetFloat(k, v);
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The trailer's material: the same shader with no team paint, its picture of eight colour fields (olive drab, dark olive, rubber, steel, burnt).</summary>
        internal static Material LampMaterial(System.Text.StringBuilder sb)
        {
            ImportTex(LampColours, true, false, false, true);
            var sh = Shader.Find("OpenZ/TeamLit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(LampMatPath);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, LampMatPath); }
            m.shader = sh;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/" + LampColours); if (!t) sb.Append(" [picture missing: " + LampColours + "]");
            m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white); m.SetColor("_EmissionColor", Color.black); m.SetColor("_EmissionMapColor", Color.black);
            var f = new (string, float)[] { ("_UseNormalMap", 0f), ("_UseMaskMap", 0f), ("_TeamMode", 0f), ("_Smoothness", 0.22f), ("_Metallic", 0.1f) };
            foreach (var (k, v) in TeamLitBase.Concat(f)) if (m.HasProperty(k)) m.SetFloat(k, v);
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }

        internal static long Tris(Renderer r) { var mf = r ? r.GetComponent<MeshFilter>() : null; var me = mf ? mf.sharedMesh : null; long n = 0; if (me) for (int i = 0; i < me.subMeshCount; i++) n += me.GetIndexCount(i) / 3; return n; }

        /// <summary>A part's mesh points (its own space); empty when the mesh cannot be read.</summary>
        internal static Vector3[] Points(Component c)
        {
            var mf = c ? c.GetComponent<MeshFilter>() : null; var me = mf ? mf.sharedMesh : null; if (!me) return new Vector3[0];
            try { return me.vertices ?? new Vector3[0]; } catch { return new Vector3[0]; }
        }

        /// <summary>The box of the parts as they are DRAWN: every mesh point at its place in the scene. (A renderer's own box is the
        /// box of its turned mesh box: for this ship, turned 30 degrees, it is two metres too wide.) `exact` = every part
        /// could be measured by its points.</summary>
        internal static Bounds TightBox(IEnumerable<Renderer> rs, out bool exact)
        {
            bool any = false; var b = new Bounds(); exact = true;
            foreach (var r in rs)
            {
                if (!r) continue;
                var vs = Points(r); var M = r.transform.localToWorldMatrix;
                if (vs.Length == 0) { exact = false; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); continue; }
                foreach (var v in vs) { var p = M.MultiplyPoint3x4(v); if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
            }
            return b;
        }

        /// <summary>The ship's long and cross sides (m): its mesh points measured along and across the root's forward (the dome's line).</summary>
        internal static Vector2 Sides(Transform root, IEnumerable<Renderer> rs)
        {
            float a0 = float.PositiveInfinity, a1 = float.NegativeInfinity, c0 = float.PositiveInfinity, c1 = float.NegativeInfinity;
            var fwd = root.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized; var side = Vector3.Cross(Vector3.up, fwd);
            foreach (var r in rs)
            {
                if (!r) continue; var M = r.transform.localToWorldMatrix;
                foreach (var v in Points(r)) { var p = M.MultiplyPoint3x4(v); float a = Vector3.Dot(p, fwd), c = Vector3.Dot(p, side); a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a); c0 = Mathf.Min(c0, c); c1 = Mathf.Max(c1, c); }
            }
            return float.IsInfinity(a0) ? Vector2.zero : new Vector2(a1 - a0, c1 - c0);
        }

        /// <summary>The nearest of the parts' mesh points to a place, across the ground (m).</summary>
        internal static float FlatDistance(IEnumerable<Renderer> rs, Vector3 at)
        {
            float best = float.PositiveInfinity;
            foreach (var r in rs)
            {
                if (!r) continue; var M = r.transform.localToWorldMatrix;
                foreach (var v in Points(r)) { var p = M.MultiplyPoint3x4(v); float d = (p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z); if (d < best) best = d; }
            }
            return float.IsInfinity(best) ? -1f : Mathf.Sqrt(best);
        }

        /// <summary>The fort the ship stands beside, as the scene draws it: the box of its base (the object under ZodObjects whose name
        /// starts with "fort_" nearest to the place; its part FortBase when there is one, else all its renderers).</summary>
        internal static bool FortBox(Vector3 near, out Bounds box, out string name)
        {
            box = new Bounds(); name = ""; var zod = GameObject.Find("ZodObjects"); if (!zod) return false;
            Transform best = null; float bd = float.PositiveInfinity;
            foreach (Transform t in zod.transform)
            {
                if (!t.name.StartsWith("fort_")) continue;
                float d = (t.position - near).sqrMagnitude; if (d < bd) { bd = d; best = t; }
            }
            if (!best) return false;
            name = best.name;
            var rs = best.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "FortBase").ToArray();
            if (rs.Length == 0) rs = best.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return false;
            box = rs[0].bounds; foreach (var r in rs) box.Encapsulate(r.bounds);
            return true;
        }

        /// <summary>The outermost points of a mesh in 192 directions spread evenly over the sphere (a flying wreck piece lands on the lowest of them).</summary>
        internal static Vector3[] Outline(Vector3[] vs)
        {
            if (vs == null || vs.Length == 0) return new Vector3[0];
            var pick = new HashSet<int>(); const int N = 192;
            for (int k = 0; k < N; k++)
            {
                float y = 1f - 2f * (k + 0.5f) / N, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = k * 2.399963f;
                var d = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a)); int best = 0; float bd = float.NegativeInfinity;
                for (int i = 0; i < vs.Length; i++) { float t = Vector3.Dot(vs[i], d); if (t > bd) { bd = t; best = i; } }
                pick.Add(best);
            }
            return pick.Select(i => vs[i]).ToArray();
        }

        /// <summary>The ground under a point, read on the level's terrain collider (COL_Terrain).</summary>
        internal static bool GroundAt(Collider col, float x, float z, out float y)
        {
            y = 0f; if (!col) return false;
            if (!col.Raycast(new Ray(new Vector3(x, 300f, z), Vector3.down), out var h, 600f)) return false;
            y = h.point.y; return true;
        }

        /// <summary>Each pad's sole (the model's marker in the middle of it) against the ground under it: negative = in the ground.</summary>
        internal static float[] PadsAgainstGround(Transform root, Collider col, out int found)
        {
            var d = new float[Pads.Length]; found = 0;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < Pads.Length; i++)
            {
                var t = all.FirstOrDefault(x => x.name == Pads[i]); d[i] = float.NaN;
                if (t && GroundAt(col, t.position.x, t.position.z, out float gy)) { d[i] = t.position.y - gy; found++; }
            }
            return d;
        }

        /// <summary>A plane through the ground under some points: ground = a x + b z + c. False when it cannot be fitted.</summary>
        static bool FitPlane(Vector3[] P, float[] gys, out float a, out float b, out float c)
        {
            a = b = 0f; c = gys.Length > 0 ? gys.Average() : 0f;
            float mx = P.Average(p => p.x), mz = P.Average(p => p.z), mg = gys.Average();
            float sxx = 0f, szz = 0f, sxz = 0f, sxg = 0f, szg = 0f;
            for (int i = 0; i < P.Length; i++) { float dx = P[i].x - mx, dz = P[i].z - mz, dg = gys[i] - mg; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; sxg += dx * dg; szg += dz * dg; }
            float det = sxx * szz - sxz * sxz; if (Mathf.Abs(det) < 1e-6f) return false;
            a = (sxg * szz - szg * sxz) / det; b = (szg * sxx - sxg * sxz) / det; c = mg - a * mx - b * mz;
            return true;
        }

        [MenuItem("OpenZ/L16 Jungle/14 Shuttle beside the red fort (open level 16 scene)")]
        public static void Menu() => Debug.Log("[OpenZ L16] " + Build());

        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            if (Application.isPlaying) return "stop Play first";
            var env = GameObject.Find("Environment"); if (!env) return "Environment missing";
            if (!File.Exists(Model)) return "missing " + Model;
            if (!File.Exists(LampModel)) return "missing " + LampModel;
            var sb = new System.Text.StringBuilder();
            ImportModel(Model); ImportModel(LampModel);
            var mat = ShipMaterial(sb); var lampMat = LampMaterial(sb);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Model); if (!src) return "the model did not import: " + Model;
            var lampSrc = AssetDatabase.LoadAssetAtPath<GameObject>(LampModel); if (!lampSrc) return "the model did not import: " + LampModel;
            var ground = env.transform.Find("TER_Ground"); int layer = ground ? ground.gameObject.layer : 0;
            var col = GameObject.Find("COL_Terrain")?.GetComponent<Collider>();
            Physics.SyncTransforms();
            float Ground(float x, float z, float fallback) => GroundAt(col, x, z, out float y) ? y : fallback;

            // an earlier run (of this version or of the first one): its plants come back, its two objects go
            bool hadOld = false, oldWasFirst = false;
            var oldT = env.transform.Find(ZWorld.L16ShuttleUid);
            if (oldT)
            {
                hadOld = true; oldWasFirst = !oldT.GetComponent<OzShuttleLights>();
                var ols = oldT.GetComponent<L16Shuttle>();
                if (ols && ols.hiddenPlants != null) foreach (var p in ols.hiddenPlants) if (p) p.SetActive(true);
                Object.DestroyImmediate(oldT.gameObject);
            }
            var oldLamp = env.transform.Find(SearchlightName); if (oldLamp) Object.DestroyImmediate(oldLamp.gameObject);

            // ---------------------------------------------------------------- the ship, built at the origin, saved as its prefab
            var root = new GameObject(ZWorld.L16ShuttleUid); root.layer = layer;
            var model = Object.Instantiate(src); model.name = "Model"; model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero; model.transform.localScale = Vector3.one;      // the file's own root turn is kept (it holds the file's axes)
            var by = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) by[t.name] = t;
            var missing = Ship.Concat(Flying).Concat(Staying).Concat(Pads).Concat(Markers).Where(n => !by.ContainsKey(n)).ToList();
            if (missing.Count > 0) { Object.DestroyImmediate(root); return "the model lacks: " + string.Join(", ", missing); }
            // the dome looks along the root's forward (read from the model's own front marker, whatever axes the file came in with)
            var topDir = by["MK_Top"].position - model.transform.position;      // the top marker stands straight over the pivot: the ship stands up by it
            if (topDir.sqrMagnitude > 1e-6f) model.transform.localRotation = Quaternion.FromToRotation(topDir.normalized, Vector3.up) * model.transform.localRotation;
            var front = by["MK_Front"].position - model.transform.position; front.y = 0f;
            model.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg, 0f) * model.transform.localRotation;
            foreach (var kv in by) if (kv.Key.StartsWith("MK_")) kv.Value.rotation = root.transform.rotation;      // markers: upright frames
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray(); r.gameObject.layer = layer;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                r.enabled = Ship.Contains(r.name);                                                                 // the wreck pieces are drawn only when the ship is destroyed
            }
            var wreck = new GameObject("RUIN_Wreck").transform; wreck.SetParent(root.transform, false); wreck.gameObject.layer = layer;
            foreach (var n in Staying) { by[n].SetParent(wreck, true); by[n].GetComponent<MeshRenderer>().enabled = true; }
            wreck.gameObject.SetActive(false);
            Renderer R(string n) => by[n].GetComponent<Renderer>();
            var shipRs = Ship.Select(R).ToArray();
            var sides = Sides(root.transform, shipRs);

            var dp = root.AddComponent<DetailProp>();
            dp.id = ZWorld.L16ShuttleUid; dp.kind = "shuttle"; dp.health = 9f; dp.hitRadius = 3.3f; dp.hitHeight = 5.1f; dp.blastOnly = true; dp.intact = false;
            dp.mat = "metal"; dp.debris = new[] { new Color(0.26f, 0.27f, 0.29f), new Color(0.12f, 0.12f, 0.13f), new Color(0.5f, 0.5f, 0.5f) }; dp.debrisCount = 36; dp.scorch = 0f;
            dp.breakEvent = "exp_vehicle_death"; dp.knockWobble = 0.04f; dp.glowAtNight = false; dp.sway = 0f;
            dp.loopEvent = ""; dp.loopRes = ""; dp.loopVol = 0.5f;                                          // no idle sound (nobody asked for one). To try a quiet hum: loopEvent = "electro_idle_hum"
            var tp = root.AddComponent<TeamPaint>(); tp.Team = Team.Red;                                // Play: the team of the nearest fort in the rules (L16Shuttle)
            var lights = root.AddComponent<OzShuttleLights>();
            lights.steady = R("SH_Steady"); lights.lampsA = R("SH_LampsA"); lights.lampsB = R("SH_LampsB"); lights.engineGlow = R("SH_EngineGlow");
            lights.neonRun = new[] { R("SH_NeonRun0"), R("SH_NeonRun1"), R("SH_NeonRun2") }; lights.neonBlink = R("SH_NeonBlink");
            lights.glowAtA = by["MK_PoolA"]; lights.glowAtB = by["MK_PoolB"];
            var exhaust = root.AddComponent<OzShuttleExhaust>();
            exhaust.mouths = new[] { by["MK_Exhaust_L"], by["MK_Exhaust_R"] }; exhaust.vents = new[] { by["MK_Vent_L"], by["MK_Vent_R"] };
            var ls = root.AddComponent<L16Shuttle>();
            ls.hull = R("SH_Hull"); ls.lights = lights; ls.exhaust = exhaust;
            ls.wreck = wreck; ls.pieces = Flying.Select(n => by[n]).ToArray(); ls.smokeAt = by["MK_Smoke"]; ls.topAt = by["MK_Top"];
            ls.pieceShapes = Flying.Select(n => new L16Shuttle.PieceShape { points = Outline(Points(by[n])) }).ToArray();
            int shapes = ls.pieceShapes.Count(s => s.points != null && s.points.Length >= 8);
            long ship = Ship.Sum(n => Tris(R(n))), neon = Lights.Where(n => n.Contains("Neon")).Sum(n => Tris(R(n))), wr = Flying.Concat(Staying).Sum(n => Tris(by[n].GetComponent<Renderer>()));

            bool prefabOk = false; string prefabNote = "";
            try { PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction, out prefabOk); }
            catch (System.Exception ex) { prefabNote = ": " + ex.Message; }
            if (!prefabOk) sb.Append(" [THE PREFAB WAS NOT SAVED" + prefabNote + ": the ship stays a plain object of the scene]");

            // ---------------------------------------------------------------- into the scene, on the ground
            root.transform.SetParent(env.transform, false);
            float gy0 = Ground(PlaceX, PlaceZ, 0f); var yawRot = Quaternion.Euler(0f, Heading, 0f);
            root.transform.SetPositionAndRotation(new Vector3(PlaceX, gy0, PlaceZ), yawRot);
            // the four pads on the ground: a plane through the ground under them, the ship tilted to it and lowered a little into the grass
            float tilt = 0f, sink = SinkMax, spread = 0f;
            for (int round = 0; round < 3; round++)
            {
                var P = Pads.Select(n => by[n].position).ToArray();
                var gys = P.Select(p => Ground(p.x, p.z, gy0)).ToArray();
                spread = gys.Max() - gys.Min();
                if (!FitPlane(P, gys, out float a, out float b, out float c)) break;
                var up = new Vector3(-a, 1f, -b).normalized; tilt = Vector3.Angle(Vector3.up, up);
                if (tilt > 8f) { sb.Append($" [the ground under the pads is {tilt:0.0} degrees steep: the ship is left level, LOOK]"); tilt = 0f; break; }
                float deep = 0f; for (int i = 0; i < P.Length; i++) deep = Mathf.Max(deep, gys[i] - (a * P[i].x + b * P[i].z + c));   // the ground stands this far above the plane under the worst pad
                sink = Mathf.Clamp(Deepest - deep, SinkMin, SinkMax);
                root.transform.SetPositionAndRotation(new Vector3(PlaceX, a * PlaceX + b * PlaceZ + c - sink, PlaceZ), Quaternion.FromToRotation(Vector3.up, up) * yawRot);
            }
            Physics.SyncTransforms();
            var soles = PadsAgainstGround(root.transform, col, out int padsFound);

            // the hut of the map it replaces: kept, switched off
            var zod = GameObject.Find("ZodObjects"); var hut = zod ? zod.transform.Find(HutName) : null;
            if (hut) { hut.gameObject.SetActive(false); ls.replacedHut = hut.gameObject; }

            // ---------------------------------------------------------------- the searchlight on its trailer
            var lamp = new GameObject(SearchlightName); lamp.layer = layer; lamp.transform.SetParent(env.transform, false);
            var lm = Object.Instantiate(lampSrc); lm.name = "Model"; lm.transform.SetParent(lamp.transform, false);
            lm.transform.localPosition = Vector3.zero; lm.transform.localScale = Vector3.one;      // the file's own root turn is kept
            var lby = new Dictionary<string, Transform>();
            foreach (var t in lm.GetComponentsInChildren<Transform>(true)) lby[t.name] = t;
            var lmiss = new[] { "SL_Stand", "RUIN_Stand", "MK_Head", "MK_Tow" }.Where(n => !lby.ContainsKey(n)).ToList();
            if (lmiss.Count > 0) { Object.DestroyImmediate(lamp); Object.DestroyImmediate(root); return "the searchlight's model lacks: " + string.Join(", ", lmiss); }
            foreach (var r in lm.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(lampMat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray(); r.gameObject.layer = layer;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
            }
            // the tow bar points away from the ship (the trailer was backed up to it), a little askew, as a thing put down by hand
            var tow = lby["MK_Tow"].position - lm.transform.position; tow.y = 0f;
            var away = new Vector3(LampX - PlaceX, 0f, LampZ - PlaceZ);
            float towWanted = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg + 18f;
            lm.transform.localRotation = Quaternion.Euler(0f, towWanted - Mathf.Atan2(tow.x, tow.z) * Mathf.Rad2Deg, 0f) * lm.transform.localRotation;
            foreach (var kv in lby) if (kv.Key.StartsWith("MK_")) kv.Value.rotation = lamp.transform.rotation;      // the head is built in its mount's frame: upright
            float lg0 = Ground(LampX, LampZ, gy0);
            lamp.transform.SetPositionAndRotation(new Vector3(LampX, lg0, LampZ), Quaternion.identity);
            float lampTilt = 0f;
            {   // on its wheels and feet: the plane through the ground under four points round it
                var P = new[] { new Vector3(LampX - 0.8f, 0f, LampZ), new Vector3(LampX + 0.8f, 0f, LampZ), new Vector3(LampX, 0f, LampZ - 1.2f), new Vector3(LampX, 0f, LampZ + 1.2f) };
                var gys = P.Select(p => Ground(p.x, p.z, lg0)).ToArray();
                if (FitPlane(P, gys, out float a, out float b, out float c))
                {
                    var up = new Vector3(-a, 1f, -b).normalized; lampTilt = Vector3.Angle(Vector3.up, up);
                    if (lampTilt <= 8f) lamp.transform.SetPositionAndRotation(new Vector3(LampX, a * LampX + b * LampZ + c - 0.02f, LampZ), Quaternion.FromToRotation(Vector3.up, up));
                    else lampTilt = 0f;
                }
            }
            lby["RUIN_Stand"].gameObject.SetActive(false);                                                   // the detail's break shows it
            var ldp = lamp.AddComponent<DetailProp>();
            ldp.id = SearchlightName; ldp.kind = "searchlight"; ldp.health = 3f; ldp.hitRadius = 0.8f; ldp.hitHeight = 2.1f; ldp.blastOnly = true; ldp.intact = false;
            ldp.mat = "metal"; ldp.debris = new[] { new Color(0.33f, 0.36f, 0.22f), new Color(0.2f, 0.21f, 0.23f), new Color(0.1f, 0.1f, 0.1f) }; ldp.debrisCount = 16; ldp.scorch = 0.6f;
            ldp.breakEvent = ""; ldp.breakLayers = new[] { "sd_lamp_glass_a" }; ldp.knockWobble = 0.5f; ldp.glowAtNight = false; ldp.sway = 0f; ldp.loopEvent = ""; ldp.loopRes = "";
            var fl = lamp.AddComponent<OzFieldSearchlight>();
            fl.headMount = lby["MK_Head"]; fl.aimAt = root.transform;
            ls.searchlight = fl;
            float lampToHull = FlatDistance(shipRs, lamp.transform.position), lampY = lamp.transform.position.y;

            // plants under the ship and under the trailer (never a tree that closes a tile: none stands there)
            var box = TightBox(shipRs, out bool exact);
            var treeNames = new HashSet<string>(ZTreeTilesData.L16.Uids);
            var hidden = new List<GameObject>();
            foreach (var t in env.GetComponentsInChildren<Transform>(false))
            {
                if (!t.name.StartsWith("PLT_") || treeNames.Contains(t.name) || t.IsChildOf(root.transform) || t.IsChildOf(lamp.transform)) continue;
                var p = t.position;
                bool underShip = p.x > box.min.x - 0.2f && p.x < box.max.x + 0.2f && p.z > box.min.z - 0.2f && p.z < box.max.z + 0.2f;
                bool underLamp = WithSearchlight && (p.x - LampX) * (p.x - LampX) + (p.z - LampZ) * (p.z - LampZ) < 1.7f * 1.7f;
                if (underShip || underLamp) hidden.Add(t.gameObject);
            }
            foreach (var g in hidden) g.SetActive(false);
            ls.hiddenPlants = hidden.ToArray();
            if (!WithSearchlight) { ls.searchlight = null; Object.DestroyImmediate(lamp); lamp = null; }               // item 173: no searchlight in the level
            if (prefabOk) { PrefabUtility.RecordPrefabInstancePropertyModifications(ls); PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform); }
            EditorUtility.SetDirty(ls); EditorUtility.SetDirty(root);

            var q = ZWorld.L16ShuttleSquare;
            float sx0 = q[0] * 2f, sx1 = (q[0] + q[2]) * 2f, sz1 = -q[1] * 2f, sz0 = -(q[1] + q[3]) * 2f;
            float over = Mathf.Max(0f, Mathf.Max(Mathf.Max(sx0 - box.min.x, box.max.x - sx1), Mathf.Max(sz0 - box.min.z, box.max.z - sz1)));
            var fr = by["MK_Front"].position - root.transform.position; fr.y = 0f;
            float deepest = soles.Where(v => !float.IsNaN(v)).DefaultIfEmpty(0f).Min(), highest = soles.Where(v => !float.IsNaN(v)).DefaultIfEmpty(0f).Max();
            bool seatOk = padsFound == 4 && deepest >= -0.05f && highest <= 0.05f;
            bool fortOk = FortBox(root.transform.position, out var fort, out string fortName);
            float fortSide = fortOk ? Mathf.Max(fort.size.x, fort.size.z) : 0f, share = fortSide > 0f ? sides.x / fortSide : 0f, clear = fortOk ? box.min.x - fort.max.x : -1f;
            var ob = OwnerBox;
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            return $"shuttle ({(hadOld ? (oldWasFirst ? "the first version's object in the scene was replaced" : "an earlier run's objects were replaced") : "new in this scene")}; " +
                   $"{(prefabOk ? "one prefab " + PrefabPath + ", the scene holds its instance" : "NO PREFAB")}): placed at {PlaceX:0.00}, {root.transform.position.y:0.00}, {PlaceZ:0.00} (tile {PlaceX / 2f:0.00}, {-PlaceZ / 2f:0.00}), " +
                   $"the dome looks to {Mathf.Atan2(fr.x, fr.z) * Mathf.Rad2Deg:0} degrees (wanted {Heading:0}); SIZE: {sides.x:0.00} m long, {sides.y:0.00} m across, {box.size.y:0.00} m high; " +
                   (fortOk ? $"the fort {fortName} is {fort.size.x:0.00} x {fort.size.z:0.00} m on the ground: the ship's long side is {share:0.000} of its side (wanted a third: 0.317 to 0.350); the ship is {clear:0.00} m clear of the fort's east face (3 or more); "
                           : "[NO FORT FOUND under ZodObjects: the size could not be held against it] ") +
                   $"the ground under its four pads differs by {spread:0.00} m: the ship is tilted {tilt:0.0} degrees to it and stands {sink * 100f:0.0} cm in the grass; each pad's sole against the ground under it (FL, FR, BL, BR): " +
                   $"{string.Join(", ", soles.Select(v => float.IsNaN(v) ? "?" : v.ToString("+0.00;-0.00")))} m{(seatOk ? "" : " [A PAD IS MORE THAN 5 cm OFF THE GROUND: look]")}; " +
                   $"its box {box.min.x:0.00} to {box.max.x:0.00} east, {box.min.z:0.00} to {box.max.z:0.00} north{(exact ? "" : " [mesh points not readable: the box is too wide]")}; outside its {q[2]} x {q[3]} tile square by {over:0.00} m; " +
                   $"over the lines of the owner's first box by (west, east, south, north) {Mathf.Max(0f, ob[0] - box.min.x):0.0}, {Mathf.Max(0f, box.max.x - ob[1]):0.0}, {Mathf.Max(0f, ob[2] - box.min.z):0.0}, {Mathf.Max(0f, box.max.z - ob[3]):0.0} m (it is a third of the fort now); " +
                   $"ship {ship} triangles in {Ship.Length} parts ({Body.Length} of the hull, {Lights.Length} lights; the neon strips {neon}), wreck {wr} triangles in {Flying.Length + Staying.Length} pieces, outlines of the flying pieces {shapes} of {Flying.Length}; " +
                   (WithSearchlight ? $"the searchlight {SearchlightName} at {LampX:0.00}, {lampY:0.00}, {LampZ:0.00} (tile {Mathf.FloorToInt(LampX / 2f)}, {Mathf.FloorToInt(-LampZ / 2f)}), {lampToHull:0.00} m from the hull (3 to 5), tilted {lampTilt:0.0} degrees with the ground, its head is put on in Play; " : "no searchlight (removed from the level); ") +
                   $"the hut {HutName}: {(hut ? "kept, switched off" : "NOT FOUND under ZodObjects")}; plants switched off under the ship and the trailer: {hidden.Count}{(hidden.Count > 0 ? " (" + string.Join(", ", hidden.Take(6).Select(g => g.name)) + ")" : "")}" +
                   $"{(col ? "" : " [COL_Terrain missing: the ship kept height 0, level]")}{sb}. SAVE THE SCENE.";
        }
    }
}
