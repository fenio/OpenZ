using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The laser robot's shot: a short green flash of light on the ground at his feet on the moment he fires (every level).
    /// It is the game's own ground light (the soft Gaussian decal every gun's muzzle lays on the ground: pooled, conforming to the
    /// ground, drawn over raised ground and low props without clipping, no shadows) in the laser's green, small (1.8 m) and short
    /// (gone in a third of a second), and one brief low light beside it (the pooled effect lights, no shadows). The second bolt of
    /// the double tap refreshes the same flash instead of laying a second one. WeaponsV2Runner.Laser calls it.
    /// </summary>
    public partial class OpenZFxPlayer
    {
        public static float LaserFlashRadius = 1.8f, LaserFlashStrength = 0.75f, LaserFlashDecay = 9f, LaserFlashLife = 0.32f;
        public static float LaserFlashLight = 2.2f, LaserFlashLightRange = 3.2f, LaserFlashLightLife = 0.1f;
        /// <summary>Linear colour: the laser bolt's own glow green.</summary>
        public static readonly Color LaserFlashColor = new Color(0.16f, 1f, 0.24f), LaserFlashLightColor = new Color(0.35f, 1f, 0.4f);
        /// <summary>Proof: flashes laid / refreshed since the level started.</summary>
        public static int LaserFlashes, LaserFlashRefreshes;

        /// <summary>The flash at a robot's feet (any point over them: the ground height is looked up). scale: the robot's drawn size.</summary>
        public void LaserGroundFlash(Vector3 feet, float scale = 1f)
        {
            if (!groundLights || flashes.Count == 0) return;
            var flat = new Vector3(feet.x, Ground(feet.x, feet.z), feet.z);
            float r = LaserFlashRadius * Mathf.Clamp(scale, 0.6f, 1.6f), w = LaserFlashStrength;
            foreach (var f in flashes)
                if (f.active && f.age < 0.12f && f.color == LaserFlashColor && (f.c - flat).sqrMagnitude < 0.36f) { f.w0 = Mathf.Max(f.w0, w); f.age = 0f; LaserFlashRefreshes++; return; }
            var d = flashes[flashCursor++ % flashes.Count];
            d.active = true; d.kind = DecalKind.Flash; d.age = 0f; d.life = LaserFlashLife; d.r0 = r; d.w0 = w; d.c = flat; d.birth = Time.time;
            d.decay = LaserFlashDecay; d.color = LaserFlashColor;
            BuildPolar(d.mesh, flat, r * 1.3f, OpenZFxCatalog.GroundLightLift, false, true);
            AddLight(flat + Vector3.up * 0.45f, LaserFlashLightColor, LaserFlashLight, LaserFlashLightRange, LaserFlashLightLife);
            LaserFlashes++;
        }
    }
}
