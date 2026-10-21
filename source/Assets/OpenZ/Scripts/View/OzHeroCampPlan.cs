using System;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads (View/OzHeroSquadView.cs): the layout of the level-start camp, plain maths with no engine types, so the
    /// view (at run time, from the drawn robots) and the headless rule check (Editor/OzV11HeroSquadCount.cs, from the start tiles)
    /// lay it out with the same rule and the same numbers. World metres on the ground plane (x, z); "toward the camera" is the
    /// standard game view's screen-down direction.
    ///  * The robots lie side by side where they start, both turned toward the camera and VAngle deg in toward the middle: a
    ///    shallow V FACING the fire. Each lounger lies BedBack x k behind its robot (the sun-lounger antic's own rule), so the
    ///    lounger's foot end is (BedBack - BedHalfLen) x k behind the robot.
    ///  * The fire stands in front of them (toward the camera) on their middle, so far that the clear gap between the fire's
    ///    footprint and the loungers' foot ends is CampGap m.
    ///  * Each parasol stands beside its lounger on the outer side (away from the other lounger), ParasolAlong x k behind the
    ///    robot (by the head end), ParasolSide x k out from the lounger's edge; it leans away from the camera as the antic's do, so
    ///    its canopy sits over the lounger's head end on screen and the two canopies never meet.
    /// k = the robot's world metres per authored metre (AnticSockets.WorldScale), wf = its lounger width factor (BedWidthFor).
    /// </summary>
    public static class OzHeroCampPlan
    {
        /// <summary>Clear gap (m) between the fire's footprint and the loungers' foot ends.</summary>
        public static double CampGap = 1.4;
        /// <summary>Each lounger turns this far (deg) in toward the fire (0 = the two PARALLEL, side by side, as in the briefing's
        /// lounger set: the owner's item 175).</summary>
        public static double VAngle = 0.0;
        /// <summary>The antic's lounger: centre behind the robot, half length, half width (authored m; AnticSockets.BedBack, the
        /// delivered 2.4 m lounger, the 1.1 m frame).</summary>
        public static double BedBack = 1.8, BedHalfLen = 1.2, BedHalfW = 0.55;
        /// <summary>The parasol pole: behind the robot and out from the lounger's outer edge (authored m).</summary>
        public static double ParasolAlong = 2.4, ParasolSide = 0.35;
        /// <summary>The campfire's footprint (m) along and across the view when the level's model cannot be measured (the jungle
        /// campfire with its two seat logs: 3.9 m across, 2.5 m deep).</summary>
        public static double FireHalfDepth = 1.25, FireHalfWidth = 1.95;
        /// <summary>The heroes' size (the one source: OzHeroSquadView and the editor menus read these): Alan's height as a multiple of a
        /// grunt's (heroes, clearly bigger than a grunt; 1.25 to 1.35 wanted) and Alan's height as a share of Brad's (about 5 % under).</summary>
        public static double AlanToGrunt = 1.30, AlanToBrad = 0.95;
        /// <summary>For the headless check only (the game measures the drawn robots): the grunt's weapon socket world scale (its
        /// prefab: Model x 1.0297, socket x 1) and the loungers' width factors of Brad and Alan (BedWidthFor from the prefabs'
        /// shoulder spans: Brad 0.82 m / k 1.084 = 0.756 -> 1.197; Alan 0.56 m / 1.030 = 0.544 -> 1.0, the narrowest bed).</summary>
        public static double GruntK = 1.0297, BradBedWidth = 1.197, AlanBedWidth = 1.0;
        /// <summary>For the check: the parasol's canopy (ANT_Parasol: centre 2.6 m up, 2.76 m across, leaning ParasolTilt deg).</summary>
        public static double ParasolCanopyY = 2.6, ParasolCanopyR = 1.38, ParasolTilt = 28.0;
        /// <summary>The nominal scale k of a member (world m per authored m) at the heroes' size.</summary>
        public static double NominalK(string member) => GruntK * AlanToGrunt / (member == "brad" ? AlanToBrad : 1.0);

        public sealed class Spot
        {
            /// <summary>The robot (where it lies) and the yaw it faces (deg, 0 = +z).</summary>
            public double x, z, yaw;
            /// <summary>The direction it faces (toward its feet) and the lounger's side direction.</summary>
            public double fx, fz, rx, rz;
            /// <summary>The lounger's centre, half length and half width (m).</summary>
            public double bedX, bedZ, hl, hw;
            /// <summary>The parasol's pole foot.</summary>
            public double poleX, poleZ;
            /// <summary>-1 = the left one on screen, +1 = the right one.</summary>
            public int side;
            /// <summary>The lounger's four corners (x0, z0, ... x3, z3).</summary>
            public double[] Corners()
            {
                var c = new double[8]; int i = 0;
                foreach (var (u, v) in new[] { (1, 1), (1, -1), (-1, -1), (-1, 1) })
                { c[i++] = bedX + fx * u * hl + rx * v * hw; c[i++] = bedZ + fz * u * hl + rz * v * hw; }
                return c;
            }
        }

        public sealed class Camp
        {
            public double fireX, fireZ, ahead, fireHalfDepth, fireHalfWidth;
            public Spot[] spots;
            /// <summary>The smallest clear gap (m) between the fire's footprint and a lounger's foot end, and between the two loungers.</summary>
            public double FireGap() { double g = double.MaxValue; foreach (var s in spots) g = Math.Min(g, GapToFire(s)); return g; }
            double GapToFire(Spot s)
            {   // the foot end's two corners, measured along the view from the fire's far edge (toward the loungers)
                double tcx = -camFx, tcz = -camFz;                                       // toward the camera
                double best = double.MaxValue;
                foreach (int v in new[] { 1, -1 })
                {
                    double cx = s.bedX + s.fx * s.hl + s.rx * v * s.hw, cz = s.bedZ + s.fz * s.hl + s.rz * v * s.hw;
                    double along = -((cx - fireX) * tcx + (cz - fireZ) * tcz);               // how far behind the fire's centre (away from the camera)
                    best = Math.Min(best, along - fireHalfDepth);
                }
                return best;
            }
            internal double camFx, camFz;
        }

        /// <summary>The camp of the robots at (xs[i], zs[i]) (leader first) with scales ks and lounger widths wfs, for the view whose
        /// screen-up ground direction is (camFx, camFz) (unit). fireHalfDepth / fireHalfWidth &lt;= 0: the defaults.</summary>
        public static Camp Plan(double[] xs, double[] zs, double[] ks, double[] wfs, double camFx, double camFz, double fireHalfDepth = 0, double fireHalfWidth = 0)
        {
            int n = xs.Length;
            double fd = fireHalfDepth > 0 ? fireHalfDepth : FireHalfDepth, fw = fireHalfWidth > 0 ? fireHalfWidth : FireHalfWidth;
            double rX = camFz, rZ = -camFx;                                                   // screen right on the ground (Cross(up, forward))
            double tX = -camFx, tZ = -camFz;                                                  // toward the camera
            double mx = 0, mz = 0, km = 0;
            for (int i = 0; i < n; i++) { mx += xs[i] / n; mz += zs[i] / n; km += ks[i] / n; }
            double a = VAngle * Math.PI / 180.0;
            double ahead = CampGap + fd - (BedBack - BedHalfLen) * km * Math.Cos(a);
            var camp = new Camp { fireX = mx + tX * ahead, fireZ = mz + tZ * ahead, ahead = ahead, fireHalfDepth = fd, fireHalfWidth = fw, spots = new Spot[n], camFx = camFx, camFz = camFz };
            for (int i = 0; i < n; i++)
            {
                double across = (xs[i] - mx) * rX + (zs[i] - mz) * rZ;
                int side = Math.Abs(across) > 1e-6 ? Math.Sign(across) : (i == 0 ? -1 : 1);   // on one spot: the leader left
                double fx = tX * Math.Cos(a) - rX * Math.Sin(a) * side, fz = tZ * Math.Cos(a) - rZ * Math.Sin(a) * side;
                double lx = fz, lz = -fx;                                                       // the lounger's side direction
                double k = ks[i], hw = BedHalfW * k * Math.Max(1.0, wfs[i]);
                var s = new Spot
                {
                    x = xs[i], z = zs[i], yaw = Math.Atan2(fx, fz) * 180.0 / Math.PI, fx = fx, fz = fz, rx = lx, rz = lz, side = side,
                    bedX = xs[i] - fx * BedBack * k, bedZ = zs[i] - fz * BedBack * k, hl = BedHalfLen * k, hw = hw,
                };
                double ox = rX * side, oz = rZ * side;                                          // outward: away from the middle
                double sx = lx, sz = lz; if (sx * ox + sz * oz < 0) { sx = -sx; sz = -sz; }
                double bx = xs[i] - fx * ParasolAlong * k, bz = zs[i] - fz * ParasolAlong * k, out_ = hw + ParasolSide * k;
                s.poleX = bx + sx * out_; s.poleZ = bz + sz * out_;
                camp.spots[i] = s;
            }
            return camp;
        }

        /// <summary>Sim pixels -> world metres on the ground (ZGame.PxToWorld without the engine type).</summary>
        public static void PxToWorld(double px, double py, out double x, out double z) { x = px / 8.0; z = -py / 8.0; }
        /// <summary>World metres -> the tile (16 px = 2 m).</summary>
        public static void Tile(double x, double z, out int tx, out int ty) { tx = (int)Math.Floor(x * 8.0 / 16.0); ty = (int)Math.Floor(-z * 8.0 / 16.0); }
    }
}
