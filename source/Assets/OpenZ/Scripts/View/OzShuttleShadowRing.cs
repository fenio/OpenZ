using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's selection ring (rule DV118, view only; drawn by SelectionOverlay for a selected shuttle): a ring like every
    /// unit's, made to fit the ship.
    /// <list type="bullet">
    /// <item>SIZE: its outer edge (the glow's) at <see cref="OzShuttleShadow.RingM"/> = 3.0 m x the units' own pulse (+-3.5 %), inside the
    /// ship's selection frame (the twin from the ship's own model at the 70 m camera, its frame matched to the measured round-2
    /// picture: ring / frame width 0.78 .. 0.84, at most 0.87 at the pulse peak, every heading; the light tank's is 0.92 .. 0.93); the old
    /// ring was 4.2 m and 1.25 .. 1.33 x the frame.</item>
    /// <item>BAND: the light tank's ring in metres (SelectionOverlay.GlowRing at 1.9 m: the glow from 0.40 m inside the edge, the bright
    /// line 0.19 .. 0.12 m inside it, the outer glow to the edge), so its line has the tank's pixel width; the old ring scaled the band with
    /// its radius (2.2 x the tank's).</item>
    /// <item>WHERE: centred on the hull's middle (the ship's root: its plan outline is symmetric about it), every point laid on the drawn
    /// surface under it (SelectionOverlay.Seat: the rendered ground, the crater dents, the water) 8 cm up, so no part of it sinks into a
    /// slope (the lopsided crescents of round 2: a flat ring 25 cm over the rules' ground). From the RTS camera its far side lies behind
    /// the 5 m hull, as any ground ring under a tall unit; the part beside the hull is the same on both sides.</item>
    /// <item>WHEN: only while it stands: none on a repair station's deck (OzPlayableShuttle.DeckStation >= 0), none while a crash landing
    /// is drawn (OzShuttleTouchdown.Active, until 0.4 s after it stands level, nor on its way down to one), then it fades in over 0.3 s;
    /// with the pads' height (0.6 .. 0.05 m) it fades out at a take-off and in at a normal landing while the soft shadow does the opposite
    /// (View/OzShuttleShadow.cs, 0.1 .. 0.8 m): never a frame without a mark.
    /// Selected while it stands: at once, as every unit's ring.</item>
    /// </list>
    /// </summary>
    public static class OzShuttleShadowRing
    {
        /// <summary>The band: metres in from the outer edge and the alpha there (SelectionOverlay.GlowRing at the light tank's 1.9 m).</summary>
        public static readonly float[] BandM = { (1.05f - 0.84f) * 1.9f, (1.05f - 0.95f) * 1.9f, (1.05f - 0.985f) * 1.9f, 0f };
        static readonly float[] BandA = { 0f, 0.55f, 1f, 0f };
        /// <summary>Fade in once it stands level, fade out, the pads' height over which it goes at a take-off, its lift over the surface (m).</summary>
        public static float FadeInSec = 0.3f, FadeOutSec = 0.15f, LiftFromM = 0.05f, LiftToM = 0.6f, LiftM = 0.08f;
        public const int Seg = 72;
        /// <summary>Proof: rings drawn; frames it was left out on a deck / in a crash landing; draws that happened on a deck / in a crash
        /// landing (0); the last drawn ring's alpha, outer radius (m), centre and ship id.</summary>
        public static int Draws, DeckHidden, TouchHidden, DrawnOnDeck, DrawnInTouch;
        public static float LastAlpha, LastOuterM; public static Vector3 LastCentre; public static int LastId = -1;
        /// <summary>Proof: draw no shuttle ring this frame (a picture with and without it).</summary>
        public static bool ProofHide;

        sealed class St
        {
            public Mesh mesh; public Vector3[] v; public Color[] c; public float[] y; public float fade, a; public int frame = -10;
            public Vector3 at = new Vector3(1e9f, 0f, 0f); public float atR = -1f, atT = -9f;
        }
        static readonly Dictionary<int, St> st = new Dictionary<int, St>(); static ZGame stGame;
        static Vector2[] dirs;

        /// <summary>The ring's alpha drawn for this shuttle in the last frame it was selected (0 = none).</summary>
        public static float AlphaOf(int id) => st.TryGetValue(id, out var s) && s.frame >= Time.frameCount - 1 ? s.a : 0f;

        /// <summary>SelectionOverlay's LateUpdate, for a selected shuttle: pulse = the units' ring pulse (1 +- 0.035); mat = the
        /// selection rings' material (OpenZ/Overlay: vertex colour x its glowing green, depth-tested, no depth write).</summary>
        public static void Draw(ZGame g, ZObject o, float pulse, Material mat)
        {
            if (g == null || o == null || !mat) return;
            if (g != stGame) { foreach (var x in st.Values) if (x.mesh) Object.Destroy(x.mesh); st.Clear(); stGame = g; }
            var ship = OzPlayableShuttle.Of(o);
            Vector3 c;
            if (ship) c = ship.transform.position;
            else if (g.Views.TryGetValue(o.Id, out var view) && view) c = view.transform.position;
            else c = g.CenterWorld(o);
            if (!st.TryGetValue(o.Id, out var s)) { s = new St(); st[o.Id] = s; }
            float dt = Mathf.Min(OzRingClock.Dt, 0.05f);                                                      // K403: the rings' frame clock (View/OzRingClock.cs)
            bool touch = ship && ship.Touch != null && ship.Touch.Active, deck = ship && ship.DeckStation >= 0, broken = ship && ship.BrokenUp;
            bool rough = o.ShuttleRoughLanding || (ship && ship.RoughArmed);
            bool standing = !broken && !touch && !deck && (o.ShuttleState == ZShuttleState.Landed || o.ShuttleState == ZShuttleState.Climb
                            || (o.ShuttleState == ZShuttleState.Descend && !rough));                         // a normal landing: it grows in as the pads come down
            float target = standing ? 1f : 0f;
            if (s.frame < Time.frameCount - 2) s.fade = target;                                            // just selected: at once, as every unit's ring
            else s.fade = Mathf.MoveTowards(s.fade, target, dt / (target > s.fade ? FadeInSec : FadeOutSec));
            s.frame = Time.frameCount;
            float feet = ship ? ship.FeetHeight : (o.ShuttleAirborne ? 9f : 0f);
            float a = s.fade * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LiftFromM, LiftToM, feet)));
            s.a = a;
            if (deck) DeckHidden++; else if (touch) TouchHidden++;
            if (a <= 0.004f) { if (o.ShuttleAirborne) OzShuttleShadow.RingSkips++; s.a = 0f; return; }   // in the air its shadow is the mark
            if (deck) DrawnOnDeck++;
            if (touch) DrawnInTouch++;
            if (ProofHide) return;
            float ro = OzShuttleShadow.RingM * pulse;
            if (dirs == null)
            {
                dirs = new Vector2[Seg + 1];
                for (int i = 0; i <= Seg; i++) { float an = i * Mathf.PI * 2f / Seg; dirs[i] = new Vector2(Mathf.Cos(an), Mathf.Sin(an)); }
            }
            int nv = (Seg + 1) * 4;
            if (!s.mesh)
            {
                s.mesh = new Mesh { name = "OZ_ShuttleRing" }; s.mesh.MarkDynamic();
                s.v = new Vector3[nv]; s.c = new Color[nv]; s.y = new float[nv];
                var tri = new int[Seg * 3 * 6]; int n = 0;
                for (int i = 0; i < Seg; i++) for (int k = 0; k < 3; k++) { int q = i * 4 + k; tri[n++] = q; tri[n++] = q + 1; tri[n++] = q + 5; tri[n++] = q; tri[n++] = q + 5; tri[n++] = q + 4; }
                s.mesh.vertices = s.v; s.mesh.colors = s.c; s.mesh.triangles = tri;
            }
            // the heights of the drawn surface under every point (again when it moved 5 cm, the pulse changed the radius 5 cm, or twice a second)
            if ((new Vector2(c.x - s.at.x, c.z - s.at.z)).sqrMagnitude > 0.0025f || Mathf.Abs(ro - s.atR) > 0.05f || Time.unscaledTime - s.atT > 0.5f)
            {
                s.at = c; s.atR = ro; s.atT = Time.unscaledTime;
                for (int i = 0; i <= Seg; i++)
                    for (int k = 0; k < 4; k++)
                    {
                        float r = ro - BandM[k]; var d = dirs[i];
                        s.y[i * 4 + k] = SelectionOverlay.Seat(g, new Vector3(c.x + d.x * r, 0f, c.z + d.y * r), LiftM, false).y;
                    }
            }
            for (int i = 0; i <= Seg; i++)
                for (int k = 0; k < 4; k++)
                {
                    float r = ro - BandM[k]; var d = dirs[i]; int q = i * 4 + k;
                    s.v[q] = new Vector3(c.x + d.x * r, s.y[q], c.z + d.y * r); s.c[q] = new Color(1f, 1f, 1f, BandA[k] * a);
                }
            s.mesh.vertices = s.v; s.mesh.colors = s.c; s.mesh.RecalculateBounds();
            Graphics.DrawMesh(s.mesh, Matrix4x4.identity, mat, 0);
            Draws++; LastAlpha = a; LastOuterM = ro; LastCentre = c; LastId = o.Id;
        }
    }
}
