using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Import settings for the animals of level 16 (Assets/OpenZ/Resources/OpenZ/L16Animals): a generic rig with an avatar from the
    /// model (the takes are played by hand through a playable graph), the same axis handling as the other OpenZ files (bake axis
    /// conversion, file units), exact poses at the sampled frames (no compression: the parrot's wing fold is a SCALE key and must
    /// survive), no imported materials, cameras, lights or blend shapes (the view gives the animals the game's own surface look at
    /// run time), readable bone transforms. Pictures: colour, mip maps, 2048 for the crocodile, 1024 for the others; the wake's
    /// picture keeps its see-through channel and is not repeated at its edges.
    /// </summary>
    public class OzL16AnimalImport : AssetPostprocessor
    {
        public const string Folder = "Assets/OpenZ/Resources/OpenZ/L16Animals";
        static bool Mine(string path) => path.Replace('\\', '/').Contains("/Resources/OpenZ/L16Animals/");

        void OnPreprocessModel()
        {
            if (!Mine(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            bool fx = assetPath.EndsWith("Croc_SurfaceFx.fbx");
            mi.animationType = fx ? ModelImporterAnimationType.None : ModelImporterAnimationType.Generic;
            if (!fx) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;
            mi.importAnimation = !fx;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.resampleCurves = true;
            mi.addCollider = false;
            mi.isReadable = fx;                                                     // the wake mesh is shared by the animals at run time
        }

        void OnPreprocessTexture()
        {
            if (!Mine(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            ti.sRGBTexture = true; ti.mipmapEnabled = true;
            bool strip = Path.GetFileName(assetPath).StartsWith("fx_") || Path.GetFileName(assetPath).StartsWith("crocfx_");   // a sign on the water: the wake's picture (and the older strips, should another step have copied them)
            ti.alphaSource = strip ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.alphaIsTransparency = strip;
            ti.wrapMode = strip ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.maxTextureSize = Path.GetFileName(assetPath).StartsWith("croc_basecolor") ? 2048 : 1024;
        }
    }

    /// <summary>
    /// Level 16 animals, the editor side of step J8. NOTHING is placed in the scene and the scene is never rebuilt: the crocodiles are
    /// made by the rules from their table (Sim/ZCrocData.cs), every other animal by its view from JungleFaunaData.cs, at run time, the
    /// way the game makes its eagles, gulls, hares and snakes. This menu only looks after the files:
    ///   1. every model, picture and sound the animals ask for is there (a missing one is named);
    ///   2. the models are imported again with the settings above (safe to run any number of times);
    ///   3. the takes are what the code expects: the crocodile's 15 by name and length, the one long take of the parrot, the
    ///      monkey and the snake, the bones the views look up, the scale keys of the parrot's wing fold; the wake's picture is
    ///      1024 x 1024 with its see-through channel;
    ///   4. the wake's material (Resources/OpenZ/L16Animals/CrocFx_WakeCard.mat: see-through, unlit, the prepared wake picture
    ///      fx_wake_v_1024) is made or updated.
    /// The last line reads "PASS n/n" when everything is in place.
    /// </summary>
    public static class OpenZL16Animals
    {
        const string Dir = OzL16AnimalImport.Folder;
        const string Sounds = "Assets/OpenZ/Resources/OpenZ/Audio/Sfx/L16";
        static readonly (string take, float seconds)[] CrocTakes =
        {
            ("blink", 0.4f), ("drag_under", 1.333f), ("lunge_bite", 1.0f), ("lurk", 3.2f), ("nose_rise", 0.6f), ("nose_travel", 2.0f), ("nose_turn_L", 1.333f), ("nose_turn_R", 1.333f),
            ("submerge", 1.2f), ("surface", 0.8f), ("swim", 1.0f), ("swim_past", 2.0f), ("thrash_roll", 1.2f), ("turn_L", 1.0f), ("turn_R", 1.0f),
        };
        static readonly string[] Models = { "Croc.fbx", "Parrot_perched.fbx", "Monkey.fbx", "JG_Snake.fbx" };
        const string WakePicture = CrocodileView.WakePicture + ".png", WakeMat = CrocodileView.WakeMaterial + ".mat";
        static readonly string[] Pictures = { "croc_basecolor.png", "croc_eye.png", WakePicture, "parrot_perched_tex.png", "Monkey_tex.png", "JG_Snake_tex.png" };
        /// <summary>The prepared sounds the animals play (file names without .wav). The named events that have no file yet are in the kit's MISSING_SOUNDS.md.</summary>
        public static readonly string[] SoundFiles =
        {
            "sd_croc_warning_a", "sd_croc_warning_b", "sd_croc_bubbles_a", "sd_croc_bubbles_b", "sd_croc_wake_loop_a", "sd_croc_wake_loop_b", "sd_croc_surface_swell_a", "sd_croc_surface_swell_b",
            "sd_croc_lunge_roar_a", "sd_croc_lunge_splash_a", "sd_croc_lunge_splash_b", "sd_croc_lunge_splash_c", "sd_croc_jaw_snap_a", "sd_croc_jaw_snap_b", "sd_croc_jaw_snap_c",
            "sd_croc_bite_metal_a", "sd_croc_bite_metal_b", "sd_robot_cry_cut_a", "sd_croc_thrash_loop_a", "sd_croc_thrash_loop_b", "sd_robot_legs_kick_a", "sd_croc_tail_slap_a",
            "sd_croc_drag_under_a", "sd_croc_drag_under_b", "sd_croc_part_float_a", "sd_croc_part_float_b", "sd_croc_submerge_a", "sd_croc_submerge_b", "sd_robot_croc_alarm_a",
            "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_c", "sd_parrot_squawk_d", "sd_parrot_flutter_a", "sd_leaves_rustle_a",
            "sd_monkey_far_a", "sd_monkey_far_b", "sd_monkey_far_c", "sd_snake_hiss_jungle_a", "sd_snake_slither_a",
        };

        [MenuItem("OpenZ/L16 Jungle/10 Animals (models, takes, wake material; nothing is placed in the scene)")]
        public static void AnimalsMenu() => Debug.Log("[OpenZ L16] animals\n" + Animals());

        public static string Animals()
        {
            var sb = new System.Text.StringBuilder(); int pass = 0, lines = 0;
            void V(bool ok, string what) { lines++; if (ok) pass++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }

            // ---- 1 the files
            var missing = Models.Concat(Pictures).Where(f => !File.Exists(Dir + "/" + f)).ToList();
            V(missing.Count == 0, $"{Models.Length + Pictures.Length - missing.Count} of {Models.Length + Pictures.Length} models and pictures are in {Dir}" + (missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + "; run the kit's apply script)" : ""));
            var noSound = SoundFiles.Where(f => !File.Exists(Sounds + "/" + f + ".wav")).ToList();
            V(noSound.Count == 0, $"{SoundFiles.Length - noSound.Count} of {SoundFiles.Length} prepared sounds are in {Sounds}" + (noSound.Count > 0 ? " (missing: " + string.Join(", ", noSound) + ")" : ""));
            if (missing.Count > 0) return sb.Append($"FAIL {pass}/{lines}").ToString();

            // ---- 2 import again with the settings of this file
            foreach (var f in Models.Concat(Pictures)) AssetDatabase.ImportAsset(Dir + "/" + f, ImportAssetOptions.ForceUpdate);

            // ---- 3 the takes and the bones
            var crocClips = Clips("Croc.fbx");
            var bad = new List<string>();
            foreach (var (take, seconds) in CrocTakes)
            {
                var c = crocClips.FirstOrDefault(x => x.name == take || x.name.EndsWith("|" + take));
                if (c == null) bad.Add(take + " (not found)");
                else if (Mathf.Abs(c.length - seconds) > 0.05f) bad.Add($"{take} ({c.length:0.00} s, wanted {seconds:0.00})");
            }
            V(bad.Count == 0 && crocClips.Count == CrocTakes.Length, $"crocodile: {crocClips.Count} takes, {CrocTakes.Length - bad.Count} of {CrocTakes.Length} by name and length" + (bad.Count > 0 ? ": " + string.Join(", ", bad) : ""));
            V(Bones("Croc.fbx", out string noBone, "prey", "head", "jaw", "tail5"), "crocodile: the bones the view looks up (prey, head, jaw, tail5)" + noBone);
            OneTake("Parrot_perched.fbx", (280 - 1) / 24f, "parrot", V);
            V(Bones("Parrot_perched.fbx", out noBone, "head", "tail_2", "wing_1_L", "wing_1_R"), "parrot: the bones the view looks up (head, tail_2, wing_1_L, wing_1_R)" + noBone);
            var pc = Clips("Parrot_perched.fbx");
            bool scaleKeys = pc.Count > 0 && AnimationUtility.GetCurveBindings(pc[0]).Any(b => b.propertyName.StartsWith("m_LocalScale") && b.path.Contains("wing_1"));
            V(scaleKeys, "parrot: the wing roots carry their scale keys (the wings fold when it sits)");
            OneTake("Monkey.fbx", (190 - 1) / 24f, "monkey", V);
            V(Bones("Monkey.fbx", out noBone, "head", "tail_2"), "monkey: the bones the view looks up (head, tail_2)" + noBone);
            OneTake("JG_Snake.fbx", (131 - 1) / 24f, "snake", V);
            V(Bones("JG_Snake.fbx", out noBone, "head", "body_01"), "snake: the bones the view looks up (head, body_01)" + noBone);
            var wakeTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + WakePicture);
            var wakeImp = AssetImporter.GetAtPath(Dir + "/" + WakePicture) as TextureImporter;
            V(wakeTex && wakeImp && wakeTex.width == 1024 && wakeTex.height == 1024 && wakeImp.DoesSourceTextureHaveAlpha() && wakeImp.alphaSource == TextureImporterAlphaSource.FromInput && wakeTex.wrapMode == TextureWrapMode.Clamp,
              "wake: the picture " + WakePicture + (wakeTex ? $" ({wakeTex.width} x {wakeTex.height}, see-through channel {(wakeImp && wakeImp.DoesSourceTextureHaveAlpha() ? "kept" : "MISSING")}, edges {(wakeTex.wrapMode == TextureWrapMode.Clamp ? "not repeated" : "REPEATED")}; it lies 6.4 m wide and 8 m long on the water: 160 by 128 picture points per metre)" : " (not found)"));

            // ---- 4 the wake's material
            V(WakeMaterial(out string mat), mat);
            AssetDatabase.SaveAssets();
            sb.Append(pass == lines ? $"PASS {pass}/{lines}" : $"FAIL {pass}/{lines}");
            return sb.ToString();
        }

        static List<AnimationClip> Clips(string file) =>
            AssetDatabase.LoadAllAssetsAtPath(Dir + "/" + file).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name).ToList();

        static void OneTake(string file, float seconds, string what, System.Action<bool, string> V)
        {
            var c = Clips(file);
            V(c.Count == 1 && Mathf.Abs(c[0].length - seconds) < 0.06f, $"{what}: one take of {seconds:0.00} s" + (c.Count == 1 ? $" (it is {c[0].length:0.00} s at {c[0].frameRate:0} frames a second)" : $" ({c.Count} takes found)"));
        }

        static bool Bones(string file, out string note, params string[] names)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/" + file);
            var have = go ? new HashSet<string>(go.GetComponentsInChildren<Transform>(true).Select(t => t.name)) : new HashSet<string>();
            var no = names.Where(n => !have.Contains(n)).ToList();
            note = no.Count > 0 ? ": MISSING " + string.Join(", ", no) : "";
            return no.Count == 0;
        }

        /// <summary>See-through, unlit, the prepared wake picture (pale crest, dark trough, soft at every edge), faded by the card's
        /// vertex colours: the engine's own particle surface (it is kept in a build because this material refers to it). Its own
        /// name: the flyover stage's stand-in wake looks for a material CrocFx_Wake made for the older strip picture on the V
        /// mesh; this one is for the flat card of CrocodileView and must not be taken for it.</summary>
        static bool WakeMaterial(out string note)
        {
            string path = Dir + "/" + WakeMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) { note = "wake material: the engine's particle surface was not found (the wake falls back to rings on the water)"; return false; }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + WakePicture);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool made = !m;
            if (made) { m = new Material(sh) { name = CrocodileView.WakeMaterial }; AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent + 4;
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);                                  // the picture carries its own colours; the view sets strength and night per animal
            EditorUtility.SetDirty(m);
            note = "wake material: " + path + (made ? " made" : " updated") + (tex ? "" : " (the wake picture is missing: plain tint)");
            return tex;
        }
    }
}
