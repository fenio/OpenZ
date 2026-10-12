using System;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// yellow circle): every destructible details_v2 prop is a neutral sim map item (ZWorld.DetailProps.cs) built from the same
    /// placement the drawn props use (ZDetailPropsData). Measured headless on L02 and L17 once on and once off (Zod): props created,
    /// tiles blocked, every Zod object identical (id + position), and a determinism replay (two worlds, same seed, 600 ticks with the
    /// bots on, identical object state).
    /// </summary>
    public static partial class OpenZParity
    {
        static Deviation DevDetailProps(Func<string, int, int> getInt)
        {
            bool pref = getInt("oz_zod_exact", 0) == 0;
            bool was = ZWorld.DetailPropsDeviation;
            try
            {
                string Probe(string lv, out bool okLv)
                {
                    ZWorld.DetailPropsDeviation = true;
                    var w = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    int expect = (lv == "L02" ? ZDetailPropsData.L02 : ZDetailPropsData.L17).Uids.Length;
                    int hp = w.DetailProps.Count > 0 ? w.DetailProps[0].MaxHealth : 0;
                    bool expl = w.DetailProps.All(o => o.AttackedByExplosives && o.Owner == Team.Neutral);
                    ZWorld.DetailPropsDeviation = false;
                    var z = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    bool same = z.Objects.Count == w.Objects.Count - w.DetailProps.Count
                                && z.Objects.Zip(w.Objects.Where(o => o.Type != ZWorld.DetailPropType), (a, b) => a.Id == b.Id && a.LocX == b.LocX && a.LocY == b.LocY && a.Type == b.Type).All(x => x);
                    // determinism: the same world twice, bots on both sides, 600 ticks
                    ZWorld.DetailPropsDeviation = true;
                    string Run()
                    {
                        var r = ZWorld.Create(SettingsText, MapText(lv), 7, lv == "L17");
                        r.Bots.Add(new ZBot(r, Team.Red, 7)); r.Bots.Add(new ZBot(r, Team.Blue, 108));
                        for (int i = 0; i < 600; i++) r.Tick(ZConst.TICK);
                        return string.Join(";", r.Objects.Select(o => $"{o.Id}:{o.LocX},{o.LocY},{o.Health},{(o.Destroyed ? 1 : 0)}"));
                    }
                    bool det = Run() == Run();
                    okLv = w.DetailProps.Count == expect && z.DetailProps.Count == 0 && same && expl && det;
                    return $"{lv}: {w.DetailProps.Count}/{expect} props, {w.DetailPropBlockedTiles} tile(s) newly blocked, {hp} HP, explosives only {(expl ? "yes" : "NO")}; " +
                           $"off: {z.DetailProps.Count} props, every Zod object identical {(same ? "yes" : "NO")}; determinism (600 ticks, bots) {(det ? "identical" : "DIFFERENT")}";
                }
                string a = Probe("L02", out bool okA), b = Probe("L17", out bool okB);
                bool ok = okA && okB;
                return new Deviation
                {
                    id = "DV67", change = "Destructible details (wind-pump, signs, palms, cacti, stones, shrubs, traffic lights, road-works signal, bus stop, neon, hydrants, street signs) are attackable map items; the wind-pump stands on open ground at the L02 east shore",
                    zod = "no such objects (OpenZ's own decor); the L02 tile (38, 56) at the east shore is open ground",
                    openz = "ZWorld.DetailProps.cs: neutral map items \"detail_prop\" from ZDetailPropsData (generated from placement_L02/L17.json, the drawn props' own placement), " +
                            "Zod map-object HP, attacked by explosives only (rifles cannot hurt them, like a cactus), hurt by missile splash, no sight block, no death debris, never a bot " +
                            "or auto target; only tiles that were passable at load are blocked (destroyable, like a rock): today only the wind-pump's tile (38, 56); destroyed -> the tile " +
                            "is ground again (flat ruins < 0.3 m). The view binds each prop to its item (DetailPropHub), takes no view damage (knock only) and breaks on the sim death. " +
                            "The tumbleweeds stay view only (they roll): shots and splash still burst them, they cannot be ordered as targets",
                    measured = a + " | " + b,
                    toggle = "follows 'Zod exact' (oz_zod_exact = 1 turns it off); bot matches use the same switch",
                    status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
                };
            }
            finally { ZWorld.DetailPropsDeviation = was; }
        }
    }
}
