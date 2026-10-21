using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads, the view (the rules: Sim/ZHeroSquad.cs). View only: nothing here writes the sim.
    ///  * Models: each member is drawn with its own prefab Resources/OpenZ/HeroSquad/Z_hero_&lt;member&gt; (the real Alan and Brad on
    ///    humanoid rigs that play the grunt's unit animations: built by the editor menu OpenZ/Hero Squad/1 Build hero prefabs), in
    ///    the team's colour (<see cref="OzHeroLook"/>). Missing prefab: the grunt's own prefab, with a warning in the log.
    ///  * Placement: the level data's hero entries are EXTRA squads (Sim/ZHeroSquad.cs): nothing of the scene is hidden or replaced.
    ///    A hero made at run time (out of a vehicle, a gun or a troop carrier) is drawn as itself too (<see cref="SpawnView"/>).
    ///  * The campfire of the entry ("camp": [x, y]): <see cref="OzHeroCampfire"/>, made with the squad. With "start": "lounger" the
    ///    camp is laid out round it (<see cref="OzHeroCampPlan"/>): the two loungers side by side in a shallow V FACING the fire, a
    ///    clear gap of OzHeroCampPlan.CampGap m between the fire and their foot ends, each parasol beside its lounger on the outer
    ///    side; the fire stands in front of them on their middle (the "camp" tile is then only where the plan falls back to).
    ///  * Size: the members are drawn heroes' size (Alan AlanToGrunt x a grunt, Brad AlanToGrunt / AlanToBrad: clearly bigger than
    ///    a grunt); the map's pick box, frame and health bar of a hero follow it (<see cref="Tall"/>).
    ///  * Shot sound: a hero's shot plays one of its three gun sounds at random (never the same twice in a row), on the grunt's shot
    ///    channel at the grunt's shot gain; the clips are levelled to the grunt's bolt bank and enter the shot leveller with their
    ///    measured loudness, as the bolts do.
    ///  * Level start ("start": "lounger" in the level data): each member lies in the sun-lounger antic from the first frames,
    ///    on the antic's own lounger laid out for the standard game view; a click, an order, a hit or an enemy near ends it the
    ///    antic's own way (the lounger and parasol pop out with its puff and sound; nothing is left on the ground).
    ///  * Every place a unit's name or picture shows: the unit panel and its crew line (a vehicle, gun or troop carrier they sit in),
    ///    the help call, the map's hover label, the end screen's face: while the two are together (<see cref="OzHeroPair"/>) the
    ///    squad's name and its picture of both (Resources/OpenZ/UI/PortraitsHero/portrait_&lt;kind&gt;_&lt;red|blue&gt;); apart (one died,
    ///    one sits in a vehicle or gun, the squad split) each one his own name ("ALAN" / "BRAD") and his own picture
    ///    (portrait_&lt;member&gt;_&lt;red|blue&gt;), all rendered by the editor menu OpenZ/Hero Squad/2 Render portraits; a missing picture:
    ///    the pair's, then the grunt's. The crew line: both aboard = the squad's name, one = his. The end screen's face stays the pair.
    ///  * Antics: their own rule (<see cref="OzHeroAntics"/>, <see cref="OzHeroAnticPlan"/>): only the drinking antic, every 30 s of
    ///    idle with no enemy around; the standing idle animation stays a grunt's.
    /// </summary>
    public sealed class OzHeroSquadView : MonoBehaviour
    {
        public const string PrefabDir = "OpenZ/HeroSquad/", PortraitDir = "OpenZ/UI/PortraitsHero/";
        /// <summary>The three gun sounds (under Resources/OpenZ/Audio/Sfx) and their integrated loudness (the clip looped x4, measured as
        /// CombatAudio.ClipLufs is): three takes levelled to the mean of the grunt's bolt bank (-10.4 LUFS).</summary>
        public static readonly string[] GunBank = { "HeroSquad/hero_gun_1", "HeroSquad/hero_gun_2", "HeroSquad/hero_gun_3" };
        public static readonly float[] GunLufs = { -10.5f, -10.4f, -10.5f };
        /// <summary>Gain of a hero shot as a multiple of the grunt's shot gain (LegacyMatch.GainBolt).</summary>
        public static float GunGain = 1f;
        /// <summary>How long (real s) the level start keeps trying to lay the members on their loungers (the views, the antic kit and
        /// the ground must be ready).</summary>
        public static float LoungeTrySeconds = 90f;
        /// <summary>The ground direction of the standard game view (screen-up on the map) the start loungers are laid out for.</summary>
        public static Vector3 StandardViewForward = Vector3.forward;

        /// <summary>Alan's height as a multiple of a grunt's (heroes: clearly bigger than a grunt) and Alan's height as a share of Brad's
        /// (Alan about 5 % under Brad). The editor menu OpenZ/Hero Squad/1 Build hero prefabs scales the prefabs by these.</summary>
        public static float AlanToGrunt => (float)OzHeroCampPlan.AlanToGrunt;
        public static float AlanToBrad => (float)OzHeroCampPlan.AlanToBrad;
        /// <summary>How many times a grunt's height this robot is drawn (1 for every robot that is not a hero): the map's pick box,
        /// selection frame and health bar of a robot use it.</summary>
        public static float Tall(ZObject o)
        {
            if (o == null || !o.IsHero) return 1f;
            return o.HeroMemberName == "brad" ? AlanToGrunt / Mathf.Max(0.5f, AlanToBrad) : AlanToGrunt;
        }

        public static int Spawned, FallbackViews, RuntimeViews, Campfires, LoungersStarted, LoungerMisses, ShotsPlayed, CampsLaid, CampFallbacks;
        public static readonly List<string> Log = new List<string>();
        static bool levelsIn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Spawned = FallbackViews = RuntimeViews = Campfires = LoungersStarted = LoungerMisses = ShotsPlayed = CampsLaid = CampFallbacks = 0; Log.Clear(); levelsIn = false; portraits.Clear(); CampText = "no camp"; }

        static void Say(string s) { if (Log.Count > 200) Log.RemoveAt(0); Log.Add(s); Debug.Log("[OpenZ HeroSquad] " + s); }

        readonly List<UnitView> toLounge = new List<UnitView>();
        /// <summary>Campfires asked at the level start before the level's own fires (L16Fires) were there to copy: made as soon as they are.</summary>
        readonly List<Vector2Int> pendingCamps = new List<Vector2Int>();
        /// <summary>The start camps (a lounger start with a campfire): laid out once both members can lie down.</summary>
        sealed class CampJob { public ZMapData.Obj entry; public readonly List<UnitView> views = new List<UnitView>(); public bool laid, fireMade, hasFire; public Vector3 fire; }
        readonly List<CampJob> camps = new List<CampJob>();
        /// <summary>Play check: the start camp as laid out (fire, gap, loungers, parasols).</summary>
        public static string CampText = "no camp";
        float since;

        // ------------------------------------------------------------------ spawn (ZGame.Awake, right after BindViews)
        public static void Spawn(ZGame g)
        {
            if (!g || g.World == null) return;
            var root = GameObject.Find("ZodObjects");
            OzHeroSquadView host = null;
            var camps = new HashSet<ZMapData.Obj>();
            foreach (var o in g.World.Objects)
            {
                if (!o.IsHero || g.Views.ContainsKey(o.Id)) continue;
                var v = MakeView(g, o, root ? root.transform : null);
                if (!v) continue;
                bool hasCamp = o.HeroFrom != null && o.HeroFrom.camp != null && o.HeroFrom.camp.Length >= 2;
                if (hasCamp && o.HeroFrom.start == "lounger")
                {   // KC4b: the camp is laid out round the fire once both can lie down (Update: TickCamp / LayCamp)
                    if (!host) host = new GameObject("OzHeroSquadView").AddComponent<OzHeroSquadView>();
                    var job = host.camps.Find(j => j.entry == o.HeroFrom);
                    if (job == null) host.camps.Add(job = new CampJob { entry = o.HeroFrom });
                    job.views.Add(v);
                    continue;
                }
                if (hasCamp && camps.Add(o.HeroFrom))
                {
                    if (CampSourceReady()) { if (OzHeroCampfire.Make(g, o.HeroFrom.camp[0], o.HeroFrom.camp[1])) Campfires++; }
                    else
                    {   // the level's fires are built after this (the level start): the campfire is made as soon as they are
                        if (!host) host = new GameObject("OzHeroSquadView").AddComponent<OzHeroSquadView>();
                        host.pendingCamps.Add(new Vector2Int(o.HeroFrom.camp[0], o.HeroFrom.camp[1]));
                    }
                }
                if (o.HeroFrom != null && o.HeroFrom.start == "lounger")
                {
                    if (!host) host = new GameObject("OzHeroSquadView").AddComponent<OzHeroSquadView>();
                    host.toLounge.Add(v);
                }
            }
            EnsureLevels();
            if (Spawned > 0) OzHeroAntics.Ensure();                                                   // KC4b: their own antic rule (the drink only) + the Play counters
        }

        static UnitView MakeView(ZGame g, ZObject o, Transform parent)
        {
            string member = o.HeroMemberName ?? "brad";
            OzHeroShotSound.Load();                                                                        // item 225: the shot takes load with the first hero view; the log line says what was found where
            var prefab = Resources.Load<GameObject>(PrefabDir + "Z_hero_" + member);
            if (!prefab) { prefab = g.Prefab("Z_bs01_" + o.Type); FallbackViews++; Say($"no prefab {PrefabDir}Z_hero_{member} (menu OpenZ/Hero Squad/1 Build hero prefabs): drawn as a {o.Type}"); }
            if (!prefab) return null;
            var go = Instantiate(prefab, parent);
            go.name = $"hero_{member}_rt{o.Id}";
            var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Team = o.Owner;
            var look = go.GetComponent<OzHeroLook>(); if (look) look.Apply(o.Owner);
            var v = go.AddComponent<UnitView>(); v.Bind(g, o);
            OzHeroLegs.Attach(v, member);   // hero legs (item 209)
            g.Views[o.Id] = v;
            Spawned++;
            Say($"{(o.Hero != null ? o.Hero.Name : "?")}: {member} #{o.Id} ({o.Owner}) at tile ({o.LocX / 16},{o.LocY / 16}){(o.IsMinion ? ", behind #" + o.Leader.Id : ", the leader")}");
            return v;
        }

        /// <summary>ZGame.SpawnUnitView hook: a hero robot made at run time (out of a vehicle, a gun or a troop carrier, Sim/ZHeroSquad.cs
        /// HeroCrewOut) is drawn as itself (null: not a hero, the game's own view).</summary>
        public static UnitView SpawnView(ZGame g, ZObject o)
        {
            if (!g || o == null || !o.IsHero) return null;
            var root = GameObject.Find("ZodObjects");
            var v = MakeView(g, o, root ? root.transform : null);
            if (v) { RuntimeViews++; OzHeroAntics.Ensure(); }
            return v;
        }

        // ------------------------------------------------------------------ the level-start loungers
        /// <summary>The level's campfire model to copy is there (L16Fires built).</summary>
        static bool CampSourceReady() => L16Fires.I && L16Fires.I.transform.Find("Fire_" + OzHeroCampfire.CopyOf);

        void Update()
        {
            since += Time.unscaledDeltaTime;
            if (pendingCamps.Count > 0 && (CampSourceReady() || since > LoungeTrySeconds))
            {
                foreach (var c in pendingCamps) if (OzHeroCampfire.Make(ZGame.I, c.x, c.y)) Campfires++;
                pendingCamps.Clear();
            }
            foreach (var job in camps) TickCamp(job);
            for (int i = toLounge.Count - 1; i >= 0; i--)
            {
                var v = toLounge[i];
                if (!v || v.Obj == null || v.Obj.Destroyed) { toLounge.RemoveAt(i); continue; }
                if (v.AnticActive || TryLounge(v)) { toLounge.RemoveAt(i); continue; }
            }
            if (toLounge.Count > 0 && since > LoungeTrySeconds)
            {
                foreach (var v in toLounge) if (v && v.Obj != null) { LoungerMisses++; Say($"#{v.Obj.Id}: no lounger after {LoungeTrySeconds:0} s ({Why(v)}): it stands"); }
                toLounge.Clear();
            }
            if (toLounge.Count == 0 && pendingCamps.Count == 0 && camps.TrueForAll(j => j.laid && j.fireMade)) enabled = false;
        }

        static string Why(UnitView v)
        {
            if (!v.CanAntic) return "no antic clip states on its animator";
            var busy = v.AnticBusyReason(); if (busy != null) return busy;
            var kit = AnticDirector.Kit; if (!kit || !kit.HasSunbed) return "antic kit: sunbeds missing";
            return "no free flat spot for the lounger";
        }

        // ------------------------------------------------------------------ KC4b: the start camp round the fire
        void TickCamp(CampJob job)
        {
            if (!job.laid)
            {
                job.views.RemoveAll(v => !v || v.Obj == null || v.Obj.Destroyed);
                bool ready = job.views.Count > 0 && job.views.TrueForAll(v => v.AnticActive || (v.CanAntic && v.AnticBusyReason() == null));
                var kit = AnticDirector.Kit;
                if (ready && kit && kit.HasSunbed) LayCamp(job);
                else if (since > LoungeTrySeconds)
                {   // the views never got ready: each member tries the antic's own spot, the fire goes on the entry's tile
                    job.laid = true; CampFallbacks++;
                    foreach (var v in job.views) if (v && !v.AnticActive) toLounge.Add(v);
                    Say($"camp: the members were not ready to lie down after {LoungeTrySeconds:0} s: the antic's own spots, the fire on its tile");
                }
            }
            if (!job.fireMade && job.laid && (CampSourceReady() || since > LoungeTrySeconds))
            {
                job.fireMade = true;
                var c = job.entry.camp;
                var f = job.hasFire ? OzHeroCampfire.Make(ZGame.I, job.fire, c[0], c[1]) : OzHeroCampfire.Make(ZGame.I, c[0], c[1]);
                if (f) Campfires++;
            }
        }

        /// <summary>Lays the camp out (OzHeroCampPlan) from the drawn robots and starts both on their loungers.</summary>
        void LayCamp(CampJob job)
        {
            job.laid = true;
            var g = ZGame.I; if (!g) return;
            var vs = job.views; int n = vs.Count;
            var xs = new double[n]; var zs = new double[n]; var ks = new double[n]; var wfs = new double[n];
            for (int i = 0; i < n; i++)
            {
                var p = vs[i].transform.position;
                xs[i] = p.x; zs[i] = p.z; ks[i] = AnticSockets.WorldScale(vs[i]); wfs[i] = AnticSockets.BedWidthFor(vs[i]);
            }
            var camF = StandardViewForward; camF.y = 0f; camF = camF.sqrMagnitude > 0.01f ? camF.normalized : Vector3.forward;
            var camR = Vector3.Cross(Vector3.up, camF);
            FireFootprint(camF, camR, out float hd, out float hw);
            var plan = OzHeroCampPlan.Plan(xs, zs, ks, wfs, camF.x, camF.z, hd, hw);
            job.fire = new Vector3((float)plan.fireX, g.GroundY((float)plan.fireX, (float)plan.fireZ), (float)plan.fireZ); job.hasFire = true;
            float ta = AnticSpot.ParasolTilt * Mathf.Deg2Rad;
            var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up * Mathf.Cos(ta) + camF * Mathf.Sin(ta)).normalized);
            var parts = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var v = vs[i]; var s = plan.spots[i]; float k = (float)ks[i];
                if (v.AnticActive) { parts.Add($"{v.Obj.HeroMemberName}: already in an antic"); continue; }
                var front = new Vector3((float)s.fx, 0f, (float)s.fz); var side = new Vector3((float)s.rx, 0f, (float)s.rz);
                var bc = new Vector3((float)s.bedX, 0f, (float)s.bedZ); var pole = new Vector3((float)s.poleX, 0f, (float)s.poleZ);
                float py = 0f;
                if (!CampFlat(g, bc, front, side, (float)s.hw, (float)s.hl, 0.18f * k, out float gy) || !CampFlat(g, pole, front, side, 0.3f * k, 0.3f * k, 0.3f * k, out py))
                {   // not flat or something stands there: this member takes the antic's own spot
                    CampFallbacks++; toLounge.Add(v);
                    parts.Add($"{v.Obj.HeroMemberName}: camp spot not free (the antic's own spot)");
                    continue;
                }
                var bedRot = Quaternion.LookRotation(front, Vector3.up);
                var spot = new AnticSpot { yaw = (float)s.yaw, bed = new Vector3(bc.x, gy, bc.z), parasol = new Vector3(pole.x, py, pole.z), bedRot = bedRot, parasolRot = tilt * bedRot, bedWidth = (float)wfs[i] };
                var run = new AnticRun(v, AnticKind.Sunbed, spot) { Hold = true, StartLying = true };
                if (!v.AnticBegin(run)) { toLounge.Add(v); parts.Add($"{v.Obj.HeroMemberName}: antic refused (retried on its own spot)"); continue; }
                AnticDirector.Ensure().Started(run);
                LoungersStarted++;
                parts.Add($"{v.Obj.HeroMemberName} lies at ({s.x:F1}, {s.z:F1}) facing {s.yaw:0} deg, lounger ({s.bedX:F1}, {s.bedZ:F1}) {2 * s.hw:F2} x {2 * s.hl:F2} m, parasol pole ({s.poleX:F1}, {s.poleZ:F1})");
            }
            CampsLaid++;
            CampText = $"camp: fire at ({plan.fireX:F1}, {plan.fireZ:F1}) {plan.ahead:F2} m in front of the pair (footprint {2 * hw:F1} x {2 * hd:F1} m), " +
                       $"clear gap fire -> foot ends {plan.FireGap():F2} m, V {2 * OzHeroCampPlan.VAngle:0} deg; " + string.Join("; ", parts);
            Say(CampText);
        }

        /// <summary>The level campfire's footprint (half depth along the view, half width across) from the model it copies; the plan's
        /// defaults when the level's fires are not built yet.</summary>
        static void FireFootprint(Vector3 camF, Vector3 camR, out float hd, out float hw)
        {
            hd = (float)OzHeroCampPlan.FireHalfDepth; hw = (float)OzHeroCampPlan.FireHalfWidth;
            var src = CampSourceReady() ? L16Fires.I.transform.Find("Fire_" + OzHeroCampfire.CopyOf) : null;
            if (!src) return;
            bool any = false; var b = new Bounds();
            foreach (var r in src.GetComponentsInChildren<MeshRenderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (!any || b.size.x < 0.3f || b.size.z < 0.3f) return;
            hd = Mathf.Abs(camF.x) * b.extents.x + Mathf.Abs(camF.z) * b.extents.z;
            hw = Mathf.Abs(camR.x) * b.extents.x + Mathf.Abs(camR.z) * b.extents.z;
        }

        /// <summary>The antic's flat-spot test for a camp footprint: no water, no blocked tile, nothing standing there, the ground
        /// within tol (y = the lowest point + 2 cm).</summary>
        static bool CampFlat(ZGame g, Vector3 c, Vector3 f, Vector3 r, float hw, float hl, float tol, out float y)
        {
            y = g.GroundY(c.x, c.z); float lo = y, hi = y;
            for (int i = 0; i < 9; i++)
            {
                var q = c + r * hw * ((i % 3) - 1) + f * hl * ((i / 3) - 1);
                if (OpenZFxPlayer.IsWaterTile(q) || OpenZFxPlayer.ClassAt(q) == ZClass.Impassable) return false;
                float h = g.GroundY(q.x, q.z); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                if (Physics.Raycast(new Vector3(q.x, h + 6f, q.z), Vector3.down, out var hit, 5.85f, ~OzRockSolid.Mask, QueryTriggerInteraction.Ignore) && hit.point.y > h + 0.15f
                    && !hit.collider.GetComponentInParent<UnitView>() && !hit.collider.GetComponentInParent<OzHeroCampfire>()) return false;
            }
            y = Mathf.Min(y, lo + 0.02f);
            return hi - lo <= tol;
        }

        static bool TryLounge(UnitView v)
        {
            if (!v.CanAntic || v.AnticBusyReason() != null) return false;
            var kit = AnticDirector.Kit; if (!kit || !kit.HasSunbed) return false;
            AnticSpot spot;
            AnticSpot.GroundForwardOverride = StandardViewForward;
            try { spot = AnticSpot.Sunbed(v); }
            finally { AnticSpot.GroundForwardOverride = null; }
            if (spot == null) return false;
            var run = new AnticRun(v, AnticKind.Sunbed, spot) { Hold = true, StartLying = true };
            if (!v.AnticBegin(run)) return false;
            AnticDirector.Ensure().Started(run);
            LoungersStarted++;
            Say($"#{v.Obj.Id} {v.Obj.HeroMemberName}: lying on the lounger from the level start (until a click, an order or a threat)");
            return true;
        }

        // ------------------------------------------------------------------ shot sound
        static void EnsureLevels()
        {
            if (levelsIn) return;
            levelsIn = true;
            for (int i = 0; i < GunBank.Length; i++)
            {
                string n = GunBank[i].Substring(GunBank[i].LastIndexOf('/') + 1);
                if (!CombatAudio.ClipLufs.ContainsKey(n)) CombatAudio.ClipLufs[n] = GunLufs[i];
            }
        }

        /// <summary>CombatView / weapons_v2 hook on a grunt-class shot: true = the shooter is a hero and its own sound was played.</summary>
        public static bool PlayShot(int shooterId, Vector3 from, CombatAudio au)
        {
            var g = ZGame.I; if (!au || !g || g.World == null) return false;
            var o = g.World.GetAny(shooterId); if (o == null || !o.IsHero) return false;
            OzHeroShotSound.Play(shooterId, from, au);                                                   // item 225: ONE event "hero_shot", its takes = every clip of one folder, never silent (OzHeroShotSound.cs)
            ShotsPlayed++;
            return true;
        }

        // ------------------------------------------------------------------ unit panel
        static readonly Dictionary<string, Texture2D> portraits = new Dictionary<string, Texture2D>();

        /// <summary>The hero squad's own picture for this unit: the pair's while the two are together, his own when apart
        /// (<see cref="OzHeroPair"/>); a missing picture: the pair's, then <paramref name="other"/> (every other unit too).</summary>
        public static Texture PortraitOr(ZObject u, Texture other)
        {
            if (u == null || !u.IsHero) return other;
            Texture t = PortraitKey(OzHeroPair.PictureKey(u), u.Owner);
            if (!t) t = Portrait(u.Hero, u.Owner);
            return t ? t : other;
        }

        static Texture Portrait(ZHeroKind k, Team team) => k == null ? null : PortraitKey(OzHeroPair.KindKey(k), team);

        static Texture2D PortraitKey(string who, Team team)
        {
            if (string.IsNullOrEmpty(who)) return null;
            string key = who + "_" + TeamColors.Key(team);
            if (!portraits.TryGetValue(key, out var t)) portraits[key] = t = Resources.Load<Texture2D>(PortraitDir + "portrait_" + key);
            return t;
        }

        /// <summary>The hero squad's name for this unit: "ALAN &amp; BRAD" while together, "ALAN" / "BRAD" apart (or
        /// <paramref name="other"/> for every other unit).</summary>
        public static string NameOr(ZObject u, string other) => u != null && u.IsHero ? OzHeroPair.NameOf(u) ?? u.Hero.Name : other;

        /// <summary>The unit panel's crew line: the hero squad's name when both members sit in the vehicle's drivers (false) or
        /// passengers (true), the one member's name when one does, else <paramref name="other"/> (the crew robot type's name).</summary>
        public static string CrewNameOr(ZObject v, bool passengers, string other) => OzHeroPair.CrewName(v, passengers) ?? other;

        // ------------------------------------------------------------------ the end screen's face (the team's most numerous robot kind)
        const string FacePrefix = "hero:";
        /// <summary>The kind a robot counts as for the end screen's face: its type, or the hero squad's own kind.</summary>
        public static string FaceKey(ZObject o) => o != null && o.IsHero ? FacePrefix + o.Hero.Id : o?.Type;
        static ZHeroKind FaceKind(string face) => face != null && face.StartsWith(FacePrefix) ? ZHeroSquads.Get(face.Substring(FacePrefix.Length)) : null;
        /// <summary>The end screen's face picture: the hero squad's own picture of both, else the robot type's portrait.</summary>
        public static Texture FacePortrait(string face, Team team)
        {
            var k = FaceKind(face);
            if (k == null) return UnitArt.Portrait(face, team);
            var t = Portrait(k, team);
            return t ? t : UnitArt.Portrait(k.BaseType, team);
        }
        /// <summary>The end screen's face name: ALAN &amp; BRAD, else the robot type's name.</summary>
        public static string FaceName(string face) { var k = FaceKind(face); return k != null ? k.Name : UnitInfo.Name(face); }

        /// <summary>Play check: the views, the loungers, the shots.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            sb.Append($"hero views {Spawned} (grunt fallback {FallbackViews}, made at run time {RuntimeViews}), campfires {Campfires}, loungers started {LoungersStarted} (missed {LoungerMisses}), hero shots played {ShotsPlayed}");
            var g = ZGame.I;
            if (g && g.World != null)
                foreach (var o in g.World.Objects)
                    if (o.IsHero)
                    {
                        g.Views.TryGetValue(o.Id, out var v);
                        sb.Append($" | {o.HeroMemberName} #{o.Id} hp {o.Health}/{o.MaxHealth} {(o.IsMinion ? "behind #" + o.Leader.Id : "leader")} tile ({o.LocX / 16},{o.LocY / 16}) {(v && v.AnticActive ? "antic " + v.Antic.Kind + " " + v.Antic.Step : "standing")}");
                    }
                    else if (o.HeroesAboard > 0) sb.Append($" | {o.Type} #{o.Id} carries {o.HeroesAboard} of them ({CrewNameOr(o, false, "-")} / {CrewNameOr(o, true, "-")})");
            if (g && g.World != null) sb.Append($" | boarded {g.World.HeroesBoarded}, out as themselves {g.World.HeroesOut}");
            sb.Append($"\n  {CampText} (camps laid {CampsLaid}, members on the antic's own spot {CampFallbacks})");
            sb.Append("\n  " + MeasuredSize());
            sb.Append("\n  " + PairText());
            sb.Append("\n  " + OzHeroAntics.Report());
            sb.Append($"\n  size set: Alan {AlanToGrunt:0.00} x a grunt, Brad {AlanToGrunt / AlanToBrad:0.00} x (Alan {AlanToBrad * 100:0} % of Brad); hero voice lines said {AudioDirector.HeroLinesSaid} (select {AudioDirector.HeroLinesBy[0]}, move {AudioDirector.HeroLinesBy[1]}, attack {AudioDirector.HeroLinesBy[2]}, board {AudioDirector.HeroLinesBy[3]}, answers {AudioDirector.HeroLinesBy[4]}); vehicles captured by them {AudioDirector.HeroCaptures}, the announcer's Vehicle captured line after {AudioDirector.HeroCaptureAnnounced}");
            sb.Append("\n  " + OzHeroCampfire.Status());
            foreach (var l in Log) sb.Append("\n  " + l);
            foreach (var l in OzHeroAntics.Log) sb.Append("\n  " + l);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ KC4b: Play measures
        /// <summary>The drawn height of a robot view (m): its skinned body as posed now (BakeMesh), weapon, kit and props left out.</summary>
        public static float DrawnHeight(UnitView v)
        {
            if (!v) return 0f;
            float lo = float.MaxValue, hi = float.MinValue; var mesh = new Mesh(); var vs = new List<Vector3>();
            try
            {
                foreach (var r in v.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!r.enabled || !r.sharedMesh) continue;
                    string n = r.name;
                    if (n.StartsWith("BS01_Weapon") || n.Contains("FUEL_CAN") || n.StartsWith("BS01Kit") || n.StartsWith("BS01_Kit")) continue;
                    r.BakeMesh(mesh, false);                                                      // the renderer's own space, unscaled: TransformPoint gives the world
                    mesh.GetVertices(vs);
                    var tr = r.transform;
                    for (int i = 0; i < vs.Count; i++) { float y = tr.TransformPoint(vs[i]).y; if (y < lo) lo = y; if (y > hi) hi = y; }
                }
            }
            finally { Destroy(mesh); }
            return hi > lo ? hi - lo : 0f;
        }

        /// <summary>The same from the body renderers' world bounds (the import pose's box at the drawn scale): a second reading.</summary>
        public static float BoundsHeight(UnitView v)
        {
            if (!v) return 0f;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var r in v.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!r.enabled || !r.sharedMesh) continue;
                string n = r.name;
                if (n.StartsWith("BS01_Weapon") || n.Contains("FUEL_CAN") || n.StartsWith("BS01Kit") || n.StartsWith("BS01_Kit")) continue;
                var b = r.bounds; lo = Mathf.Min(lo, b.min.y); hi = Mathf.Max(hi, b.max.y);
            }
            return hi > lo ? hi - lo : 0f;
        }

        public const float AlanToGruntMin = 1.25f, AlanToGruntMax = 1.35f, BradToAlanMin = 1.03f, BradToAlanMax = 1.07f;

        /// <summary>Play check: the heroes' drawn heights against the grunts on the map (the median of up to 7 grunt views), wanted
        /// Alan 1.25-1.35 x a grunt and Brad 1.03-1.07 x Alan. Stand them idle beside grunts (a run or an aim changes a pose).</summary>
        public static string MeasuredSize()
        {
            var g = ZGame.I; if (!g || g.World == null) return "size (measured): no game";
            var grunts = new List<float>(); var gruntsB = new List<float>(); float alan = 0f, brad = 0f, alanB = 0f, bradB = 0f;
            foreach (var v in g.Views.Values)
            {
                if (!v || !v.isActiveAndEnabled || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Obj.Destroyed || v.Obj.Type != "grunt") continue;
                if (v.Obj.IsHero)
                {
                    float h = DrawnHeight(v), hb = BoundsHeight(v);
                    if (v.Obj.HeroMemberName == "alan") { alan = Mathf.Max(alan, h); alanB = Mathf.Max(alanB, hb); }
                    else if (v.Obj.HeroMemberName == "brad") { brad = Mathf.Max(brad, h); bradB = Mathf.Max(bradB, hb); }
                }
                else if (grunts.Count < 7) { float h = DrawnHeight(v); if (h > 0.5f) { grunts.Add(h); gruntsB.Add(BoundsHeight(v)); } }
            }
            if (grunts.Count == 0 || alan <= 0f || brad <= 0f) return $"size (measured): needs both heroes and a grunt on the map (grunts {grunts.Count}, Alan {alan:F2} m, Brad {brad:F2} m)";
            grunts.Sort(); float gh = grunts[grunts.Count / 2];
            gruntsB.Sort(); float ghB = gruntsB[gruntsB.Count / 2];
            float a = alan / gh, b = brad / alan;
            bool ok = a >= AlanToGruntMin && a <= AlanToGruntMax && b >= BradToAlanMin && b <= BradToAlanMax;
            return $"size (measured, posed mesh): grunt {gh:F2} m (median of {grunts.Count}), Alan {alan:F2} m = {a:F2} x a grunt (wanted {AlanToGruntMin:0.00}-{AlanToGruntMax:0.00}), Brad {brad:F2} m = {b:F3} x Alan (wanted {BradToAlanMin:0.00}-{BradToAlanMax:0.00}): {(ok ? "PASS" : "FAIL")}" +
                   $"; renderer bounds: Alan {(ghB > 0f ? alanB / ghB : 0f):F2} x a grunt, Brad {(alanB > 0f ? bradB / alanB : 0f):F3} x Alan";
        }

        /// <summary>Play check: each hero's name and picture as the unit panel shows them now (together / apart), and the crew lines.</summary>
        public static string PairText()
        {
            var g = ZGame.I; if (!g || g.World == null) return "pair: no game";
            var parts = new List<string>();
            foreach (var o in g.World.Objects)
            {
                if (o.IsHero && !o.Destroyed && o.KillAt < 0)
                {
                    string key = OzHeroPair.PictureKey(o);
                    parts.Add($"{o.HeroMemberName} #{o.Id}: name \"{OzHeroPair.NameOf(o)}\", picture {key} ({(PortraitKey(key, o.Owner) ? "there" : "MISSING: the pair's shown")})");
                }
                else if (!o.IsHero && (o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && o.HeroesAboard > 0)
                    parts.Add($"{o.Type} #{o.Id}: crew line \"{OzHeroPair.CrewName(o, false) ?? "-"}\", passengers \"{OzHeroPair.CrewName(o, true) ?? "-"}\"");
            }
            return "pair: " + (parts.Count > 0 ? string.Join("; ", parts) : "no hero on the map");
        }
    }
}
