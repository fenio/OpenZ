using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// A dead unit's glows go out. Added to the unit the moment it dies (UnitView.Die, a thrown crew body): over Seconds a
    /// robot's glow parts (the laser's lens and tubes, the electrobot's coils, pack, emitter and cells), the team glow of its
    /// paint, any emissive part and any light under it fade to nothing in a few steps and stay off; a particle system under it
    /// stops emitting. Where the unit has a TeamPaint the fade is its GlowLevel, so the later paint steps of a death (charring)
    /// keep the glows off; a figure without one is dimmed directly. A vehicle or gun wreck fades the team glow of its paint
    /// only: its lamps belong to the vehicle lights, its fire and smoke to the wreck. A robot shot to bits is hidden at once and
    /// its thrown parts carry copies of the body's colours: RobotPopDeath fades them with Dim. A living unit never has this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OzGlowFade : MonoBehaviour
    {
        /// <summary>Seconds from death to dark.</summary>
        public const float Seconds = 1.8f;
        public const int Steps = 12;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor"),
                            TeamGlowId = Shader.PropertyToID("_OZTeamGlow");
        static MaterialPropertyBlock mpb;

        float t0; int step = -1; bool paintOnly; static bool nextPaintOnly;
        TeamPaint paint; Light[] lights; float[] light0;
        /// <summary>1 = as alive, 0 = dark.</summary>
        public float Level { get; private set; } = 1f;

        /// <summary>Off = a dead robot keeps its glows (the old look; checks only).</summary>
        public static bool On = true;

        /// <summary>The robot `root` died: its glows fade out and stay off. Calling it again does nothing.</summary>
        public static void Begin(GameObject root, bool paintOnly = false)
        {
            if (!On || !root || root.GetComponent<OzGlowFade>()) return;
            nextPaintOnly = paintOnly;
            root.AddComponent<OzGlowFade>();
            nextPaintOnly = false;
        }

        void Awake()
        {
            t0 = Time.time; paintOnly = nextPaintOnly;
            paint = GetComponent<TeamPaint>();
            if (paintOnly) { lights = new Light[0]; light0 = new float[0]; return; }
            lights = GetComponentsInChildren<Light>(true);
            light0 = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) light0[i] = lights[i] ? lights[i].intensity : 0f;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            float k = 1f - Mathf.Clamp01((Time.time - t0) / Seconds);
            int s = Mathf.CeilToInt(k * Steps);
            if (s == step) return;
            step = s; Level = s / (float)Steps;
            if (paint) paint.GlowLevel = Level;
            else if (!paintOnly) foreach (var r in GetComponentsInChildren<Renderer>(true)) Dim(r, Level);
            for (int i = 0; i < lights.Length; i++)
            {
                if (!lights[i]) continue;
                lights[i].intensity = light0[i] * Level;
                if (s == 0) lights[i].enabled = false;
            }
            if (s == 0) enabled = false;                                                                // dark: nothing left to do
        }

        /// <summary>A glow part: an unlit material named as a glow, or an unlit one brighter than white.</summary>
        public static bool IsGlow(Material m)
        {
            if (!m || !m.shader || m.shader.name != "Universal Render Pipeline/Unlit" || !m.HasProperty(BaseColorId)) return false;
            return m.name.Contains("Glow") || m.GetColor(BaseColorId).maxColorComponent > 1.05f;
        }

        /// <summary>True when the renderer shows a glow part, an emissive one or team paint (its team glow).</summary>
        public static bool HasGlow(Renderer r)
        {
            if (!r) return false;
            foreach (var m in r.sharedMaterials)
                if (m && (IsGlow(m) || m.HasProperty(TeamGlowId) || (m.HasProperty(EmissionId) && m.IsKeywordEnabled("_EMISSION") && m.GetColor(EmissionId).maxColorComponent > 0.001f))) return true;
            return false;
        }

        /// <summary>Sets the glow of one renderer to `level` x what its materials ask for (thrown parts, figures without a paint).</summary>
        public static void Dim(Renderer r, float level)
        {
            if (!r) return;
            mpb ??= new MaterialPropertyBlock();
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i]; if (!m) continue;
                bool glow = IsGlow(m), emis = m.HasProperty(EmissionId), team = m.HasProperty(TeamGlowId);
                if (!glow && !emis && !team) continue;
                r.GetPropertyBlock(mpb, i);
                if (glow && m.HasProperty(BaseColorId)) mpb.SetColor(BaseColorId, m.GetColor(BaseColorId) * new Color(level, level, level, 1f));
                if (emis) mpb.SetColor(EmissionId, m.GetColor(EmissionId) * level);
                if (team) mpb.SetVector(TeamGlowId, mpb.GetVector(TeamGlowId) * (level <= 0f ? 0f : Mathf.Min(1f, level * Steps / Mathf.Max(1f, level * Steps + 1f))));
                r.SetPropertyBlock(mpb, i);
            }
        }
    }
}
