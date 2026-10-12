using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View side of the rules: flag + zone recolour with a capture pulse, building owner colours, factory output sounds,
    /// repair-station and crane sparks, bridges swapping to their destroyed model and back, building deaths (Zod's
    /// explosion counts), the fort nuke, and a simple win / elimination banner (the real end screen comes with the HUD).
    /// Called by CombatView for every sim event.
    /// </summary>
    public class RulesView : MonoBehaviour
    {
        ZGame game;
        readonly Dictionary<int, float> craneWork = new Dictionary<int, float>();              // building id -> sparks until
        string banner; float bannerTime = -100; Color bannerColor = Color.white;
        float nextSpark;
        float lastNukeFx = -100f;                                                                   // fx_v2: one mushroom for both fort halves

        void Awake() { game = GetComponent<ZGame>(); if (!game) game = FindFirstObjectByType<ZGame>(); }

        /// <summary>The sim sets building and flag owners from the zones at map init (no events): paint the scene to match.</summary>
        void Start()
        {
            if (!GetComponent<RobotGestureDirector>()) gameObject.AddComponent<RobotGestureDirector>();
            if (game == null || game.World == null) return;
            foreach (var kv in game.StaticViews)
            {
                var o = game.World.Get(kv.Key); if (o == null || !kv.Value) continue;
                if (o.Kind != ZKind.Building && o.Type != "flag") continue;
                var tp = kv.Value.GetComponent<TeamPaint>(); if (tp && tp.Team != o.Owner) tp.Team = o.Owner;
            }
        }

        GameObject Static(int id) => game.StaticViews.TryGetValue(id, out var g) ? g : null;
        Vector3 Ground(int px, int py) { var p = ZGame.PxToWorld(px, py); p.y = game.GroundY(p.x, p.z); return p; }

        public void Handle(ZEvent e)
        {
            var W = game.World; var a = W.GetAny(e.objId); var b = W.GetAny(e.otherId);
            var fx = FxManager.I; var au = CombatAudio.I;
            switch (e.type)
            {
                case ZEventType.FlagCaptured:
                {
                    var team = (Team)(int)e.value; var old = (Team)e.radius;
                    var g = Static(e.objId); if (g) { var tp = g.GetComponent<TeamPaint>(); if (tp) tp.Team = team; }
                    var p = Ground(e.x, e.y);
                    if (!FlagView.Active)
                    {   // FlagView (flags package) owns the capture ring and sounds when present
                        StartCoroutine(Pulse(p, TeamColors.Paint(team)));
                        au?.Play("flag", p, 0.9f, 0.2f, e.objId, "flag-capture-1", "v3-capture-raise");
                        if (team == game.player) au?.PlayWide("flag", p, 0.55f, "v3-flag-success");
                    }
                    Debug.Log($"[OpenZ] {W.Time:F1}s zone {W.ZoneOf(a)?.Id} flag taken by {team} (was {old})");
                    RobotGestures.OnFlagCaptured(game, b);
                    break;
                }
                case ZEventType.OwnerChanged:
                    if (a != null && a.Kind == ZKind.Building) { var g = Static(a.Id); if (g) { var tp = g.GetComponent<TeamPaint>(); if (tp) tp.Team = a.Owner; } }
                    break;
                case ZEventType.UnitBuilt:
                    if (a != null && b != null)
                    {
                        var p = game.CenterWorld(a);
                        fx?.Dust(p, 0.6f);
                        au?.Play("built", p, 0.8f, 0.3f, b.Id, a.Kind == ZKind.Vehicle ? OzSoundV3.BankEngineStart : OzSoundV3.BankRolloutRobot);   // sound_v3: robots were the flag-capture clip
                        Debug.Log($"[OpenZ] {W.Time:F2}s {b.Type} {b.Owner} built {a.Type}{(a.Minions.Count > 0 ? " x" + (a.Minions.Count + 1) : "")}");
                    }
                    break;
                case ZEventType.CannonStored:
                    if (a != null) Debug.Log($"[OpenZ] {W.Time:F2}s {a.Type} {a.Owner} stored a cannon ({a.StoredCannons.Count} waiting)");
                    break;
                case ZEventType.CannonPlaced:
                    if (a != null) PlacementPop.Placed(game, a);
                    break;
                case ZEventType.RepairIn:
                {
                    var v = game.Views.TryGetValue(e.objId, out var uv) ? uv : null; if (v && !ZWorld.RepairInPlaceDeviation) v.Hide();   // DV62: it stays on the pad (RepairBayView: sparks, weld)
                    if (b != null) { if (!ZWorld.RepairInPlaceDeviation) craneWork[b.Id] = Time.time + 5f; au?.Play("repair", game.CenterWorld(b), 0.9f, 0, b.Id, OzSoundV3.BankWrench);   /* sound_v3 */ }
                    break;
                }
                case ZEventType.RepairOut:
                    if (a != null) { var p = game.CenterWorld(a); fx?.Pop(p + Vector3.up, 0.8f); au?.Play("repairout", p, 0.8f, 0, a.Id, OzSoundV3.BankEngineStart);   /* sound_v3 */ }
                    break;
                case ZEventType.CraneAnim:
                    if (b != null)
                    {
                        if (e.flag)
                        {
                            craneWork[b.Id] = Time.time + 30f; au?.Play("crane", game.CenterWorld(b), 0.9f, 0, b.Id, OzSoundV3.BankWrench);   /* sound_v3 */
                            if (a != null) craneOf[b.Id] = a.Id;
                            CraneJobs++;
                            OzRuinFires.End(b.Id);                                                     // a crane starts on it: the ruin's fire is out
                            CraneArm.Job(game, a, b, true);
                            // and are welded; held until the sim repairs it, then they merge into the intact deck)
                            if (b.IsBridge && b.Destroyed && !rebuilding.Contains(b.Id)) StartCoroutine(Rebuild(b, 3.4f, true));
                        }
                        else { craneWork.Remove(b.Id); CraneArm.Job(game, a, b, false); }
                    }
                    break;
                case ZEventType.Revived:
                    if (a != null && a.Kind == ZKind.Building)
                    {
                        craneWork.Remove(a.Id);
                        if (a.IsBridge) SwapBridge(a, false);
                        else
                        {
                            var g = Static(a.Id); var br = g ? g.GetComponent<BuildingBreaker>() : null; bool was = br && br.Collapsed;
                            if (br) br.Restore();
                            if (g && was && RiseOnRestore) StartCoroutine(RiseUp(a, g.transform));
                        }
                        var p = game.CenterWorld(a); fx?.Pop(p + Vector3.up * 2, 1.2f);
                        if (!AudioDirector.Has("bld_building_revived")) au?.Play("revived", p, 0.8f, 0, a.Id, "v3-capture-raise");
                    }
                    break;
                case ZEventType.FortEntered:
                    Debug.Log($"[OpenZ] {W.Time:F1}s {a} walked into {b}");
                    break;
                case ZEventType.TeamEliminated:
                {
                    var team = (Team)(int)e.value;
                    if (!FindFirstObjectByType<OpenZEndScreen>()) Show($"{TeamColors.Name(team)} TEAM ELIMINATED", TeamColors.Of(team));   // fx_v2: no IMGUI text over the nuke
                    break;
                }
                case ZEventType.GameOver:
                {
                    var win = e.value < 0 ? Team.Neutral : (Team)(int)e.value;
                    if (!FindFirstObjectByType<OpenZEndScreen>())   // fx_v2 CHECK: no IMGUI label over the strike and the celebration
                        Show(win == game.player ? "VICTORY" : win == Team.Neutral ? "DRAW" : "DEFEAT", win == Team.Neutral ? Color.white : TeamColors.Of(win));
                    Debug.Log($"[OpenZ] {W.Time:F1}s game over, winner {win}");
                    break;
                }
            }
        }

        /// <summary>Building death (called from CombatView): fort = nuke; bridge = destroyed model + explosions along the deck;
        /// others = rubble with Zod's 8-10 (factory) / 6-8 (repair) / 4-6 (radar) explosions over ~2-3 s.</summary>
        public void BuildingDeath(ZObject o, Vector3 p)
        {
            var fx = FxManager.I; var au = CombatAudio.I;
            if (o.IsFort)
            {
                var c = game.CenterWorld(o);
                var fg = Static(o.Id);
                System.Action boom = () =>
                {
                    if (!(fg && BuildingBreaker.For(fg, o).Collapse(c, 1.7f))) game.RuinStatic(o.Id);    // cells fly out under the mushroom
                    if (Time.time - lastNukeFx > 5f) { lastNukeFx = Time.time; NukeFx.Trigger(c); }
                };
                // fx_v2: the nuke is delivered (bomber fly-by, the bomb falls, slow-motion impact); the fort stands until it hits
                if (!OpenZNukeStrike.Begin(c, o.Owner == game.player, boom)) boom();
                var cam = FindFirstObjectByType<RtsCamera>();
                if (cam) { cam.FlyTo(c + new Vector3(0, 0, 9f)); cam.ZoomTo(78f); }   // frame the whole column at game height (55 deg pitch)
                return;
            }
            OpenZFxShake.I?.Kick(game.CenterWorld(o), o.IsBridge ? OpenZFxCatalog.ShakeBridgePx : OpenZFxCatalog.ShakeBuildingPx, o.IsBridge ? "bridge" : "building", true);   // a building or a bridge blowing up in view always kicks the camera
            if (o.IsBridge) { SwapBridge(o, true); StartCoroutine(Booms(o, 6, 1.2f)); if (!AudioDirector.Has("dst_bridge_collapse")) au?.Play("collapse", p, 1f, 0, -1, "v3-collapse-large-a", "building-collapse-20260909"); return; }
            var g = Static(o.Id);
            if (g && BuildingBreaker.For(g, o).Collapse(game.CenterWorld(o), 1f)) return;                 // physics collapse (map 13 style)
            game.RuinStatic(o.Id);
            int n = o.Type == "radar" ? 4 + Random.Range(0, 3) : o.Type == "repair" ? 6 + Random.Range(0, 3) : 8 + Random.Range(0, 3);
            StartCoroutine(Booms(o, n, 2.5f));
            au?.Play("collapse", p, 1f, 0, -1, "building-collapse-20260909", "v3-collapse-medium-a", "collapse");
        }

        IEnumerator Booms(ZObject o, int n, float span)
        {
            var fx = FxManager.I;
            for (int i = 0; i < n && fx; i++)
            {
                int px = o.LocX + Random.Range(8, o.WidthPix - 8), py = o.LocY + Random.Range(8, o.HeightPix - 8);
                var p = Ground(px, py) + Vector3.up * Random.Range(0.8f, 3f);
                fx.Explosion(p, 1.2f);
                if (i % 2 == 0) fx.Breakup(p, o.IsBridge && game.level != "L17" && i % 3 == 0 ? "stone" : "metal", 3f);   // steel bridges (Z kit)
                yield return new WaitForSeconds(span / n);
            }
        }

        /// <summary>Bridges swap between the intact and destroyed prefab (same transform), both ways.</summary>
        // ------------------------------------------------------------------ bridges: intact / damaged / destroyed (Zod bbridge.cpp)
        // Zod draws the damaged image while 0 < health < max/2 and the destroyed one when dead; revive goes back to intact.
        readonly Dictionary<int, GameObject[]> bridgeVariants = new Dictionary<int, GameObject[]>();
        readonly Dictionary<int, int> bridgeState = new Dictionary<int, int>();
        static readonly string[] BridgeStates = { "intact", "damaged", "destroyed" };

        GameObject[] Variants(ZObject o)
        {
            if (bridgeVariants.TryGetValue(o.Id, out var v)) return v;
            var cur = Static(o.Id); if (!cur) return null;
            string biome = game.level == "L17" ? "city" : "desert";
            bool placedDestroyed = o.Source != null && o.Source.health_percent <= 0;
            v = new GameObject[3]; v[placedDestroyed ? 2 : 0] = cur;
            for (int st = 0; st < 3; st++)
            {
                if (v[st]) continue;
                var pf = game.Prefab($"Z_bridge_{biome}_{BridgeStates[st]}"); if (!pf) continue;
                var go = Instantiate(pf, cur.transform.parent); go.name = cur.name + "_" + BridgeStates[st];
                go.transform.SetLocalPositionAndRotation(cur.transform.localPosition, cur.transform.localRotation); go.transform.localScale = cur.transform.localScale;
                go.SetActive(false); v[st] = go;
            }
            bridgeVariants[o.Id] = v; bridgeState[o.Id] = placedDestroyed ? 2 : 0;
            return v;
        }

        void SetBridge(ZObject o, int st, bool effects)
        {
            var v = Variants(o); if (v == null) return;
            bridgeState.TryGetValue(o.Id, out var was); bridgeState[o.Id] = st;
            if (st != 2) OzRuinFires.End(o.Id);                                                 // standing again: its ruin fire goes at once
            for (int i = 0; i < 3; i++) if (v[i]) v[i].SetActive(i == st || (v[st] == null && i == 0));
            if (st < 2 && v[st]) game.MakeWalkable(v[st]);                                     // intact / damaged decks carry units
            var cur = Static(o.Id);
            var under = cur && cur.transform.parent ? cur.transform.parent.Find("underlay_" + cur.name) : null;
            if (under) under.gameObject.SetActive(st == 0);
            if (!effects || st == was) return;
            var p = game.CenterWorld(o);
            if (st == 1) { StartCoroutine(Booms(o, 3, 0.8f)); BridgeChunks(o, 3, true); CombatAudio.I?.Play("collapse", p, 0.7f, 0, -1, "v3-collapse-small-a", "v3-collapse-small-b"); }
            if (st == 2) { BridgeChunks(o, 14, false); OzRuinFires.Bridge(o); }                 // its broken ends and banks burn for a while
        }

        /// <summary>Kit chunks (8 per biome) thrown from the middle third of the deck: real physics, bounce, settle, sink.</summary>
        void BridgeChunks(ZObject o, int n, bool small)
        {
            var set = game.Prefab($"Z_bridge_{(game.level == "L17" ? "city" : "desert")}_chunks"); if (!set) return;
            var parts = new List<Transform>(); foreach (Transform t in set.transform) parts.Add(t);
            if (parts.Count == 0) return;
            var c = game.CenterWorld(o); var deck = c.y + (game.level == "L17" ? 0.3f : 1.2f);
            bool vert = o.Type == "bridge_vert";
            for (int i = 0; i < n; i++)
            {
                var src = small ? parts[new[] { 0, 2, 5 }[i % 3] % parts.Count] : parts[Random.value < 0.7f ? Random.Range(0, Mathf.Min(6, parts.Count)) : Random.Range(0, parts.Count)];
                float along = Random.Range(-1.8f, 1.8f), across = Random.Range(-2f, 2f);
                var pos = new Vector3(c.x + (vert ? across : along), deck + Random.Range(1f, 3f), c.z + (vert ? along : across));
                var go = Instantiate(src.gameObject, pos, Random.rotation);
                go.layer = BuildingBreaker.DebrisLayer;
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf && mf.sharedMesh) { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true; }
                var rb = go.AddComponent<Rigidbody>(); rb.mass = small ? 20f : 60f; rb.linearDamping = 0.28f; rb.angularDamping = 0.95f;
                rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var away = new Vector3(pos.x - c.x, 0, pos.z - c.z); if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0; away.Normalize();
                rb.linearVelocity = away * Random.Range(2f, 6.5f) * (small ? 0.5f : 1f) + Vector3.up * Random.Range(6f, 12.5f) * (small ? 0.6f : 1f);
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(2f, 7f);
                if (game.level == "L17") AudioDirector.AttachImpacts(go, "dst_debris_metal", "dst_debris_splash");   // sound_v3: the city bridge is steel
                go.AddComponent<RockChunkLife>();
            }
            if (!small) FxManager.I?.DemolitionDust(c + Vector3.up * 0.5f, 5f, 5f, 0.6f);
        }

        void SwapBridge(ZObject o, bool destroyed)
        {
            SetBridge(o, destroyed ? 2 : 0, destroyed);
            if (!destroyed && !rebuilding.Contains(o.Id)) { var p = game.CenterWorld(o); FxManager.I?.DemolitionDust(p, 4f, 4f, 0.35f, 0.28f, 2.2f); /* critic: faint wide haze, not orange balls */ CombatAudio.I?.Play("repair", p, 0.8f, 0, -1, "atom-repair-wrench"); }
        }

        // ---- OpenZ bridge rule: countdown arc while down, chunks fly back and weld into place before the sim repair
        readonly HashSet<int> rebuilding = new HashSet<int>();

        void LateUpdate()
        {
            if (game == null || game.World == null) return;
            var W = game.World;
            foreach (var o in W.Objects)
            {
                if (!o.IsBridge) continue;
                bool down = o.Destroyed && o.DoAutoRepair;
                if (!down) continue;
                double left = o.NextAutoRepairTime - W.Time;
                OzSoundV3.BridgeCountdown(o, left, game.CenterWorld(o));                                 // sound_v3                                   // the badge (BridgeRepairBadges) shows the countdown
                if (left <= 3.5 && !rebuilding.Contains(o.Id)) StartCoroutine(Rebuild(o, (float)left));
            }
        }

        // The crane's work = weld bursts (sparks, a blue-white light, the weld
        // crackle) at the spot it mends, every 0.35 s; a destroyed bridge under a crane rebuilds (Rebuild, crane = hold until the sim
        // repair); a restored building rises 35 % of its height back out of its rubble in 1.6 s with dust, sparks and the rebuild sound.
        public static bool RiseOnRestore = true;
        public static int CraneJobs, CraneWelds, CraneRebuilds, BuildingRises;
        readonly Dictionary<int, int> craneOf = new Dictionary<int, int>();

        IEnumerator RiseUp(ZObject o, Transform t)
        {
            BuildingRises++;
            var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) yield break;
            var b = rs[0].bounds; foreach (var r in rs) if (r.enabled) b.Encapsulate(r.bounds);
            float depth = Mathf.Clamp(b.size.y * 0.35f, 0.8f, 4f), dur = 1.6f, t0 = Time.time;
            var home = t.position; var fx = FxManager.I; float nextSpark = 0f;
            t.position = home + Vector3.down * depth;
            fx?.DemolitionDust(home, 5f, 5f, 0.45f, 0.3f, 2.2f);
            while (t && Time.time - t0 < dur)
            {
                float k = Mathf.Clamp01((Time.time - t0) / dur), e = 1f - (1f - k) * (1f - k) * (1f - k);
                t.position = home + Vector3.down * depth * (1f - e);
                if (fx && Time.time >= nextSpark)
                {
                    nextSpark = Time.time + 0.18f;
                    var sp = new Vector3(Random.Range(b.min.x, b.max.x), home.y + (b.max.y - home.y) * Random.Range(0.3f, 0.9f) - depth * (1f - e), Random.Range(b.min.z, b.max.z));
                    var outw = sp - b.center; outw.y = 0f; fx.WeldSparks(sp, outw.sqrMagnitude > 0.01f ? outw.normalized : Vector3.up, 0.9f);
                    AudioDirector.I?.Play("bld_repair_weld", sp, o.Id);
                }
                yield return null;
            }
            if (t) t.position = home;
            fx?.DemolitionDust(home, 4f, 4f, 0.35f, 0.28f, 1.6f);
            CombatAudio.I?.Play("repair", home, 0.8f, 0, o.Id, OzSoundV3.BankWrench);
        }

        IEnumerator Rebuild(ZObject o, float span) => Rebuild(o, span, false);

        /// <summary>The crane working on building `id` is still there (a crane shot dead never sends its CraneAnim off).</summary>
        bool CraneAlive(int id) { if (!craneOf.TryGetValue(id, out int cid)) return true; var c = game.World.GetAny(cid); return c != null && !c.Destroyed; }

        IEnumerator Rebuild(ZObject o, float span, bool crane)
        {
            rebuilding.Add(o.Id); if (crane) CraneRebuilds++;
            OzRuinFires.End(o.Id);                                                              // the deck comes back: no fire under it
            var set = game.Prefab($"Z_bridge_{(game.level == "L17" ? "city" : "desert")}_chunks");
            var c = game.CenterWorld(o); float deck = c.y + (game.level == "L17" ? 0.3f : 1.2f); bool vert = o.Type == "bridge_vert";
            var pieces = new List<(Transform t, Vector3 from, Vector3 to, Quaternion r0, Quaternion r1, float t0, float t1)>();
            if (set)
            {
                var parts = new List<Transform>(); foreach (Transform ch in set.transform) parts.Add(ch);
                for (int i = 0; i < 12 && parts.Count > 0; i++)
                {
                    float along = Mathf.Lerp(-1.8f, 1.8f, (i % 6) / 5f) * (i < 6 ? 1 : -1), across = (i % 3 - 1) * 1.3f;   // from both ends inward
                    var to = new Vector3(c.x + (vert ? across : along), deck, c.z + (vert ? along : across));
                    var from = to + new Vector3(Random.Range(-4.5f, 4.5f), -2.8f, Random.Range(-4.5f, 4.5f));      // out of the rubble / water around the gap
                    var go = Instantiate(parts[i % Mathf.Min(6, parts.Count)].gameObject, from, Random.rotation);
                    go.transform.localScale *= 1.6f;                                                               // readable from the RTS camera
                    float t0 = span * 0.05f + i * span * 0.055f;
                    pieces.Add((go.transform, from, to, go.transform.rotation, Quaternion.Euler(0, vert ? 0 : 90, 0), t0, t0 + span * 0.3f));
                }
            }
            var fx = FxManager.I; var au = CombatAudio.I; float t = 0; var welded = new HashSet<int>();
            OzSoundV3.BridgeRebuild(c, span, pieces.ConvertAll(x => (x.from, x.t0)), float.MaxValue);          // sound_v3: rise whooshes + drips
            while (t < span)
            {
                t += Time.deltaTime;
                for (int i = 0; i < pieces.Count; i++)
                {
                    var pc = pieces[i]; if (!pc.t) continue;
                    float k = Mathf.Clamp01((t - pc.t0) / (pc.t1 - pc.t0)); float e = k * k * (3 - 2 * k);
                    pc.t.position = Vector3.Lerp(pc.from, pc.to, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 3.2f;
                    pc.t.rotation = Quaternion.Slerp(pc.r0, pc.r1, e);
                    if (k >= 1 && welded.Add(i))
                    {   // snap: weld sparks, a blue-white flash, a clank
                        fx?.Pop(pc.to + Vector3.up * 0.1f, 0.35f); fx?.AddLight(pc.to + Vector3.up * 0.4f, new Color(0.75f, 0.85f, 1f), 3f, 4f, 0.15f, 0.8f, true);
                        au?.Play("weld", pc.to, 0.5f, 0.08f, -1, OzSoundV3.BankMetalMedium);   // sound_v3
                    }
                }
                yield return null;
            }
            if (crane)
            {
                float hold = 0f, nextW = 0f;
                while (o.Destroyed && hold < 60f && craneWork.ContainsKey(o.Id) && CraneAlive(o.Id))
                {
                    hold += Time.deltaTime;
                    if (fx && pieces.Count > 0 && hold >= nextW)
                    {
                        nextW = hold + Random.Range(0.35f, 0.65f);
                        var pc = pieces[Random.Range(0, pieces.Count)];
                        if (pc.t) { fx.WeldSparks(pc.to + Vector3.up * 0.15f, Vector3.up, 0.8f); AudioDirector.I?.Play("bld_repair_weld", pc.to, o.Id); }
                    }
                    yield return null;
                }
                if (o.Destroyed)
                {   // the crane gave up: the plates drop back into the gap
                    foreach (var pc in pieces) if (pc.t) Destroy(pc.t.gameObject);
                    fx?.DemolitionDust(c, 4f, 4f, 0.35f, 0.28f, 1.6f);
                    rebuilding.Remove(o.Id); yield break;
                }
            }
            // the sim repairs now (Revived -> intact deck, passable): dust and the pieces vanish into the rebuilt deck
            yield return new WaitForSeconds(0.1f);
            foreach (var pc in pieces) if (pc.t) Destroy(pc.t.gameObject);
            fx?.DemolitionDust(c + Vector3.up * 0.4f, 5f, 5f, 0.4f, 0.28f, 2.2f); /* critic: faint wide haze */
            au?.Play("repair", c, 0.8f, 0, -1, OzSoundV3.BankWrench);   // sound_v3
            rebuilding.Remove(o.Id);
        }

        void PollBridges()
        {
            foreach (var o in game.World.Objects)
            {
                if (!o.IsBridge || o.Destroyed) continue;                                      // death: BuildingDeath -> SwapBridge
                int want = o.Health < o.MaxHealth / 2 ? 1 : 0;
                bridgeState.TryGetValue(o.Id, out var st);
                if (!bridgeVariants.ContainsKey(o.Id) && want == 0) continue;
                if (st != want && st != 2) SetBridge(o, want, want == 1);
            }
        }

        void Update()
        {
            if (game == null || game.World == null || Time.time < nextSpark) return;
            nextSpark = Time.time + 0.35f;
            PollBridges();
            var fx = FxManager.I; if (!fx) return;
            List<int> done = null;
            foreach (var kv in craneWork)
            {
                if (Time.time > kv.Value || !CraneAlive(kv.Key)) { (done ??= new List<int>()).Add(kv.Key); continue; }
                var b = game.World.Get(kv.Key); if (b == null) continue;
                if (CraneArm.Covers(b.Id)) continue;
                // faces the crane (else a random side), plus a faint work puff
                var bc = game.CenterWorld(b); Vector3 side;
                var cr = craneOf.TryGetValue(b.Id, out int cid) ? game.World.Get(cid) : null;
                if (cr != null && !cr.Destroyed) { var cp = game.CenterWorld(cr); side = cp - bc; side.y = 0f; } else side = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.forward;
                float reach = b.IsBridge ? 1.5f : Mathf.Max(1.5f, Mathf.Min(b.WidthPix, b.HeightPix) / 16f * 0.45f);
                var p = bc + side * reach + Vector3.Cross(Vector3.up, side) * Random.Range(-1.6f, 1.6f) + Vector3.up * (b.IsBridge ? Random.Range(0.6f, 1.4f) : Random.Range(1f, 3f));
                BuildingSurface.OnFacade(game, b, ref p, ref side);                                     // crane_front_reach: on the real face toward the crane (never inside the walls / in the air)
                fx.WeldSparks(p, side, Random.Range(0.8f, 1.1f)); CraneWelds++;
                AudioDirector.I?.Play("bld_repair_weld", p, b.Id);
                if (Random.value < 0.35f) fx.Pop(p, 0.25f);
            }
            if (done != null) foreach (var k in done) craneWork.Remove(k);
        }

        IEnumerator Pulse(Vector3 at, Color c)
        {
            var go = new GameObject("CapturePulse"); var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = true; lr.positionCount = 48; lr.widthMultiplier = 0.25f;
            lr.material = new Material(Shader.Find("Sprites/Default")); lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                float k = t / 1.2f, r = Mathf.Lerp(0.6f, 7f, 1 - (1 - k) * (1 - k));
                var col = c; col.a = 1 - k; lr.startColor = lr.endColor = col;
                for (int i = 0; i < 48; i++)
                {
                    float ang = i * Mathf.PI * 2 / 48; var p = at + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
                    p.y = game.GroundY(p.x, p.z) + 0.12f; lr.SetPosition(i, p);
                }
                yield return null;
            }
            Destroy(lr.material); Destroy(go);
        }

        void Show(string text, Color c) { banner = text; bannerColor = c; bannerTime = Time.unscaledTime; }

        GUIStyle style;
        void OnGUI()
        {
            if (banner == null || Time.unscaledTime - bannerTime > 8f) return;
            style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.fontSize = Mathf.RoundToInt(Screen.height * 0.07f);
            float a = Mathf.Clamp01((8f - (Time.unscaledTime - bannerTime)) / 1.5f);
            var r = new Rect(0, Screen.height * 0.18f, Screen.width, Screen.height * 0.15f);
            GUI.color = new Color(0, 0, 0, 0.7f * a); GUI.Label(new Rect(r.x + 4, r.y + 4, r.width, r.height), banner, style);
            GUI.color = new Color(bannerColor.r, bannerColor.g, bannerColor.b, a); GUI.Label(r, banner, style);
            GUI.color = Color.white;
        }
    }
}
