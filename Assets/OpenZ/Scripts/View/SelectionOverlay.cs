using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Map overlays for the controls: a ring under every selected unit (squad minions get a thin one), the waypoint path of
    /// each selected unit for 3 s after selecting / ordering (zobject.cpp:1068-1111), and the order marker at the clicked
    /// point for 3 s (the "-ED" cursor echo, zplayer.cpp:2970-2977). Drawn with OpenZ/Overlay (no depth write, offset).
    /// </summary>
    public class SelectionOverlay : MonoBehaviour
    {
        public PlayerInput input;
        public Material overlayMaterial;
        Mesh ring, dot;
        Material selMat, minionMat, markerMat, pathMat;
        // Enemy target markers stay red
        static readonly Color SelColor = new Color(0.30f, 1f, 0.42f, 0.95f);
        Mesh selRing;

        void Awake()
        {
            if (!input) input = GetComponent<PlayerInput>();
            var baseMat = overlayMaterial ? overlayMaterial : new Material(Shader.Find("OpenZ/Overlay"));
            selMat = Tinted(baseMat, Hdr(SelColor, 1.9f), 4);
            minionMat = Tinted(baseMat, Hdr(new Color(0.30f, 1f, 0.42f, 0.55f), 1.5f), 4);
            selRing = GlowRing(40);
            markerMat = Tinted(baseMat, Hdr(new Color(1f, 0.95f, 0.4f, 1f), 2.2f), 4);
            pathMat = Tinted(baseMat, Hdr(new Color(1f, 0.9f, 0.62f, 0.65f), 1.2f), 4);
            ring = Annulus(0.82f, 1f, 40);
            dot = Annulus(0f, 1f, 12);
        }

        static Color Hdr(Color c, float gain) => new Color(c.r * gain, c.g * gain, c.b * gain, c.a);

        static Material Tinted(Material b, Color c, float zTest)
        {
            var m = new Material(b); m.SetColor("_Color", c); m.SetFloat("_ZTest", zTest); return m;
        }

        /// <summary>A thin bright line (0.95-0.985 r) with a soft glow fading in to 0.84 r and out to 1.05 r (vertex alpha).</summary>
        static Mesh GlowRing(int seg)
        {
            float[] r = { 0.84f, 0.95f, 0.985f, 1.05f }; float[] a = { 0f, 1f, 1f, 0f };
            var v = new List<Vector3>(); var t = new List<int>(); var col = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float ang = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                for (int k = 0; k < 4; k++) { v.Add(d * r[k]); col.Add(new Color(1f, 1f, 1f, k == 0 || k == 3 ? 0f : a[k] * (k == 1 ? 0.55f : 1f))); }
                if (i < seg) for (int k = 0; k < 3; k++) { int q = i * 4 + k; t.AddRange(new[] { q, q + 1, q + 5, q, q + 5, q + 4 }); }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        static Mesh Annulus(float rIn, float rOut, int seg)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var col = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * rIn); v.Add(d * rOut); col.Add(Color.white); col.Add(Color.white);
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        void LateUpdate()
        {
            if (!input || !input.game || input.game.World == null) return;
            var g = input.game;
            float now = Time.unscaledTime;
            var kbd = UnityEngine.InputSystem.Keyboard.current;
            bool queuing = (kbd != null && kbd.shiftKey.isPressed) || input.Pending.Count > 0;
            bool showPath = queuing || now - Mathf.Max(input.SelectTime, input.MarkerTime) < 3f;
            foreach (var o in input.Selection)
            {
                float r = (o.Kind == ZKind.Robot ? 0.95f : 1.9f) * (1f + 0.035f * Mathf.Sin(now * 3.2f));   // subtle pulse
                var cp = g.Views.TryGetValue(o.Id, out var ov) && ov ? ov.transform.position : g.CenterWorld(o);
                cp.y = ov && ov.OnTower ? ov.transform.position.y : g.GroundY(cp.x, cp.z);            // crews_v1: a tower gun's ring on its pad
                Draw(selRing, cp + Vector3.up * 0.25f, r, selMat);                                   // clears the render terrain (it can sit above the collider), still under the feet
                foreach (var m in o.Minions) Draw(selRing, (g.Views.TryGetValue(m.Id, out var mv) ? mv.transform.position : g.CenterWorld(m)) + Vector3.up * 0.25f, 0.7f, minionMat);
                if (o.Patrolling)
                {
                    var pa = Px(g, o.PatrolAx, o.PatrolAy); var pb = Px(g, o.PatrolBx, o.PatrolBy); float pl = Vector3.Distance(pa, pb);
                    for (float s = 0.7f; s < pl; s += 1.6f) Draw(dot, Vector3.Lerp(pa, pb, s / pl) + Vector3.up * 0.1f, 0.14f, markerMat);
                    Draw(ring, pa + Vector3.up * 0.1f, 0.8f, markerMat); Draw(ring, pb + Vector3.up * 0.1f, 0.8f, markerMat);
                }
                if (!showPath) continue;
                // waypoint path: current target, remaining path points, then the queued waypoints
                var prev = g.CenterWorld(o);
                var pts = new List<Vector3>();
                if (o.IsMoving) pts.Add(Px(g, o.CurrentTarget.x, o.CurrentTarget.y));
                if (o.PathPoints != null) foreach (var p in o.PathPoints) pts.Add(Px(g, p.x, p.y));
                for (int i = 1; i < o.Waypoints.Count; i++) pts.Add(Px(g, o.Waypoints[i].x, o.Waypoints[i].y));
                foreach (var p in pts)
                {
                    float len = Vector3.Distance(prev, p);
                    for (float s = 0.8f; s < len; s += 1.2f) Draw(dot, Vector3.Lerp(prev, p, s / len) + Vector3.up * 0.1f, 0.12f, pathMat);
                    prev = p;
                }
                // while Shift-queuing
                float mAge = now - input.MarkerTime, markLife = OpenZUnitOverlay.OrderMarkLife;
                if (queuing || (mAge >= 0f && mAge < markLife))
                {
                    float ms = queuing ? 0.45f : 0.45f * (mAge < 0.12f ? Mathf.Lerp(0.6f, 1.3f, mAge / 0.12f) : Mathf.Lerp(1.3f, 1.05f, (mAge - 0.12f) / (markLife - 0.12f)));
                    for (int i = 0; i < o.Waypoints.Count; i++) Draw(ring, Px(g, o.Waypoints[i].x, o.Waypoints[i].y) + Vector3.up * 0.08f, ms, pathMat);
                }
                // Shift-queued orders (not sent yet): brighter dots + a marker ring at every waypoint
                if (input.Pending.TryGetValue(o, out var q))
                    foreach (var w in q)
                    {
                        var p = Px(g, w.x, w.y); float len = Vector3.Distance(prev, p);
                        for (float s = 0.6f; s < len; s += 0.9f) Draw(dot, Vector3.Lerp(prev, p, s / len) + Vector3.up * 0.12f, 0.16f, markerMat);
                        Draw(ring, p + Vector3.up * 0.1f, 0.7f, markerMat); prev = p;
                    }
            }
            // (the old pulsing yellow order ring at input.MarkerPos is gone: the restyled HUD draws the "-ED" order marker,
        }

        static Vector3 Px(ZGame g, int x, int y) { var p = ZGame.PxToWorld(x, y); p.y = g.GroundY(p.x, p.z); return p; }

        static void Draw(Mesh m, Vector3 pos, float scale, Material mat) =>
            Graphics.DrawMesh(m, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale), mat, 0);
    }
}
