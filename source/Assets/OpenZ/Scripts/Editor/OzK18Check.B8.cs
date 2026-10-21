using System.Linq;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>Kit K18b (the blue fort's towers on level 16, row DV99): the level check's line L13. The east door tower (tile 54,12)
    /// holds the blue missile cannon, the east back tower (tile 54,9) the blue howitzer, the west door tower (tile 48,12) its howitzer,
    /// each with its view on its tower pad, and the scene's own howitzer object of the east door tower is hidden (one gun per tower).</summary>
    public static partial class OzK18Check
    {
        static partial void TurretLine(ZGame g, ref bool done)
        {
            if (g == null || g.World == null || g.level != "L16") return;
            string Tower(int tx, int ty, string type, out bool good)
            {
                var c = g.World.Objects.FirstOrDefault(o => o.Kind == ZKind.Cannon && o.LocX == tx * 16 && o.LocY == ty * 16);
                UnitView v = null; bool hasView = c != null && g.Views.TryGetValue(c.Id, out v) && v && v.gameObject.activeInHierarchy;
                good = c != null && c.Type == type && c.Owner == Team.Blue && !c.Ejectable && hasView && v.OnTower;
                return $"{tx},{ty} {(c == null ? "no cannon" : c.Owner + " " + c.Type + (c.Destroyed ? " DESTROYED" : "") + ", view " + (hasView ? v.name + " on the pad " + v.OnTower : "NONE"))}";
            }
            string a = Tower(54, 12, "missile_cannon", out bool okA), b = Tower(54, 9, "howitzer", out bool okB), c2 = Tower(48, 12, "howitzer", out bool okC);
            var root = GameObject.Find("ZodObjects");
            var old = root ? root.transform.Find("howitzer_54_12") : null;
            bool oldHidden = old == null || !old.gameObject.activeInHierarchy;
            Check(okA && okB && okC && oldHidden,
                  $"L13 blue fort towers: east door {a}; east back {b}; west door {c2}; the scene's howitzer of the east door tower {(old == null ? "not in the scene" : oldHidden ? "hidden" : "STILL SHOWN")}; " +
                  $"tower views made at the start {OzTowerCannonViews.Spawned}, scene tower objects hidden {OzTowerCannonViews.Hidden}");
            done = true;
        }
    }
}
