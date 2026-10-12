using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// ONE switch decides which grenade crate the game uses everywhere: the pickup crate + its break pieces (Z_grenades.prefab) and the
    /// Supply stacks (base props v2 BP2_Lxx_Supply). One click each, no code edit:
    ///    Assets/OpenZ/Models/Props/GrenadeCrate_v2 (own atlas crate_atlas_v2 + material M_GrenadeCrate_v2, lid sounds), the prefab rebuilt
    ///    on it, the v2 Supply files copied in and OpenZ/Base Props/1 re-run;
    ///    the v1 Supply files, the same way.
    /// No v1 file is ever overwritten or deleted. The Supply files of both versions are kept next to each other in
    /// OpenZGrenadeCrateV2.CrateMaterial for any later Base Props rebuild).
    /// </summary>
    public static class OpenZGrenadeCrateSwitch
    {
        public const string Marker = "Assets/OpenZ/Models/Props/grenade_crate_active.txt";
        public const string V2Dir = "Assets/OpenZ/Models/Props/GrenadeCrate_v2", V2Tex = V2Dir + "/crate_atlas_v2.png",
            V2Mat = "Assets/OpenZ/Materials/Props/M_GrenadeCrate_v2.mat", SfxDir = "Assets/OpenZ/Resources/OpenZ/Audio/Sfx",
            Prefab = "Assets/OpenZ/Prefabs/Z_grenades.prefab";
        static readonly string[] Fbx = { "GrenadeCrate.fbx", "GrenadeCrate_Pieces.fbx", "GrenadeCrate_Stack.fbx" };
        static readonly string[] SupplyFiles = { "BP2_L02_Supply.fbx", "BP2_L17_Supply.fbx", "base_props_v2_manifest.json", "base_props_v2_sets.json" };
        static string Staging(string v) => Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "grenade_crate_" + v);
        static string BpLive => Path.Combine(OpenZPaths.RepoRoot, "projects", "blender", "export", "base_props_v2");
        static string BpKeep(string v) => Path.Combine(BpLive, "crate_" + v);

        /// <summary>"v1" or "v2" (no marker = v1, the crate that was live before this row).</summary>
        public static string Active
        {
            get
            {
                try { var f = Path.GetFullPath(Marker); return File.Exists(f) && File.ReadAllText(f).Trim() == "v2" ? "v2" : "v1"; }
                catch { return "v1"; }
            }
        }
        public static void SetActive(string v)
        {
            var f = Path.GetFullPath(Marker);
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            if (!File.Exists(f) || File.ReadAllText(f).Trim() != v) { File.WriteAllText(f, v + "\n"); AssetDatabase.ImportAsset(Marker); }
        }

        static bool CopyIfChanged(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst) && File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) return false;
            File.Copy(src, dst, true); return true;
        }

        [MenuItem("OpenZ/Grenade Crate/Use v2 (owner reference crate)")]
        public static string UseV2() => Switch("v2");
        [MenuItem("OpenZ/Grenade Crate/Revert to v1 (earlier-project crate)")]
        public static string RevertV1() => Switch("v1");
        [MenuItem("OpenZ/Grenade Crate/Status")]
        public static string Status()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            bool lid = root && root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "CRATE_lid");
            var mr = root ? root.GetComponentInChildren<Renderer>(true) : null;
            var s = $"[OpenZ Crate] active {Active}; Z_grenades lid {(lid ? "yes (v2)" : "no (v1)")}, material {(mr && mr.sharedMaterial ? mr.sharedMaterial.name : "?")}; Supply material {OpenZGrenadeCrateV2.CrateMaterial().name}";
            Debug.Log(s); return s;
        }

        static string Switch(string v)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[OpenZ Crate] stop Play first"); return "in Play"; }
            var sb = new StringBuilder($"[OpenZ Crate] switch to {v}: ");
            KeepSupplyCopies(sb);
            SetActive(v);
            if (v == "v2") { ImportV2(sb); sb.Append(OpenZGrenadeCrateV2.BuildPrefab(V2Dir, OpenZGrenadeCrateV2.CrateMaterialFor(V2Mat, V2Tex))); }
            else sb.Append(OpenZGrenadeCrateV2.ImportCrate());                                              // the v1 import, unchanged (copy-if-changed)
            SetActive(v);
            // Supply stacks: that version's 4 files into the live base_props_v2 folder, then OpenZ/Base Props/1
            int n = 0;
            foreach (var f in SupplyFiles) if (CopyIfChanged(Path.Combine(BpKeep(v), f), Path.Combine(BpLive, f))) n++;
            sb.Append($"; supply: {n} files from crate_{v} -> base props rebuild: " + OpenZBaseProps.ImportAndBuild());
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        /// <summary>Both versions' Supply files kept side by side (crate_v1 = the live files before the first switch, = grenade_crate_v1's).</summary>
        static void KeepSupplyCopies(StringBuilder sb)
        {
            foreach (var v in new[] { "v1", "v2" })
            {
                var keep = BpKeep(v); Directory.CreateDirectory(keep);
                foreach (var f in SupplyFiles)
                {
                    var dst = Path.Combine(keep, f); if (File.Exists(dst)) continue;
                    var src = v == "v2" ? Path.Combine(Staging("v2"), "export", "bp2", f) : Path.Combine(Staging("v1"), "export", "bp2", f);
                    if (v == "v1" && Active == "v1" && File.Exists(Path.Combine(BpLive, f))) src = Path.Combine(BpLive, f);   // the live v1 files
                    if (File.Exists(src)) File.Copy(src, dst); else sb.Append($"MISSING {src}; ");
                }
            }
        }

        static void ImportV2(StringBuilder sb)
        {
            var exp = Path.Combine(Staging("v2"), "export");
            int c = 0;
            foreach (var f in Fbx) if (CopyIfChanged(Path.Combine(exp, f), $"{V2Dir}/{f}")) c++;
            if (CopyIfChanged(Path.Combine(exp, "crate_atlas.png"), V2Tex)) c++;
            foreach (var f in new[] { "crate_manifest.json", "crate_atlas.json" }) if (CopyIfChanged(Path.Combine(exp, f), $"{V2Dir}/{f}")) c++;
            foreach (var f in new[] { "sfx_crate_lid_open_1.mp3", "sfx_crate_lid_open_2.mp3" }) if (CopyIfChanged(Path.Combine(Staging("v2"), "sfx", f), $"{SfxDir}/{f}")) c++;
            AssetDatabase.Refresh();
            sb.Append($"{c} v2 files copied; ");
            var ti = (TextureImporter)AssetImporter.GetAtPath(V2Tex);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            foreach (var f in Fbx)
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath($"{V2Dir}/{f}");
                mi.globalScale = 1; mi.useFileUnits = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false;
                mi.isReadable = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false; mi.importVisibility = false;
                mi.SaveAndReimport();
            }
            foreach (var f in new[] { "sfx_crate_lid_open_1.mp3", "sfx_crate_lid_open_2.mp3" })
            {
                var ai = (AudioImporter)AssetImporter.GetAtPath($"{SfxDir}/{f}");
                var s = ai.defaultSampleSettings; s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.9f;
                ai.defaultSampleSettings = s; ai.forceToMono = false; ai.SaveAndReimport();
            }
            // the v1 prefab is kept once as a file next to the other prefab backups (the v1 crate itself stays in GrenadeCrate/)
            var bak = Path.Combine(OpenZPaths.RepoRoot, "backups", "unity", "prefabs");
            Directory.CreateDirectory(bak);
            var bakFile = Path.Combine(bak, "Z_grenades_v1_before_owner_v2.prefab");
            if (!File.Exists(bakFile)) File.Copy(Path.GetFullPath(Prefab), bakFile);
        }
    }
}
