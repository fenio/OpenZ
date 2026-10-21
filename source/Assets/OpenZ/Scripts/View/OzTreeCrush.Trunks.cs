using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Item 360 (OzTreeCrush): the trunks round a crash, solid for the big pieces only. Round the shuttle's break, the fort cannon's
    /// landing and a vehicle or gun wreck near trees, every standing tall plant within the reach of the pieces gets a capsule round its
    /// drawn trunk (foot to 0.6 of its height, at most 7 m; 0.3 m round), on a layer of its own that the layer table keeps out of every
    /// contact; only the big pieces' colliders include it (the shuttle's and the fort cannon's pieces, a wreck's heavy parts), so every
    /// other body, ray and unit behaves as before. A tree of the rules that is standing is SOLID: a piece bounces off it or lies beside
    /// it (a knock and a few leaves), never through it. A tree the rules are felling under the faller, and every drawn tree with no
    /// rules, is a TRIGGER: the first big piece that enters it breaks it there (a held tree of the rules: its drawn break; the others
    /// fall at once, view only). After the game is over (the rules decide nothing then) every tree is a trigger. A capsule goes when its
    /// tree falls or after the crash's time; a solid trunk a piece already lies in when it is made becomes a trigger (no shove) and is
    /// counted (<see cref="StartInPiece"/>). A capsule's collider is on only while a big piece is within <see cref="OnNear"/> m of it, so
    /// no ray or query of the rest of the game meets a trunk anywhere else.
    /// </summary>
    public partial class OzTreeCrush
    {
        /// <summary>The reach of a crash's pieces (m) and how long its trunks stay (s); a wreck near trees: its reach and time.</summary>
        public static float ArmRadius = 18f, CrashArmSeconds = 26f, WreckArmRadius = 12f, WreckArmSeconds = 22f, WreckWatchSeconds = 5f, TreeNearWreck = 9f;
        public const float TrunkR = 0.3f, TrunkMaxH = 7f;
        /// <summary>Counts: trunks made (solid / trigger), big pieces that include them, solid hits (knocks), trunks a piece lay in when made.</summary>
        public static int TrunksMade, SolidTrunks, TriggerTrunks, PiecesIncluded, SolidHits, StartInPiece, StartTriggers, Arms, WreckArms, TrunksOn;

        static void ResetTrunkCounts() { TrunksMade = SolidTrunks = TriggerTrunks = PiecesIncluded = SolidHits = StartInPiece = StartTriggers = Arms = WreckArms = TrunksOn = 0; }

        // ------------------------------------------------------------------ the layer
        static int layer = -2;
        /// <summary>The trunks' own layer: the first unnamed one of 6, 7 and 3 that the rocks (OzRockSolid.Layer) do not take. The game's
        /// own searches for a free layer look at 8..31 only (the debris 31, the walk layer 30, the crater cut 27 and the camera's cameo 29
        /// from the top down), so none of them meets it; -1 = none free (then no trunk is made and the big pieces pass the trees as before).</summary>
        public static int Layer { get { if (layer == -2) layer = PickLayer(); return layer; } }
        static int PickLayer()
        {
            int rock = OzRockSolid.Layer;
            foreach (int l in new[] { 6, 7, 3 }) if (l != rock && string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            return -1;
        }
        static bool tableSet;
        static void LayerTable()
        {
            if (tableSet || Layer < 0) return;
            tableSet = true;
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layer, i, true);                    // nothing meets the trunks by the table ...
        }
        /// <summary>... only a big piece's collider that includes the layer (its override wins over the trunk's own).</summary>
        public static void Include(Collider c)
        {
            if (!c || Layer < 0) return;
            LayerTable();
            if (I && !I.bigCols.Contains(c)) I.bigCols.Add(c);
            if ((c.includeLayers.value & (1 << Layer)) != 0) return;
            c.includeLayers = c.includeLayers.value | (1 << Layer);
            if (c.layerOverridePriority < 1) c.layerOverridePriority = 1;
            PiecesIncluded++;
        }
        /// <summary>The big pieces that include the trunks (a trunk's collider is on only while one of them is within <see cref="OnNear"/> m of it,
        /// so no ray or query of the rest of the game meets a trunk anywhere else).</summary>
        readonly List<Collider> bigCols = new List<Collider>();
        public static float OnNear = 1.5f;
        public static bool IsTrunk(Collider c) => c && Layer >= 0 && c.gameObject.layer == Layer;

        // ------------------------------------------------------------------ the trees round a point
        internal struct TreeRef { public int kind, simId, key; public ZObject o; public Transform plant; public DetailProp dp; public Vector3 foot; public float height, radius, r; public bool rules; }
        readonly List<TreeRef> nearBuf = new List<TreeRef>();
        readonly List<OzTreeFall.NearTree> floraBuf = new List<OzTreeFall.NearTree>();

        /// <summary>The standing (drawn) tall plants within r of p: the rules' (kind 0), the jungle's other trees (1), the desert's view palms (2).</summary>
        List<TreeRef> TreesNear(ZWorld w, Vector3 p, float r)
        {
            nearBuf.Clear(); float r2 = r * r;
            for (int i = 0; i < w.HeavyTreeCount; i++)
            {
                var o = w.HeavyTreeAt(i, out double fx, out double fy);
                var f0 = ZGame.PxToWorld(fx, fy);
                if ((f0.x - p.x) * (f0.x - p.x) + (f0.z - p.z) * (f0.z - p.z) > r2) continue;
                var t = new TreeRef { kind = 0, simId = o.Id, key = o.Id, o = o, rules = true, foot = f0, height = 4f, r = TrunkR };
                if (w.IsTreeTile(o))
                {
                    if (!OzTreeFall.RulesTree(o.LocX >> 4, o.LocY >> 4, out var ft, out var h, out bool down)) { if (!w.HeavyTreeStanding(o)) continue; t.foot.y = game ? game.GroundY(f0.x, f0.z) : 0f; }
                    else { if (down) continue; t.foot = ft; t.height = h; }
                }
                else if (o.Type == ZWorld.DetailPropType)
                {
                    var dp = DetailPropHub.OfSim(o.Id);
                    if (dp) { if (dp.Broken) continue; t.dp = dp; t.foot = dp.transform.position; t.height = PropHeight(dp); }
                    else { if (!w.HeavyTreeStanding(o)) continue; t.foot.y = game ? game.GroundY(f0.x, f0.z) : 0f; }
                }
                else { if (!w.HeavyTreeStanding(o)) continue; t.foot.y = game ? game.GroundY(f0.x, f0.z) : 0f; t.height = 3f; }
                nearBuf.Add(t);
            }
            floraBuf.Clear(); OzTreeFall.FloraNear(p, r, floraBuf);
            foreach (var f in floraBuf) nearBuf.Add(new TreeRef { kind = 1, simId = -1, key = f.plant.GetInstanceID(), plant = f.plant, foot = f.foot, height = f.height, radius = f.radius, r = TrunkR });
            var props = DetailProp.All;
            for (int i = 0; i < props.Count; i++)
            {
                var dp = props[i];
                if (!dp || dp.SimBacked || dp.HpOwned || dp.Broken || dp.intact) continue;
                var f = dp.transform.position;
                if ((f.x - p.x) * (f.x - p.x) + (f.z - p.z) * (f.z - p.z) > r2) continue;
                if (!IsPlant(dp)) continue;                                                             // the name only for the props near
                nearBuf.Add(new TreeRef { kind = 2, simId = -1, key = dp.GetInstanceID(), dp = dp, foot = f, height = PropHeight(dp), r = TrunkR });
            }
            return nearBuf;
        }

        static bool IsPlant(DetailProp dp) { var n = dp.name.ToLowerInvariant(); return n.Contains("palm") || n.Contains("cactus"); }
        static readonly Dictionary<int, float> propHeight = new Dictionary<int, float>();
        static float PropHeight(DetailProp dp)
        {
            int k = dp.GetInstanceID();
            if (propHeight.TryGetValue(k, out float h)) return h;
            h = 3f; bool any = false; float top = 0f;
            foreach (var r in dp.GetComponentsInChildren<Renderer>()) { if (!r.enabled) continue; top = any ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y; any = true; }
            if (any) h = Mathf.Max(1.5f, top - dp.transform.position.y);
            propHeight[k] = h; return h;
        }

        /// <summary>A drawn tree with no rules falls (view only) away from `from`.</summary>
        bool FellDrawn(TreeRef t, Vector3 from, Vector3 hit)
        {
            if (t.kind == 1) { if (OzTreeFall.CrushFlora(t.plant, t.foot, t.height, t.radius, from, hit)) { FloraFelled++; TrunkDownKey(t.key); return true; } return false; }
            if (t.kind == 2 && t.dp && !t.dp.Broken) { t.dp.Break(from); PropsFelled++; TrunkDownKey(t.key); return true; }
            return false;
        }

        // ------------------------------------------------------------------ the trunks
        internal sealed class Trunk { public TreeRef t; public GameObject go; public CapsuleCollider col; public float until; public bool solid, down; public readonly HashSet<int> knocked = new HashSet<int>(); }
        readonly Dictionary<int, Trunk> rulesTrunks = new Dictionary<int, Trunk>(), drawnTrunks = new Dictionary<int, Trunk>();
        readonly List<Trunk> allTrunks = new List<Trunk>();

        bool IsTargetOfLiveFall(ZWorld w, int id)
        {
            var f = w.HeavyFallOfTree(id);
            if (f == null) return false;
            if (!f.landed) return f.targets.Contains(id);
            return f.trees.Contains(id) && !TrunkIsDown(id);
        }

        /// <summary>The trunks within radius of `at` made (or kept longer): solid for a standing tree of the rules, else a trigger.</summary>
        void Arm(ZWorld w, Vector3 at, float radius, float seconds, ZHeavyFall f)
        {
            if (Layer < 0) return;
            LayerTable(); Arms++;
            var near = TreesNear(w, at, radius);
            bool over = w.GameOver;
            foreach (var t in near)
            {
                var map = t.kind == 0 ? rulesTrunks : drawnTrunks;
                bool solid = t.kind == 0 && !over && !IsTargetOfLiveFall(w, t.simId) && !held.Exists(h => h.o.Id == t.simId);
                if (!map.TryGetValue(t.key, out var k) || !k.go)
                {
                    k = new Trunk { t = t };
                    k.go = new GameObject("OZ_Trunk_" + (t.kind == 0 ? "rules_" + t.simId : t.plant ? t.plant.name : t.dp ? t.dp.name : "plant")) { layer = Layer };
                    k.go.transform.SetParent(transform, false); k.go.transform.position = t.foot;
                    k.col = k.go.AddComponent<CapsuleCollider>();
                    float h = Mathf.Clamp(t.height * 0.6f, 1.2f, TrunkMaxH);
                    k.col.direction = 1; k.col.radius = t.r; k.col.height = h; k.col.center = new Vector3(0f, h * 0.5f, 0f);
                    k.go.AddComponent<OzTrunk>().Bind(this, k);
                    map[t.key] = k; allTrunks.Add(k); TrunksMade++;
                    k.solid = !solid; SetSolid(k, solid);
                }
                else if (k.solid != solid) SetSolid(k, solid);
                k.until = Mathf.Max(k.until, Time.time + seconds);
            }
        }

        static void SetSolid(Trunk k, bool solid)
        {
            if (k.solid == solid || !k.col) return;
            k.solid = solid; k.col.isTrigger = !solid;
            if (solid) SolidTrunks++; else TriggerTrunks++;
        }

        /// <summary>Pieces that start inside a solid trunk (they were made where the drawn faller was): that trunk is a trigger from now on (no shove).</summary>
        void StartOverlaps(List<Collider> cols, Vector3 at)
        {
            foreach (var k in allTrunks)
            {
                if (k.down || !k.col || !k.go) continue;
                float h = k.col.height;
                var b = new Bounds(k.t.foot + Vector3.up * (h * 0.5f), new Vector3(2f * k.col.radius, h, 2f * k.col.radius));   // its own box (a trunk switched off has no bounds)
                bool was = k.col.enabled;
                foreach (var c in cols)
                {
                    if (!c || !c.enabled || !c.bounds.Intersects(b)) continue;
                    if (!was) k.col.enabled = true;
                    bool deep = Physics.ComputePenetration(k.col, k.go.transform.position, k.go.transform.rotation, c, c.transform.position, c.transform.rotation, out _, out float d) && d >= 0.02f;
                    if (!was) k.col.enabled = false;
                    if (!deep) continue;
                    if (k.solid) { SetSolid(k, false); StartInPiece++; LastLog = $"a standing tree ({k.go.name}) lies inside {c.name} at the start: made a trigger"; }
                    else StartTriggers++;
                    break;
                }
            }
        }

        void TrunkDown(int simId) { if (rulesTrunks.TryGetValue(simId, out var k)) Down(k); }
        void TrunkDownKey(int key) { if (drawnTrunks.TryGetValue(key, out var k)) Down(k); }
        bool TrunkIsDown(int simId)
        {
            if (rulesTrunks.TryGetValue(simId, out var k)) return k.down;
            var dp = DetailPropHub.OfSim(simId); if (dp) return dp.Broken;
            var w = game ? game.World : null; var o = w != null ? w.GetAny(simId) : null;
            return o != null && w.IsTreeTile(o) && OzTreeFall.RulesTree(o.LocX >> 4, o.LocY >> 4, out _, out _, out bool down) && down;
        }

        static void Down(Trunk k)
        {
            if (k.down) return;
            k.down = true;
            if (k.go) Object.Destroy(k.go);
        }

        void StepTrunks(ZWorld w)
        {
            for (int i = allTrunks.Count - 1; i >= 0; i--)
            {
                var k = allTrunks[i];
                if (!k.down && Time.time < k.until && k.go) continue;
                Down(k); allTrunks.RemoveAt(i);
                var map = k.t.kind == 0 ? rulesTrunks : drawnTrunks;
                if (map.TryGetValue(k.t.key, out var m) && m == k) map.Remove(k.t.key);
            }
            for (int i = bigCols.Count - 1; i >= 0; i--) if (!bigCols[i] || !bigCols[i].enabled || !bigCols[i].gameObject.activeInHierarchy) bigCols.RemoveAt(i);
            foreach (var k in allTrunks)
            {   // a trunk is on only while a big piece is near it
                if (k.down || !k.col) continue;
                float h = k.col.height;
                var box = new Bounds(k.t.foot + Vector3.up * (h * 0.5f), new Vector3(2f * k.col.radius + 2f * OnNear, h + 2f * OnNear, 2f * k.col.radius + 2f * OnNear));
                bool on = false;
                for (int j = 0; j < bigCols.Count && !on; j++) on = bigCols[j].bounds.Intersects(box);
                if (k.col.enabled != on) { k.col.enabled = on; if (on) TrunksOn++; }
            }
            for (int i = watches.Count - 1; i >= 0; i--)
            {   // a wreck near trees: its heavy parts include the trunks as they appear
                var wa = watches[i];
                if (Time.time > wa.until) { watches.RemoveAt(i); continue; }
                int n = Physics.OverlapSphereNonAlloc(wa.at, WreckArmRadius, overlap, DebrisMask(), QueryTriggerInteraction.Ignore);
                fresh.Clear();
                for (int j = 0; j < n; j++)
                {
                    var c = overlap[j]; var rb = c ? c.attachedRigidbody : null;
                    if (!rb || rb.mass < BigPieceKg || MaxSide(c.bounds) < BigPieceM || (c.includeLayers.value & (1 << Layer)) != 0) continue;
                    Include(c); fresh.Add(c);
                }
                if (fresh.Count > 0) StartOverlaps(fresh, wa.at);
            }
        }

        static float MaxSide(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        static int debrisMask;
        static int DebrisMask()
        {
            if (debrisMask != 0) return debrisMask;
            int heavy = LayerMask.NameToLayer("DebrisHeavy"); if (heavy < 0) heavy = OzFortGunBreakup.HeavyLayerFallback;
            debrisMask = (1 << heavy) | (1 << BuildingBreaker.DebrisLayer);
            return debrisMask;
        }
        sealed class Watch { public Vector3 at; public float until; }
        readonly List<Watch> watches = new List<Watch>();
        readonly Collider[] overlap = new Collider[256];
        readonly List<Collider> fresh = new List<Collider>();

        /// <summary>The rules' deaths of this frame: a vehicle or gun destroyed near trees arms the trunks round its wreck.</summary>
        void ScanDeaths(ZWorld w)
        {
            var ev = w.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i]; if (e.type != ZEventType.Death) continue;
                var o = w.GetAny(e.objId);
                if (o == null || (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) || o.IsShuttle) continue;
                var at = game.CenterWorld(o);
                if (TreesNear(w, at, TreeNearWreck).Count == 0) continue;
                Arm(w, at, WreckArmRadius, WreckArmSeconds, null); WreckArms++;
                watches.Add(new Watch { at = at, until = Time.time + WreckWatchSeconds });
            }
        }

        void ClearTrunks()
        {
            foreach (var k in allTrunks) if (k.go) Destroy(k.go);
            allTrunks.Clear(); rulesTrunks.Clear(); drawnTrunks.Clear(); watches.Clear(); bigCols.Clear();
        }

        // ------------------------------------------------------------------ contacts
        static bool Big(Collider c, out Rigidbody rb)
        {
            rb = c ? c.attachedRigidbody : null;
            return rb && rb.mass >= BigPieceKg && MaxSide(c.bounds) >= BigPieceM && (c.includeLayers.value & (1 << Layer)) != 0;
        }

        /// <summary>OzTrunk: a collider met this trunk (trigger = it entered it; else it hit the solid trunk).</summary>
        void Touched(Trunk k, Collider c, bool trigger, Vector3 contact)
        {
            if (k.down || !Enabled) return;
            if (!Big(c, out var rb)) return;
            var w = game ? game.World : null; if (w == null) return;
            var v = rb.linearVelocity; v.y = 0f;
            var foot = k.t.foot;
            var from = v.sqrMagnitude > 0.25f ? foot - v.normalized * 3f : rb.worldCenterOfMass;            // the tree goes the way the piece went
            var hit = new Vector3(foot.x, Mathf.Clamp(contact.y, foot.y + 0.3f, foot.y + k.col.height), foot.z);
            if (k.t.kind != 0) { if (FellDrawn(k.t, from, hit)) Extras(hit, k.t.key); return; }
            int id = k.t.simId;
            int hi = held.FindIndex(h => h.o.Id == id);
            if (hi >= 0) { Release(hi, "touch", from, hit); return; }
            var o = k.t.o;
            if (IsTargetOfLiveFall(w, id))
            {   // a piece reached a tree the rules are about to destroy: its drawn break now (the rules' Death follows, already drawn)
                var dp = DetailPropHub.OfSim(id);
                if (dp && !dp.Broken) { Extras(hit, id); dp.Break(from); earlyBroken.Add(id); Down(k); }
                return;
            }
            if (w.GameOver)
            {   // the rules decide nothing any more: the view fells it (as the fort's blast does)
                var dp = DetailPropHub.OfSim(id);
                if (dp && !dp.Broken) { Extras(hit, id); dp.Break(from); GameOverFelled++; Down(k); }
                return;
            }
            if (!trigger && k.knocked.Add(rb.GetInstanceID()))
            {   // a standing tree of the rules stops the piece: a knock, a few leaves
                SolidHits++;
                var dp = DetailPropHub.OfSim(id); if (dp) dp.Damage(1f, rb.worldCenterOfMass, true);
            }
        }

        /// <summary>The trunk's own component: hands its contacts to OzTreeCrush.</summary>
        internal sealed class OzTrunk : MonoBehaviour
        {
            OzTreeCrush hub; Trunk k;
            internal void Bind(OzTreeCrush o, Trunk t) { hub = o; k = t; }
            void OnTriggerEnter(Collider c) { if (hub) hub.Touched(k, c, true, c.bounds.center); }
            void OnCollisionEnter(Collision c) { if (hub) hub.Touched(k, c.collider, false, c.contactCount > 0 ? c.GetContact(0).point : c.collider.bounds.center); }
        }
    }
}
