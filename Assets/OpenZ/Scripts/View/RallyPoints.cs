using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// Rally points, Z / Zod style. The Sim already has them (ZObject.RallyPoints, ZWorld.SetRallyPoints, appended to every
    /// new unit after its force-move out of the door, kept on capture); this is the player side:
    /// - left-click your own fort / robot factory / vehicle factory (a click that selects no unit, zplayer.cpp:3253-3331)
    ///   selects it (Zod opens its production window; the HUD's window can call Select / Deselect instead);
    /// - with that building selected and NO units selected, right-button release on the map sets its rally point
    ///   (zplayer.cpp:2866-2878: MOVE, attack-move, player given); Shift + right-clicks chain several and are sent when Shift
    ///   is released (zplayer_events.cpp:394-427, 598-602); a new plain right-click replaces the list (zcore.cpp:580-630);
    /// - a click elsewhere, Esc, or losing / destroying the building deselects it.
    /// Drawn: corner brackets around the selected building, Zod's rally line (creation point -> exit -> each rally point,
    /// zobject.cpp:1113-1330) as marching dots, a team flag planted on the last rally point (a small post on the others),
    /// and optionally the flags of every own factory. Execution order 10: runs after PlayerInput's Update.
    /// Stand-alone fallback: it needs no HUD, and it stands by automatically while the UI track's OpenZRallyPoints (HUD
    /// production window + SET RALLY tool) is in the scene.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class RallyPoints : MonoBehaviour
    {
        public static RallyPoints I { get; private set; }
        /// <summary>(building, its new rally list) after a successful SetRallyPoints.</summary>
        public static event System.Action<ZObject, IReadOnlyList<ZWaypoint>> RallySet;
        public event System.Action<ZObject> SelectedChanged;

        public PlayerInput input;
        public ZGame game;
        [Tooltip("flags of every own factory stay visible (Zod shows the rally line only for the open building)")] public bool showAllOwnFlags = true;
        public Color dotColor = new Color(0.94f, 0.93f, 0.86f, 0.9f);
        public float dotSpacing = 1.1f;

        public ZObject Selected { get; private set; }
        /// <summary>Shift-chained points not sent yet (Zod's building dev waypoint list).</summary>
        public readonly List<ZWaypoint> Pending = new List<ZWaypoint>();

        Vector2 pressPos; bool pressed, rightArmed;
        readonly Dictionary<int, float> planted = new Dictionary<int, float>();
        readonly List<(Vector3 p, float t)> pulses = new List<(Vector3, float)>();
        Mesh dot, ring, bar, pole, banner; Material dotMat, pendingMat, selMat, pulseMat, poleMat, bannerMat;
        readonly Dictionary<Team, Material> ringMats = new Dictionary<Team, Material>();
        MaterialPropertyBlock mpb;
        Vector3[] bannerRest; Vector3[] bannerNow;

        Camera Cam => input && input.cam ? input.cam.GetComponent<Camera>() : Camera.main;

        void Awake()
        {
            I = this;
            if (!input) input = GetComponent<PlayerInput>(); if (!input) input = FindFirstObjectByType<PlayerInput>();
            if (!game) game = input && input.game ? input.game : ZGame.I;
            var overlay = FactoryMeshes.FindShader("OpenZ/Overlay", "Sprites/Default");
            dotMat = Overlay(overlay, dotColor, 4);                                      // Zod draws rally dots under the objects
            pendingMat = Overlay(overlay, new Color(dotColor.r, dotColor.g, dotColor.b, 0.45f), 4);
            selMat = Overlay(overlay, new Color(0.55f, 1f, 0.55f, 0.95f), 8);            // SelectionOverlay's green, always on top
            pulseMat = Overlay(overlay, new Color(1f, 0.95f, 0.4f, 1f), 8);              // the order marker's yellow
            var lit = FactoryMeshes.FindShader("OpenZ/FxLit", "Universal Render Pipeline/Lit");
            poleMat = new Material(lit) { name = "RallyPole" }; poleMat.SetColor("_BaseColor", new Color(0.8f, 0.79f, 0.76f, 1f));
            if (poleMat.HasProperty("_Unlit")) poleMat.SetFloat("_Unlit", 0.2f);
            bannerMat = new Material(lit) { name = "RallyBanner" }; bannerMat.SetColor("_BaseColor", Color.white);
            if (bannerMat.HasProperty("_Unlit")) bannerMat.SetFloat("_Unlit", 0.35f);
            if (bannerMat.HasProperty("_Cull")) bannerMat.SetFloat("_Cull", 0f);          // both sides of the cloth
            mpb = new MaterialPropertyBlock();
            dot = Disc(0f, 1f, 12); ring = Disc(0.78f, 1f, 40); bar = Bar();
            pole = Pole(); banner = Banner(out bannerRest); bannerNow = (Vector3[])bannerRest.Clone();
        }

        static Material Overlay(Shader s, Color c, float zTest)
        {
            var m = new Material(s) { name = "RallyOverlay" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", zTest);
            return m;
        }

        Material RingMat(Team t)
        {
            if (ringMats.TryGetValue(t, out var m) && m) return m;
            var c = TeamColors.Of(t); c.a = 0.9f;
            m = Overlay(FactoryMeshes.FindShader("OpenZ/Overlay", "Sprites/Default"), c, 4); ringMats[t] = m; return m;
        }

        bool Ready => game != null && game.World != null && input != null && !YieldToHud();

        // the scene this component steps aside so a right-click never sets the rally twice and nothing is drawn twice.
        static System.Type hudRallyType; static bool hudTypeChecked; float nextHudCheck; bool hudPresent;
        bool YieldToHud()
        {
            if (Time.unscaledTime >= nextHudCheck)
            {
                nextHudCheck = Time.unscaledTime + 1f;
                if (!hudTypeChecked)
                {
                    hudTypeChecked = true;
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) { var t = asm.GetType("OpenZ.OpenZRallyPoints"); if (t != null) { hudRallyType = t; break; } }
                }
                bool was = hudPresent;
                hudPresent = hudRallyType != null && FindFirstObjectByType(hudRallyType) != null;
                if (hudPresent && !was) { Deselect(); Debug.Log("[OpenZ] RallyPoints: the HUD's OpenZRallyPoints handles rally points, standing by"); }
            }
            return hudPresent;
        }
        bool InView(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;

        public bool IsOwnProducer(ZObject o) =>
            o != null && o.Kind == ZKind.Building && o.IsProducer && !o.Destroyed && game != null && o.Owner == game.player && game.World.Get(o.Id) == o;

        // ------------------------------------------------------------------ selection API (HUD production window, factory list)
        /// <summary>Zod ObjectMakeGuiWindow (zplayer.cpp:3969-3994): own building only; clears the unsent rally points.</summary>
        public bool Select(ZObject b)
        {
            if (!IsOwnProducer(b)) return false;
            Pending.Clear();
            if (Selected != b) { Selected = b; SelectedChanged?.Invoke(b); }
            return true;
        }

        public void Deselect()
        {
            Pending.Clear();
            if (Selected == null) return;
            Selected = null; SelectedChanged?.Invoke(null);
        }

        /// <summary>SEND_RALLYPOINTS for the player's team (the Sim keeps MOVE points only, zcore.cpp:189-194).</summary>
        public bool Set(ZObject b, IList<ZWaypoint> points)
        {
            if (!Ready || b == null) return false;
            bool ok = game.World.SetRallyPoints(game.player, b, points);
            if (ok)
            {
                planted[b.Id] = Time.unscaledTime;
                RallySet?.Invoke(b, b.RallyPoints);
                var at = b.RallyPoints.Count > 0 ? Px(b.RallyPoints[b.RallyPoints.Count - 1].x, b.RallyPoints[b.RallyPoints.Count - 1].y) : game.CenterWorld(b);
                FactoryViews.Sound("rally_set", at, b.Id, b, 0.5f, true);
            }
            return ok;
        }

        // ------------------------------------------------------------------ input
        void Update()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null || kb == null || !Ready) return;
            if (Selected != null && !IsOwnProducer(Selected)) Deselect();                  // captured, destroyed
            if (input.Placing != null) { pressed = false; return; }                          // cannon placement owns the mouse
            Vector2 mp = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame && InView(mp)) { pressed = true; pressPos = mp; }
            if (pressed && mouse.leftButton.wasReleasedThisFrame)
            {
                pressed = false;
                bool click = (mp - pressPos).sqrMagnitude <= 4f;                            // PlayerInput: moved <= 2 px
                var b = click && input.Selection.Count == 0 ? BuildingUnder(mp) : null;     // units under the pointer win (CouldCollectSelectables)
                if (b != null) Select(b); else Deselect();
            }
            if (kb.escapeKey.wasPressedThisFrame) Deselect();

            // armed on the press, so a Z-order that just dropped the last selected unit (PlayerInput.Flush) never also sets a rally
            if (mouse.rightButton.wasPressedThisFrame) rightArmed = Selected != null && input.Selection.Count == 0;
            if (rightArmed && mouse.rightButton.wasReleasedThisFrame) { rightArmed = false; RightRelease(mp, kb.shiftKey.isPressed); }
            if (kb.shiftKey.wasReleasedThisFrame && !kb.shiftKey.isPressed && Pending.Count > 0) Send();
        }

        void RightRelease(Vector2 mp, bool shift)
        {
            if (Selected != null && input.Selection.Count == 0 && InView(mp) && game.GroundPoint(Cam.ScreenPointToRay(mp), out var gp))
            {
                var px = ZGame.WorldToPx(gp);
                // Zod's MapCoordsOfMouseWithHud can only return points on the map (the view never scrolls past it); the 3D
                // terrain extends beyond the Zod grid, so clamp like the HUD's rally tool does (never an off-map MOVE)
                var map = game.World.Map;
                int x = Mathf.Clamp(px.x, 0, map.WidthPix - 1), y = Mathf.Clamp(px.y, 0, map.HeightPix - 1);
                if (x != px.x || y != px.y) { gp = ZGame.PxToWorld(x, y); gp.y = game.GroundY(gp.x, gp.z); }
                Pending.Add(new ZWaypoint { mode = WpMode.Move, refId = -1, x = x, y = y, attackTo = true, playerGiven = true });
                pulses.Add((gp, Time.unscaledTime));
                if (!shift) Send();
            }
        }

        void Send()
        {
            if (Selected != null && Pending.Count > 0) Set(Selected, Pending);
            Pending.Clear();
        }

        /// <summary>Own producer whose screen rectangle (PlayerInput.ScreenRect, 6 m tall) holds the pointer; nearest centre wins.</summary>
        ZObject BuildingUnder(Vector2 mp)
        {
            ZObject best = null; float bd = float.MaxValue;
            foreach (var o in game.World.Objects)
            {
                if (!IsOwnProducer(o)) continue;
                var r = input.ScreenRect(o); if (!r.Contains(mp)) continue;
                float d = (r.center - mp).sqrMagnitude; if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        // ------------------------------------------------------------------ drawing
        Vector3 Px(int x, int y) { var p = ZGame.PxToWorld(x, y); p.y = game.GroundY(p.x, p.z); return p; }

        void LateUpdate()
        {
            if (!Ready) return;
            float t = Time.unscaledTime;
            AnimateBanner(t);
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Building || !o.IsProducer || o.Owner != game.player || o.Destroyed || o.RallyPoints.Count == 0) continue;
                bool sel = o == Selected;
                if (!sel && !showAllOwnFlags) continue;
                float pop = planted.TryGetValue(o.Id, out var pt) ? EaseOutBack(Mathf.Clamp01((t - pt) / 0.35f)) : 1f;
                var rp = o.RallyPoints;
                for (int i = 0; i < rp.Count; i++)
                {
                    bool last = i == rp.Count - 1;
                    if (!last && !sel) continue;
                    DrawFlag(Px(rp[i].x, rp[i].y), o.Owner, (last ? (sel ? 1f : 0.75f) : 0.5f) * pop, last);
                }
            }
            if (Selected != null)
            {
                DrawBrackets(Selected, t);
                var pts = ptsBuf; pts.Clear();
                if (Selected.GetCreationPoint(out int cx, out int cy)) pts.Add(Px(cx, cy));
                if (Selected.GetCreationMovePoint(out int ex, out int ey)) pts.Add(Px(ex, ey));
                int committed = pts.Count;
                foreach (var r in Selected.RallyPoints) pts.Add(Px(r.x, r.y));
                if (pts.Count > committed) Dotted(pts, dotMat, t);
                if (Pending.Count > 0)
                {
                    var pp = pendingBuf; pp.Clear(); if (committed > 1) pp.Add(pts[committed - 1]);
                    foreach (var r in Pending) { var p = Px(r.x, r.y); pp.Add(p); Draw(ring, p + Vector3.up * 0.1f, 0.6f, pendingMat); }
                    Dotted(pp, pendingMat, t);
                }
            }
            for (int i = pulses.Count - 1; i >= 0; i--)
            {
                float k = (t - pulses[i].t) / 0.6f;
                if (k >= 1f) { pulses.RemoveAt(i); continue; }
                Draw(ring, pulses[i].p + Vector3.up * 0.1f, Mathf.Lerp(1.6f, 0.45f, k), pulseMat);
            }
        }

        readonly List<Vector3> ptsBuf = new List<Vector3>(), pendingBuf = new List<Vector3>();

        void Dotted(List<Vector3> pts, Material m, float t)
        {
            float phase = Mathf.Repeat(t * 1.4f, dotSpacing);                                 // marching toward the flag
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i]; float len = Vector3.Distance(a, b); if (len < 0.01f) continue;
                for (float s = phase; s < len; s += dotSpacing)
                {
                    var p = Vector3.Lerp(a, b, s / len); p.y = game.GroundY(p.x, p.z) + 0.12f;
                    Draw(dot, p, 0.14f, m);
                }
            }
        }

        /// <summary>Four corner brackets on the ground around the building's Zod footprint.</summary>
        void DrawBrackets(ZObject b, float t)
        {
            var a = ZGame.PxToWorld(b.LocX, b.LocY); var c = ZGame.PxToWorld(b.LocX + b.WidthPix, b.LocY + b.HeightPix);
            float x0 = Mathf.Min(a.x, c.x), x1 = Mathf.Max(a.x, c.x), z0 = Mathf.Min(a.z, c.z), z1 = Mathf.Max(a.z, c.z);
            float grow = 0.35f + 0.12f * Mathf.Sin(t * 5f); x0 -= grow; x1 += grow; z0 -= grow; z1 += grow;
            float arm = Mathf.Min(x1 - x0, z1 - z0) * 0.22f;
            foreach (var (x, z, sx, sz) in new[] { (x0, z0, 1f, 1f), (x1, z0, -1f, 1f), (x1, z1, -1f, -1f), (x0, z1, 1f, -1f) })
            {
                float y = game.GroundY(x, z) + 0.15f;
                Graphics.DrawMesh(bar, Matrix4x4.TRS(new Vector3(x + sx * arm * 0.5f, y, z), Quaternion.identity, new Vector3(arm, 1f, 0.2f)), selMat, 0);
                Graphics.DrawMesh(bar, Matrix4x4.TRS(new Vector3(x, y, z + sz * arm * 0.5f), Quaternion.identity, new Vector3(0.2f, 1f, arm)), selMat, 0);
            }
        }

        void DrawFlag(Vector3 ground, Team team, float scale, bool big)
        {
            if (scale <= 0.01f) return;
            var m = Matrix4x4.TRS(ground, Quaternion.identity, Vector3.one * scale);
            Graphics.DrawMesh(pole, m, poleMat, 0);
            var c = TeamColors.Of(team);
            mpb.Clear(); mpb.SetColor("_BaseColor", c);
            if (bannerMat.HasProperty("_EmissionColor")) { var e = (c.linear * 0.35f).gamma; e.a = 1f; mpb.SetColor("_EmissionColor", e); }
            Graphics.DrawMesh(banner, m, bannerMat, 0, null, 0, mpb);
            Draw(ring, ground + Vector3.up * 0.07f, (big ? 0.85f : 0.5f) * Mathf.Max(0.6f, scale), RingMat(team));
        }

        static void Draw(Mesh m, Vector3 pos, float scale, Material mat) =>
            Graphics.DrawMesh(m, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale), mat, 0);

        static float EaseOutBack(float k) { const float c1 = 1.70158f, c3 = c1 + 1f; float x = k - 1f; return 1f + c3 * x * x * x + c1 * x * x; }

        void AnimateBanner(float t)
        {
            if (banner == null || bannerRest == null) return;
            for (int i = 0; i < bannerRest.Length; i++)
            {
                var r = bannerRest[i]; float u = Mathf.Clamp01((r.x - 0.04f) / 0.96f);
                bannerNow[i] = new Vector3(r.x, r.y + Mathf.Sin(t * 4f - u * 3f) * 0.03f * u, Mathf.Sin(t * 5f - u * 3.2f) * 0.1f * u);
            }
            banner.vertices = bannerNow; banner.RecalculateNormals(); banner.RecalculateBounds();
        }

        // ------------------------------------------------------------------ meshes
        static Mesh Disc(float rIn, float rOut, int seg)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var col = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * rIn); v.Add(d * rOut); col.Add(Color.white); col.Add(Color.white);
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            var m = new Mesh { name = "RallyDisc" }; m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        /// <summary>Unit flat bar (1 x 1 in XZ, centred) for the brackets.</summary>
        static Mesh Bar()
        {
            var m = new Mesh { name = "RallyBar" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds(); return m;
        }

        /// <summary>Flag pole (2.2 m, 7 cm square) with a knob, pivot at its foot.</summary>
        static Mesh Pole()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Box(Vector3 c, Vector3 s)
            {
                var h = s * 0.5f; int k = v.Count;
                for (int i = 0; i < 8; i++) v.Add(c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
                int[] f = { 0,2,3, 0,3,1, 4,5,7, 4,7,6, 0,1,5, 0,5,4, 2,6,7, 2,7,3, 0,4,6, 0,6,2, 1,3,7, 1,7,5 };
                foreach (var i in f) t.Add(k + i);
            }
            Box(new Vector3(0, 1.1f, 0), new Vector3(0.07f, 2.2f, 0.07f));
            Box(new Vector3(0, 2.24f, 0), new Vector3(0.14f, 0.1f, 0.14f));
            var m = new Mesh { name = "RallyPole" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Banner 0.96 x 0.6 m on the pole top, 7 x 3 vertex grid (waved in AnimateBanner).</summary>
        static Mesh Banner(out Vector3[] rest)
        {
            const int NX = 7, NY = 3;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j < NY; j++) for (int i = 0; i < NX; i++) v.Add(new Vector3(0.04f + 0.96f * i / (NX - 1), 1.5f + 0.6f * j / (NY - 1), 0f));
            for (int j = 0; j < NY - 1; j++) for (int i = 0; i < NX - 1; i++)
                {
                    int a = j * NX + i, b = a + 1, c = a + NX, d = c + 1;
                    t.AddRange(new[] { a, c, d, a, d, b });
                }
            rest = v.ToArray();
            var m = new Mesh { name = "RallyBanner" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); m.MarkDynamic();
            return m;
        }

        void OnDestroy()
        {
            foreach (var mm in new[] { dotMat, pendingMat, selMat, pulseMat, poleMat, bannerMat }) if (mm) Destroy(mm);
            foreach (var mm in ringMats.Values) if (mm) Destroy(mm);
            if (I == this) I = null;
        }
    }
}
