using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The level data's cannons on a fort's four turret spots (fort tiles (1,0) (7,0) (1,3) (7,3), bfort.cpp:534-548) and the scene:
    /// a cannon of the level data that has no object in the level's scene gets its view at the start, the same way the Hard door-tower
    /// cannons get theirs (ZGame, DV73): SpawnUnitView, seated on its tower pad by UnitView. A cannon object of the scene that stands on
    /// a turret spot where the level data no longer has a cannon of its type (the entry moved to another tower or changed its type) is
    /// hidden, so a tower never shows two guns or a gun the rules do not have. Today: Level 16, the blue fort (row DV99): the east door
    /// tower's howitzer stands on the east back tower (tile 54,9; no scene object there: a view is made) and the east door tower
    /// (tile 54,12) carries a missile cannon (a view is made; the scene's howitzer object of that tower is hidden). Every other tower
    /// cannon of every level keeps its scene object.
    /// </summary>
    public static class OzTowerCannonViews
    {
        /// <summary>Views made by the last call, scene objects hidden by it (for the checks and the log).</summary>
        public static int Spawned, Hidden;
        static readonly (int x, int y)[] Spots = { (16, 0), (112, 0), (16, 48), (112, 48) };   // px from the fort's corner (ZWorld.MarkFortTurretCannons)

        public static void Spawn(ZGame g)
        {
            Spawned = Hidden = 0;
            if (g == null || g.World == null) return;
            var zroot = GameObject.Find("ZodObjects");
            var bound = new HashSet<GameObject>();
            foreach (var v in g.Views.Values) if (v) bound.Add(v.gameObject);
            var todo = new List<(ZObject c, ZObject f)>();
            foreach (var f in g.World.Objects)
            {
                if (f.Kind != ZKind.Building || !f.IsFort) continue;
                foreach (var (ox, oy) in Spots)
                {
                    int px = f.LocX + ox, py = f.LocY + oy;
                    var types = new HashSet<string>();
                    foreach (var c in g.World.Objects)
                    {
                        if (c.Kind != ZKind.Cannon || c.Destroyed || c.LocX != px || c.LocY != py) continue;
                        types.Add(c.Type);
                        if (c.Source != null && !g.Views.ContainsKey(c.Id)) todo.Add((c, f));
                    }
                    if (!zroot) continue;
                    foreach (var ct in ZBuildLists.Cannons)
                    {
                        if (types.Contains(ct)) continue;
                        var t = zroot.transform.Find($"{ct}_{px / 16}_{py / 16}");
                        if (!t || !t.gameObject.activeSelf || bound.Contains(t.gameObject)) continue;
                        t.gameObject.SetActive(false); Hidden++;
                        Debug.Log($"[OpenZ] tower {px / 16},{py / 16} of the {f.Owner} {f.Type}: the scene's {ct} hidden (the level data has no {ct} there)");
                    }
                }
            }
            foreach (var (c, f) in todo)
            {
                if (g.Views.ContainsKey(c.Id) || g.SpawnUnitView(c) == null) continue;
                Spawned++;
                Debug.Log($"[OpenZ] tower cannon of the level data: {c.Owner} {c.Type} on the {f.Owner} {f.Type} tower at tile {c.LocX / 16},{c.LocY / 16} (view made at the start)");
            }
            FieldCannons(g, zroot);
        }

        /// <summary>Views made / scene objects hidden by the field pass of the last call (for the checks and the log).</summary>
        public static int FieldSpawned, FieldHidden;

        /// <summary>The same rule for the cannons in the field: a cannon of the level data with no object in the level's scene gets its
        /// view at the start, and a cannon object of the scene that is bound to no cannon and on whose tile the level data has no cannon
        /// of its type any more (the entry moved or changed its type) is hidden, so the field never shows a gun the rules do not have.
        /// Today: Level 16, the empty gun of tile 83,109 is an empty missile cannon on tile 81,108 in the level data (row DV114).</summary>
        static void FieldCannons(ZGame g, GameObject zroot)
        {
            FieldSpawned = FieldHidden = 0;
            var have = new HashSet<string>();
            foreach (var c in g.World.Objects)
            {
                if (c.Kind != ZKind.Cannon || c.Destroyed) continue;
                have.Add($"{c.Type}_{c.LocX / 16}_{c.LocY / 16}");
                if (c.Source != null) have.Add($"{c.Type}_{c.Source.x}_{c.Source.y}");
            }
            if (zroot)
            {
                var bound = new HashSet<GameObject>();
                foreach (var v in g.Views.Values) if (v) bound.Add(v.gameObject);
                foreach (Transform t in zroot.transform)
                {
                    if (!t.gameObject.activeSelf || bound.Contains(t.gameObject) || have.Contains(t.name)) continue;
                    foreach (var ct in ZBuildLists.Cannons)
                    {
                        if (!t.name.StartsWith(ct + "_")) continue;
                        var xy = t.name.Substring(ct.Length + 1).Split('_');
                        if (xy.Length != 2 || !int.TryParse(xy[0], out int tx) || !int.TryParse(xy[1], out int ty)) continue;
                        t.gameObject.SetActive(false); FieldHidden++;
                        Debug.Log($"[OpenZ] field cannon: the scene's {ct} at tile {tx},{ty} hidden (the level data has no {ct} there)");
                        break;
                    }
                }
            }
            foreach (var c in g.World.Objects)
            {
                if (c.Kind != ZKind.Cannon || c.Destroyed || c.Source == null || g.Views.ContainsKey(c.Id)) continue;
                if (g.SpawnUnitView(c) == null) continue;
                FieldSpawned++;
                Debug.Log($"[OpenZ] field cannon of the level data: {c.Owner} {c.Type} at tile {c.LocX / 16},{c.LocY / 16} (no scene object: view made at the start)");
            }
        }
    }
}
