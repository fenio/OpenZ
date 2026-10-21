using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256f (rule DV118, view only): an EMPTY shuttle (no pilot, the null team: Sim/ZWorld.ShuttleEmpty.cs) looks like the game's other
    /// empty vehicles: dark. Its lamps, neon strips and engine glow (OzShuttleLights) and its exhaust wisp (OzShuttleExhaust) are off
    /// (mode 2), its night lights have no pilot to work them (OzShuttleNightLights asks <see cref="Dark"/>), its paint is the empty grey
    /// (TeamPaint through UnitView.SetOwner, as every vehicle). The moment the squad that took it sits inside (CaptureHold: the capture is
    /// shown when the boarding figures are in), the lights come on in that team's neon colours.
    /// Fitted to every shuttle the testing panel drops (UI/OzCheatShuttle.cs) and to every map-placed one, whose view it also makes
    /// (<see cref="SpawnMapShuttles"/>, called by ZGame right after level 16's own ship is bound).
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class OzShuttleEmptyLook : MonoBehaviour
    {
        /// <summary>Proof counters: looks fitted, switches to dark and to lit, views made for map-placed shuttles.</summary>
        public static int Made, Darkened, Lit, MapViews;
        OzPlayableShuttle ship; OzShuttleLights lights; OzShuttleExhaust exhaust; int dark = -1; Team shown = (Team)(-1);

        /// <summary>The ship is drawn dark now (no pilot, or its capture is still being shown).</summary>
        public bool IsDark => dark == 1;
        public OzShuttleLights Lights => lights;
        public OzShuttleExhaust Exhaust => exhaust;

        /// <summary>The ship has no pilot to light it: empty, or the squad that took it is still walking in (the capture is not shown yet).</summary>
        public static bool Dark(ZObject o) => o != null && o.IsShuttle && !o.Destroyed && (o.Drivers.Count == 0 || CaptureHold.Shown(o) == Team.Neutral);

        public static OzShuttleEmptyLook Ensure(OzPlayableShuttle s)
        {
            if (!s) return null;
            var l = s.GetComponent<OzShuttleEmptyLook>();
            if (l) return l;
            l = s.gameObject.AddComponent<OzShuttleEmptyLook>();
            l.ship = s; l.lights = s.GetComponent<OzShuttleLights>(); l.exhaust = s.GetComponent<OzShuttleExhaust>();
            Made++; l.Step();
            return l;
        }

        /// <summary>ZGame, at the level start: every shuttle of the rules without a view (a map's entry) gets the shuttle's full view; every
        /// drawn one gets this look.</summary>
        public static void SpawnMapShuttles(ZGame g)
        {
            if (!g || g.World == null) return;
            foreach (var o in g.World.Objects)
            {
                if (o == null || !o.IsShuttle || o.Destroyed) continue;
                var v = OzPlayableShuttle.Of(o);
                if (!v && !g.Views.ContainsKey(o.Id)) { v = OzPlayableShuttle.SpawnView(g, o); if (v) MapViews++; }
                if (v) Ensure(v);
            }
        }

        void LateUpdate() => Step();

        void Step()
        {
            var o = ship ? ship.Obj : null;
            if (o == null || o.Destroyed || ship.BrokenUp) return;                          // a destroyed ship is darkened by its own death
            bool d = Dark(o);
            if ((d ? 1 : 0) != dark)
            {
                if (d && dark == 0) OzShuttleHeroesView.PowerDown(o, transform.position);      // K256f2: lit to dark, the crew has left (a hero pilot got out with everyone): the engine dies (View/OzShuttleHeroesView.cs)
                int was = dark; dark = d ? 1 : 0;
                if (OzShuttleCaptureFx.On) OzShuttleCaptureFx.Switch(this, o, d, was);                 // K389: the capture flash, the lamps' start, the paint's ease; the fade when the crew leaves (View/OzShuttleCaptureFx.cs)
                else if (lights) lights.SetMode(d ? 2 : 0);
                if (exhaust) exhaust.SetMode(d ? 2 : 0);
                if (d) Darkened++; else Lit++;
            }
            var t = CaptureHold.Shown(o);
            if (t != shown) { shown = t; if (lights && t != Team.Neutral) lights.SetTeam(TeamColors.Shown(t)); }
        }
    }
}
