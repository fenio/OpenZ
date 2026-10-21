using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View-side (they are decor, not Zod objects): every prop-sized DEC_* object under Environment is
    /// registered; an explosion (FxManager.Exploded) within its reach breaks it: hidden, chunks in its material (wood,
    /// metal or stone) thrown by the blast, dust and a break sound. At start, decor that would clip into a RockZ pillar is
    /// removed (no clipping anywhere).
    /// </summary>
    public class PropBreakables : MonoBehaviour
    {
        class Prop { public GameObject go; public Bounds b; public string mat; public string[] snd; }
        /// <summary>Night package: NightLights answers true for the lamps it owns (they bend, topple, spark and go out there),
        /// so they are not registered as generic breakable props. The rock-pillar clipping rule still applies to them.</summary>
        public static System.Func<GameObject, bool> ClaimBreak;
        readonly List<Prop> props = new List<Prop>();

        void Start()
        {
            var env = GameObject.Find("Environment"); if (!env) return;
            var pillars = new List<Bounds>();
            foreach (var rp in FindObjectsByType<RockPillar>(FindObjectsSortMode.None))
                if (rp.pillar) foreach (var r in rp.pillar.GetComponentsInChildren<Renderer>()) { var pb = r.bounds; pb.Expand(new Vector3(-0.4f, 0, -0.4f)); pillars.Add(pb); }
            int removed = 0;
            foreach (Transform t in env.transform)
            {
                if (!t.name.StartsWith("DEC_") || !t.gameObject.activeInHierarchy) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.x > 7f || b.size.z > 7f || b.size.y > 12f) continue;                      // islands, ridges, big decor
                bool clips = false; foreach (var pb in pillars) if (pb.Intersects(b)) { clips = true; break; }
                if (clips) { t.gameObject.SetActive(false); removed++; continue; }
                if (ClaimBreak != null && ClaimBreak(t.gameObject)) continue;           // night package: NightLights breaks its lamps
                string n = t.name.ToLower();
                string mat = n.Contains("crate") || n.Contains("tower") || n.Contains("cover") ? "wood" : n.Contains("barrel") || n.Contains("lamp") || n.Contains("solar") || n.Contains("sign") || n.Contains("car") ? "metal" : "stone";
                var snd = OzSoundV3.PropBreakBank(n) ?? (mat == "metal" ? OzSoundV3.BankMetalMedium                  // sound_v3: cactus / bollard + the metal pool
                        : mat == "wood" ? new[] { "v3-collapse-small-a", "v3-collapse-small-b" } : new[] { "v3-rock-break-a", "v3-rock-break-b" });
                props.Add(new Prop { go = t.gameObject, b = b, mat = mat, snd = snd });
            }
            FxManager.Exploded += OnExplosion;
            Debug.Log($"[OpenZ] breakable props: {props.Count} (decor clipping into rock pillars removed: {removed})");
        }

        void OnDestroy() { FxManager.Exploded -= OnExplosion; }

        void OnExplosion(Vector3 at, float strength)
        {
            float reach = 1.2f + 2.4f * strength;
            for (int i = props.Count - 1; i >= 0; i--)
            {
                var p = props[i];
                if (!p.go) { props.RemoveAt(i); continue; }
                if (at.y > p.b.max.y + 3f) continue;                                                       // K337: a blast high over it (up at the flying shuttle) does not reach it (the sandbags' rule)
                var c = p.b.ClosestPoint(at); c.y = at.y;
                if ((c - at).sqrMagnitude > reach * reach) continue;
                props.RemoveAt(i);
                Break(p, at);
            }
        }

        void Break(Prop p, Vector3 blast)
        {
            var c = p.b.center; var fx = FxManager.I;
            p.go.SetActive(false);
            if (fx)
            {
                float w = Mathf.Max(p.b.size.x, p.b.size.z);
                fx.Breakup(new Vector3(c.x, p.b.min.y + p.b.size.y * 0.4f, c.z), p.mat, Mathf.Max(1f, w), Mathf.Clamp(Mathf.RoundToInt(w * 5), 6, 16), Mathf.Clamp(w * 0.5f, 0.6f, 1.6f));
                fx.Dust(new Vector3(c.x, p.b.min.y + 0.3f, c.z), Mathf.Clamp(w * 0.35f, 0.4f, 1.2f));
                if (p.go.name.ToLower().Contains("lamp")) fx.AddLight(c + Vector3.up, new Color(1f, 0.85f, 0.6f), 3f, 5f, 0.25f, 0.6f, true);   // the bulb pops
            }
            CombatAudio.I?.Play("prop", c, 0.7f, 0.05f, -1, p.snd);
        }
    }
}
