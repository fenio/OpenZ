using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Every flag the player does not own gets a soft glowing
    /// ring on the ground (the selection ring's OpenZ/Overlay look, warm amber, HDR for a light bloom) that breathes and sends a thin
    /// wave outward every few seconds; it fades out when the player takes the flag. L17 by default (L02 flags stand out on the sand).
    /// View only: reads the sim's flags and owners.
    /// </summary>
    public class FlagBeacons : MonoBehaviour
    {
        public static bool OnL17 = true, OnL02 = false;
        public float radius = 1.9f, pulsePeriod = 3.2f, glow = 1.2f;
        public Color color = new Color(1f, 0.78f, 0.36f, 0.4f);
        Material mat, waveMat; Mesh ring;
        readonly Dictionary<int, float> fade = new Dictionary<int, float>();
        MaterialPropertyBlock mpb;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (!FindFirstObjectByType<FlagBeacons>()) new GameObject("OZ_FlagBeacons").AddComponent<FlagBeacons>(); }

        void Start()
        {
            var sh = Shader.Find("OpenZ/Overlay"); if (!sh) { enabled = false; return; }
            mat = new Material(sh); mat.SetColor("_Color", Hdr(color, glow)); mat.SetFloat("_ZTest", 4);
            waveMat = new Material(sh); waveMat.SetColor("_Color", Hdr(color, glow)); waveMat.SetFloat("_ZTest", 4);
            ring = GlowRing(48); mpb = new MaterialPropertyBlock();
        }

        static Color Hdr(Color c, float g) => new Color(c.r * g, c.g * g, c.b * g, c.a);

        void LateUpdate()
        {
            var g = ZGame.I; if (!g || g.World == null || !mat) return;
            bool on = g.level == "L17" ? OnL17 : OnL02; if (!on) return;
            float t = Time.time, dt = Time.deltaTime;
            foreach (var f in g.World.Flags)
            {
                bool want = f.Owner != g.player;
                fade.TryGetValue(f.Id, out float k); k = Mathf.MoveTowards(k, want ? 1f : 0f, dt / 0.8f); fade[f.Id] = k;
                if (k <= 0.001f) continue;
                var p = g.CenterWorld(f); p.y = g.GroundY(p.x, p.z) + 0.06f;
                float ph = (t + f.Id * 0.37f) / pulsePeriod;
                float breathe = 0.75f + 0.25f * Mathf.Sin(ph * Mathf.PI * 2f);
                var c = Hdr(color, glow); c.a *= breathe * k;
                mpb.SetColor("_Color", c);
                Graphics.DrawMesh(ring, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * radius), mat, 0, null, 0, mpb);
                float w = Mathf.Repeat(ph, 1f);                                                     // the outward wave
                var cw = Hdr(color, glow); cw.a *= (1f - w) * (1f - w) * 0.7f * k;
                mpb.SetColor("_Color", cw);
                Graphics.DrawMesh(ring, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * radius * (1f + 0.8f * w)), waveMat, 0, null, 0, mpb);
            }
        }

        /// <summary>SelectionOverlay's glow ring: a thin bright line with a soft glow on both sides (vertex alpha).</summary>
        static Mesh GlowRing(int seg)
        {
            float[] r = { 0.84f, 0.95f, 0.985f, 1.05f }; float[] a = { 0f, 1f, 1f, 0f };
            var v = new List<Vector3>(); var tr = new List<int>(); var col = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float ang = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                for (int k = 0; k < 4; k++) { v.Add(d * r[k]); col.Add(new Color(1f, 1f, 1f, k == 0 || k == 3 ? 0f : a[k] * (k == 1 ? 0.55f : 1f))); }
                if (i < seg) for (int k = 0; k < 3; k++) { int q = i * 4 + k; tr.AddRange(new[] { q, q + 1, q + 5, q, q + 5, q + 4 }); }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetColors(col); m.SetTriangles(tr, 0); m.RecalculateBounds();
            return m;
        }
    }
}
