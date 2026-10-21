using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Every piece of the shuttle's break-up gets its own small share of the blast (rule DV118, view only: the kill, the damage and the
    /// crew's deaths are the rules' as before). The pieces used to leave the blast in the same frame with the same factors on every side,
    /// so they flew out like one shell opening. Now, for every break (the air bursts, splits and cracks, the ground death and the drop):
    /// <list type="bullet">
    /// <item>each piece's outward throw x<see cref="VaryOut0"/>..<see cref="VaryOut1"/>, its upward throw x<see cref="VaryUp0"/>..<see cref="VaryUp1"/>,
    /// its way out turned by up to <see cref="VaryTurnDeg"/> degrees (about an axis across its throw: a little left, right, up or down);</item>
    /// <item>its spin x<see cref="VarySpin0"/>..<see cref="VarySpin1"/> about its own random axis (the 14 rad/s cap kept);</item>
    /// <item>the pieces leave the blast over <see cref="VaryStaggerSec"/> s, the heaviest first: a piece still waiting rides on with the
    /// ship's own way (and falls with it) and gets its throw at its moment (on the physics steps: 0 / 20 / 40 / 60 ms);</item>
    /// <item>the crew's bodies are thrown x<see cref="VaryCrew0"/>..<see cref="VaryCrew1"/> each (BoardingView.ShuttleCrewLost's hook).</item>
    /// </list>
    /// All of it is drawn from one seed per death (the ship's id and the break count, as the launch's own scatter), so the same kill
    /// breaks the same way every time. A piece is never slowed below the slower of its own throw and <see cref="VaryFloorBurst"/> /
    /// <see cref="VaryFloorCrack"/> m/s (the slowest piece keeps the big blast's floor). The big blast's factors (K396, OzShuttleBreakup.BigBlast.cs),
    /// the killing round's push, the pieces' look, their rest and solid rules (K256h2, K331b / K331d) are not touched; the landing field
    /// stays within the big blast's range (the kit's twin). No garbage per frame: the tables are made once per death.
    /// Switch: <see cref="ThrowVaryOn"/> (off = every piece leaves at once with the old factors).
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>Switch: each piece its own throw and moment (on).</summary>
        public static bool ThrowVaryOn = true;
        /// <summary>The ranges per piece: out, up (x), the turn (degrees, at most), spin (x), the stagger (s, all pieces), the crew's throw (x).</summary>
        public static float VaryOut0 = 0.8f, VaryOut1 = 1.2f, VaryUp0 = 0.85f, VaryUp1 = 1.15f, VaryTurnDeg = 12f, VarySpin0 = 0.7f, VarySpin1 = 1.3f, VaryStaggerSec = 0.06f, VaryCrew0 = 0.9f, VaryCrew1 = 1.1f;
        /// <summary>A piece's launch out of the blast (relative to the ship) never drops below the slower of its own unvaried launch and this
        /// (m/s): air bursts and splits, the crack. The ground death and the drop: its own launch only.</summary>
        public static float VaryFloorBurst = 2.0f, VaryFloorCrack = 1.2f;

        // ------------------------------------------------------------------ the proof reads these
        /// <summary>One piece's draw in the last break: its factors, its planned moment and the moment it really left (ms after the break;
        /// -1 = not yet), its launch out of the blast before / after the variation (m/s), its spin (rad/s), its rank by mass (0 = heaviest).</summary>
        public struct VaryRow
        {
            public string name, kind; public float mass, outK, upK, turnDeg, spinK, delayMs, leftMs, speed0, speed, spin; public int rank; public bool floored;
        }
        public static readonly List<VaryRow> LastVary = new List<VaryRow>(24);
        /// <summary>The crew's throw factors of the last break (one per body, in throw order).</summary>
        public static readonly List<float> LastCrewVary = new List<float>(12);
        public static int VaryBreaks, VaryPieces, VaryHeld, VaryLeft, VaryFloored, VarySteeredAtOnce, LastVarySeed, LastVaryShip = -1;
        public static string LastVaryHow = "-";
        /// <summary>The last break's pieces left over this many ms (the last one's moment less the first one's), -1 = still leaving.</summary>
        public static float LastVarySpreadMs = -1f;

        static Group varyGrp; static VaryRunner varyRun; static int varyN, crewFor = int.MinValue, varySerial;
        static float[] vOut = new float[0], vUp = new float[0], vTurn = new float[0], vAz = new float[0], vSpin = new float[0], vDelay = new float[0];
        static Vector3[] vAxis = new Vector3[0]; static int[] vRank = new int[0];

        static void VaryEnsure(int n)
        {
            if (vOut.Length >= n) return;
            int m = Mathf.Max(n, 24);
            vOut = new float[m]; vUp = new float[m]; vTurn = new float[m]; vAz = new float[m]; vSpin = new float[m]; vDelay = new float[m]; vAxis = new Vector3[m]; vRank = new int[m];
        }

        /// <summary>The death's draw, once (the first piece of the launch loop): every piece's factors from one seed, its rank by mass, its moment.</summary>
        static void VaryPlan(ZObject o, Group grp, Transform holder, string how)
        {
            varyGrp = grp; int n = varyN = grp.pieces.Count; VaryEnsure(n);
            int seed = unchecked(o.Id * 104729 + Breaks * 389 + 399);
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var jit = new float[n];
            for (int i = 0; i < n; i++)
            {   // the draw order is fixed (the kit's twin repeats it): out, up, turn, its direction, spin, the spin axis (2), the moment's place
                vOut[i] = R(VaryOut0, VaryOut1); vUp[i] = R(VaryUp0, VaryUp1); vTurn[i] = R(0f, VaryTurnDeg); vAz[i] = R(0f, 360f); vSpin[i] = R(VarySpin0, VarySpin1);
                float z = R(-1f, 1f), a = R(0f, 6.2831853f), rr = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                vAxis[i] = new Vector3(rr * Mathf.Cos(a), z, rr * Mathf.Sin(a));
                jit[i] = R(0.05f, 0.95f);
            }
            for (int i = 0; i < n; i++)
            {   // the heaviest leaves first (ties: the earlier piece)
                var pi = grp.pieces[i]; float mi = pi ? pi.MassKg : 0f; int rank = 0;
                for (int j = 0; j < n; j++) { if (j == i) continue; var pj = grp.pieces[j]; float mj = pj ? pj.MassKg : 0f; if (mj > mi || (mj == mi && j < i)) rank++; }
                vRank[i] = rank; vDelay[i] = VaryStaggerSec * (rank + jit[i]) / Mathf.Max(1, n);
            }
            LastVary.Clear(); LastVarySeed = seed; LastVaryShip = o.Id; LastVaryHow = how; LastVarySpreadMs = -1f; VaryBreaks++; varySerial++;
            varyRun = holder ? holder.gameObject.AddComponent<VaryRunner>() : null;
            if (varyRun) varyRun.Begin(n, varySerial, o.Id);
        }

        /// <summary>Break's launch loop, in place of the plain launch: this piece's own throw, spin and moment (false = the plain launch).
        /// vOut / vUp = its outward and upward throw (m/s, every factor in); v0 = the plain launch (the ship's velocity, the throw, the
        /// killing round's push); sp = its spin (rad/s).</summary>
        static bool ThrowVary(ZObject o, Group grp, Transform holder, OzShuttleBreakup pc, string how, Vector3 vel, Vector3 dir, float vOutMps, float vUpMps, Vector3 v0, float sp)
        {
            if (!ThrowVaryOn || o == null || grp == null || !pc || !pc.rb) return false;
            if (varyGrp != grp) VaryPlan(o, grp, holder, how);
            int i = grp.pieces.IndexOf(pc); if (i < 0 || i >= varyN) return false;
            var rel0 = v0 - vel; var pure = dir * vOutMps + Vector3.up * vUpMps; var push = rel0 - pure;   // the round's push stays as it is
            var pv = dir * (vOutMps * vOut[i]) + Vector3.up * (vUpMps * vUp[i]);
            if (pv.sqrMagnitude > 1e-6f && vTurn[i] > 0f)
            {   // turned by up to VaryTurnDeg about an axis across the throw (its direction round the throw: vAz)
                var f = pv.normalized; var side = Vector3.Cross(f, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(f, Vector3.right);
                var across = Quaternion.AngleAxis(vAz[i], f) * side.normalized;
                pv = Quaternion.AngleAxis(vTurn[i], across) * pv;
            }
            var rel = pv + push;
            float floor = how == OzShuttleDeathPlan.Crack ? VaryFloorCrack : how == OzShuttleDeathPlan.Burst || how == OzShuttleDeathPlan.Split ? VaryFloorBurst : 0f;
            floor = Mathf.Min(floor, rel0.magnitude);
            bool floored = false;
            if (rel.magnitude < floor && rel.sqrMagnitude > 1e-8f) { rel *= floor / rel.magnitude; floored = true; VaryFloored++; }
            var w = vAxis[i] * (sp * vSpin[i]);
            pc.Launch(vel + rel, w);
            VaryPieces++;
            LastVary.Add(new VaryRow
            {
                name = pc.PieceName, kind = pc.Kind, mass = pc.MassKg, outK = vOut[i], upK = vUp[i], turnDeg = vTurn[i], spinK = vSpin[i], delayMs = vDelay[i] * 1000f, leftMs = -1f,
                speed0 = rel0.magnitude, speed = rel.magnitude, spin = Mathf.Min(w.magnitude, pc.rb.maxAngularVelocity), rank = vRank[i], floored = floored
            });
            if (varyRun) varyRun.Add(pc.rb, rel, vel + rel, vDelay[i], LastVary.Count - 1);
            else { var r = LastVary[LastVary.Count - 1]; r.leftMs = 0f; LastVary[LastVary.Count - 1] = r; }
            return true;
        }

        /// <summary>BoardingView.ShuttleCrewLost: body n's throw factor (seeded by the ship, the break count and n).</summary>
        public static float CrewVary(int shipId, int n)
        {
            if (crewFor != Breaks) { crewFor = Breaks; LastCrewVary.Clear(); }
            if (!ThrowVaryOn) return 1f;
            uint h = unchecked((uint)(shipId * 73856093) ^ (uint)((Breaks + 1) * 19349663) ^ (uint)((n + 1) * 83492791) ^ 0x18fu);
            h = unchecked(h ^ (h >> 16)); h = unchecked(h * 0x7feb352du); h = unchecked(h ^ (h >> 15)); h = unchecked(h * 0x846ca68bu); h = unchecked(h ^ (h >> 16));
            float k = Mathf.Lerp(VaryCrew0, VaryCrew1, (h & 0xFFFFFFu) / 16777215f);
            LastCrewVary.Add(k);
            return k;
        }

        // ------------------------------------------------------------------ the pieces leaving one after another
        /// <summary>On the break's holder: holds the pieces whose moment has not come (they ride on with the ship's way, no throw, no spin)
        /// and gives each its throw on the physics step of its moment. It runs on the physics steps after the break frame (the launch
        /// itself, as the big blast's proof reads it, is the full one). A piece the falling trees' rule steered onto a trunk after the
        /// launch (OzTreeCrush.ShuttleBurst: its way differs from the others' common shift) leaves at once on that way.</summary>
        sealed class VaryRunner : MonoBehaviour
        {
            Rigidbody[] rbs; Vector3[] dv, w, sent; float[] at, tx, ty, tz; int[] row; bool[] held; int n, serial, ship, waiting; float t; bool started; float firstMs = -1f, lastMs = -1f;

            internal void Begin(int count, int s, int shipId)
            {
                rbs = new Rigidbody[count]; dv = new Vector3[count]; w = new Vector3[count]; sent = new Vector3[count]; at = new float[count]; row = new int[count]; held = new bool[count];
                tx = new float[count]; ty = new float[count]; tz = new float[count];
                serial = s; ship = shipId; n = 0;
            }

            internal void Add(Rigidbody rb, Vector3 rel, Vector3 launched, float delay, int r)
            {
                if (rbs == null || n >= rbs.Length) return;
                rbs[n] = rb; dv[n] = rel; sent[n] = launched; at[n] = delay; row[n] = r; n++;
            }

            static float Median(float[] a, int m) { System.Array.Sort(a, 0, m); return m == 0 ? 0f : (m & 1) == 1 ? a[m / 2] : 0.5f * (a[m / 2 - 1] + a[m / 2]); }

            void Note(int i, float ms)
            {
                firstMs = firstMs < 0f ? ms : Mathf.Min(firstMs, ms); lastMs = Mathf.Max(lastMs, ms);
                if (serial != varySerial || row[i] < 0 || row[i] >= LastVary.Count) return;              // a newer break owns the table
                var r = LastVary[row[i]]; r.leftMs = ms; LastVary[row[i]] = r;
            }

            void FixedUpdate()
            {
                var g = ZGame.I; if (g && g.paused) return;
                float step = Mathf.Max(0.001f, Time.fixedDeltaTime);
                if (!started)
                {   // the first physics step after the break: who waits (the shift every piece got after the launch, e.g. the trees' carry, is common)
                    started = true; waiting = 0; int m = 0;
                    for (int i = 0; i < n; i++) { var rb = rbs[i]; if (!rb || rb.isKinematic) continue; var d = rb.linearVelocity - sent[i]; tx[m] = d.x; ty[m] = d.y; tz[m] = d.z; m++; }
                    var common = new Vector3(Median(tx, m), Median(ty, m), Median(tz, m));
                    for (int i = 0; i < n; i++)
                    {
                        var rb = rbs[i];
                        if (!rb || rb.isKinematic || at[i] < step * 0.5f) { Note(i, 0f); continue; }
                        if ((rb.linearVelocity - sent[i] - common).sqrMagnitude > 0.0025f) { Note(i, 0f); VarySteeredAtOnce++; continue; }   // steered onto a trunk: keeps that way
                        rb.linearVelocity -= dv[i]; w[i] = rb.angularVelocity; rb.angularVelocity = Vector3.zero;
                        held[i] = true; waiting++; VaryHeld++;
                    }
                }
                else t += step;
                for (int i = 0; i < n && waiting > 0; i++)
                {
                    if (!held[i] || t < at[i] - step * 0.5f) continue;
                    held[i] = false; waiting--;
                    var rb = rbs[i];
                    if (rb && !rb.isKinematic) { rb.WakeUp(); rb.linearVelocity += dv[i]; rb.angularVelocity += w[i]; VaryLeft++; }
                    Note(i, t * 1000f);
                }
                if (waiting > 0) return;
                if (serial == varySerial) LastVarySpreadMs = lastMs - firstMs;
                Debug.Log($"[OpenZ] shuttle #{ship} break-up: {n} pieces each with its own throw (out x{VaryOut0:F2}-{VaryOut1:F2}, up x{VaryUp0:F2}-{VaryUp1:F2}, turned up to {VaryTurnDeg:F0} deg, spin x{VarySpin0:F2}-{VarySpin1:F2}), left the blast over {lastMs - firstMs:F0} ms, the heaviest first");
                Destroy(this);
            }
        }
    }
}
