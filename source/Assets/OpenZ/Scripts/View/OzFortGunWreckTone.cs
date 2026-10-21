using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Item 212, round 2: the killed fort cannon's wreck at the fort debris' brightness. The first wreck look (OzFortGunWreckLook)
    /// measured 0.27 of the fort's concrete debris in the game (0.32 in flight) and its Tone search could not lift it, for two
    /// reasons read in the project: (1) its levels were set against the old textured wall panels (albedo about 0.12 linear), but a
    /// fort breaks into the concrete chunk library, light cream concrete several times brighter (ConcreteChunkLib.ApplyLook: the
    /// outer tile, nearly white, tinted (0.84, 0.78, 0.67)); (2) about half of what the camera sees of the broken cannon is not its
    /// paint at all (the break faces, torn rims, ribs, rebar and the hydraulic sleeves), all fixed dark, so a multiplier on the
    /// paint alone could not move the measured median. The light, the normals, the probes, the shadows and the paint blocks are
    /// the same for the cannon and the debris (the KD2 A/B menu shows it, one thing at a time).
    /// The correction, on top of the wreck look: the wreck paints and the break-face steel are re-levelled in their textures
    /// (Resources/OpenZ/FortV2/Wreck: soot to ash grey, heat-bleached chipped paint and bare steel, the scratches, cracks, rust and
    /// the team mask kept texel for texel), and the materials the wreck look builds get the matching levels here: the break faces
    /// less metallic (a metal with no bright reflection to show reads black), a light rust on the rebar, and the killed cannon's
    /// dark hydraulic sleeves as sooty burnt steel. The living cannon keeps its own materials and textures.
    /// Switch <see cref="Enabled"/> (off = the first wreck look's material levels; the textures stay as imported).
    /// </summary>
    public static class OzFortGunWreckTone
    {
        public static bool Enabled = true;
        /// <summary>Break faces (torn steel): metallic and smoothness (were 0.35 / 0.22).</summary>
        public static float FaceMetallic = 0.12f, FaceSmoothness = 0.22f;
        /// <summary>Rebar: light rust (sRGB) and metallic (were (0.55, 0.36, 0.25) and 0.25).</summary>
        public static Color RebarColor = new Color(0.60f, 0.43f, 0.31f, 1f);
        public static float RebarMetallic = 0.12f, RebarSmoothness = 0.2f;
        /// <summary>The killed cannon's hydraulic ram sleeves (dark gunmetal on the living cannon): sooty burnt steel (sRGB).</summary>
        public static Color RamSleeve = new Color(0.46f, 0.44f, 0.42f, 1f);
        public static float RamMetallic = 0.2f, RamSmoothness = 0.3f;
        /// <summary>The living cannon's sleeve material (FortGunView.BuildRams); the rods stay bright steel.</summary>
        public const string RamSleeveMaterial = "FortGunRam_Dark";

        // proof
        public static int FacesToned, RebarToned, RamsSwapped;
        static readonly Dictionary<Material, Material> ramOf = new Dictionary<Material, Material>();
        static readonly HashSet<Material> burntRams = new HashSet<Material>();

        /// <summary>OzFortGunWreckLook.FaceMat, after it built the break-face material.</summary>
        public static void Face(Material m)
        {
            if (!Enabled || !m) return;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", FaceMetallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", FaceSmoothness);
            FacesToned++;
        }

        /// <summary>OzFortGunWreckLook.RebarMat, after it built the rebar material.</summary>
        public static void Rebar(Material m)
        {
            if (!Enabled || !m) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", RebarColor);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", RebarMetallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", RebarSmoothness);
            RebarToned++;
        }

        /// <summary>OzFortGunWreckLook.Swap: a ram sleeve of the thrown cannon (never one already burnt).</summary>
        public static bool IsRamSleeve(Material m) => Enabled && m && m.name == RamSleeveMaterial && !burntRams.Contains(m);

        /// <summary>The sleeve's burnt copy (one per source material; the breakup copies it onto the ring piece with the ram).</summary>
        public static Material BurntRam(Material src)
        {
            if (ramOf.TryGetValue(src, out var b) && b) return b;
            b = new Material(src) { name = src.name + "_Burnt" };
            if (b.HasProperty("_BaseColor")) b.SetColor("_BaseColor", RamSleeve);
            if (b.HasProperty("_Metallic")) b.SetFloat("_Metallic", RamMetallic);
            if (b.HasProperty("_Smoothness")) b.SetFloat("_Smoothness", RamSmoothness);
            ramOf[src] = b; burntRams.Add(b); RamsSwapped++;
            return b;
        }

        /// <summary>For the checks.</summary>
        public static string State => Enabled
            ? $"wreck tone on: break faces metallic {FaceMetallic:0.00}, rebar {RebarColor.r:0.00} {RebarColor.g:0.00} {RebarColor.b:0.00} metallic {RebarMetallic:0.00}, ram sleeves {RamSleeve.r:0.00} metallic {RamMetallic:0.00}; faces toned {FacesToned}, rebar {RebarToned}, sleeves swapped {RamsSwapped}"
            : "wreck tone OFF (the first wreck look's material levels)";
    }
}
