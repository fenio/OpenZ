using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K431 (item 430): the selection rings, the frame and the health bar of a robot that is leaving a carrier follow its DRAWN figure.
    /// While a robot gets out (the shuttle's and the APC's bench, aisle, doorway and ramp; a tank's hatch; a gun's seat; a cab door) the
    /// rules already stand it on its spot beside the carrier, and its own model is spawned there but hidden (<see cref="HideLive"/>) while
    /// an exit figure walks out to it. SelectionOverlay (the rings) and OpenZUnitOverlay (the frame, the health bars, the mouse-over) read
    /// the place of that hidden model, so the rings and the frame stood on the empty spot before anyone had come out.
    /// Now, for every overlay that marks a robot (<see cref="FigureAt"/>):
    ///  * its figure is still inside the hull (waiting on a bench, standing up, in the aisle, in the doorway: up to the moment it steps
    ///    onto the ramp's sill, the walk's own key; or not drawn yet, a hatch / seat figure waiting its turn): no ring, frame or bar;
    ///  * from then on they stand at the figure and walk with it, down the ramp and on to the spot, where the robot's own model takes over
    ///    when the walk ends (the same place: the figure's last key is the model's position);
    ///  * a figure saved from a destroyed shuttle that walks on to its robot (<see cref="DoorWalk"/>): the same, at that figure.
    /// Every other robot is marked where its model is drawn, as before. Boarding needs nothing: a robot that has gone in is marked by no
    /// overlay (KillAt), its walk up the ramp is its own model until then. View only: nothing of the rules or the sounds changes.
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>On: the overlays of a robot that is getting out follow its figure (off: on its spot from the start, as before).</summary>
        public static bool ExitRingsFollowOn = true;
        /// <summary>Proof: overlay asks answered "none yet, still inside" / "at its figure" (the proof's own asks are not counted).</summary>
        public static int ExitRingsHeld, ExitRingsOnFigure;

        readonly Dictionary<int, Ghost> exitFigureOf = new Dictionary<int, Ghost>(); int exitFigureFrame = -1;
        readonly Dictionary<int, GameObject> doorWalkerOf = new Dictionary<int, GameObject>();

        /// <summary>The exit walk drawn for that robot now (null = none); the list is gathered once a frame.</summary>
        Ghost ExitFigureOf(int robotId)
        {
            if (exitFigureFrame != Time.frameCount)
            {
                exitFigureFrame = Time.frameCount; exitFigureOf.Clear();
                foreach (var x in ghosts) if (!x.enter && !x.demo && !x.done && x.live && x.robotId >= 0) exitFigureOf[x.robotId] = x;
            }
            return exitFigureOf.TryGetValue(robotId, out var g) && !g.done ? g : null;
        }

        /// <summary>ShuttleDoorway hook: a saved figure walks on to its robot (its robot's model hidden until it arrives).</summary>
        void ExitRingsWalker(int robotId, GameObject go)
        {
            if (go) doorWalkerOf[robotId] = go;
            if (doorWalkerOf.Count > 32) { var gone = new List<int>(); foreach (var kv in doorWalkerOf) if (!kv.Value) gone.Add(kv.Key); foreach (var id in gone) doorWalkerOf.Remove(id); }
        }

        /// <summary>0 = no exit figure (marked where its model is: p as given), 1 = its figure is still inside the carrier (not marked),
        /// 2 = marked at its figure (p = the figure's feet).</summary>
        int FigureState(ZObject o, ref Vector3 p)
        {
            if (doorWalkerOf.Count > 0 && doorWalkerOf.TryGetValue(o.Id, out var w))
            {
                if (w) { p = w.transform.position; return 2; }
                doorWalkerOf.Remove(o.Id);
            }
            var g = ExitFigureOf(o.Id);
            if (g == null || !g.go) return 0;                                        // no walk (or it was just dropped): its model shows from this frame
            if (!g.go.activeInHierarchy) return 1;                                  // a hatch / seat / door figure waiting its turn inside
            if (g.seatK >= 0 && g.time < g.scaleT0) return 1;                       // shuttle / APC: on the bench, in the aisle or the doorway (scaleT0 = the moment at the ramp's sill, BuildShuttleExit / BuildApcExit)
            p = g.go.transform.position; return 2;
        }

        /// <summary>For the overlays (rings, frames, health bars, the mouse-over box): false = do not mark this robot this frame (it is still
        /// inside its carrier); true = mark it at p (its exit figure while it walks out, else p unchanged: where its model is drawn).</summary>
        public static bool FigureAt(ZObject o, ref Vector3 p)
        {
            var bv = I; if (!ExitRingsFollowOn || o == null || !bv) return true;
            int s = bv.FigureState(o, ref p);
            if (s == 1) { ExitRingsHeld++; return false; }
            if (s == 2) ExitRingsOnFigure++;
            return true;
        }

        /// <summary>Is the robot marked at all this frame (see <see cref="FigureAt"/>)?</summary>
        public static bool FigureShown(ZObject o) { var p = Vector3.zero; return FigureAt(o, ref p); }

        /// <summary>Proof (not counted): the state of that robot as <see cref="FigureAt"/> sees it, the figure's place for state 2.</summary>
        public static int ExitFigureState(ZObject o, out Vector3 figure)
        {
            figure = Vector3.zero; var bv = I;
            if (!ExitRingsFollowOn || o == null || !bv) return 0;
            return bv.FigureState(o, ref figure);
        }

        /// <summary>Proof: the robots that have an exit walk drawn now.</summary>
        public void ExitFigureIds(List<int> into)
        {
            into.Clear();
            foreach (var x in ghosts) if (!x.enter && !x.demo && !x.done && x.live && x.robotId >= 0 && x.go) into.Add(x.robotId);
            foreach (var kv in doorWalkerOf) if (kv.Value && !into.Contains(kv.Key)) into.Add(kv.Key);
        }
    }
}
