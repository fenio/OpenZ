using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Staged demo fight on L02 in the open field south-west of the centre bridges (tiles 20-34 x 53-59, for the video and
    /// the combat check): BLUE holds the east side by the river, RED attacks from the west. Every order goes through the normal command layer (SendWaypoints),
    /// everything else is Zod's own unit AI: passive engage, agro, attack-move, dodging, auto-capture.
    /// </summary>
    public class FightStage : MonoBehaviour
    {
        public readonly List<string> Log = new List<string>();
        ZGame game; ZWorld W => game.World;
        public ZObject redTank, blueTank, blueJeep, neutralJeep, gatling, crate, grenSquad, jeepSquad, snipers, toughs, rockA;

        public static FightStage Begin()
        {
            var fs = FindFirstObjectByType<FightStage>() ?? ZGame.I.gameObject.AddComponent<FightStage>();
            if (fs.running) return fs;                                     // a repeated call (tool retry) must not stage a second army
            fs.running = true;
            fs.StartCoroutine(fs.Run());
            return fs;
        }

        ZObject Squad(string type, int tx, int ty, Team t) { var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); Spawn(o); foreach (var m in o.Minions) Spawn(m); return o; }
        ZObject Unit(string cat, string type, int tx, int ty, Team t) { var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); Spawn(o); return o; }
        void Spawn(ZObject o) => game.SpawnUnitView(o);

        void Order(ZObject u, WpMode mode, ZObject target = null, int tx = 0, int ty = 0, bool attackTo = true)
        {
            var wp = new ZWaypoint { mode = mode, refId = target?.Id ?? -1, x = target?.CenterX ?? tx * 16 + 8, y = target?.CenterY ?? ty * 16 + 8, attackTo = attackTo };
            bool ok = W.SendWaypoints(u.Owner, u, new[] { wp });
            Log.Add($"{W.Time:F1}s order {u} {mode} {(target != null ? target.ToString() : $"({tx},{ty})")} -> {(ok && u.Waypoints.Count > 0 ? "ok" : "refused")}");
        }

        /// <summary>Nearest tile (spiral) where a vehicle's 2x2 footprint is passable.</summary>
        (int, int) VehicleSpot(int tx, int ty)
        {
            for (int r = 0; r < 6; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = tx + dx, y = ty + dy;
                        if (W.Path.TilePassable(x, y, false) && W.Path.TilePassable(x + 1, y, false) && W.Path.TilePassable(x, y + 1, false) && W.Path.TilePassable(x + 1, y + 1, false)) return (x, y);
                    }
            return (tx, ty);
        }
        ZObject Vehicle(string cat, string type, int tx, int ty, Team t) { var (x, y) = VehicleSpot(tx, ty); return Unit(cat, type, x, y, t); }

        public string Phase = "setup";
        bool running;

        IEnumerator Run()
        {
            game = ZGame.I;
            // ---- phase A: snipe a crewed jeep, capture it with another squad; grenades from a crate blast a rock
            snipers = Squad("sniper", 23, 56, Team.Red);
            jeepSquad = Squad("grunt", 22, 54, Team.Red);
            grenSquad = Squad("grunt", 21, 58, Team.Red);
            crate = Unit("map_item", "grenades", 20, 59, Team.Neutral);
            blueJeep = Vehicle("vehicle", "jeep", 32, 57, Team.Blue);
            rockA = null; double best = 1e9;
            foreach (var o in W.Objects)
                if (o.Type == "rock" && !o.Destroyed) { double d = (o.CenterX - 16 * 16) * (double)(o.CenterX - 16 * 16) + (o.CenterY - 58 * 16) * (double)(o.CenterY - 58 * 16); if (d < best) { best = d; rockA = o; } }
            Log.Add($"staged: {W.Objects.Count} objects, rock {rockA}");
            yield return new WaitForSeconds(1.0f);
            Phase = "A: snipe + grenades";
            Order(snipers, WpMode.Attack, blueJeep);
            Order(grenSquad, WpMode.PickupGrenades, crate);

            float t0 = Time.time; bool captureOrdered = false, rockOrdered = false;
            float capturedAt = -1;
            while (Time.time - t0 < 25f && (capturedAt < 0 || Time.time - capturedAt < 2.5f))     // the snipe is a dice roll: wait for it
            {
                if (capturedAt < 0 && blueJeep.Owner == Team.Red) capturedAt = Time.time;
                if (W.Get(blueJeep.Id) == null) break;
                if (!captureOrdered && blueJeep.Owner == Team.Neutral && W.Get(blueJeep.Id) != null)
                {
                    // Zod lets an explicit attack order keep shooting an emptied (neutral) vehicle; a player re-orders the snipers
                    captureOrdered = true; Order(snipers, WpMode.Move, null, snipers.CenterX / 16, snipers.CenterY / 16); Order(jeepSquad, WpMode.Enter, blueJeep);
                }
                if (!rockOrdered && grenSquad.Grenades > 0 && rockA != null && W.Get(rockA.Id) != null) { rockOrdered = true; Order(grenSquad, WpMode.Attack, rockA); }
                yield return null;
            }

            // ---- phase B: the blue force arrives from the east; tanks, rockets, flames, a gatling
            Phase = "B: clash";
            // rocket toughs + tank only now: in phase A their passive engage blew the jeep up before the snipers could empty it
            toughs = Squad("tough", 20, 56, Team.Red);
            redTank = Vehicle("vehicle", "light", 19, 53, Team.Red);
            var psychos = Squad("psycho", 33, 53, Team.Blue);
            var pyros = Squad("pyro", 33, 58, Team.Blue);
            blueTank = Vehicle("vehicle", "medium", 30, 55, Team.Blue);
            gatling = Vehicle("cannon", "gatling", 33, 55, Team.Blue);
            yield return new WaitForSeconds(0.5f);
            Order(blueTank, WpMode.Move, null, 25, 55);
            Order(psychos, WpMode.Move, null, 26, 54);
            Order(redTank, WpMode.Move, null, 28, 55);
            Order(toughs, WpMode.Attack, blueTank);
            if (W.Get(grenSquad.Id) != null) Order(grenSquad, WpMode.Attack, blueTank);
            if (blueJeep.Owner == Team.Red) Order(blueJeep, WpMode.Move, null, 31, 58);
            yield return new WaitForSeconds(25f);
            Phase = "done";
        }
    }
}
