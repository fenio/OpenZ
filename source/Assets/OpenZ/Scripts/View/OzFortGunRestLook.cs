using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Item 212, round 3: the broken fort cannon's pieces once they lie on the ground. The thrown cannon reads as the fort's debris in
    /// flight (the wreck look and its tone), but its resting pieces read near black, for two reasons measured in the game (OzKD3Probe):
    /// the ending's cinematic vignette takes most of the light where the cannon lands (25-35 m from the fort = the frame's edge at the
    /// ending camera's 70 m; it stays as it is: the tension of the ending), and what the pieces show at rest is darker than the flying cannon:
    /// torn cut faces, rims, ribs, rebar and burnt sleeves at a third of the concrete's level, undersides and sides in their own
    /// shade, soot caps up. The ending vignette is the same for the concrete chunks beside the pieces, so the pieces are measured
    /// against those; this component closes the rest of the gap on the pieces only (the flying cannon is untouched):
    /// <list type="bullet">
    /// <item>the paint on the pieces eases from the thrown cannon's level to <see cref="PieceTone"/> x over <see cref="EaseSeconds"/> s
    /// after the break (under the landing's dust): heat-bleached ash grey, the soot, scratches, cracks, rust and the team stripes kept
    /// (a multiplier on the paint block, texel detail untouched);</item>
    /// <item>the fixed surfaces (the torn steel of the cut faces, rims and ribs, the rebar, the burnt hydraulic sleeves) x <see cref="FaceLift"/>:
    /// pale torn steel and light rust instead of near black, as cooled burnt steel is. The break-face and rebar materials exist on the
    /// pieces only, so they are lifted on the material once a session; the burnt sleeve material is ONE copy per source, worn by every
    /// thrown cannon of the session in flight too, so it is lifted per piece slot (a property block), never on the material.</item>
    /// </list>
    /// Switch <see cref="Enabled"/> (off = the pieces as the wreck look leaves them). The KD3 A/B measures the two numbers and prints the
    /// values to set; the KD check's tone probe includes PieceTone (OzFortGunWreckLook.ProbeTone).
    /// </summary>
    [DefaultExecutionOrder(96)]                                                                   // after the wreck look (95)
    public sealed class OzFortGunRestLook : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>The paint on the resting pieces, x the thrown cannon's level (linear). 1 = unchanged. Set from the judged frame: the
        /// piece tops read 0.25-0.5 of the concrete chunk beside them (its lit face and its shade together), so x1.8 puts them at
        /// 0.45-0.9 of it; the KD3 A/B's search prints the measured value to set (in Play: x1.8 / x2.2 read 1.34 of the chunks beside them, x1.3 / x1.8 read 0.76).</summary>
        public static float PieceToneWreck = 1.3f;
        /// <summary>Item 375: the same for the fracture look's pieces (the cannon's own paint, not the ash grey wreck paint); the KD3 A/B prints
        /// the value to set (wanted: the pieces at rest 0.60-0.72 of the concrete chunks beside them).</summary>
        public static float PieceToneFracture = 1.7f;
        /// <summary>The tone of the look in use (OzFortGunBreakup.FractureLook): every reader and the KD3 search use it as before.</summary>
        public static float PieceTone { get => OzFortGunBreakup.FractureLook ? PieceToneFracture : PieceToneWreck; set { if (OzFortGunBreakup.FractureLook) PieceToneFracture = value; else PieceToneWreck = value; } }
        /// <summary>The cut faces, rims, ribs, rebar and burnt sleeves, x their wreck-look level (linear). 1 = unchanged.</summary>
        public static float FaceLift = 1.8f;
        /// <summary>The paint's ease from the thrown cannon's level to PieceTone (s after the break; the dust covers it).</summary>
        public static float EaseSeconds = 2f;
        // proof
        public static int PiecesLifted, PaintSlots, FixedSlots, SharedMaterials;
        public static string State => Enabled ? $"rest look on: paint x{PieceTone:0.00} eased over {EaseSeconds:0.0} s, fixed surfaces x{FaceLift:0.00}; pieces {PiecesLifted}, paint slots {PaintSlots}, fixed slots {FixedSlots}, shared materials {SharedMaterials}" : "rest look OFF";
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Dictionary<Material, Vector4> sharedBase = new Dictionary<Material, Vector4>();
        static readonly List<OzFortGunRestLook> live = new List<OzFortGunRestLook>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { OzFortGunBreakup.Broke -= OnBroke; OzFortGunBreakup.Broke += OnBroke; }

        static void OnBroke(Vector3 at)
        {
            if (!Enabled) return;
            foreach (var pc in OzFortGunBreakup.LastPieces) if (pc && !pc.GetComponent<OzFortGunRestLook>()) pc.gameObject.AddComponent<OzFortGunRestLook>();
        }

        /// <summary>A material of the pieces that is not the paint: the torn steel, the rebar, the burnt sleeves (all built at run time by the
        /// wreck look / tone; never the living cannon's own sleeve material).</summary>
        public static bool IsFixed(Material m) => m && (m.name.StartsWith("FortGun_BreakFace") || m.name == "FortGun_Rebar" || IsSleeve(m));
        /// <summary>The burnt sleeve copy (OzFortGunWreckTone.BurntRam): one material per source for the whole session, on the thrown cannon in
        /// flight as well as on its pieces, so the rest look lifts it per piece slot and leaves the material as it is.</summary>
        public static bool IsSleeve(Material m) => m && m.name.EndsWith("_Burnt");
        public static bool IsPaint(Material m) => m && m.name.EndsWith("_Wreck");

        /// <summary>The paint value the KD check's tone probe writes on a piece: the sRGB level k (as TeamPaint's SetColor would give it) x PieceTone (linear).</summary>
        public static Vector4 PaintValue(float k)
        {
            var c = new Color(k, k, k, 1f).linear; float t = Enabled ? PieceTone : 1f;
            return new Vector4(c.r * t, c.g * t, c.b * t, 1f);
        }

        sealed class Slot { public Renderer r; public int i; public Vector4 baseValue; public bool paint; }
        readonly List<Slot> slots = new List<Slot>(); float age; bool done, set;

        void Start()
        {   // the frame after the break: the wreck look has dressed the piece (its cut faces, rims, ribs, rebar)
            live.Add(this);
            var mpb = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    bool paint = IsPaint(m), fixedM = IsFixed(m);
                    if (!paint && !fixedM) continue;
                    r.GetPropertyBlock(mpb, i);
                    bool inBlock = mpb.HasVector(BaseColorId) || mpb.HasColor(BaseColorId);
                    if (!inBlock && fixedM && !IsSleeve(m))
                    {   // no block: the shared material carries the level (torn steel, rebar: materials of the pieces only): lifted once a session
                        if (!sharedBase.ContainsKey(m)) { sharedBase[m] = m.HasProperty(BaseColorId) ? (Vector4)m.GetVector(BaseColorId) : Vector4.one; SharedMaterials++; }
                        continue;
                    }
                    // a slot of this piece's renderer: the paint (its block), or the burnt sleeve (the material's own level as the base; the
                    // material itself stays as the thrown cannon wears it)
                    var v = inBlock ? mpb.GetVector(BaseColorId) : (m.HasProperty(BaseColorId) ? (Vector4)m.GetVector(BaseColorId) : Vector4.one);
                    slots.Add(new Slot { r = r, i = i, baseValue = v, paint = paint });
                    if (paint) PaintSlots++; else FixedSlots++;
                }
            }
            PiecesLifted++;
            ApplyShared(FaceLift);
            Apply(0f);
        }

        void OnDestroy() { live.Remove(this); }

        void Update()
        {
            if (done) return;
            age += Time.deltaTime;
            float k = EaseSeconds > 0.01f ? Mathf.Clamp01(age / EaseSeconds) : 1f;
            Apply(k * k * (3f - 2f * k));
            if (k >= 1f) done = true;
        }

        /// <summary>The paint at lerp(1, PieceTone, k), the fixed surfaces at lerp(1, FaceLift, k) (k = 1 once eased).</summary>
        void Apply(float k, float tone = -1f, float lift = -1f)
        {
            if (tone < 0f) tone = PieceTone; if (lift < 0f) lift = FaceLift;
            var mpb = new MaterialPropertyBlock();
            foreach (var s in slots)
            {
                if (!s.r) continue;
                float f = Mathf.Lerp(1f, s.paint ? tone : lift, k);
                var v = s.baseValue; v.x *= f; v.y *= f; v.z *= f;
                s.r.GetPropertyBlock(mpb, s.i); mpb.SetVector(BaseColorId, v); s.r.SetPropertyBlock(mpb, s.i);
            }
            set = true;
        }

        static void ApplyShared(float lift)
        {
            foreach (var kv in sharedBase)
            {
                var m = kv.Key; if (!m || !m.HasProperty(BaseColorId)) continue;
                var v = kv.Value; v.x *= lift; v.y *= lift; v.z *= lift;
                m.SetVector(BaseColorId, v);
            }
        }

        /// <summary>The A/B's search: every live piece at these numbers now (the ease finished); Probe(PieceTone, FaceLift) puts the configured values back.</summary>
        public static int Probe(float tone, float lift)
        {
            int n = 0;
            ApplyShared(lift);
            foreach (var p in live) if (p && p.set) { p.done = true; p.Apply(1f, tone, lift); n++; }
            return n;
        }
    }
}
