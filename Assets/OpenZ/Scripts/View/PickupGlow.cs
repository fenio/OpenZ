using UnityEngine;

namespace OpenZ
{
    /// <summary>Soft pulsing amber ring on the ground under a pickup (grenade crates: a soft pickup glow),
    /// depth-tested so units walking over it cover it, HDR so bloom catches it.</summary>
    public class PickupGlow : MonoBehaviour
    {
        public float radius = 1.3f;
        /// <summary></summary>
        [System.NonSerialized] public float fade = 1f;
        static Mesh ring; static Material mat;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock mpb;
        float phase;

        void Start() { phase = (transform.position.x * 0.37f + transform.position.z * 0.21f) % 6.283f; }

        void LateUpdate()
        {
            if (!ring) ring = Annulus(0.78f, 1f, 48);
            if (!mat)
            {
                var sh = Shader.Find("OpenZ/Overlay"); if (!sh) return;
                mat = new Material(sh); mat.SetColor("_Color", new Color(2.2f, 1.45f, 0.45f, 0.55f)); mat.SetFloat("_ZTest", 4);
            }
            float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.4f + phase);
            if (fade <= 0.001f) return;
            var c = new Color(2.2f, 1.45f, 0.45f, (0.35f + 0.3f * k) * Mathf.Clamp01(fade));
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetColor(ColorId, c);                                                                  // per crate (was one shared colour for all)
            var p = transform.position; var gy = ZGame.I ? ZGame.I.GroundY(p.x, p.z) : p.y;
            Graphics.DrawMesh(ring, Matrix4x4.TRS(new Vector3(p.x, gy + 0.25f, p.z), Quaternion.identity, Vector3.one * radius * (1f + 0.06f * k)), mat, 0, null, 0, mpb);
        }

        static Mesh Annulus(float rIn, float rOut, int seg)
        {
            var v = new Vector3[(seg + 1) * 2]; var col = new Color[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * rIn; v[i * 2 + 1] = d * rOut; col[i * 2] = new Color(1, 1, 1, 0f); col[i * 2 + 1] = Color.white;
                if (i < seg) { int k = i * 2, j = i * 6; t[j] = k; t[j + 1] = k + 1; t[j + 2] = k + 3; t[j + 3] = k; t[j + 4] = k + 3; t[j + 5] = k + 2; }
            }
            var m = new Mesh(); m.vertices = v; m.colors = col; m.triangles = t; m.RecalculateBounds(); return m;
        }
    }
}
