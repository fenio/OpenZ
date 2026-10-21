#if UNITY_EDITOR
using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// Level 16 crocodiles: the scenario catch of the editor's epic flybys (View/EpicFlyby/OzEpicFlyby.Island.cs, step croc_catch).
    /// Editor only and never called in normal play: the rule of ZWorld.Crocodiles.cs (DV91 to DV94), its numbers, its timing and its
    /// own hunts are untouched; nothing here runs unless a scenario asks for it.
    /// A scenario catch is the rule's own catch with the rule's own start skipped: the animal goes under (the rule's dive, when it is
    /// up), comes up beside the named robot (the rule's "moved under" note: the view takes it there without a slide, as for a hunt
    /// whose animal is placed under water) and RISES at once; from there the rule runs as for any hunt: the glide to the robot, the
    /// lunge, the bite 0.47 s in (the robot within the jaws' reach is taken: Eaten, health 0, the death event, nobody gets the kill),
    /// the roll or the fast dive of the view, the drag under and the animal back at its own place later.
    /// Where it comes up: one of 16 sides CrocRobotAheadM from the robot with the animal's turning room (CrocTurnM) in the robot's
    /// pool, the whole attack box on water when such a side exists.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>Scenario catches started (checks).</summary>
        public int CrocScenarioCatches { get; private set; }

        /// <summary>A rise, lunge, roll or drag: the animal is in a catch and cannot be given another.</summary>
        public static bool CrocInCatch(ZCroc c) =>
            c != null && (c.State == ZCrocState.Rise || c.State == ZCrocState.Lunge || c.State == ZCrocState.Thrash || c.State == ZCrocState.Drag);

        /// <summary>The animal goes under now by the rule's own dive (1.2 s, then under), ending a hunt of its own first. True when
        /// it dives or is under / diving already.</summary>
        public bool CrocScenarioDive(ZCroc c)
        {
            if (c == null || crocFree == null || CrocInCatch(c)) return false;
            if (c.State == ZCrocState.Dive || c.State == ZCrocState.Under) return true;
            if (c.PreyId >= 0) CrocEndHunt(c, 0);
            CrocSet(c, ZCrocState.Dive, CrocDiveSeconds);
            return true;
        }

        /// <summary>Where an animal can come up for a catch of this robot (map px of its middle). Of the 16 sides CrocRobotAheadM from
        /// the robot with CrocTurnM free water in the robot's pool, the lowest score: +2 when the attack box does not fit there,
        /// + |cos| between the side and (viewX, viewY) (the camera's ground direction in map axes: 0 = the animal seen side on;
        /// both 0 = no preference). False = no side has room (the robot is too near a bank).</summary>
        public bool CrocScenarioSide(ZObject prey, double viewX, double viewY, out double x, out double y, out bool boxFits)
        {
            x = y = 0; boxFits = false;
            if (crocFree == null || prey == null) return false;
            int pool = CrocPoolAt(prey.CenterX, prey.CenterY); if (pool < 0) return false;
            double vl = Math.Sqrt(viewX * viewX + viewY * viewY), best = double.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8, dx = Math.Cos(a), dy = Math.Sin(a);                       // the animal faces (dx, dy): it lies behind the robot
                double cx = prey.CenterX - dx * CrocRobotAheadM * CrocPx, cy = prey.CenterY - dy * CrocRobotAheadM * CrocPx;
                if (CrocFreeAt(cx, cy) < CrocTurnM || CrocPoolAt((int)cx, (int)cy) != pool) continue;
                bool fits = CrocBoxFits(cx, cy, a);
                double s = (fits ? 0 : 2) + (vl > 1e-9 ? Math.Abs((dx * viewX + dy * viewY) / vl) : 0) + k * 1e-6;   // ties: the lower side
                if (s < best) { best = s; x = cx; y = cy; boxFits = fits; }
            }
            return best < double.MaxValue;
        }

        /// <summary>The catch: the animal comes up at (x, y) (map px), faces the robot and rises (the rule's rise, lunge and bite take it
        /// from here). The robot must be a living robot on a water tile; the animal not in a catch; (x, y) its turning room in the
        /// robot's pool. A hunt of the animal's own ends first. The pool's hunt slot is taken when free (the robots near it hurry as
        /// in any hunt; the rule frees it after the drag), else the catch runs without it. Null = started, else why not.</summary>
        public string CrocScenarioCatch(ZCroc c, ZObject prey, double x, double y)
        {
            if (c == null || crocFree == null) return "no crocodile on this map";
            if (!CrocPreyOk(prey)) return "the robot is not a living robot in the water";
            if (CrocInCatch(c)) return "crocodile " + c.Index + " is in a catch (" + c.State + ")";
            int pool = CrocPoolAt(prey.CenterX, prey.CenterY);
            if (pool < 0 || c.Pool != pool) return "crocodile " + c.Index + " is not in the robot's water";
            if (CrocFreeAt(x, y) < CrocTurnM || CrocPoolAt((int)x, (int)y) != pool) return "no room for the crocodile at that place";
            if (c.PreyId >= 0) CrocEndHunt(c, 0);
            if ((c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y) > 0.25 * CrocPx * CrocPx)
            {
                CrocNote(ZCrocNote.Moved, c, prey.Id);                                                  // where it went under: no slide in the view
                CrocMoved++;
            }
            c.X = x; c.Y = y; c.Heading = Math.Atan2(prey.CenterY - y, prey.CenterX - x); c.Speed = 0;
            c.PreyId = prey.Id; c.Took = false; c.BiteAt = -1; c.slid = 0; c.aimNext = 0; c.aimSide = -1;
            if (c.Pool >= 0 && c.Pool < crocPools && crocPoolHunt[c.Pool] < 0) { crocPoolHunt[c.Pool] = c.Index; CrocHuntsActive++; }
            CrocHunts++; CrocScenarioCatches++;
            CrocSet(c, ZCrocState.Rise, CrocRiseSeconds);
            return null;
        }
    }
}
#endif
