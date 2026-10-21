using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K419b, a Play proof's staging guard (test only; added to the proof's own object, so it ends with the proof). Run 28 lost the
    /// shuttle in the middle of three proofs: the route proof's night flight and the damage smoke proof's repair stage never ran, the roof
    /// rounds proof's ship fell to one shell. The proofs held the other teams' fire ONCE at their start, so every squad the factories
    /// and forts built later fired freely, and the fort's big gun has no attack radius to hold.
    /// <list type="bullet">
    /// <item>Every 0.25 s every unit and gun of another team (not neutral) holds its fire: its attack radius 0, the old value kept for
    /// the end (units made later are caught within 0.25 s). <see cref="holdDefenders"/> false leaves the forts' roof defenders free
    /// (the roof rounds proof wants exactly their rounds).</item>
    /// <item><see cref="quietFortGuns"/>: the forts' big guns are quiet for the proof (the rule's switch, put back at the end).</item>
    /// <item><see cref="guardHp"/>: the ship's hit points far over any single hit, filled up every frame (its own back at the end). Proofs
    /// that stage the ship's health themselves (the damage smoke stages) leave it off.</item>
    /// </list>
    /// The rules, the ship's flight and the look are untouched; this only decides whether the stage keeps its ship.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class OzProofHoldFire : MonoBehaviour
    {
        /// <summary>The ship's hit points while guarded.</summary>
        public const int GuardHp = 100000000;
        /// <summary>Proof counts (all guards so far): units and guns that held their fire, hits topped up on a guarded ship.</summary>
        public static int Held, HitsTopped;

        ZWorld W; ZObject ship; Team me; bool guardHp, quietFortGuns, holdDefenders, fortGun0, quieted, released, lostSaid;
        int max0 = -1; float t; System.Action<string> log;
        readonly Dictionary<ZObject, int> held = new Dictionary<ZObject, int>();

        /// <summary>Adds the guard to the proof's object (or sets the one there again) and holds the fire at once.</summary>
        public static OzProofHoldFire Attach(GameObject host, ZWorld w, ZObject ship, Team me, bool guardHp, bool quietFortGuns, bool holdDefenders, System.Action<string> log)
        {
            if (!host || w == null) return null;
            var h = host.GetComponent<OzProofHoldFire>();
            if (!h) h = host.AddComponent<OzProofHoldFire>();
            h.Init(w, ship, me, guardHp, quietFortGuns, holdDefenders, log);
            return h;
        }

        void Init(ZWorld w, ZObject s, Team team, bool hp, bool quiet, bool defenders, System.Action<string> l)
        {
            W = w; ship = s; me = team; guardHp = hp; holdDefenders = defenders; log = l; released = false;
            if (hp && s != null && !s.Destroyed && max0 < 0) { max0 = s.MaxHealth; s.MaxHealth = GuardHp; s.SetHealth(GuardHp); }
            if (quiet && !quieted) { fortGun0 = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false; quieted = true; }
            quietFortGuns = quiet;
            Hold();
            log?.Invoke($"staging guard (K419b): the other teams hold their fire ({held.Count} units and guns now, every 0.25 s again for new ones{(defenders ? "" : "; the forts' roof defenders left free")}); the forts' big guns {(quieted ? "quiet" : "as the rules say")}; the ship #{(s != null ? s.Id : -1)} {(max0 > 0 ? "cannot be shot down (hit points " + GuardHp + ", filled up every frame)" : "keeps its own hit points")}");
        }

        void Hold()
        {
            if (W == null) return;
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || held.ContainsKey(o)) continue;
                if (!holdDefenders && o.IsFortDefender) continue;
                held[o] = o.AttackRadius; o.AttackRadius = 0; Held++;
            }
        }

        void Update()
        {
            if (released || W == null) return;
            if ((t -= Time.unscaledDeltaTime) <= 0f) { t = 0.25f; Hold(); }
            if (ship == null) return;
            if (ship.Destroyed) { if (!lostSaid) { lostSaid = true; log?.Invoke("staging guard (K419b): the ship was lost although guarded: the stages after this are NOT PROVEN"); } return; }
            if (max0 > 0 && ship.Health < ship.MaxHealth) { HitsTopped++; ship.SetHealth(ship.MaxHealth); }
        }

        /// <summary>The ship's own hit points back (full) and the guard's hold on its health off; the fire stays held.</summary>
        public void HpOff()
        {
            if (max0 > 0 && ship != null && !ship.Destroyed) { ship.MaxHealth = max0; ship.SetHealth(max0); }
            max0 = -1;
        }

        /// <summary>Everything back: attack radii (unless the game set a new one meanwhile), the big guns' switch, the ship's hit points.</summary>
        public void Release()
        {
            if (released) return;
            released = true;
            int back = 0;
            foreach (var kv in held) if (kv.Key != null && kv.Key.AttackRadius == 0) { kv.Key.AttackRadius = kv.Value; back++; }
            held.Clear();
            HpOff();
            if (quieted) { ZWorld.FortGunDeviation = fortGun0; quieted = false; }
            if (back > 0) Debug.Log("[OzProofHoldFire] the other teams' attack radius back on " + back + " units and guns" + (quietFortGuns ? "; the forts' big guns back as the rules say" : ""));
        }

        void OnDestroy() { Release(); }
    }
}
