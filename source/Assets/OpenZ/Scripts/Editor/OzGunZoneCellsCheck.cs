using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted test of the gun zone's cells (no scene, no Play mode; headless worlds of the three maps, each zone with a building):
    /// every drawn cell is a spot the placement rule allows (ZWorld.CannonPlacable), no two cells share a tile, every cell tile lies
    /// in the marked area, every joint "+" is a spot the rule allows centred on that corner, and an allowed ground spot is exactly a
    /// spot whose four tiles are marked (the rule the cells are built from). Pass = 0 violations.
    /// </summary>
    public static class OzGunZoneCellsCheck
    {
        static readonly string[] Levels = { "L02", "L16", "L17" };

        [MenuItem("OpenZ/Checks/Gun Placement: Zone Cells Counted Test (no Play mode)")]
        static void Run() => Debug.Log(Report(out _));

        public static string Report(out int violations)
        {
            violations = 0;
            var sb = new StringBuilder("GUN ZONE CELLS COUNTED TEST\n");
            string settings = Resources.Load<TextAsset>("OpenZ/default_settings").text;
            foreach (var lv in Levels)
            {
                var w = ZWorld.Create(settings, Resources.Load<TextAsset>("OpenZ/Maps/" + lv + "_grid").text, 1);
                var seen = new HashSet<int>();
                foreach (var b in w.Objects)
                {
                    if (b.Kind != ZKind.Building || w.ZoneOf(b) == null || !seen.Add(b.ZoneIndex)) continue;
                    var c = GunCover.Compute(w, b);
                    int n = c.zw * c.zh, spotsBad = 0;
                    var spot = new bool[n];
                    for (int j = 0; j < c.zh; j++)
                        for (int i = 0; i < c.zw; i++)
                        {
                            int tx = c.zx + i, ty = c.zy + j;
                            spot[j * c.zw + i] = c.Tile(tx, ty) && c.Tile(tx + 1, ty) && c.Tile(tx, ty + 1) && c.Tile(tx + 1, ty + 1);
                            bool pad = false; foreach (var p in c.pads) if (p.x == tx && p.y == ty) pad = true;
                            if (!pad && spot[j * c.zw + i] != w.CannonPlacable(b, tx, ty)) spotsBad++;
                        }
                    var cells = new GunCells();
                    cells.Build(c.zw, c.zh, spot, c.tile, ((b.LocX >> 4) - c.zx) & 1, ((b.LocY >> 4) - c.zy) & 1, true);
                    int bad = cells.Verify() + spotsBad;
                    foreach (int k in cells.cells) if (!w.CannonPlacable(b, c.zx + k % c.zw, c.zy + k / c.zw)) bad++;
                    foreach (int k in cells.joints) if (!w.CannonPlacable(b, c.zx + k % (c.zw + 1) - 1, c.zy + k / (c.zw + 1) - 1)) bad++;
                    cells.EncodeObjects(w.Objects, c.zx, c.zy);                                 // the objects texture (outline and brackets fade next to them)
                    int objBad = cells.VerifyObjects(w.Objects, c.zx, c.zy);
                    bad += objBad;
                    violations += bad;
                    sb.Append(bad == 0 ? "OK   " : "FAIL ").Append(lv).Append(" zone ").Append(b.ZoneIndex).Append(" (").Append(b.Type).Append("): allowed ground spots ")
                      .Append(c.validSpots - c.pads.Count).Append(", cells ").Append(cells.cells.Count).Append(" (lattice ").Append(cells.lattice).Append(", one tile off ").Append(cells.shifted)
                      .Append("), joints ").Append(cells.joints.Count).Append(", brackets ").Append(cells.bracketCorner.Count).Append(", area tiles in no cell ").Append(cells.loose)
                      .Append("; spots where 'four marked tiles' and the rule differ ").Append(spotsBad).Append("; object tiles on and round the zone ").Append(cells.objectTiles)
                      .Append(" (bits wrong against the objects the rule tests ").Append(objBad).Append("); violations ").Append(bad).Append('\n');
                }
                if (seen.Count == 0) { violations++; sb.Append("FAIL ").Append(lv).Append(": no zone with a building\n"); }
            }
            sb.Append(violations == 0 ? "GUN ZONE CELLS COUNTED TEST: PASS (0 violations)" : "GUN ZONE CELLS COUNTED TEST: FAIL (" + violations + " violations)");
            return sb.ToString();
        }
    }
}
