using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// Factory rally points, Zod style. The Sim already stores and applies them (ZWorld.SetRallyPoints keeps MOVE waypoints
    /// only, zcore.cpp:189-194; RulesSideEffects appends them after the new unit's FORCE_MOVE out of the door,
    /// zserver.cpp:1240-1256); this component is the player side:
    ///  - Zod's own gesture (zplayer.cpp:2866-2878, zplayer_events.cpp:390-429, 596-603): with a production window open and
    ///    NOTHING selected, right-click the map or the minimap = rally point (MOVE_WP, attack_to = true). Hold Shift to chain
    ///    several; releasing Shift sends the chain. Each send replaces the factory's whole rally list.
    ///  - The production window's SET RALLY button arms a rally tool: the next left click on the map or minimap sets the
    ///    point even while units stay selected (Shift+clicks chain, Esc cancels); CLEAR removes the rally list.
    ///  - Display like Zod (zplayer.cpp:2143-2145, zobject.cpp:1135-1206: drawn while the window is open, from the unit
    ///    creation point inside the building to the exit, then through every rally point): marching dots, a team flag on
    ///    each point, a pulsing ring on the last one, a ghost flag under the pointer while the tool is armed. UI kit rule
    ///    (UI_STYLE_GUIDE.md 1.1 / 9.9): world marks are INK white with a dark keyline, never amber (the L02 sand is amber);
    ///    the RALLY chip on the last point is drawn by OpenZUnitOverlay.
    /// <c></c>
    ///    ("OpenZ.OpenZRallyPoints") and stands by while it exists, so rally input and drawing happen once. Keep the name.
    /// </summary>
    [DefaultExecutionOrder(11)]
    public class OpenZRallyPoints : MonoBehaviour
    {
        [Tooltip("Also draw the flags of every own factory's rally route in the 3D view (Zod: only the open window's)")] public bool showAllInWorld;

        public bool Armed { get; private set; }
        public ZObject Building
        {
            get
            {
                if (Armed && armedFor != null && !armedFor.Destroyed && hud && armedFor.Owner == hud.Player) return armedFor;
                return hud && hud.Production ? hud.Production.Building : null;
            }
        }
        ZObject armedFor;

        OpenZHud hud; ZGame game; ZWorld W => game ? game.World : null;
        readonly List<ZWaypoint> pending = new List<ZWaypoint>(); ZObject pendingFor;
        Material dotMat, ringMat, poleMat, flagMat, ghostMat, keyMat;
        Mesh dot, ring, pole, pennant;
        float pulseAt = -10f; Vector3 pulsePos;

        public IReadOnlyList<ZWaypoint> PendingFor(ZObject b) => b != null && b == pendingFor && pending.Count > 0 ? pending : null;

        public void Build(OpenZHud h)
        {
            hud = h; game = h.game;
            var so = FindFirstObjectByType<SelectionOverlay>();
            var shader = so && so.overlayMaterial ? so.overlayMaterial.shader : Shader.Find("OpenZ/Overlay");   // the scene's overlay material keeps the shader in builds
            if (!shader) shader = Shader.Find("Sprites/Default");
            Material M(Color c, float zTest) { var m = new Material(shader) { name = "RallyOverlay" }; if (m.HasProperty("_Color")) m.SetColor("_Color", c); else m.color = c; if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", zTest); return m; }
            dotMat = M(HudStyle.WithA(OzUi.Ink, 0.9f), 8);                                               // INK in world space (guide 1.1 rule 2)
            ringMat = M(OzUi.Ink, 8);
            ghostMat = M(HudStyle.WithA(OzUi.Ink, 0.45f), 8);
            keyMat = M(new Color(0.03f, 0.035f, 0.04f, 0.7f), 8);                                        // the dark keyline under every INK mark
            poleMat = M(HudStyle.Ink, 4);
            flagMat = M(HudStyle.TeamUi(h.Player), 4);
            dot = Disc(0f, 1f, 14); ring = Disc(0.8f, 1f, 40);
            pole = Quad(0.09f, 2.6f);
            pennant = new Mesh { name = "RallyPennant" }; pennant.MarkDynamic();
        }

        public void Arm()
        {
            if (Building == null) return;
            armedFor = Building;
            Armed = true; AudioDirector.UI("ui_rally_arm");                                         // sound_v3
            hud.Toast("RALLY: CLICK THE MAP · SHIFT = SEVERAL POINTS", HudStyle.Amber, 3f);
        }
        public void Disarm() { Armed = false; armedFor = null; }

        /// <summary>One rally point for the open factory (map px). chain = Shift held: keep collecting until Shift is released.</summary>
        public void AddPoint(int px, int py, bool chain)
        {
            var b = Building; if (b == null || W == null) return;
            if (pendingFor != b) { pending.Clear(); pendingFor = b; }
            px = Mathf.Clamp(px, 0, W.Map.WidthPix - 1); py = Mathf.Clamp(py, 0, W.Map.HeightPix - 1);
            pending.Add(new ZWaypoint { mode = WpMode.Move, refId = -1, x = px, y = py, attackTo = true, playerGiven = true });   // zplayer.cpp:2869-2875
            if (!chain) Send(); else AudioDirector.UI("ui_rally_point", 1f, 1f + 0.06f * Mathf.Min(6, pending.Count - 1));   // sound_v3
        }

        /// <summary>SEND_RALLYPOINTS (zserver_events.cpp:423-448): replaces the building's rally list.</summary>
        public void Send()
        {
            var b = pendingFor;
            if (b != null && pending.Count > 0 && W.SetRallyPoints(hud.Player, b, pending))
            {
                OzFactoryReminder.RallySet(b);   // factory reminder (k247, UI/OzFactoryReminder.cs): he set this factory's rally point himself
                var last = pending[pending.Count - 1];
                pulsePos = Ground(last.x, last.y); pulseAt = Time.unscaledTime;
                hud.Toast(pending.Count == 1 ? "RALLY POINT SET" : $"RALLY ROUTE SET · {pending.Count} POINTS", HudStyle.Amber);
                var au = CombatAudio.I; if (au) au.Play2D("v3-capture-raise", 0.35f);
                Debug.Log($"[OpenZ] rally {b.Type}#{b.Id}: {pending.Count} point(s), last ({last.x},{last.y})");
            }
            pending.Clear(); pendingFor = null;
            Armed = false;
        }

        public void Clear(ZObject b)
        {
            if (b == null || W == null) return;
            if (W.SetRallyPoints(hud.Player, b, Array.Empty<ZWaypoint>()) && OzSoundV3.RallyCleared()) hud.Toast("RALLY CLEARED · UNITS WAIT AT THE DOOR", HudStyle.Muted);
            if (b.Owner == hud.Player) OzFactoryReminder.RallySet(b);   // factory reminder (k247, UI/OzFactoryReminder.cs): CLEAR is his rally choice too
            if (pendingFor == b) { pending.Clear(); pendingFor = null; }
        }

        Vector3 Ground(int px, int py) { var p = ZGame.PxToWorld(px, py); p.y = game.GroundY(p.x, p.z); return p; }

        bool PointerGround(Vector2 mp, out Vector2Int px)
        {
            px = default;
            var cam = hud.rts ? hud.rts.GetComponent<Camera>() : Camera.main; if (!cam) return false;
            if (!game.GroundPoint(cam.ScreenPointToRay(mp), out var g)) return false;
            px = ZGame.WorldToPx(g);
            px.x = Mathf.Clamp(px.x, 0, W.Map.WidthPix - 1); px.y = Mathf.Clamp(px.y, 0, W.Map.HeightPix - 1);
            return true;
        }

        void Update()
        {
            if (hud == null || W == null) return;
            var b = Building;
            if (b != null || showAllInWorld) UpdatePennant();
            if (b == null)
            {
                pending.Clear(); pendingFor = null; Armed = false; armedFor = null; if (showAllInWorld) DrawAll(null);
                float pz = (Time.unscaledTime - pulseAt) / 0.8f;                                          // bug 10: the "set" pulse after the menu closed
                if (pz >= 0 && pz < 1 && ringMat) DrawAt(ring, pulsePos + Vector3.up * 0.12f, Mathf.Lerp(0.6f, 4f, pz), ringMat);
                return;
            }
            if (pendingFor != null && pendingFor != b) { pending.Clear(); pendingFor = null; }
            var m = Mouse.current; var kb = Keyboard.current; if (m == null) return;
            Vector2 mp = hud.Pointer; bool shift = kb != null && kb.shiftKey.isPressed;
            bool overUi = hud.PointerOverUi;
            // Zod: right release on the map, production window open, nothing selected (tested before PlayerInput sent its orders)
            if (m.rightButton.wasReleasedThisFrame && !overUi && hud.SelectionCountBeforeInput == 0 && hud.input.Selection.Count == 0
                && hud.input.Placing == null && PointerGround(mp, out var rp))
                AddPoint(rp.x, rp.y, shift);
            // rally tool: this left press was captured by the HUD, PlayerInput never saw it
            if (Armed && m.leftButton.wasReleasedThisFrame && hud.PressCapture == OpenZHud.Capture.Rally && PointerGround(mp, out var lp))
                AddPoint(lp.x, lp.y, shift);
            // releasing the last Shift sends the chain (zplayer_events.cpp:596-603)
            if (kb != null && kb.shiftKey.wasReleasedThisFrame && !kb.shiftKey.isPressed && pending.Count > 0) Send();
            if (Armed && kb != null && kb.escapeKey.wasPressedThisFrame) { Disarm(); pending.Clear(); pendingFor = null; AudioDirector.UI("ui_cancel"); }   // sound_v3

            Draw(b, !overUi && Armed ? mp : (Vector2?)null);
            if (showAllInWorld) DrawAll(b);
        }

        // ------------------------------------------------------------------ drawing
        readonly List<Vector3> route = new List<Vector3>();

        void DrawAll(ZObject except)
        {
            foreach (var o in W.Objects)
                if (o != except && o.IsProducer && o.Owner == hud.Player && o.RallyPoints.Count > 0)
                    foreach (var w in o.RallyPoints) Flag(Ground(w.x, w.y), 0.8f, poleMat, flagMat);
        }

        void Draw(ZObject b, Vector2? ghost)
        {
            if (!b.GetCreationMovePoint(out int ex, out int ey)) return;
            var exit = Ground(ex, ey);
            var pend = PendingFor(b);
            var list = pend ?? (IReadOnlyList<ZWaypoint>)b.RallyPoints;
            float march = Time.unscaledTime * 2.4f % 1.2f;
            Vector3 tail = exit;
            DrawAt(ring, exit + Vector3.up * 0.07f, 0.95f, keyMat);
            DrawAt(ring, exit + Vector3.up * 0.08f, 0.9f, ghostMat);                                        // the door the units leave by
            if (list.Count > 0)
            {
                route.Clear();
                if (b.GetCreationPoint(out int cx, out int cy)) route.Add(Ground(cx, cy));                   // Zod draws from inside the building
                route.Add(exit);
                int first = route.Count;
                for (int wi = 0; wi < list.Count; wi++) { var w = list[wi]; route.Add(Ground(w.x, w.y)); }  // index loop: no boxed enumerator per frame
                var dm = pend != null ? ghostMat : dotMat; var fm = pend != null ? ghostMat : flagMat;      // unsent Shift chain = ghost style
                for (int i = 1; i < route.Count; i++) Dots(route[i - 1], route[i], march, dm);
                for (int i = first; i < route.Count; i++) Flag(route[i], i == route.Count - 1 ? 1f : 0.75f, poleMat, fm);
                tail = route[route.Count - 1];
                float k = Mathf.Repeat(Time.unscaledTime * 1.4f, 1f);
                DrawAt(ring, tail + Vector3.up * 0.09f, Mathf.Lerp(0.5f, 1.6f, k) * 1.06f, keyMat);
                DrawAt(ring, tail + Vector3.up * 0.1f, Mathf.Lerp(0.5f, 1.6f, k), ringMat);
            }
            float pa = (Time.unscaledTime - pulseAt) / 0.8f;                                             // "set" pulse
            if (pa >= 0 && pa < 1) DrawAt(ring, pulsePos + Vector3.up * 0.12f, Mathf.Lerp(0.6f, 4f, pa), ringMat);
            if (ghost.HasValue && PointerGround(ghost.Value, out var gp))
            {
                var g = Ground(gp.x, gp.y);
                Dots(tail, g, march, ghostMat);
                Flag(g, 0.9f, ghostMat, ghostMat);
            }
        }

        void Dots(Vector3 a, Vector3 b, float offset, Material mat)
        {
            float len = Vector3.Distance(a, b); if (len < 0.01f) return;
            for (float s = 0.6f + offset; s < len; s += 1.2f)
            {
                var p = Vector3.Lerp(a, b, s / len); p.y = game.GroundY(p.x, p.z) + 0.1f;
                DrawAt(dot, p - Vector3.up * 0.01f, 0.2f, keyMat);                                          // keyline: a dark disc just under the dot
                DrawAt(dot, p, 0.14f, mat);
            }
        }

        void Flag(Vector3 at, float scale, Material poleM, Material flagM)
        {
            var m = Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * scale);
            Graphics.DrawMesh(pole, m, poleM, 0);
            Graphics.DrawMesh(pennant, m, flagM, 0);
            DrawAt(dot, at + Vector3.up * 0.07f, 0.35f * scale, poleM);
        }

        static void DrawAt(Mesh mesh, Vector3 pos, float scale, Material mat) =>
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale), mat, 0);

        // ------------------------------------------------------------------ meshes
        static Mesh Disc(float rIn, float rOut, int seg)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var c = new List<Color>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * rIn); v.Add(d * rOut); c.Add(Color.white); c.Add(Color.white);
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            var m = new Mesh { name = "RallyDisc" }; m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        static Mesh Quad(float w, float h)
        {
            var m = new Mesh { name = "RallyPole" };
            m.SetVertices(new List<Vector3> { new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, h, 0), new Vector3(-w / 2, h, 0) });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds(); return m;
        }

        readonly List<Vector3> pv = new List<Vector3>(); readonly List<Color> pc = new List<Color>(); readonly List<int> pt = new List<int>();
        /// <summary>Pennant from the pole top, fluttering (one shared mesh per frame).</summary>
        void UpdatePennant()
        {
            const int N = 6; const float L = 1.15f, top = 2.55f, half = 0.3f;
            float time = Time.unscaledTime;
            pv.Clear(); pc.Clear(); pt.Clear();
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N, x = t * L, hh = half * (1 - t * 0.92f);
                float y = top - half + Mathf.Sin(time * 6f + t * 3.2f) * 0.07f * t;
                float z = Mathf.Sin(time * 4.3f + t * 4f) * 0.12f * t;
                pv.Add(new Vector3(x, y + hh, z)); pv.Add(new Vector3(x, y - hh, z));
                var c = Color.Lerp(Color.white, new Color(0.8f, 0.8f, 0.8f, 1f), t * 0.5f); pc.Add(c); pc.Add(c);
                if (i < N) { int k = i * 2; pt.Add(k); pt.Add(k + 1); pt.Add(k + 3); pt.Add(k); pt.Add(k + 3); pt.Add(k + 2); }   // no array per quad per frame
            }
            pennant.Clear(); pennant.SetVertices(pv); pennant.SetColors(pc); pennant.SetTriangles(pt, 0); pennant.RecalculateBounds();
        }

        void OnDestroy()
        {
            foreach (var m in new[] { dotMat, ringMat, poleMat, flagMat, ghostMat, keyMat }) if (m) Destroy(m);
            foreach (var m in new[] { dot, ring, pole, pennant }) if (m) Destroy(m);
        }
    }
}
