#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV99 "level 16: the blue fort's towers: a missile cannon on the east door tower, its howitzer moved to
    /// the east back tower" (level data only: in Resources/OpenZ/Maps/L16_grid.json the blue howitzer entry of tile 54,12 stands on
    /// 54,9 and one more map cannon, a blue missile cannon, stands on 54,12; no rule code). Measured headless: the four turret spots of
    /// both forts (what stands on each), the moved howitzer and the new missile cannon (type, owner, crew, not ejectable like every
    /// tower cannon), Hard (DV73) finds the missile cannon already on the east door tower and adds none, a replay of 600 ticks with
    /// both computer players is identical twice, and levels 2 and 17 are untouched (no missile cannon at their start). The DV73 switch
    /// is put back afterwards.</summary>
    public static partial class OpenZParity
    {
        /// <summary>The owner's layout: the missile cannon on the east door tower, the howitzer of that tower on the east back tower.</summary>
        internal const int L16MissileX = 54, L16MissileY = 12, L16BackX = 54, L16BackY = 9;
        internal const string L16TurretType = "missile_cannon";

        static void AddL16FortTurretDeviation(List<Deviation> list)
        {
            string measured; bool pass; bool was = ZWorld.FortDoorGunDeviation;
            try
            {
                string m16 = MapText("L16");
                ZWorld.FortDoorGunDeviation = false; var w = ZWorld.Create(SettingsText, m16, 1);
                ZObject At(ZWorld ww, int tx, int ty) => ww.Objects.FirstOrDefault(o => o.Kind == ZKind.Cannon && !o.Destroyed && o.LocX == tx * 16 && o.LocY == ty * 16);
                string Spots(ZWorld ww, Team team)
                {
                    var f = ww.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.IsFort && o.Owner == team);
                    if (f == null) return team + " fort: none";
                    var parts = new List<string>();
                    foreach (var (ox, oy) in new[] { (1, 0), (7, 0), (1, 3), (7, 3) })
                    {
                        int tx = f.LocX / 16 + ox, ty = f.LocY / 16 + oy; var c = At(ww, tx, ty);
                        parts.Add($"({ox},{oy}) tile {tx},{ty}: {(c == null ? "free" : c.Owner + " " + c.Type + (c.Ejectable ? " EJECTABLE" : "") + " crew " + c.Drivers.Count)}");
                    }
                    return $"{team} {f.Type} at {f.LocX / 16},{f.LocY / 16}: " + string.Join(", ", parts);
                }
                var mc = At(w, L16MissileX, L16MissileY); var back = At(w, L16BackX, L16BackY); var west = At(w, 48, 12);
                bool made = mc != null && mc.Type == L16TurretType && mc.Owner == Team.Blue && !mc.Ejectable && mc.Drivers.Count > 0
                            && back != null && back.Type == "howitzer" && back.Owner == Team.Blue && !back.Ejectable && back.Drivers.Count == mc.Drivers.Count
                            && west != null && west.Type == "howitzer" && west.Owner == Team.Blue && At(w, 48, 9) == null
                            && back.Source != null && back.Source.y == L16BackY && mc.Id > back.Id;
                ZWorld.FortDoorGunDeviation = true; var hard = ZWorld.Create(SettingsText, m16, 1);
                int hardMissiles = hard.Objects.Count(o => o.Kind == ZKind.Cannon && o.Owner == Team.Blue && o.Type == L16TurretType && !o.Ejectable);
                var hb = At(hard, L16BackX, L16BackY);
                bool hardOk = hardMissiles == 1 && hard.FortDoorGuns.Count == 2 && hb != null && hb.Type == "howitzer" && hard.FortDoorGuns.Any(c => c.LocX == L16MissileX * 16 && c.LocY == L16MissileY * 16);
                ZWorld.FortDoorGunDeviation = false;
                string Run()
                {
                    var r = ZWorld.Create(SettingsText, m16, 7);
                    r.Bots.Add(new ZBot(r, Team.Red, 7)); r.Bots.Add(new ZBot(r, Team.Blue, 108));
                    for (int i = 0; i < 600; i++) r.Tick(ZConst.TICK);
                    return string.Join(";", r.Objects.Select(o => $"{o.Id}:{o.LocX},{o.LocY},{o.Health},{(o.Destroyed ? 1 : 0)}"));
                }
                bool det = Run() == Run();
                bool others = true; string otherText = "";
                foreach (var lv in new[] { "L02", "L17" })
                {
                    var a = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    int n = a.Objects.Count(o => o.Kind == ZKind.Cannon && o.Type == L16TurretType);
                    others &= n == 0; otherText += $"; level {lv.Substring(1)}: missile cannons at the start {n}";
                }
                pass = made && hardOk && det && others;
                measured = $"level 16 turret spots: {Spots(w, Team.Blue)}; {Spots(w, Team.Red)}; the owner's layout {(made ? "is there" : "is NOT right")} " +
                           $"(east door tower {(mc == null ? "none" : mc.Owner + " " + mc.Type + ", crew " + mc.Drivers.Count + (mc.Ejectable ? ", EJECTABLE" : ", not ejectable"))}; " +
                           $"east back tower {(back == null ? "none" : back.Owner + " " + back.Type + " (the map's entry of the door tower, moved)")}); " +
                           $"Hard (DV73): blue missile cannons on the fort {hardMissiles} (the door tower already carries it), door-tower guns {hard.FortDoorGuns.Count}; replay (600 ticks, both computer players) {(det ? "identical" : "DIFFERENT")}{otherText}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.FortDoorGunDeviation = was; }
            list.Add(new Deviation
            {
                id = "DV99", change = "Level 16: the blue fort's east door tower carries a missile cannon, its howitzer stands on the east back tower",
                zod = "the jungle map's blue fort carries two howitzers on its two door towers (fort tiles (1,3) and (7,3)); its two back towers (1,0) and (7,0) are empty (the map file)",
                openz = $"the level data of level 16: the blue howitzer of the east door tower (tile 54,12) stands on the east back tower (tile {L16BackX},{L16BackY}, the same entry), and a blue {L16TurretType} " +
                        $"stands on the east door tower (tile {L16MissileX},{L16MissileY}, one more map cannon); the west door tower keeps its howitzer. Created by the map's own path like every tower cannon " +
                        "(the start crew of an owned cannon, not ejectable on a turret spot); no rule code; the view draws both on their tower pads and hides the scene's howitzer of the door tower. " +
                        "Hard (DV73) finds the missile cannon already on that tower and adds none",
                measured = measured,
                toggle = "none (level data)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
