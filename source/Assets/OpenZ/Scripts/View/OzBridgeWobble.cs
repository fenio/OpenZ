using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the plank bridge gives a little under a vehicle (view only, no rule reads it).
    /// - the planks under the vehicle dip (jeep 1.5 cm, tanks 2.5 cm, heavy tank and rocket launcher 3.5 cm), each board with its
    ///   own looseness (0.7 to 1.3), and spring back behind it with one overshoot (3.4 swings a second, gone after about 0.6 s);
    /// - the span sags in a soft bowl that travels with the vehicle (2 / 4 / 6 cm, 2.5 m to each side), nothing at the bridge ends;
    /// - posts and ropes sway sideways (1 / 2 / 3 cm at rope height, 1.5 swings a second); pile feet stay on the bed;
    /// - the vehicle rides on the deck: ZGame.GroundY takes the same dip off the walk deck;
    /// - soft rings at the outer piles of each pile row the vehicle passes; a plank rumble loop and creaks (sound log).
    /// Several vehicles: sags add up to 8 cm at most, nothing moves more than 12 cm.
    /// </summary>
    public sealed partial class OzBridgeWobble : MonoBehaviour
    {
        public static bool Active;                                  // a vehicle is on a deck this frame
        public static int Loads;                                    // vehicles on a deck this frame
        public static float LastPlank, LastSag, LastSway, LastRide; // m, of the heaviest vehicle on a deck (ride = what the vehicle itself goes down)
        public static float MaxMoved;                               // m, the largest move of any deck point since the last reset
        public static string LastType = "";
        static OzBridgeWobble inst;

        const float RampM = 2f, EndFadeM = 1.6f, SagHalfM = 1.25f, PlankM = 0.3f, FootprintM = 1.6f, SwingHz = 3.4f, SwingTau = 0.2f, SwayHz = 1.5f;
        const string LoopRes = "OpenZ/Audio/Sfx/L16/sd_wheels_plank_loop";
        const string HeavyLoopRes = "OpenZ/Audio/Sfx/L16/sd_bridge_rumble_heavy_loop", LoadCreakRes = "OpenZ/Audio/Sfx/L16/sd_plank_creak_load_a";
        const string SpringRes = "OpenZ/Audio/Sfx/L16/sd_plank_spring_a", RopeRes = "OpenZ/Audio/Sfx/L16/sd_bridge_rope_creak_a";
        static readonly string[] CreakRes = { "OpenZ/Audio/Sfx/L16/sd_plank_creak_a", "OpenZ/Audio/Sfx/L16/sd_plank_creak_b", "OpenZ/Audio/Sfx/L16/sd_plank_creak_c" };

        sealed class Part { public MeshFilter mf; public Mesh mesh; public Vector3[] basePos, work; public float[] along, lift, loose, rope; public Vector3 downLocal, sideLocal; }
        sealed class Deck
        {
            public Transform walk, art; public bool alongZ; public float c, half, mid, halfWidth, deckY; public Vector3 centre;
            public readonly List<Part> parts = new List<Part>(); public bool moved;
        }
        struct Load { public float s, v, plank, sag, sway, across; public int id, cls; public Vector3 pos; }

        readonly List<Deck> decks = new List<Deck>();
        readonly List<Load> loads = new List<Load>();
        readonly Dictionary<int, float> lastS = new Dictionary<int, float>();
        readonly Dictionary<int, float> nextCreak = new Dictionary<int, float>();
        float nextScan; AudioSource loop, creak; bool loopOn; Deck loadDeck;
        readonly Dictionary<int, Vector3> lastPos = new Dictionary<int, Vector3>(); readonly Dictionary<int, int> lastCls = new Dictionary<int, int>();
        float nextRope; int loopCls = -1;

        void OnEnable() { inst = this; }
        void OnDisable() { if (inst == this) inst = null; Active = false; Loads = 0; Restore(); if (loop) loop.Stop(); }

        static void Classify(string type, out float plank, out float sag, out float sway, out int cls)
        {
            switch (type)
            {
                case "jeep": plank = 0.015f; sag = 0.02f; sway = 0.01f; cls = 0; break;
                case "heavy": case "missile_launcher": plank = 0.035f; sag = 0.06f; sway = 0.03f; cls = 2; break;
                default: plank = 0.025f; sag = 0.04f; sway = 0.02f; cls = 1; break;
            }
        }

        static float Hash(int k) { float s = Mathf.Sin(k * 12.9898f + 4.137f) * 43758.5453f; return s - Mathf.Floor(s); }

        void Scan()
        {
            var root = GameObject.Find("ZodObjects");
            var found = new List<Transform>();
            if (root) foreach (var mc in root.GetComponentsInChildren<MeshCollider>(false)) if (mc.name == "WalkDeck") found.Add(mc.transform);
            bool same = found.Count == decks.Count;
            if (same) for (int i = 0; i < found.Count; i++) if (decks[i].walk != found[i]) { same = false; break; }
            if (same) return;
            Restore(); decks.Clear();
            foreach (var w in found)
            {
                var b = w.GetComponent<MeshCollider>().bounds;
                var d = new Deck { walk = w, art = w.parent, alongZ = b.size.z >= b.size.x, deckY = b.max.y, centre = b.center };
                d.c = d.alongZ ? b.center.z : b.center.x; d.mid = d.alongZ ? b.center.x : b.center.z;
                d.half = (d.alongZ ? b.extents.z : b.extents.x) - RampM; d.halfWidth = d.alongZ ? b.extents.x : b.extents.z;
                if (d.half < 2f) continue;
                foreach (var mf in d.art.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (mf.transform == w || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                    var p = new Part { mf = mf, mesh = mf.mesh };                               // its own copy: the prefab mesh stays as it is
                    p.mesh.MarkDynamic(); p.basePos = p.mesh.vertices; int n = p.basePos.Length;
                    p.work = new Vector3[n]; p.along = new float[n]; p.lift = new float[n]; p.loose = new float[n]; p.rope = new float[n];
                    var tr = mf.transform;
                    for (int i = 0; i < n; i++)
                    {
                        var wp = tr.TransformPoint(p.basePos[i]);
                        p.along[i] = (d.alongZ ? wp.z : wp.x) - d.c;
                        p.lift[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(d.deckY - 1.3f, d.deckY - 0.3f, wp.y));   // pile feet stay on the bed
                        p.rope[i] = Mathf.Clamp01((wp.y - d.deckY - 0.1f) / 0.9f);                                       // posts and ropes sway, planks do not
                        p.loose[i] = 0.7f + 0.6f * Hash(Mathf.FloorToInt(p.along[i] / PlankM));
                    }
                    p.downLocal = tr.InverseTransformVector(Vector3.down); p.sideLocal = tr.InverseTransformVector(d.alongZ ? Vector3.right : Vector3.forward);
                    d.parts.Add(p);
                }
                decks.Add(d);
            }
        }

        void Restore()
        {
            foreach (var d in decks)
            {
                if (!d.moved) continue;
                foreach (var p in d.parts) if (p.mesh) { p.mesh.vertices = p.basePos; p.mesh.RecalculateBounds(); }
                d.moved = false;
            }
        }

        /// <summary>What the deck gives at a point along it (m, down is positive). smooth = what a vehicle rides on (no single boards, no swing).</summary>
        float Field(Deck d, float s, float loose, bool smooth, out float sway)
        {
            sway = 0f;
            float end = Mathf.Clamp01((d.half - Mathf.Abs(s)) / EndFadeM); if (end <= 0f) return 0f;
            float sag = 0f, plank = 0f;
            for (int i = 0; i < loads.Count; i++)
            {
                var L = loads[i]; float ds = s - L.s;
                float k = ds / SagHalfM; sag += L.sag * Mathf.Exp(-k * k);
                float k3 = ds / 3f; sway += L.sway * Mathf.Exp(-k3 * k3);
                float ad = Mathf.Abs(ds);
                if (ad <= FootprintM) plank += L.plank * (smooth ? 1f : loose);
                else if (!smooth)
                {
                    float b = ad - FootprintM; bool behind = L.v * ds < 0f || Mathf.Abs(L.v) < 0.3f;
                    if (behind && b < 6f)
                    {   // the contact has left: one overshoot upward, then still
                        float t = b / Mathf.Max(Mathf.Abs(L.v), 0.5f);
                        plank += L.plank * loose * Mathf.Exp(-t / SwingTau) * Mathf.Cos(2f * Mathf.PI * SwingHz * t);
                    }
                    else if (!behind && b < 0.15f) plank += L.plank * loose * (1f - b / 0.15f);
                }
                else if (ad < FootprintM + 0.6f) plank += L.plank * (1f - (ad - FootprintM) / 0.6f);
            }
            sway *= end;
            return Mathf.Clamp(Mathf.Min(sag, 0.08f) + plank, -0.04f, 0.12f) * end;
        }

        /// <summary>For ZGame.GroundY: how far the walk deck is down at this point right now (0 off a deck).</summary>
        public static float DipAt(Collider hit, float x, float z)
        {
            if (!Active || inst == null || hit == null) return 0f;
            var d = inst.loadDeck; if (d == null || hit.transform != d.walk) return 0f;
            return Mathf.Max(0f, inst.Field(d, (d.alongZ ? z : x) - d.c, 1f, true, out _));
        }

        public static void ResetProof() { MaxMoved = 0f; LastPlank = LastSag = LastSway = LastRide = 0f; LastType = ""; }

        void Update()
        {
            var game = ZGame.I; if (game == null || game.World == null) return;
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1.5f; Scan(); }
            loads.Clear(); loadDeck = null; int heavy = -1; Load top = default(Load);
            if (decks.Count > 0)
            {
                var objs = game.World.Objects;
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i]; if (o.Kind != ZKind.Vehicle || o.Destroyed) continue;
                    var p = game.CenterWorldSmooth(o);
                    foreach (var d in decks)
                    {
                        if (!d.walk || !d.walk.gameObject.activeInHierarchy) continue;
                        float s = (d.alongZ ? p.z : p.x) - d.c, a = (d.alongZ ? p.x : p.z) - d.mid;
                        if (Mathf.Abs(s) > d.half + FootprintM || Mathf.Abs(a) > d.halfWidth) continue;
                        if (loadDeck != null && loadDeck != d) continue;                              // one deck at a time carries the field
                        loadDeck = d;
                        var L = new Load { s = s, id = o.Id, pos = p, across = a };
                        Classify(o.Type, out L.plank, out L.sag, out L.sway, out L.cls);
                        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                        L.v = lastS.TryGetValue(o.Id, out var was) && Time.deltaTime > 0f ? Mathf.Clamp((s - was) / dt, -12f, 12f) : 0f;
                        if (lastS.ContainsKey(o.Id)) Rings(game, d, was, s, L.cls);
                        else if (L.cls > 0) Creak(L, true);                                           // the weight comes on: a long creak
                        lastS[o.Id] = s; lastPos[o.Id] = p; lastCls[o.Id] = L.cls; loads.Add(L);
                        if (L.cls > heavy) { heavy = L.cls; top = L; LastType = o.Type; }
                        break;
                    }
                }
            }
            if (loads.Count == 0) { if (lastS.Count > 0) { foreach (var kv in lastS) Left(kv.Key); lastS.Clear(); nextCreak.Clear(); lastPos.Clear(); lastCls.Clear(); } }
            else
            {   // forget vehicles that left
                if (lastS.Count > loads.Count) { var keep = new HashSet<int>(); foreach (var L in loads) keep.Add(L.id); var gone = new List<int>(); foreach (var kv in lastS) if (!keep.Contains(kv.Key)) gone.Add(kv.Key); foreach (var g in gone) { Left(g); lastS.Remove(g); nextCreak.Remove(g); lastPos.Remove(g); lastCls.Remove(g); } }
            }
            VoiceStep(game);                                                                        // plank knocks at the pile rows, creaks under robots on foot (OzBridgeWobble.Voice.cs)
            Active = loads.Count > 0; Loads = loads.Count;
            if (!Active) { Restore(); Sound(false, 0, Vector3.zero, false); return; }

            var dk = loadDeck; float swing = Mathf.Sin(2f * Mathf.PI * SwayHz * Time.time);
            foreach (var p in dk.parts)
            {
                if (!p.mesh) continue;
                var bp = p.basePos; var w = p.work;
                for (int i = 0; i < bp.Length; i++)
                {
                    float dip = Field(dk, p.along[i], p.loose[i], false, out float sway);
                    float moved = dip * p.lift[i]; if (Mathf.Abs(moved) > MaxMoved) MaxMoved = Mathf.Abs(moved);
                    w[i] = bp[i] + p.downLocal * moved + p.sideLocal * (sway * swing * p.rope[i]);
                }
                p.mesh.vertices = w; p.mesh.RecalculateBounds();
            }
            dk.moved = true;
            LastPlank = top.plank; LastSway = top.sway;
            { float k = 0f; LastSag = top.sag * Mathf.Clamp01((dk.half - Mathf.Abs(top.s)) / EndFadeM); LastRide = Mathf.Max(0f, Field(dk, top.s, 1f, true, out k)); }
            bool rolling = false; foreach (var L in loads) { if (Mathf.Abs(L.v) > 0.3f) rolling = true; if (L.cls >= 0 && Mathf.Abs(L.v) > 0.3f) Creak(L, false); }
            Sound(rolling, heavy, top.pos, game.paused);
            if (rolling && heavy >= 0 && !game.paused && Time.time >= nextRope && Mathf.Abs(top.sway) > 0.0001f)
            {   // the rope rails strain on the sway of the span under a vehicle (a jeep's lighter sway too), at most every 2 s
                nextRope = Time.time + Random.Range(2f, 3.4f);
                if (!VoiceRope(top, heavy)) OneShot(RopeRes, "bld_bridge_rope_creak", top.pos + Vector3.up * 1.2f, heavy <= 1 ? 0.4f : 0.6f, "the span sways under a vehicle");
            }
        }

        /// <summary>A vehicle has left the planks: they spring back.</summary>
        void Left(int id)
        {
            if (!lastPos.TryGetValue(id, out var at) || !lastCls.TryGetValue(id, out int cls) || cls < 1) return;
            OneShot(SpringRes, "bld_bridge_plank_spring", at, cls == 1 ? 0.5f : 0.7f, "the last axle leaves the span");
        }

        void OneShot(string res, string evt, Vector3 at, float vol, string why)
        {
            var c = DetailKit.Pool3D.Clip(res); vol *= Vol();
            if (c) { if (!creak) creak = Src("L16_BridgeCreak", false); creak.transform.position = at; creak.pitch = Random.Range(0.95f, 1.05f); creak.PlayOneShot(c, vol); }
            OzSoundLog.Add("L16", evt, c ? c.name : null, c ? "PLAY World (" + why + ")" : "DROP clip missing", vol, at);
        }

        void Rings(ZGame game, Deck d, float was, float now, int cls)
        {   // a pile row every 2 m: soft rings at its outer piles when the vehicle reaches it
            int a = Mathf.FloorToInt(was / 2f), b = Mathf.FloorToInt(now / 2f);
            if (a == b || Mathf.Abs(now) > d.half) return;
            var fx = OpenZFxPlayer.I; if (!fx) return;
            float row = Mathf.Max(a, b) * 2f + d.c, strength = cls == 0 ? 0.45f : cls == 1 ? 0.7f : 1f;
            for (int side = -1; side <= 1; side += 2)
            {
                float ax = d.mid + side * (d.halfWidth - 0.4f);
                var p = d.alongZ ? new Vector3(ax, 0f, row) : new Vector3(row, 0f, ax);
                p.y = OzWaterLevel.At(game.level, p.x, p.z, d.deckY - 1.2f);
                fx.Ripple(p, 0.6f * strength);
            }
        }

        static float Vol() => CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;

        AudioSource Src(string name, bool looping)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.loop = looping; s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 8f; s.maxDistance = 70f;
            return s;
        }

        void Creak(Load L, bool comingOn)
        {
            float now = Time.time;
            if (!comingOn) { if (nextCreak.TryGetValue(L.id, out var at) && now < at) return; if (!nextCreak.ContainsKey(L.id)) { nextCreak[L.id] = now + (L.cls == 0 ? Random.Range(1.2f, 2.5f) : Random.Range(0.4f, 0.9f)); return; } }
            nextCreak[L.id] = now + (L.cls == 0 ? Random.Range(2.5f, 4f) : L.cls == 1 ? Random.Range(0.8f, 1.3f) : Random.Range(0.4f, 0.65f));
            bool load = comingOn && L.cls == 2;                                                    // a heavy vehicle comes on: the long creak under load
            if (VoiceCreak(L, comingOn)) return;                                                   // through the sound table: wood creak / beam groan variations (OzBridgeWobble.Voice.cs)
            string res = load ? LoadCreakRes : comingOn ? CreakRes[2] : CreakRes[Random.Range(0, 2)];
            var c = DetailKit.Pool3D.Clip(res);
            float vol = (L.cls == 0 ? 0.35f : L.cls == 1 ? 0.55f : 0.8f) * Vol();
            if (c) { if (!creak) creak = Src("L16_BridgeCreak", false); creak.transform.position = L.pos; creak.pitch = Random.Range(0.92f, 1.08f); creak.PlayOneShot(c, vol); }
            OzSoundLog.Add("L16", load ? "bld_bridge_plank_creak_load" : "bld_bridge_plank_creak", c ? c.name : null, c ? (comingOn ? "PLAY World (the weight comes on)" : "PLAY World") : "DROP clip missing", vol, L.pos);
        }

        void Sound(bool rolling, int cls, Vector3 at, bool paused)
        {
            bool want = rolling && !paused && Time.timeScale > 0f;
            if (want && loopOn && (cls == 2) != (loopCls == 2)) { if (loop) loop.Stop(); loopOn = false; OzSoundLog.Add("L16", loopCls == 2 ? "bld_bridge_rumble_heavy" : "veh_wheels_plank", null, "LOOP STOP (another weight class)"); }
            if (want && !loopOn)
            {
                bool heavyLoop = cls == 2;                                                             // a heavy vehicle: the deep rumble instead of the wheel loop
                var c = DetailKit.Pool3D.Clip(heavyLoop ? HeavyLoopRes : LoopRes);
                if (!c && heavyLoop) { c = DetailKit.Pool3D.Clip(LoopRes); heavyLoop = false; }
                if (!loop) loop = Src("L16_BridgePlankLoop", true);
                loop.clip = c; loopCls = heavyLoop ? 2 : Mathf.Min(cls, 1);
                if (c) { loop.time = Random.Range(0f, Mathf.Max(0f, c.length - 0.1f)); loop.Play(); }
                loopOn = true;
                OzSoundLog.Add("L16", heavyLoop ? "bld_bridge_rumble_heavy" : "veh_wheels_plank", c ? c.name : null, c ? "LOOP World (a vehicle on the plank bridge)" : "DROP clip missing", (cls == 0 ? 0.3f : cls == 1 ? 0.5f : 0.75f) * Vol(), at);
            }
            else if (!want && loopOn)
            {
                if (loop) loop.Stop(); loopOn = false;
                OzSoundLog.Add("L16", "veh_wheels_plank", null, "LOOP STOP");
            }
            if (loopOn && loop) { loop.transform.position = at; loop.volume = (cls == 0 ? 0.3f : cls == 1 ? 0.5f : 0.75f) * Vol(); }
        }
    }
}
