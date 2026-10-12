using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Zod throws every destroyed map object as EMapObjectTurrent to its debris point (omapobject.cpp:107-131: +-300 px,
    /// 3-4 s, 2083 HP r 40, NULL team), and the sim already fires that debris missile (ZWorld.DeathDebris). The view now shows it for
    /// the barrels (L17 map_object 11 = Z_barrel): the barrel itself is blown up, spins (Zod dangle +-240 deg/s) along an arc to
    /// </summary>
    public class PropToss : MonoBehaviour
    {
        Vector3 from, to, axis; float dur, t, apex, spin; int id;

        /// <summary>CombatView.Death, map objects: true = this prop is a barrel and now flies (the caller skips its hide + break).</summary>
        public static bool TryToss(ZGame game, ZObject o, Vector3 p)
        {
            if (game == null || o == null || !o.Type.StartsWith("map_object")) return false;
            if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go || !go.activeInHierarchy) return false;
            bool barrel = go.name.ToLowerInvariant().Contains("barrel") || (game.level == "L17" && o.Type == "map_object11");   // OpenZSetup: L17 map_object 11 = Z_barrel
            if (!barrel) return false;
            ZMissile m = null;
            foreach (var x in game.World.PendingMissiles) if (x.debris && x.attackerId == o.Id) { m = x; break; }
            if (m == null) foreach (var x in game.World.QueuedMissiles) if (x.debris && x.attackerId == o.Id) { m = x; break; }
            if (m == null) return false;
            var tt = go.GetComponent<PropToss>(); if (!tt) tt = go.AddComponent<PropToss>();
            tt.id = o.Id;
            tt.from = go.transform.position;
            tt.to = ZGame.PxToWorld(m.x, m.y); tt.to.y = game.GroundY(tt.to.x, tt.to.z) + 0.4f;
            tt.dur = Mathf.Max(0.6f, OpenZFxPlayer.RealFlight((float)(m.explodeTime - game.World.Time), game.gameSpeed));
            tt.apex = Mathf.Min(7f, 9.81f * tt.dur * tt.dur / 8f);
            uint h = RobotDeathRule.Hash(o.Id, 7, 31u);
            tt.axis = Random.onUnitSphere; tt.spin = 240f + (h % 240u);                                  // Zod dangle 240 - rand() % 480 deg/s
            tt.t = 0f; tt.enabled = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            FxManager.I?.Explosion(tt.from + Vector3.up * 0.4f, 0.7f);                                  // it blows up off its spot
            AudioDirector.Fire("dst_turret_toss", tt.from, o.Id);
            return true;
        }

        void Update()
        {
            t += Time.deltaTime; float k = Mathf.Clamp01(t / dur);
            var p = Vector3.Lerp(from, to, k); p.y += 4f * apex * k * (1f - k);
            transform.position = p;
            transform.rotation = Quaternion.AngleAxis(spin * Time.deltaTime, axis) * transform.rotation;
            if (k < 1f) return;
            enabled = false;
            var fxp = OpenZFxPlayer.I;                                                                  // bursts where it lands (the sim's debris blast fires now too)
            if (fxp && fxp.isActiveAndEnabled) fxp.Blast(to, 0f, FxBlastKind.Barrel, 9100 + id);
            else FxManager.I?.Explosion(to, 1f);
            gameObject.SetActive(false);
        }
    }
}
