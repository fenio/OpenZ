using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The v3 bazooka mesh is split (Models/RobotsV3/Split: the tube + the brass warhead = its two
    /// cone / collar pieces, child BS01_Tough_Warhead of the weapon mesh). On the exact launch frame (WeaponsV2Runner.DoLaunch, or
    /// CombatView's fallback launch) the warhead is hidden: the flying rocket takes over and the dark bore shows. SlideLen before the
    /// sim's reload is over (the Tough's attack_speed 1.442 s / game speed, minus SeatMargin so it is seated before the next shot) the
    /// warhead slides out of the bore to its seat, clicks with a small overshoot, and plays wpn_tough_reload (sound_v3's reload clip:
    /// in view, camera below 95 m, the event's own cooldown). A knocked-down Tough reloads once it is up, a dying one stays as it is,
    /// a Tough that boards a vehicle (view disabled) comes back loaded. View only: the sim is untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToughWarhead : MonoBehaviour
    {
        public const string PartName = "BS01_Tough_Warhead";
        public static float SlideLen = 0.3f, SeatMargin = 0.12f, SlideDepth = 0.19f, ClickLen = 0.09f;
        /// <summary>Proof counters: launches that emptied a tube, reloads seated, reload sounds fired.</summary>
        public static int Emptied, Seated, Sounds;
        public static bool Verbose;

        Transform part; Renderer rend; Vector3 rest; Vector3 restScale; UnitView view; int emitter = -1; bool searched;
        bool empty, sliding, soundOn = true; float reloadAt, slideT;

        /// <summary>The rocket left the drawn tube now: hide the warhead and schedule the reload. False = this Tough has no split
        /// warhead (old prefab): the caller keeps its own reload sound.</summary>
        public static bool Fired(UnitView av, ZObject a, bool sound = true)
        {
            if (!av || a == null || a.Type != "tough") return false;
            if (!av.TryGetComponent(out ToughWarhead w)) w = av.gameObject.AddComponent<ToughWarhead>();   // (no ?? on Unity objects)
            if (!w.Find()) return false;
            w.view = av; w.emitter = a.Id; w.soundOn = sound;
            float speed = ZGame.I ? Mathf.Max(0.25f, ZGame.I.gameSpeed) : 1f;
            float reload = Mathf.Max(0.5f, (float)a.AttackSpeed) / speed;
            w.empty = true; w.sliding = false; w.rend.enabled = false;
            w.part.localPosition = w.rest; w.part.localScale = w.restScale;
            w.reloadAt = Time.time + Mathf.Max(SlideLen + 0.05f, reload - SeatMargin);
            Emptied++;
            if (Verbose) Debug.Log($"[OpenZ] ToughWarhead: #{a.Id} fired, tube empty, seated in {w.reloadAt - Time.time:F2} s");
            return true;
        }

        bool Find()
        {
            if (part) return rend;
            if (searched) return false;
            searched = true;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == PartName) { part = t; break; }
            if (!part) return false;
            rend = part.GetComponent<Renderer>(); rest = part.localPosition; restScale = part.localScale;
            return rend;
        }

        void Update()
        {
            if (!empty || !part) return;
            if (view && view.Dying) return;                                                   // dies mid-reload: the tube stays empty
            float now = Time.time;
            if (!sliding)
            {
                if (now < reloadAt - SlideLen) return;
                if (view && view.KnockedDown) { reloadAt = now + SlideLen + 0.05f; return; }  // stunned: reload once it is up
                sliding = true; slideT = 0f; rend.enabled = true;
                if (soundOn) { AudioDirector.Fire("wpn_tough_reload", part.position, emitter); Sounds++; }
            }
            slideT += Time.deltaTime;
            float u = Mathf.Clamp01(slideT / SlideLen), e = 1f - (1f - u) * (1f - u) * (1f - u);  // ease out: slides out of the bore
            part.localPosition = rest + Vector3.forward * (SlideDepth * (1f - e));          // weapon mesh frame: barrel -Z, so +Z = into the tube
            float c = Mathf.Clamp01((slideT - SlideLen) / ClickLen);                         // the click: a small overshoot pop, then rest
            part.localScale = restScale * (u < 1f ? 1f : 1f + 0.07f * Mathf.Sin(c * Mathf.PI));
            if (c >= 1f)
            {
                part.localPosition = rest; part.localScale = restScale; empty = false; sliding = false; Seated++;
                if (Verbose) Debug.Log($"[OpenZ] ToughWarhead: #{emitter} reloaded");
            }
        }

        void OnDisable()
        {   // boarded a vehicle / pooled: it comes back loaded
            if (!part) return;
            empty = false; sliding = false; if (rend) rend.enabled = true;
            part.localPosition = rest; part.localScale = restScale;
        }
    }
}
