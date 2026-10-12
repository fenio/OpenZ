using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// born inside a building's footprint (factory / fort creation point) is drawn at <see cref="ExitStartScale"/> and grows smoothly to
    /// full size as its drawn centre leaves that footprint (by how deep inside it still is, eased, never shrinking again); safety: full
    /// size after <see cref="ExitMaxSeconds"/>. The sim, pick boxes and paths are unchanged.
    /// </summary>
    public partial class UnitView
    {
        public const float ExitStartScale = 0.87f, ExitMaxSeconds = 15f;
        /// <summary>The doorway fit (factory and fort doors): the air kept on each side of the hull in the door (m), the smallest scale the
        /// fit may ask for, and the run (m) past the door over which the hull grows back once its tail is through.</summary>
        public const float ExitDoorAirM = 0.08f, ExitMinScale = 0.62f, ExitGrowRunM = 2.5f;
        /// <summary>Check: the smallest doorway fit scale used so far and how many exits needed one below 1.</summary>
        public static float ExitFitMin = 1f; public static int ExitFitted;
        /// <summary>Proof: vehicles that started the grow / finished it.</summary>
        public static int ExitGrowStarted, ExitGrowDone;
        /// <summary>Check: vehicles whose hull was held on the door axis / released, and the largest turn (deg) the path steering asked
        /// for while a hull was held.</summary>
        public static int ExitStraightStarted, ExitStraightDone; public static float ExitStraightMaxOffDeg;
        /// <summary>Proof: the scale factor this view draws at now (1 = full).</summary>
        public float ExitScaleNow => exitGrowing ? exitCur : 1f;

        bool exitGrowing; float exitCur = 1f, exitDepth0, exitAge; Vector3 exitBase = Vector3.one; ZObject exitFrom;
        bool exitStraight; float exitYaw, exitStraightAge;
        bool exitDoor, exitLane; Vector3 exitDir, exitDrawn; float exitRear, exitDoorAlong, exitFit = ExitStartScale;
        /// <summary>Check: units the building push-out left alone while they were on their exit move.</summary>
        public static int ExitLaneSkips;

        /// <summary>True while this unit (vehicle or robot) is still on the exit move out of the building that made it. The view's
        /// building push-out skips it: inside the vehicle factory it shoved a new hull up to 3 m to the side, so the hull slid
        /// through a door post as soon as it was past the door.</summary>
        bool ExitInside()
        {
            if (!exitLane) return false;
            bool on = false;
            if (Obj != null && exitFrom != null && !exitFrom.Destroyed) foreach (var w in Obj.Waypoints) { on = w.mode == WpMode.ForceMove; break; }
            if (!on) exitLane = false; else ExitLaneSkips++;
            return on;
        }

        /// <summary>The door of a producer as its model has it (the gate parts, closed): the clear width (m) and how far (m) the door
        /// plane lies inside the building box on the exit side. False = no door known: the hull grows with its depth inside the box.</summary>
        static bool ExitDoorOf(string type, out float width, out float inset)
        {
            switch (type)
            {
                case "fort_front": width = 3.2f; inset = 5.6f; return true;
                case "fort_back": width = 3.2f; inset = 4.6f; return true;
                case "vehicle_factory": width = 3.0f; inset = 2.4f; return true;
            }
            width = inset = 0f; return false;
        }

        void ExitGrowBind()
        {
            exitGrowing = false; exitStraight = false; exitDoor = false; exitLane = false; exitFit = ExitStartScale; exitCur = 1f; exitFrom = null;
            if (Obj == null || (Obj.Kind != ZKind.Vehicle && Obj.Kind != ZKind.Robot) || game == null || game.World == null) return;
            int cx = Obj.LocX + Obj.WidthPix / 2, cy = Obj.LocY + Obj.HeightPix / 2;
            foreach (var b in game.World.Objects)
                if (b.Kind == ZKind.Building && !b.Destroyed && !b.IsBridge && b.Contains(cx, cy)) { exitFrom = b; break; }
            if (exitFrom == null) return;
            exitLane = true;
            if (Obj.Kind != ZKind.Vehicle) return;
            exitDepth0 = Mathf.Max(0.5f, Depth(transform.position));
            exitBase = transform.localScale; exitAge = 0f; exitCur = ExitStartScale; exitGrowing = true;
            transform.localScale = exitBase * exitCur;
            ExitGrowStarted++;
            ExitStraightBind();
            ExitDoorFit();
        }

        /// <summary>A hull wider than the door it leaves through is drawn just small enough to pass between the door posts (never
        /// below ExitMinScale; a hull that fits keeps its full size) and grows back only when its tail is through the door.</summary>
        void ExitDoorFit()
        {
            if (!exitStraight || !ExitDoorOf(exitFrom.Type, out float doorW, out float inset)) return;
            exitDir = Quaternion.Euler(0f, exitYaw, 0f) * Vector3.forward; bool alongX = Mathf.Abs(exitDir.x) > Mathf.Abs(exitDir.z);
            transform.localScale = exitBase;                                                     // measured at full size, on the door axis
            bool has = false; var bb = new Bounds();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!has) { bb = r.bounds; has = true; } else bb.Encapsulate(r.bounds);
            }
            transform.localScale = exitBase * exitCur;
            if (!has) return;
            var c = transform.position;
            float half = alongX ? Mathf.Min(c.z - bb.min.z, bb.max.z - c.z) : Mathf.Min(c.x - bb.min.x, bb.max.x - c.x);   // the narrower side: a one-sided aerial does not count
            exitRear = alongX ? (exitDir.x > 0f ? c.x - bb.min.x : bb.max.x - c.x) : (exitDir.z > 0f ? c.z - bb.min.z : bb.max.z - c.z);
            exitFit = Mathf.Clamp((doorW - 2f * ExitDoorAirM) / Mathf.Max(0.5f, 2f * half), ExitMinScale, 1f);
            var a = ZGame.PxToWorld(exitFrom.LocX, exitFrom.LocY); var d = ZGame.PxToWorld(exitFrom.LocX + exitFrom.WidthPix, exitFrom.LocY + exitFrom.HeightPix);
            float edge = Mathf.Max(Mathf.Max(Vector3.Dot(a, exitDir), Vector3.Dot(d, exitDir)), Mathf.Max(Vector3.Dot(new Vector3(a.x, 0f, d.z), exitDir), Vector3.Dot(new Vector3(d.x, 0f, a.z), exitDir)));
            exitDoorAlong = edge - inset; exitDoor = true; exitDrawn = c;
            exitCur = exitFit; transform.localScale = exitBase * exitCur;
            if (exitFit < 0.999f) { ExitFitted++; ExitFitMin = Mathf.Min(ExitFitMin, exitFit); }
        }

        /// <summary>A vehicle born inside a building starts with its hull on the door axis (creation point -> exit point; the repair
        /// bay: centre -> entrance) and keeps it there while the exit move runs (ExitHull).</summary>
        void ExitStraightBind()
        {
            int ax = 0, ay = 0, bx = 0, by = 0;
            if (!(exitFrom.GetCreationPoint(out ax, out ay) && exitFrom.GetCreationMovePoint(out bx, out by)) &&
                !(exitFrom.GetRepairCenter(out ax, out ay) && exitFrom.GetRepairEntrance(out bx, out by))) return;
            if (ax == bx && ay == by) return;
            exitYaw = Mathf.Atan2(bx - ax, -(by - ay)) * Mathf.Rad2Deg;                          // px (x right, y down) -> world yaw
            exitStraight = true; exitStraightAge = 0f; ExitStraightStarted++;
            yaw = heading = exitYaw; if (hullSteer != null) hullSteer.Adopt(exitYaw);
            if (slip != null) slip.Reset(slip.X, slip.Z, exitYaw);                               // the no-glide follower took the placed facing in the first Sync: a door that faces the other way had the hull turn round inside
            transform.rotation = Quaternion.Euler(0f, yaw + modelYaw, 0f);
        }

        /// <summary>The vehicle facing step: `steer` is the path steering's hull yaw. While the exit move (the FORCE_MOVE out of the
        /// building) is the vehicle's first waypoint the hull stays on the door axis: the path look-ahead reaches past the exit point
        /// and would start the turn to the next waypoint inside the door. After it the steering goes on from that heading.</summary>
        float ExitHull(float steer, float dt)
        {
            if (exitGrowing) exitDrawn = transform.position;                                     // where the hull was drawn last frame (the factory view draws a new unit deeper in its hall than its game point)
            if (!exitStraight) return steer;
            exitStraightAge += dt;
            bool exiting = false;
            if (Obj != null) foreach (var w in Obj.Waypoints) { exiting = w.mode == WpMode.ForceMove; break; }
            if (!exiting || exitFrom == null || exitFrom.Destroyed || exitStraightAge > ExitMaxSeconds) { exitStraight = false; ExitStraightDone++; return steer; }
            ExitStraightMaxOffDeg = Mathf.Max(ExitStraightMaxOffDeg, Mathf.Abs(Mathf.DeltaAngle(steer, exitYaw)));
            if (hullSteer != null) hullSteer.Adopt(exitYaw);
            return exitYaw;
        }

        /// <summary>Right after the no-glide follower: while the hull is held on the door axis the follower may not steer it off that
        /// axis (a sideways push from the unit spacing turned long hulls in the doorway).</summary>
        void ExitHoldSlip()
        {
            if (!exitStraight || slip == null) return;
            if (Mathf.Abs(Mathf.DeltaAngle(slip.Yaw, exitYaw)) > 0.01f) slip.Reset(slip.X, slip.Z, exitYaw);
            yaw = exitYaw;
        }

        /// <summary>How far (m) the point is inside exitFrom's footprint (0 or less = outside).</summary>
        float Depth(Vector3 p)
        {
            var a = ZGame.PxToWorld(exitFrom.LocX, exitFrom.LocY); var c = ZGame.PxToWorld(exitFrom.LocX + exitFrom.WidthPix, exitFrom.LocY + exitFrom.HeightPix);
            float x0 = Mathf.Min(a.x, c.x), x1 = Mathf.Max(a.x, c.x), z0 = Mathf.Min(a.z, c.z), z1 = Mathf.Max(a.z, c.z);
            return Mathf.Min(Mathf.Min(p.x - x0, x1 - p.x), Mathf.Min(p.z - z0, z1 - p.z));
        }

        void ExitGrowStep(float dt)
        {
            exitAge += dt;
            float k = exitFrom == null || exitFrom.Destroyed || exitAge > ExitMaxSeconds ? 1f
                : exitDoor ? Mathf.Clamp01((Vector3.Dot(exitDrawn, exitDir) - exitRear * exitCur - exitDoorAlong) / ExitGrowRunM)   // the drawn tail through the door first
                : 1f - Mathf.Clamp01(Depth(transform.position) / exitDepth0);
            float target = Mathf.Lerp(exitFit, 1f, Mathf.SmoothStep(0f, 1f, k));
            exitCur = Mathf.Max(exitCur, Mathf.MoveTowards(exitCur, target, dt * 0.6f));   // smooth, never shrinks back
            if (exitCur >= 0.999f) { exitCur = 1f; exitGrowing = false; ExitGrowDone++; }
            transform.localScale = exitBase * exitCur;
        }
    }
}
