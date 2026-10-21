#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;

namespace OpenZ.EditorTools
{
    public static partial class OpenZParity
    {
        const string L16MapDvId = "DV86";
        [Serializable] sealed class L16GridDoc { public int width, height; public int[] classes; }

        /// <summary>Level 16: the two deliberate differences of its level data from the original map file (stream steps, standing scenery).</summary>
        static void AddL16MapDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            try
            {
                var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("OpenZ/Maps/L16_grid");
                var g = UnityEngine.JsonUtility.FromJson<L16GridDoc>(ta.text);
                int[] n = new int[5]; foreach (int c in g.classes) if (c >= 0 && c < 5) n[c]++;
                int steps = 0; var st = OzL16Proof.StepTiles;
                for (int i = 0; i < st.Length; i += 2) if (g.classes[st[i + 1] * g.width + st[i]] == 2) steps++;
                int closed = n[3] - 1601;
                pass = steps == st.Length / 2 && n[1] == 1142 && n[0] + n[2] + n[3] == 9224 + 1345 + 1601 && closed >= 0;
                measured = $"level data {g.width} x {g.height}: {steps} of {st.Length / 2} step tiles are shallow water; {closed} tiles closed in the level data under standing scenery (the trees close their tiles as map objects of the rules now, DV88); ground {n[0]} (map file 9224), road {n[1]} (1142), shallow water {n[2]} (1345), impassable {n[3]} (1601); the map file itself converts byte for byte (level data check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            list.Add(new Deviation
            {
                id = L16MapDvId, change = "Level 16: stream steps closed to vehicles; standing trees close their tiles until they are destroyed (DV88)",
                zod = "the map file's own tile classes: every ground tile carries vehicles and robots, the jungle streams and rock steps are drawn tiles without a rule, trees and ruins of the tile art block nothing (zmap.cpp Read, the planet's tileinfo)",
                openz = "the level data of level 16 changes tile classes at the eight rock steps of the two streams (and, DV119, the swamp channel west of the gatling island): they are shallow water (robots only), so vehicles ford the streams elsewhere (no rule code: the converter writes the classes); " +
                        "the tile under a standing tree or mangrove of the dressed level is closed as well, each accepted only if closing it cuts nothing off (never a road tile, never at a building, flag, gun, start unit, bridge or step; robots and 2 x 2 vehicles keep every place they reached): " +
                        "since DV88 by a map object of the rules that can be destroyed, which opens the tile again, no longer by a class of the level data",
                measured = measured,
                toggle = "none (level data)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
