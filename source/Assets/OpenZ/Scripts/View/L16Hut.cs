using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: one of the 18 neutral huts of the map as the thatched hut on stilts (prepared answer 25). It stands on the scene
    /// object hut_x_y the rules bind by name; tile, blocking and hit points stay the map's hut (only the look and the break are
    /// this level's). At half hit points it leans with a creak and loose thatch; when the rules destroy it (CombatView calls
    /// SimDied) it collapses: straw and wood fly in the hut's own colours, dust, the wood debris sound, the fallen hut (child
    /// RUIN) lies there for a few seconds and then goes the way all debris of the game goes, a scorch stays. The tile is open
    /// again by the rules, so nothing tall is left on it.
    /// Play check: All, Broken, Leaning, LastBreak.
    /// </summary>
    public sealed class L16Hut : MonoBehaviour
    {
        public static readonly List<L16Hut> All = new List<L16Hut>();
        public static int Breaks; public static string LastBreak = "";
        public GameObject hut, ruin;
        public Color[] debris = { new Color(0.62f, 0.52f, 0.28f), new Color(0.36f, 0.25f, 0.14f), new Color(0.45f, 0.5f, 0.2f) };
        public float ruinSeconds = 9f;
        public bool Broken { get; private set; }
        public bool Leaning { get; private set; }
        int simId = -1; float nextPoll; DetailKit.Bits bits;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        void Awake() { if (ruin) ruin.SetActive(false); if (hut) hut.SetActive(true); }

        int SimId()
        {
            if (simId >= 0) return simId;
            var g = ZGame.I; if (!g) return -1;
            foreach (var kv in g.StaticViews) if (kv.Value == gameObject) { simId = kv.Key; break; }
            return simId;
        }

        void Update()
        {
            if (Broken || Leaning || Time.time < nextPoll) return;
            nextPoll = Time.time + 0.25f;
            var g = ZGame.I; if (!g || g.World == null) return;
            int id = SimId(); if (id < 0) return;
            var o = g.World.Get(id);
            if (o != null && o.MaxHealth > 0 && o.Health > 0 && o.Health <= o.MaxHealth / 2) Lean();
        }

        DetailKit.Bits Bits()
        {
            if (bits == null) { DetailPropHub.Ensure(); bits = new DetailKit.Bits(debris); DetailPropHub.Track(bits); }
            return bits;
        }

        /// <summary>Half hit points: the hut leans, thatch comes loose.</summary>
        public void Lean()
        {
            if (Leaning || Broken) return;
            Leaning = true;
            var p = transform.position + Vector3.up * 1.6f;
            Bits().Burst(p, 10, 0, 2.2f, 0.07f, new System.Random(GetInstanceID()));
            if (AudioDirector.Has("dst_debris_wood")) AudioDirector.Fire("dst_debris_wood", p, SimId(), 0.6f, 1.2f);
            else OzSoundLog.Add("L16", "dst_debris_wood", null, "DROP no event", 0f, p);
            if (hut) StartCoroutine(Tilt(hut.transform, new Vector3(5f, 0f, ((GetInstanceID() & 1) == 0 ? 4f : -4f)), 0.35f));
        }

        static IEnumerator Tilt(Transform t, Vector3 euler, float seconds)
        {
            var q0 = t.localRotation; var q1 = q0 * Quaternion.Euler(euler); float k = 0f;
            while (k < 1f && t) { k += Time.deltaTime / seconds; t.localRotation = Quaternion.Slerp(q0, q1, Mathf.SmoothStep(0f, 1f, k)); yield return null; }
        }

        /// <summary>CombatView: the rules destroyed a hut. True = this level's hut broke itself (false: not ours, the caller goes on).</summary>
        public static bool SimDied(ZGame game, ZObject o, Vector3 from)
        {
            if (game == null || o == null || !game.StaticViews.TryGetValue(o.Id, out var go) || !go) return false;
            var h = go.GetComponent<L16Hut>();
            if (!h) return false;
            h.simId = o.Id; h.Break(from);
            return true;
        }

        /// <summary>The collapse (also callable by a check).</summary>
        public void Break(Vector3 from)
        {
            if (Broken) return;
            Broken = true; Breaks++;
            var p = transform.position; var c = p + Vector3.up * 1.2f;
            if (hut) hut.SetActive(false);
            if (ruin) ruin.SetActive(true);
            var rng = new System.Random(GetInstanceID() * 31 + Time.frameCount);
            for (int i = 0; i < debris.Length; i++) Bits().Burst(c, 12, (byte)i, 3.2f, 0.08f, rng);
            if (FxManager.I)
            {
                FxManager.I.Breakup(c, "wood", 2.2f, 14, 0.9f);
                FxManager.I.DemolitionDust(p, 2.4f, 2.4f, 0.45f);
            }
            ScorchDecals.Add(p, 1.8f, ScorchKind.Explosion);
            if (AudioDirector.Has("dst_debris_wood")) AudioDirector.Fire("dst_debris_wood", c, simId, 1f, 0.95f);
            else OzSoundLog.Add("L16", "dst_debris_wood", null, "DROP no event", 0f, c);
            L16Pyramid.Named("dst_l16_hut_collapse", (simId & 1) == 0 ? "OpenZ/Audio/Sfx/L16/sd_hut_collapse_a" : "OpenZ/Audio/Sfx/L16/sd_hut_collapse_b", c, 0.8f);   // two takes, chosen by the hut (no random draw)
            LastBreak = name + " collapsed at " + Time.time.ToString("0.0") + " s";
            Debug.Log("[OpenZ] L16 hut " + LastBreak);
            if (ruin) StartCoroutine(RuinAway());
        }

        IEnumerator RuinAway()
        {
            yield return new WaitForSeconds(ruinSeconds + (GetInstanceID() & 3) * 0.5f);
            if (ruin) PopOut.Begin(ruin.transform, PopOut.Style.Debris, null, false, true);
        }
    }
}
