#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV114 "level 16: the empty gun on the right side, beside the red fort, is an empty missile cannon, set two
    /// tiles west and one north of its old place" (level data only: in Resources/OpenZ/Maps/L16_grid.json the neutral entry of tile 83,109
    /// "cannon:gun" stands on 81,108 as "cannon:missile_cannon"; no rule code). Measured headless: what stands on the new and the old tile
    /// (type, owner, crew), its distance to the nearest flag, the map's neutral cannons by type, and a replay of 600 ticks with both computer
    /// players identical twice.</summary>
    public static partial class OpenZParity
    {
        internal const int L16EmptyLauncherX = 81, L16EmptyLauncherY = 108, L16EmptyGunOldX = 83, L16EmptyGunOldY = 109;

        static void AddL16EmptyLauncherDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            try
            {
                string m16 = MapText("L16");
                var w = ZWorld.Create(SettingsText, m16, 1);
                Func<int, int, ZObject> at = (tx, ty) => w.Objects.FirstOrDefault(o => o.Kind == ZKind.Cannon && !o.Destroyed && o.LocX == tx * 16 && o.LocY == ty * 16);
                var mc = at(L16EmptyLauncherX, L16EmptyLauncherY); var old = at(L16EmptyGunOldX, L16EmptyGunOldY);
                double flagTiles = -1; string flagAt = "none";
                if (mc != null)
                    foreach (var f in w.Flags)
                    {
                        double d = Math.Sqrt(Math.Pow(f.LocX / 16 - L16EmptyLauncherX, 2) + Math.Pow(f.LocY / 16 - L16EmptyLauncherY, 2));
                        if (flagTiles < 0 || d < flagTiles) { flagTiles = d; flagAt = (f.LocX / 16) + "," + (f.LocY / 16); }
                    }
                string byType = string.Join(", ", w.Objects.Where(o => o.Kind == ZKind.Cannon && o.Owner == Team.Neutral)
                    .GroupBy(o => o.Type).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()));
                bool made = mc != null && mc.Type == "missile_cannon" && mc.Owner == Team.Neutral && mc.Drivers.Count == 0 && old == null && flagTiles >= 3.0;
                Func<string> run = () =>
                {
                    var r = ZWorld.Create(SettingsText, m16, 7);
                    r.Bots.Add(new ZBot(r, Team.Red, 7)); r.Bots.Add(new ZBot(r, Team.Blue, 108));
                    for (int i = 0; i < 600; i++) r.Tick(ZConst.TICK);
                    return string.Join(";", r.Objects.Select(o => o.Id + ":" + o.LocX + "," + o.LocY + "," + o.Health + "," + (o.Destroyed ? 1 : 0)));
                };
                bool det = run() == run();
                pass = made && det;
                measured = "tile " + L16EmptyLauncherX + "," + L16EmptyLauncherY + ": " + (mc == null ? "nothing" : mc.Owner + " " + mc.Type + ", crew " + mc.Drivers.Count) +
                           "; old tile " + L16EmptyGunOldX + "," + L16EmptyGunOldY + ": " + (old == null ? "free" : old.Owner + " " + old.Type) +
                           "; nearest flag " + flagAt + " at " + flagTiles.ToString("F1") + " tiles (was 2.0 from the old tile)" +
                           "; level 16 neutral cannons: " + byType + "; replay (600 ticks, both computer players) " + (det ? "identical" : "DIFFERENT");
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            list.Add(new Deviation
            {
                id = "DV114", change = "Level 16: the empty gun on the right side beside the red fort is an empty missile cannon, set a little further from its flag",
                zod = "the jungle map has a neutral (empty) gun on tile 83,109, two tiles from its zone's flag (the map file)",
                openz = "the level data of level 16: that entry is a neutral missile_cannon on tile " + L16EmptyLauncherX + "," + L16EmptyLauncherY +
                        " (two tiles west, one north: open ground, off the road, the same zone); empty like every map cannon without an owner, any robot can climb in; no rule code",
                measured = measured,
                toggle = "none (level data)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
