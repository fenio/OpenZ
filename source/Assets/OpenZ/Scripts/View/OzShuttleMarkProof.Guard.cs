using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256g2 mark proof's staging (test only): its ship is not shot down while it flies the lines. The line B1 "across the fort"
    /// crosses the enemy fort with its defenders alive (only the computer player's orders are removed), so the ship was shot down there
    /// and every later stage ran without a ship.
    /// <list type="bullet">
    /// <item><see cref="GuardOn"/>: the ship's hit points are raised far over any hit (<see cref="GuardHp"/>) and filled up every frame;
    /// the other teams' units and guns hold their fire (their attack radius 0, looked at again every second for units made later). The
    /// fort's own gun may still lob a shell: it cannot bring the ship down.</item>
    /// <item><see cref="GuardHpOff"/>: the ship's own hit points back (full). The deck stage runs with them as it was written (60 %, R
    /// to the station); the enemies still hold their fire.</item>
    /// <item><see cref="GuardRelease"/> (also when the proof object goes): every attack radius back, unless the game set a new one
    /// meanwhile.</item>
    /// </list>
    /// The rules, the mark, the shadows and the ship's flight are untouched; the hit points only decide whether it dies.
    /// </summary>
    public sealed partial class OzShuttleMarkProof
    {
        /// <summary>The ship's hit points while guarded (no single hit comes near).</summary>
        public const int GuardHp = 100000000;
        /// <summary>Proof: hits the guarded ship took (each filled up at once); units that held their fire.</summary>
        public static int GuardHits, GuardHeld;
        bool guarded; int guardMax0 = -1; float holdT; bool lostSaid;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();

        void GuardOn()
        {
            if (s == null || W == null || s.Destroyed) return;
            if (!guarded)
            {
                if (guardMax0 < 0) guardMax0 = s.MaxHealth;
                s.MaxHealth = GuardHp; s.SetHealth(GuardHp); guarded = true;
            }
            HoldFire();
            Log($"guard on: the ship #{s.Id} cannot be shot down (hit points {GuardHp}, filled up every frame); the other teams hold their fire ({heldFire.Count} units and guns)");
        }

        void GuardHpOff()
        {
            if (!guarded || s == null) return;
            guarded = false;
            if (guardMax0 > 0 && !s.Destroyed) { s.MaxHealth = guardMax0; s.SetHealth(guardMax0); }
            Log($"guard off: the ship's own hit points back ({(s != null ? s.Health : 0)} of {guardMax0}); hits taken while guarded so far {GuardHits}; the enemies still hold their fire");
        }

        void GuardRelease()
        {
            GuardHpOff();
            int back = 0;
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) { kv.Key.AttackRadius = kv.Value; back++; }
            heldFire.Clear();
            if (back > 0) Debug.Log("[OzShuttleMarkProof] the other teams' attack radius back on " + back + " units and guns");
        }

        /// <summary>Every unit and gun of another team (not neutral) holds its fire: its attack radius 0, the value kept for the end.</summary>
        void HoldFire()
        {
            if (W == null) return;
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || heldFire.ContainsKey(o)) continue;
                heldFire[o] = o.AttackRadius; o.AttackRadius = 0; GuardHeld++;
            }
        }

        string GuardText() => $"guard: hits taken while guarded {GuardHits} (each filled up), units and guns that held their fire {heldFire.Count}; the ship {(s == null ? "none" : s.Destroyed ? "LOST" : "alive")}";

        void Update()
        {
            if (s == null || W == null) return;
            if (heldFire.Count > 0 && (holdT -= Time.unscaledDeltaTime) <= 0f) { holdT = 1f; HoldFire(); }
            if (!guarded) return;
            if (s.Destroyed) { if (!lostSaid) { lostSaid = true; Log("the ship was lost although guarded: the lines after this are NOT PROVEN"); } return; }
            if (s.Health < s.MaxHealth) { GuardHits++; s.SetHealth(s.MaxHealth); }
        }

        void OnDestroy() { GuardRelease(); }
    }
}
