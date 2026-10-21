using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Flying robot bodies are heard (every level). A ragdoll thrown hard (RobotRagdoll.Launch faster than WhooshFromMps: a blast,
    /// not a robot that only falls back) gets a soft long whoosh (dst_ragdoll_whoosh) as it leaves; every ragdoll, thrown or only
    /// dropped, gets one metal body thud (dst_ragdoll_thud) the first time it hits something that is not itself harder than
    /// ThudFromMps, at least ThudAfter seconds after the launch (not on water: there the splash rule of OzWaterLanding plays).
    /// Only on camera (the events are in-view-only, and a body off the picture is not even asked for), and capped: at most
    /// WhooshPerSecond whooshes and ThudPerSecond thuds in any second over the whole map; the requests of one frame are served
    /// nearest the camera first, the rest are dropped with a line in the sound log ("DROP cap"). The takes are picked by the sound
    /// table (shuffled, never the same twice running) with its own pitch and volume spread.
    /// The scripted flight of a robot body (UnitView's "airborne" death, used for blast kills when the ragdoll cap is reached) is heard
    /// the same way: FlightThrown on its launch frame, FlightLanded on its landing frame, through the same caps.
    /// View only. The existing clatter of the limbs (dst_ragdoll_impact) stays as it was.
    /// </summary>
    public static class OzBodySounds
    {
        public static bool On = true;
        public const string WhooshEvent = "dst_ragdoll_whoosh", ThudEvent = "dst_ragdoll_thud";
        public static float WhooshFromMps = 4.5f, ThudFromMps = 2.5f, ThudAfter = 0.15f, ThudWindow = 8f;
        public static int WhooshPerSecond = 3, ThudPerSecond = 4;
        /// <summary>Checks: asked (thrown hard / landed), played, dropped by the cap, not asked because off the picture, landings on water.</summary>
        public static int WhooshAsked, WhooshPlayed, WhooshCapped, WhooshOffScreen, ThudAsked, ThudPlayed, ThudCapped, ThudOffScreen, ThudOnWater;

        struct Ask { public string evt; public Vector3 at; public float vol; public int emitter; public OzBodyLanding body; }
        static readonly List<Ask> asks = new List<Ask>();
        static readonly Queue<float> whooshAt = new Queue<float>(), thudAt = new Queue<float>();
        static Runner runner;

        public static void Reset() { WhooshAsked = WhooshPlayed = WhooshCapped = WhooshOffScreen = ThudAsked = ThudPlayed = ThudCapped = ThudOffScreen = ThudOnWater = 0; }

        /// <summary>RobotRagdoll.Launch, after the throw: the whoosh (thrown hard) and the landing watch on every body of it.</summary>
        public static void Launched(RobotRagdoll rd, Vector3 velocity)
        {
            if (!On || !rd) return;
            var state = rd.GetComponent<OzBodyLanding>(); if (!state) state = rd.gameObject.AddComponent<OzBodyLanding>();
            state.Begin();
            foreach (var rb in rd.GetComponentsInChildren<Rigidbody>(true))
                if (rb.gameObject != rd.gameObject && !rb.GetComponent<OzBodyLandingPart>()) rb.gameObject.AddComponent<OzBodyLandingPart>().state = state;
            float speed = velocity.magnitude;
            if (speed < WhooshFromMps) return;
            WhooshAsked++;
            var at = rd.transform.position + Vector3.up * 1.0f;
            if (!OnScreen(at)) { WhooshOffScreen++; return; }
            Queue(WhooshEvent, at, Mathf.Clamp(0.55f + speed * 0.04f, 0.6f, 1f), rd.GetInstanceID(), null);
        }

        /// <summary>OzBodyLandingPart: the first hard contact of a body after its launch.</summary>
        public static void Landed(OzBodyLanding state, Vector3 at, float speed) => Thud(at, speed, state ? state.GetInstanceID() : 0, state);

        /// <summary>UnitView, the scripted flight (always a blast) leaves the ground: the whoosh. from / to = its start and landing point,
        /// seconds = its flight time, apex = how high it goes over the straight line.</summary>
        public static void FlightThrown(Vector3 from, Vector3 to, float seconds, float apex, int emitter)
        {
            if (!On) return;
            float speed = FlightSpeed(from, to, seconds, apex);
            WhooshAsked++;
            var at = from + Vector3.up * 1.0f;
            if (!OnScreen(at)) { WhooshOffScreen++; return; }
            Queue(WhooshEvent, at, Mathf.Clamp(0.55f + speed * 0.04f, 0.6f, 1f), emitter, null);
        }

        /// <summary>UnitView, the scripted flight lands (its landing frame, once): the metal body thud (not on water).</summary>
        public static void FlightLanded(Vector3 at, Vector3 from, Vector3 to, float seconds, float apex, int emitter) => Thud(at, FlightSpeed(from, to, seconds, apex), emitter, null);

        /// <summary>The scripted arc's speed at its ends: along the ground (distance / time) and up or down (pi x apex / time).</summary>
        static float FlightSpeed(Vector3 from, Vector3 to, float seconds, float apex)
        {
            float T = Mathf.Max(0.1f, seconds), dx = to.x - from.x, dz = to.z - from.z;
            float along = Mathf.Sqrt(dx * dx + dz * dz) / T, up = Mathf.PI * Mathf.Max(0f, apex) / T;
            return Mathf.Sqrt(along * along + up * up);
        }

        static void Thud(Vector3 at, float speed, int emitter, OzBodyLanding body)
        {
            if (!On) return;
            ThudAsked++;
            if (OzWaterLanding.UnderSurface(at) || (OzWaterLanding.OpenWaterAt(at, out float wy) && at.y < wy + 0.3f)) { ThudOnWater++; if (body) body.thud = "on water"; return; }
            if (!OnScreen(at)) { ThudOffScreen++; if (body) body.thud = "off the picture"; return; }
            if (body) body.thud = "asked";
            Queue(ThudEvent, at, Mathf.Clamp01(0.45f + speed * 0.06f), emitter, body);
        }

        static bool OnScreen(Vector3 p)
        {
            var cam = Camera.main; if (!cam) return true;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

        static void Queue(string evt, Vector3 at, float vol, int emitter, OzBodyLanding body)
        {
            asks.Add(new Ask { evt = evt, at = at, vol = vol, emitter = emitter, body = body });
            if (!runner) { var go = new GameObject("OzBodySounds"); go.hideFlags = HideFlags.HideInHierarchy; runner = go.AddComponent<Runner>(); }
        }

        /// <summary>Once a frame, after the frame's launches and landings: nearest the camera first, within the per-second caps.</summary>
        static void Serve()
        {
            if (asks.Count == 0) return;
            var cam = Camera.main; var eye = cam ? cam.transform.position : Vector3.zero;
            asks.Sort((a, b) => (a.at - eye).sqrMagnitude.CompareTo((b.at - eye).sqrMagnitude));
            float now = Time.unscaledTime;
            while (whooshAt.Count > 0 && now - whooshAt.Peek() >= 1f) whooshAt.Dequeue();
            while (thudAt.Count > 0 && now - thudAt.Peek() >= 1f) thudAt.Dequeue();
            foreach (var a in asks)
            {
                bool whoosh = a.evt == WhooshEvent; var q = whoosh ? whooshAt : thudAt; int cap = whoosh ? WhooshPerSecond : ThudPerSecond;
                if (q.Count >= cap)
                {
                    if (whoosh) WhooshCapped++; else ThudCapped++;
                    if (a.body) a.body.thud = "DROP cap";
                    OzSoundLog.Add("AD", a.evt, null, "DROP cap " + cap + " per s (nearest first)", a.vol, a.at);
                    continue;
                }
                q.Enqueue(now);
                bool played = AudioDirector.I && AudioDirector.I.Play(a.evt, a.at, a.emitter, a.vol);
                if (played) { if (whoosh) WhooshPlayed++; else ThudPlayed++; }
                if (a.body) a.body.thud = played ? "PLAY" : "not played by the sound table";
            }
            asks.Clear();
        }

        sealed class Runner : MonoBehaviour
        {
            void LateUpdate() => Serve();
            void OnDestroy() { asks.Clear(); }
        }
    }

    /// <summary>One ragdoll's landing state (on its root): launched when, landed or not, and what became of its thud ("" none asked,
    /// "asked", "PLAY", "DROP cap", "on water", "off the picture", "not played by the sound table").</summary>
    public sealed class OzBodyLanding : MonoBehaviour
    {
        public float launchedAt; public bool landed; public string thud = "";
        public void Begin() { launchedAt = Time.time; landed = false; thud = ""; }
    }

    /// <summary>On every body of a ragdoll: its first hard contact with something that is not the same robot is the landing.</summary>
    public sealed class OzBodyLandingPart : MonoBehaviour
    {
        public OzBodyLanding state;
        void OnCollisionEnter(Collision c)
        {
            if (!state || state.landed || c.contactCount == 0) return;
            float age = Time.time - state.launchedAt;
            if (age < OzBodySounds.ThudAfter || age > OzBodySounds.ThudWindow) return;
            if (c.transform && c.transform.root == transform.root) return;
            float v = c.relativeVelocity.magnitude;
            if (v < OzBodySounds.ThudFromMps) return;
            if (OzShuttleCrewLanding.InAir(state, c)) return;                                      // K256h: the shuttle's crew land on the ground, not on a piece flying beside them
            state.landed = true;
            OzBodySounds.Landed(state, c.GetContact(0).point, v);
        }
    }
}
