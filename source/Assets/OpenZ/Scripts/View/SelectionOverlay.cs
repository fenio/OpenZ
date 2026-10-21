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
    [DefaultExecutionOrder(205)]                                                                         // K387: after the ships (OzPlayableShuttle 200): a flying shuttle's route starts at the ship of this frame (SelectionOverlay.ShuttleRoute.cs)
    public partial class SelectionOverlay : MonoBehaviour
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
            markerMat = Tinted(baseMat, MarkerBase, 4);
            pathMat = Tinted(baseMat, PathBase, 4);
            ring = Annulus(0.82f, 1f, 40);
            dot = Annulus(0f, 1f, 12);
        }

        static Color Hdr(Color c, float gain) => new Color(c.r * gain, c.g * gain, c.b * gain, c.a);

        // ---- the order marks by night: the dots and rings are unlit and bright enough to bloom, which by day reads as a soft
        // mark and at night as a row of lamps. Their colour is scaled by the day / night value: 1 by day (nothing changes),
        // NightDim at full night, smooth through dusk and dawn. The selection rings keep their own glow.
        static readonly Color MarkerBase = Hdr(new Color(1f, 0.95f, 0.4f, 1f), 2.2f), PathBase = Hdr(new Color(1f, 0.9f, 0.62f, 0.65f), 1.2f);
        /// <summary>Brightness of the waypoint dots and order marks at full night (1 = as by day).</summary>
        public const float NightDim = 0.40f;
        /// <summary>Checks: the factor on the dots' brightness now (exactly 1 by day).</summary>
        public static float DotBrightness { get; private set; } = 1f;
        float dimNow = -1f;
        void ApplyNight()
        {
            float k = Mathf.Lerp(1f, NightDim, Mathf.Clamp01(NightGrade.NightK));
            if (Mathf.Abs(k - dimNow) < 0.002f) return;
            dimNow = k; DotBrightness = k;
            markerMat.SetColor("_Color", new Color(MarkerBase.r * k, MarkerBase.g * k, MarkerBase.b * k, MarkerBase.a));
            pathMat.SetColor("_Color", new Color(PathBase.r * k, PathBase.g * k, PathBase.b * k, PathBase.a));
        }

        // ---- every dot on the surface drawn under it
        /// <summary>Checks: dots seated in the last frame, and how many of them took the bridge-deck look-up.</summary>
        public static int DotsSeated, DotsOnDeckLegs;
        /// <summary>The point p on the surface that is drawn under it, lifted by lift: the rendered ground (TrackSurface: exact,
        /// no ray), on a leg that touches a bridge deck the walk surface (one ray a dot, rare), and over water the water
        /// surface when that lies higher. The old rule drew a leg's dots on the straight line between its two ends, so on a
        /// slope, a crater bowl, a bank or a ramp the dots in between sank into the ground or hung over it.</summary>
        public static Vector3 Seat(ZGame g, Vector3 p, float lift, bool deckLeg)
        {
            var s = TrackMarks.I ? TrackMarks.I.Surface : null; float y;
            bool onDeck = false;                                                                           // this dot itself on the deck (a deck leg also has dots before and after the deck)
            if (deckLeg) { if (s == null || !s.Sample(p.x, p.z, out y, out onDeck)) y = g.GroundY(p.x, p.z); }
            else if (s != null && s.RenderY(p.x, p.z, out y)) y += CraterField.DentRender(p.x, p.z);
            else y = g.GroundY(p.x, p.z);
            if (!onDeck && OpenZFxPlayer.IsWaterTile(p))
            {
                var fx = OpenZFxPlayer.I;
                float wy = fx ? fx.WaterYAt(p) : float.NegativeInfinity;
                if (!float.IsNegativeInfinity(wy) && wy > y) y = wy;
            }
            p.y = y + lift;
            return p;
        }

        /// <summary>Is the walk surface at p a bridge deck (one ray)?</summary>
        static bool OnDeck(Vector3 p)
        {
            var s = TrackMarks.I ? TrackMarks.I.Surface : null; bool deck = false;
            if (s != null) s.Sample(p.x, p.z, out _, out deck);
            return deck;
        }

        /// <summary>The dots of one leg, each on the drawn surface. A leg with an end on a bridge deck follows the deck (a ray a
        /// dot); every other leg reads the rendered ground without a ray.</summary>
        void Leg(ZGame g, Vector3 a, bool aDeck, Vector3 b, bool bDeck, float first, float step, float lift, float size, Material mat)
        {
            float len = Vector3.Distance(a, b); if (len <= first) return;
            bool deck = aDeck || bDeck; if (deck) DotsOnDeckLegs++;
            for (float s = first; s < len; s += step) { Draw(dot, Seat(g, Vector3.Lerp(a, b, s / len), lift, deck), size, mat); DotsSeated++; }
        }
        readonly List<Vector3> legPts = new List<Vector3>(); readonly List<bool> legDeck = new List<bool>();   // reused every frame

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
            ApplyNight(); DotsSeated = 0; DotsOnDeckLegs = 0;
            float now = Time.unscaledTime;
            var kbd = UnityEngine.InputSystem.Keyboard.current;
            bool queuing = (kbd != null && kbd.shiftKey.isPressed) || input.Pending.Count > 0;
            bool showPath = queuing || now - Mathf.Max(input.SelectTime, input.MarkerTime) < 3f;
            foreach (var o in input.Selection)
            {
                if (o.Destroyed || o.KillAt >= 0) continue;                                                   // KC4b: got into a vehicle / gun / troop carrier / fort (or leaving the game): no ring left on the map
                float r = (o.Kind == ZKind.Robot ? 0.95f * OzHeroSquadView.Tall(o) : 1.9f) * OzRingClock.Pulse;   // subtle pulse (K403: on the rings' own frame clock, View/OzRingClock.cs: smooth in a fixed-step recording too); KC4b: a hero's ring grows with him
                var cp = g.Views.TryGetValue(o.Id, out var ov) && ov ? ov.transform.position : g.CenterWorld(o);
                cp.y = ov && ov.OnTower ? ov.transform.position.y : g.GroundY(cp.x, cp.z);            // crews_v1: a tower gun's ring on its pad
                if (!o.IsShuttle) { if (BoardingView.FigureAt(o, ref cp)) Draw(selRing, cp + Vector3.up * 0.25f, r, selMat); }   /* K431: none while it is still inside its carrier, at its figure while it gets out (View/BoardingView.ExitRings.cs) */                   // clears the render terrain (it can sit above the collider), still under the feet
                else OzShuttleShadowRing.Draw(g, o, r / 1.9f, selMat);                                  // K256e: the shuttle's own ring (View/OzShuttleShadowRing.cs): inside its frame, the tank's band, on the drawn ground; none in the air, on a deck or in a crash landing
                foreach (var m in o.Minions) { if (m.KillAt >= 0) continue; var mp = g.Views.TryGetValue(m.Id, out var mv) ? mv.transform.position : g.CenterWorld(m); if (BoardingView.FigureAt(m, ref mp)) Draw(selRing, mp + Vector3.up * 0.25f, 0.7f * OzHeroSquadView.Tall(m), minionMat); }   /* K431: a member still inside its carrier gets no ring, one getting out its ring at its figure */   // KC4b: a hero's size; none for a member that left the map
                if (o.Patrolling)
                {
                    var pa = Px(g, o.PatrolAx, o.PatrolAy, out bool paDeck); var pb = Px(g, o.PatrolBx, o.PatrolBy, out bool pbDeck);
                    Leg(g, pa, paDeck, pb, pbDeck, 0.7f, 1.6f, 0.1f, 0.14f, markerMat);
                    Draw(ring, pa + Vector3.up * 0.1f, 0.8f, markerMat); Draw(ring, pb + Vector3.up * 0.1f, 0.8f, markerMat);
                }
                if (!showPath) continue;
                if (o.IsShuttle && ShuttleRoute(g, o, queuing)) continue;                                   // K387: in the air (or taking off, or a Shift queue) a shuttle's route runs from the ship in the sky down to its goal (SelectionOverlay.ShuttleRoute.cs)
                // waypoint path: current target, remaining path points, then the queued waypoints
                var prev = g.CenterWorld(o); bool prevDeck = OnDeck(prev), d;
                var pts = legPts; pts.Clear(); legDeck.Clear();
                if (o.IsMoving) { pts.Add(Px(g, o.CurrentTarget.x, o.CurrentTarget.y, out d)); legDeck.Add(d); }
                if (o.PathPoints != null) foreach (var p in o.PathPoints) { pts.Add(Px(g, p.x, p.y, out d)); legDeck.Add(d); }
                for (int i = 1; i < o.Waypoints.Count; i++) { pts.Add(Px(g, o.Waypoints[i].x, o.Waypoints[i].y, out d)); legDeck.Add(d); }
                for (int i = 0; i < pts.Count; i++)
                {
                    Leg(g, prev, prevDeck, pts[i], legDeck[i], 0.8f, 1.2f, 0.1f, 0.12f, pathMat);
                    prev = pts[i]; prevDeck = legDeck[i];
                }
                // while Shift-queuing
                float mAge = now - input.MarkerTime, markLife = OpenZUnitOverlay.OrderMarkLife;
                if (queuing || (mAge >= 0f && mAge < markLife))
                {
                    float ms = queuing ? 0.45f : 0.45f * (mAge < 0.12f ? Mathf.Lerp(0.6f, 1.3f, mAge / 0.12f) : Mathf.Lerp(1.3f, 1.05f, (mAge - 0.12f) / (markLife - 0.12f)));
                    for (int i = 0; i < o.Waypoints.Count; i++) Draw(ring, Px(g, o.Waypoints[i].x, o.Waypoints[i].y, out d) + Vector3.up * 0.08f, ms, pathMat);
                }
                // Shift-queued orders (not sent yet): brighter dots + a marker ring at every waypoint
                if (input.Pending.TryGetValue(o, out var q))
                    foreach (var w in q)
                    {
                        var p = Px(g, w.x, w.y, out d);
                        Leg(g, prev, prevDeck, p, d, 0.6f, 0.9f, 0.12f, 0.16f, markerMat);
                        Draw(ring, p + Vector3.up * 0.1f, 0.7f, markerMat); prev = p; prevDeck = d;
                    }
            }
            // (the old pulsing yellow order ring at input.MarkerPos is gone: the restyled HUD draws the "-ED" order marker,
        }

        /// <summary>A map point on the surface drawn there (rendered ground, bridge deck or water surface); deck = it is a bridge deck.</summary>
        static Vector3 Px(ZGame g, int x, int y, out bool deck) { var p = ZGame.PxToWorld(x, y); deck = OnDeck(p); return Seat(g, p, 0f, deck); }

        static void Draw(Mesh m, Vector3 pos, float scale, Material mat) =>
            Graphics.DrawMesh(m, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale), mat, 0);
    }
}
