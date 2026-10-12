using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// A destroyed building or bridge burns for a while: a few small ground fires and a few smoke columns in the ruin, burning
    /// BurnMin to BurnMax seconds (from the building's seed), then dying down over FadeOut seconds, the smoke staying a little
    /// longer. The fort is not here: it has its own blaze.
    /// <list type="bullet">
    /// <item><b>Fire</b> = the burning ground's flame tongue, drawn only (OpenZPyroFire.Tongue: a quad in the ground fire's one
    /// mesh, handed in every frame, kept nowhere). Three tongues make one fire. No ground patch is lit, nothing the rules read:
    /// it cannot hurt a unit.</item>
    /// <item><b>Smoke</b> = the burning wreck's column (the effect player's leg_smoke puffs), dark while it burns, grey and
    /// thinner afterwards; one pooled fire light per burning ruin.</item>
    /// <item><b>Where</b>: a building hands in spots on its ruin floor (BuildingBreaker.RuinFire.cs); a bridge gets them here, on
    /// dry ground at its two broken ends and on the banks next to them, never over open water.</item>
    /// <item><b>Sound</b>: a destroyed building already has the fire crackle loop of the sound director; a burning bridge gets a
    /// quiet one here (same loop, at most MaxBridgeLoops at once), with a line in the sound log.</item>
    /// <item><b>Caps</b>: FiresPerRuin / FiresPerBridge and SmokesPer... per ruin, MaxFires and MaxSmokes for all ruins together;
    /// a ruin that comes when the caps are full gets what is left.</item>
    /// <item><b>End</b>: when it has burnt out the entry is dropped; End(id) drops it at once (a crane starts on it, it is
    /// rebuilt or repaired). The object itself belongs to the level and goes with it.</item>
    /// </list>
    /// Play check: Readout(), Sites, FiresNow, SmokesNow, TonguesDrawn, Started, Ended, Skipped.
    /// </summary>
    public sealed class OzRuinFires : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>Ground fires and smoke columns of one ruin / one bridge; tongues in one fire.</summary>
        public const int FiresPerRuin = 4, FiresPerBridge = 4, SmokesPerRuin = 3, SmokesPerBridge = 2, TonguesPerFire = 3;
        /// <summary>All burning ruins together: six ruins at once get everything (24 fires = 72 tongues of the ground fire's loose
        /// tongue budget, 18 columns = about 160 smoke puffs in the air); a ruin beyond that gets what is left, or nothing.</summary>
        public const int MaxFires = 24, MaxSmokes = 18;
        /// <summary>Seconds a ruin burns (seeded per building between the two); a fire's fade in, and its dying down after its time;
        /// how much longer than the last flame the smoke goes on, thinning over SmokeFade; the wait after the blast; the fires start
        /// within Stagger seconds.</summary>
        public const float BurnMin = 12f, BurnMax = 18f, FadeIn = 0.7f, FadeOut = 2.5f, SmokeExtra = 5f, SmokeFade = 4f, StartDelay = 0.9f, Stagger = 2.2f;
        /// <summary>Tongue height and width (a ground fire patch uses up to 0.85 / 0.8); smoke puff size (m).</summary>
        public static float FlameHeight = 0.8f, FlameWidth = 0.8f, SmokeSize = 2.6f;
        /// <summary>A bridge's ground fires: how far onto the bank beyond each end (m), how far in from each end (share of its length).</summary>
        public const float BridgeBank = 2.2f, BridgeEnd = 0.3f;
        public const float BridgeLoopVol = 0.5f;
        public const int MaxBridgeLoops = 2;

        public static OzRuinFires I { get; private set; }
        /// <summary>Play check: burning ruins, their fires and smoke columns now, tongues handed to the ground fire in the last frame.</summary>
        public static int Sites => I ? I.sites.Count : 0;
        public static int FiresNow => I ? I.Count(true) : 0;
        public static int SmokesNow => I ? I.Count(false) : 0;
        public static int TonguesDrawn { get; private set; }
        /// <summary>Play check, counted since the program started: ruins set on fire, ended (burnt out or put out), not lit (caps
        /// full or no dry spot).</summary>
        public static int Started { get; private set; }
        public static int Ended { get; private set; }
        public static int Skipped { get; private set; }

        struct Fire { public Vector3 root; public float seed, size, speed, t0, t1; }
        struct Smoke { public Vector3 at; public float t0, t1, next, phase; }
        sealed class Site
        {
            public int id; public bool bridge, lit, loop, loopDone; public ZObject obj;
            public float start, burn, end, nextLight; public Vector3 centre;
            public readonly List<Fire> fires = new List<Fire>(); public readonly List<Smoke> smokes = new List<Smoke>();
        }
        readonly List<Site> sites = new List<Site>();
        int loopsOn;

        static OzRuinFires Ensure()
        {
            if (!I) new GameObject("OzRuinFires").AddComponent<OzRuinFires>();
            return I;
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this; TonguesDrawn = 0;
        }

        void OnDestroy()
        {
            if (I != this) return;
            for (int n = sites.Count - 1; n >= 0; n--) StopLoop(sites[n], 0.3f, "level closed");
            sites.Clear(); I = null;
        }

        int Count(bool fires) { int c = 0; foreach (var s in sites) c += fires ? s.fires.Count : s.smokes.Count; return c; }

        static string LoopKey(int id) => "ruin_fire_" + id;

        /// <summary>A destroyed building: `spots` = points on its ruin floor, the first ones take the fires (BuildingBreaker.RuinFireSpots).</summary>
        public static void Building(ZObject o, List<Vector3> spots)
        {
            if (!Enabled || o == null || spots == null) return;
            if (spots.Count == 0) { Skipped++; return; }
            var me = Ensure(); if (!me) return;
            var s = me.Open(o, false);
            if (!me.Kindle(s, spots)) { Skipped++; me.sites.Remove(s); }                                // the caps are full
        }

        /// <summary>A bridge destroyed just now. Its spots are looked for when the fire starts (StartDelay after the blast), on the
        /// ground as it is then: the deck is gone, so only the banks and the broken ends count as dry.</summary>
        public static void Bridge(ZObject o)
        {
            if (!Enabled || o == null) return;
            var me = Ensure(); if (!me) return;
            me.Open(o, true);
        }

        /// <summary>The ruin's fire goes at once (a crane starts on it, it is rebuilt or repaired). Smoke already in the air drifts off.</summary>
        public static void End(int id)
        {
            if (!I) return;
            for (int n = I.sites.Count - 1; n >= 0; n--) if (I.sites[n].id == id) I.Close(n, "put out");
        }

        Site Open(ZObject o, bool bridge)
        {
            for (int n = sites.Count - 1; n >= 0; n--) if (sites[n].id == o.Id) Close(n, "destroyed again");
            var rnd = new System.Random(o.Id * 9973 + 39 + 404);
            var s = new Site { id = o.Id, obj = o, bridge = bridge, start = Time.time + StartDelay, burn = BurnMin + (BurnMax - BurnMin) * (float)rnd.NextDouble() };
            s.end = s.burn + FadeOut + SmokeExtra;
            sites.Add(s);
            return s;
        }

        /// <summary>Gives the site its fires and smoke columns from the spots, within the caps. False = nothing could be lit.</summary>
        bool Kindle(Site s, List<Vector3> spots)
        {
            s.lit = true;
            var rnd = new System.Random(s.id * 9973 + 39 + 405);
            int fires = Mathf.Min(s.bridge ? FiresPerBridge : FiresPerRuin, Mathf.Min(spots.Count, MaxFires - Count(true)));
            int smokes = Mathf.Min(s.bridge ? SmokesPerBridge : SmokesPerRuin, Mathf.Min(spots.Count, MaxSmokes - Count(false)));
            if (fires <= 0 && smokes <= 0) return false;
            var sum = Vector3.zero;
            for (int i = 0; i < fires; i++)
            {   // the first fire starts at once and burns the whole time; the others start within the first seconds and die down one after another before it
                float t0 = i == 0 ? 0f : Stagger * (float)rnd.NextDouble(), t1 = i == 0 ? s.burn : s.burn * (0.7f + 0.3f * (float)rnd.NextDouble());
                s.fires.Add(new Fire { root = spots[i], seed = (float)rnd.NextDouble(), size = 0.9f + 0.5f * (float)rnd.NextDouble(), speed = 0.5f + 0.5f * (float)rnd.NextDouble(), t0 = t0, t1 = t1 });
                sum += spots[i];
            }
            for (int i = 0; i < smokes; i++)
            {   // one column over the first fire, the others on spots of their own (smouldering rubble) while there are any
                int k = i == 0 ? 0 : fires + i - 1 < spots.Count ? fires + i - 1 : i % spots.Count;
                s.smokes.Add(new Smoke { at = spots[k], t0 = 0.3f + 1.5f * (float)rnd.NextDouble(), t1 = s.burn + FadeOut + SmokeExtra * (0.6f + 0.4f * (float)rnd.NextDouble()), phase = 6.28f * (float)rnd.NextDouble() });
                if (fires == 0) sum += spots[k];
            }
            s.centre = sum / Mathf.Max(1, fires > 0 ? fires : smokes);
            Started++;
            Debug.Log($"[OpenZ] ruin fire #{s.id} ({(s.bridge ? "bridge" : s.obj != null ? s.obj.Type : "building")}): {fires} fires, {smokes} smoke columns, burns {s.burn:0.0} s, out at {s.burn + FadeOut:0.0} s, smoke to {s.end:0.0} s; all ruins: {Count(true)} of {MaxFires} fires, {Count(false)} of {MaxSmokes} columns");
            return true;
        }

        void Close(int n, string why)
        {
            var s = sites[n];
            StopLoop(s, 0.4f, why);
            if (s.lit && (s.fires.Count > 0 || s.smokes.Count > 0))
            {
                Ended++;
                Debug.Log($"[OpenZ] ruin fire #{s.id}: {why} after {Mathf.Max(0f, Time.time - s.start):0.0} s");
            }
            sites.RemoveAt(n);
        }

        void StopLoop(Site s, float fade, string why)
        {
            if (!s.loop) return;
            s.loop = false; s.loopDone = true; loopsOn = Mathf.Max(0, loopsOn - 1);
            if (AudioDirector.I) AudioDirector.I.StopLoop(LoopKey(s.id), fade);
            OzSoundLog.Add("RF", "dst_fire_loop", null, "STOP loop " + LoopKey(s.id) + " (" + why + ")", 0f, s.centre);
        }

        /// <summary>Dry ground at the bridge's two broken ends and on the banks next to them, the two ends in turn. Empty when
        /// there is none (the fire is then left out).</summary>
        static List<Vector3> BridgeSpots(ZObject o)
        {
            var spots = new List<Vector3>(); var rnd = new System.Random(o.Id * 9973 + 39 + 406);
            Vector3 a = ZGame.PxToWorld(o.LocX, o.LocY), c = ZGame.PxToWorld(o.LocX + o.WidthPix, o.LocY + o.HeightPix);
            bool vert = o.Type == "bridge_vert";                                                     // crossed along the map's y (world z)
            float x0 = Mathf.Min(a.x, c.x), x1 = Mathf.Max(a.x, c.x), z0 = Mathf.Min(a.z, c.z), z1 = Mathf.Max(a.z, c.z);
            float len = vert ? z1 - z0 : x1 - x0, wide = vert ? x1 - x0 : z1 - z0;
            int want = FiresPerBridge + SmokesPerBridge;
            for (int t = 0; t < want * 14 && spots.Count < want; t++)
            {
                bool far = (t & 1) == 1;
                float along = Mathf.Lerp(-BridgeBank, BridgeEnd * len, (float)rnd.NextDouble()), across = Mathf.Lerp(-0.6f, wide + 0.6f, (float)rnd.NextDouble());
                float u = far ? len - along : along;
                float x = vert ? x0 + across : x0 + u, z = vert ? z0 + u : z0 + across;
                if (!OpenZPyroFire.DrySpot(x, z, out var g)) continue;                               // open water: nothing burns there
                bool near = false;
                for (int i = 0; i < spots.Count && !near; i++) near = (spots[i] - g).sqrMagnitude < 1.44f;
                if (!near) spots.Add(g + Vector3.up * 0.06f);
            }
            return spots;
        }

        void Update()
        {
            TonguesDrawn = 0;
            if (sites.Count == 0) return;
            float now = Time.time; var fxp = OpenZFxPlayer.I; bool fx = fxp && fxp.isActiveAndEnabled, tongues = OpenZPyroFire.TonguesReady;
            for (int n = sites.Count - 1; n >= 0; n--)
            {
                var s = sites[n]; float age = now - s.start;
                if (age < 0f) continue;                                                                 // still under the blast and the dust
                if (!s.lit)
                {   // a bridge: its spots on the ground as it is now
                    if (s.obj == null || !s.obj.Destroyed || !Kindle(s, BridgeSpots(s.obj))) { Skipped++; sites.RemoveAt(n); continue; }
                }
                if (age > s.end) { Close(n, "burnt out"); continue; }
                float hot = 0f;
                for (int i = 0; i < s.fires.Count; i++)
                {
                    var f = s.fires[i];
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Min((age - f.t0) / FadeIn, (f.t1 + FadeOut - age) / FadeOut));   // full until its time is up, then down over FadeOut
                    if (k <= 0.001f) continue;
                    if (k > hot) hot = k;
                    if (!tongues) continue;
                    // one fire = the tongue and two smaller ones a hand away, each on its own cycle
                    float a = f.seed * 40f;
                    OpenZPyroFire.Tongue(f.root, f.seed, f.size, f.speed, k, FlameHeight, FlameWidth);
                    OpenZPyroFire.Tongue(f.root + new Vector3(Mathf.Cos(a) * 0.28f, 0f, Mathf.Sin(a) * 0.28f), Mathf.Repeat(f.seed + 0.37f, 1f), f.size * 0.75f, f.speed * 1.25f, k, FlameHeight * 0.75f, FlameWidth);
                    OpenZPyroFire.Tongue(f.root + new Vector3(Mathf.Cos(a + 2.4f) * 0.34f, 0f, Mathf.Sin(a + 2.4f) * 0.34f), Mathf.Repeat(f.seed + 0.71f, 1f), f.size * 0.6f, f.speed * 1.4f, k, FlameHeight * 0.6f, FlameWidth);
                    TonguesDrawn += TonguesPerFire;
                }
                if (fx)
                {
                    bool burning = age < s.burn + FadeOut;
                    for (int i = 0; i < s.smokes.Count; i++)
                    {
                        var m = s.smokes[i];
                        if (age < m.t0 || age > m.t1 || now < m.next) continue;
                        float k = Mathf.Clamp01((m.t1 - age) / SmokeFade), ph = Mathf.Sin(m.phase + now * 3.7f);
                        m.next = now + Mathf.Lerp(0.6f, 0.3f, k); s.smokes[i] = m;
                        fxp.EmitChannel("leg_smoke", m.at + new Vector3(ph * 0.3f, 0.4f, Mathf.Cos(m.phase * 1.3f + now * 3.1f) * 0.3f),
                            new Vector3(ph * 0.25f + 0.15f, 1.3f + Random.value * 0.6f, -ph * 0.2f), 2.4f + Random.value * 0.6f,
                            SmokeSize * (0.78f + 0.44f * Random.value) * Mathf.Lerp(0.6f, 1f, k), burning ? OpenZFxCatalog.WreckBurnSmoke : OpenZFxCatalog.WreckSmokeGrey, 0.85f * Mathf.Lerp(0.55f, 1f, k));
                    }
                    if (hot > 0.05f && fxp.pointLights && now >= s.nextLight && s.fires.Count > 0)
                    {   // the flickering fire light of a burning wreck, one per ruin, at its first fire
                        s.nextLight = now + 0.25f;
                        fxp.AddLight(s.fires[0].root + Vector3.up * 1.1f, OpenZFxCatalog.WreckFireLight.color, OpenZFxCatalog.BurnLightThree * 1.3f * hot, OpenZFxCatalog.BurnLightRange * 0.9f, 0.3f, 0.5f, 0.2f, false);
                    }
                }
                if (s.bridge) BridgeLoop(s, hot);
            }
        }

        /// <summary>The quiet crackle of a burning bridge: on with its first flame, down with the flames, off when the last one is out.</summary>
        void BridgeLoop(Site s, float hot)
        {
            var ad = AudioDirector.I; if (!ad) return;
            string key = LoopKey(s.id);
            if (s.loop)
            {
                if (hot <= 0.05f) { StopLoop(s, 2f, "burnt out"); return; }
                if (!ad.LoopActive(key)) { s.loop = false; loopsOn = Mathf.Max(0, loopsOn - 1); return; }   // the director dropped it: it may start again
                ad.SetLoop(key, BridgeLoopVol * Mathf.Lerp(0.4f, 1f, hot), 1f);
                return;
            }
            if (s.loopDone || hot <= 0.05f || loopsOn >= MaxBridgeLoops || !AudioDirector.Has("dst_fire_loop")) return;
            if (ad.StartLoop(key, "dst_fire_loop", s.centre + Vector3.up * 0.5f, null, BridgeLoopVol) <= 0) { s.loopDone = true; return; }
            s.loop = true; loopsOn++;
            OzSoundLog.Add("RF", "dst_fire_loop", null, "PLAY loop " + key + " (burning bridge)", BridgeLoopVol, s.centre);
        }

        /// <summary>Play check, one line: the totals and every burning ruin with its age.</summary>
        public static string Readout()
        {
            if (!I) return "ruin fires: none yet (started " + Started + ", ended " + Ended + ", not lit " + Skipped + ")";
            var sb = new System.Text.StringBuilder();
            sb.Append("ruin fires: ").Append(I.sites.Count).Append(" burning, fires ").Append(I.Count(true)).Append(" of ").Append(MaxFires)
              .Append(", smoke columns ").Append(I.Count(false)).Append(" of ").Append(MaxSmokes).Append(", tongues drawn ").Append(TonguesDrawn)
              .Append(", bridge loops ").Append(I.loopsOn).Append("; started ").Append(Started).Append(", ended ").Append(Ended).Append(", not lit ").Append(Skipped);
            foreach (var s in I.sites)
                sb.Append(" | #").Append(s.id).Append(s.bridge ? " bridge " : " building ").Append(s.fires.Count).Append(" fires ").Append(s.smokes.Count).Append(" columns, ")
                  .Append((Time.time - s.start).ToString("0.0")).Append(" of ").Append(s.burn.ToString("0.0")).Append(" s (smoke to ").Append(s.end.ToString("0.0")).Append(")");
            return sb.ToString();
        }
    }
}
