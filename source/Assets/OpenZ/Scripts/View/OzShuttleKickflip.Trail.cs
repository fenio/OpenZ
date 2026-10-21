using System;

namespace OpenZ
{
    /// <summary>
    /// K390, the kickflip's exhaust as one smooth trail per jet (view only; plain C#, shared by the game's drawing in
    /// View/OzPlayableShuttle.KickTrails.cs and the counted check). A trail is a chain of points born at the jet's mouth at a FIXED
    /// rate in time (<see cref="Interval"/> s, whatever the frame rate: between two frames the mouth and the jet's axis are interpolated,
    /// so a fast roll at 30 fps still gives a smooth curve), each blown out along the axis the jet had at its birth (an exhaust speed,
    /// slowed by the air: <see cref="Drag"/>) plus a share of the ship's own speed, and gone after <see cref="Life"/> s. The drawing
    /// joins them into one ribbon (a hot core and a soft glow, fading with age): no separate puffs, no gaps. The newest point is the
    /// mouth itself while the jet emits, so the ribbon always starts at the nozzle. Nothing allocated per frame, no random draw, no
    /// clock of its own (the caller hands in the seconds: 0 on a paused frame = the trail stands still).
    /// </summary>
    public sealed class OzKickTrail
    {
        /// <summary>The seconds between two points (fixed), the longest a point lives (s), the air's slowing (1/s).</summary>
        public readonly double Interval, Life, Drag;
        readonly int cap;
        readonly double[] bx, by, bz, vx, vy, vz, age, wt;
        int head = -1, count;
        double since; bool havePrev; double pmx, pmy, pmz, pax, pay, paz, pw;
        double hx, hy, hz, hw; bool headOn;
        /// <summary>Counts: points born, points dropped because the chain was full (0 when the cap is right).</summary>
        public int Born, Overflow;

        public OzKickTrail(double interval, double life, double drag)
        {
            Interval = Math.Max(1e-3, interval); Life = Math.Max(0.02, life); Drag = Math.Max(0.0, drag);
            cap = (int)Math.Ceiling(Life / Interval) + 4;
            bx = new double[cap]; by = new double[cap]; bz = new double[cap]; vx = new double[cap]; vy = new double[cap]; vz = new double[cap]; age = new double[cap]; wt = new double[cap];
        }

        /// <summary>The points in the chain now, the mouth's point included while it emits.</summary>
        public int Count => count + (headOn ? 1 : 0);
        public int Capacity => cap + 1;
        public bool Live => Count > 0;

        public void Clear() { head = -1; count = 0; havePrev = false; headOn = false; since = 0; }

        /// <summary>The way a point has gone at age a along its birth velocity (m per m/s): (1 - e^(-Drag a)) / Drag.</summary>
        public double Gone(double a) => Drag > 1e-9 ? (1.0 - Math.Exp(-Drag * a)) / Drag : a;

        /// <summary>One frame of dt s. (mx, my, mz) = the mouth now, (ax, ay, az) = the jet's axis now (unit, the way the gas leaves),
        /// speed = the gas's speed out of the mouth (m/s), (cx, cy, cz) = the velocity the gas carries along (m/s: a share of the ship's),
        /// w = how strongly it burns now (0..1, the ribbon's brightness for the points born now), emit = the jet is drawn this frame.</summary>
        public void Step(double dt, double mx, double my, double mz, double ax, double ay, double az, double speed, double cx, double cy, double cz, double w, bool emit)
        {
            if (dt < 0 || double.IsNaN(dt)) dt = 0;
            if (dt > 0)
            {
                for (int k = 0; k < count; k++) age[Idx(k)] += dt;
                while (count > 0 && age[Idx(count - 1)] >= Life) count--;                                // the oldest go first
            }
            if (!emit) { havePrev = false; headOn = false; since = 0; return; }
            if (!havePrev) { pmx = mx; pmy = my; pmz = mz; pax = ax; pay = ay; paz = az; pw = w; havePrev = true; since = Interval; }   // the first frame: one point at the mouth
            else since += dt;
            while (since >= Interval)
            {
                since -= Interval;
                // this point was born `since` s ago: at the fraction f of the way from the last frame's mouth to this one's
                double f = dt > 1e-9 ? 1.0 - since / dt : 1.0; if (f < 0) f = 0; if (f > 1) f = 1;
                double px = pmx + (mx - pmx) * f, py = pmy + (my - pmy) * f, pz = pmz + (mz - pmz) * f;
                double qx = pax + (ax - pax) * f, qy = pay + (ay - pay) * f, qz = paz + (az - paz) * f, ql = Math.Sqrt(qx * qx + qy * qy + qz * qz);
                if (ql > 1e-9) { qx /= ql; qy /= ql; qz /= ql; } else { qx = ax; qy = ay; qz = az; }
                double ww = pw + (w - pw) * f;
                Push(px, py, pz, qx * speed + cx, qy * speed + cy, qz * speed + cz, since, ww);
            }
            pmx = mx; pmy = my; pmz = mz; pax = ax; pay = ay; paz = az; pw = w;
            hx = mx; hy = my; hz = mz; hw = w; headOn = true;
        }

        void Push(double x, double y, double z, double ux, double uy, double uz, double a, double w)
        {
            head = (head + 1) % cap;
            if (count < cap) count++; else Overflow++;
            bx[head] = x; by[head] = y; bz[head] = z; vx[head] = ux; vy[head] = uy; vz[head] = uz; age[head] = a; wt[head] = w; Born++;
        }

        int Idx(int k) { int i = head - k; return i < 0 ? i + cap : i; }

        /// <summary>Point i (0 = the newest: the mouth while it emits .. Count - 1 = the oldest): where it is, its age share u (0..1) and
        /// its brightness w.</summary>
        public void Point(int i, out double x, out double y, out double z, out double u, out double w)
        {
            if (headOn) { if (i == 0) { x = hx; y = hy; z = hz; u = 0; w = hw; return; } i--; }
            int j = Idx(i); double a = age[j], g = Gone(a);
            x = bx[j] + vx[j] * g; y = by[j] + vy[j] * g; z = bz[j] + vz[j] * g; u = a / Life; w = wt[j];
        }

        /// <summary>How far the trail reaches now (m): the farthest point from its newest one (the mouth while it emits), of the points younger
        /// than maxU of their life (1 = all; 0.4 = its bright part: the drawing fades a point as (1 - u)^1.6).</summary>
        public double Reach(double maxU = 1.0)
        {
            int n = Count; if (n < 2) return 0;
            Point(0, out double x0, out double y0, out double z0, out _, out _); double m = 0;
            for (int i = 1; i < n; i++) { Point(i, out double x, out double y, out double z, out double u, out _); if (u > maxU) break; m = Math.Max(m, (x - x0) * (x - x0) + (y - y0) * (y - y0) + (z - z0) * (z - z0)); }
            return Math.Sqrt(m);
        }

        /// <summary>The ribbon's length (m) along its points now, and its largest gap between two neighbours (m).</summary>
        public double Length(out double maxGap)
        {
            double len = 0; maxGap = 0; int n = Count;
            if (n < 2) return 0;
            Point(0, out double x0, out double y0, out double z0, out _, out _);
            for (int i = 1; i < n; i++)
            {
                Point(i, out double x, out double y, out double z, out _, out _);
                double d = Math.Sqrt((x - x0) * (x - x0) + (y - y0) * (y - y0) + (z - z0) * (z - z0));
                len += d; if (d > maxGap) maxGap = d; x0 = x; y0 = y; z0 = z;
            }
            return len;
        }
    }
}
