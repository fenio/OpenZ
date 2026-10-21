using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K421: the roof-shot rule of K394 (OzK394RoofShots: a fort roof man's drawn round leaves his muzzle and goes over his fort's real
    /// parapet only where it must; his own fort is no block for it in FortGunView.TopAt) now knows the fort's ROOF DEFENDER by who he is,
    /// not only by how high his muzzle is.
    /// <para>The fault: K394 told a roof round by its start alone: a muzzle in the deck band (fort-local 5.8-9.0 m). The band's top came
    /// from a muzzle modelled 1.0 m (kneeling) / 1.4 m (standing) over his soles; the drawn sniper (full grunt size, the long rifle) holds it
    /// 2.2-2.6 m over his soles at the flying ship (8.6-8.97 m in the fort on Levels 16 and 17, 3 cm under the band's top) and higher still
    /// standing or aiming steeply up (9.65 m on Level 17, between a nest and a front deck spot, the ship over the gate). The deck is flat for him
    /// (soles 6.37 m on the deck frame, 6.42 m on a hatch's lift plate, the same fort model on every level), so the band missed that round
    /// and the old bend (the fort's 8.76 m block + 0.3 m) applied to it.</para>
    /// <para>The fix (view only): when the band does not hold a muzzle, <see cref="Defender"/> asks whether the round starts at the drawn
    /// muzzle (WeaponsV2Runner.DrawnMuzzle, within <see cref="MatchM"/>) of a live fort roof defender (ZObject.IsFortDefender: his fort's
    /// FortDefenderMan and any man of that fort), whatever his type (the sniper, DV111; the grunt with that rule off) and however high he
    /// holds it (inside a box round his fort's deck up to <see cref="BoxMaxY"/>). Tower guns and the fort's big gun are never roof men:
    /// a start at least as close to one of their muzzles (the tower guns' UnitView.MuzzlePos, the big gun's FortGunMuzzle) as to his is
    /// theirs (every drawer starts its round AT its own muzzle), so they keep the old test whatever their height. Hooks: OzK394RoofShots.RoofFort (after the band) and FortGunView.TopAt
    /// (<see cref="OwnFort"/>, after OnRoofDeck).</para>
    /// </summary>
    public static class OzK421RoofDefenders
    {
        /// <summary>The kit's switch (off = K394's band alone, as before).</summary>
        public static bool On = true;
        /// <summary>How close a round's start must be to the defender's drawn muzzle (m). The drawers start it AT his muzzle (the K394
        /// check measures 0.0 cm).</summary>
        public static float MatchM = 0.15f;
        /// <summary>Where a roof defender's muzzle can be at all (fort-local m): over his deck and out past the parapet's outer faces
        /// (7.55 / -7.8 / 6.25) by a long rifle's reach, from the deck band's floor up to a man on his feet aiming steeply up (soles
        /// 6.42 m + about 3.3 m) with room. Only a pre-test: the round must still start at his drawn muzzle.</summary>
        public const float BoxHalfX = 9.0f, BoxZ0 = -9.0f, BoxZ1 = 9.0f, BoxMinY = OzK394RoofShots.DeckMinY, BoxMaxY = 11.5f;
        const float FortHalfX = 10.05f, FortHalfZ = 9.85f;                                       // FortGunView's footprint: the tower guns on its pads

        /// <summary>Counts (the K421 State menu): roof-defender rounds known by the man (outside the band among them), refused as a
        /// gun's round; the last one outside the band (who, where, how he stood).</summary>
        public static int Matches, OverBand, GunRefused;
        public static string LastOverBand = "none";

        sealed class Fort
        {
            public ZObject obj; public Transform view; public Transform bigMuzzle;
            public readonly List<UnitView> guns = new List<UnitView>(4);
            public readonly List<ZObject> men = new List<ZObject>(2);
        }
        static readonly List<Fort> forts = new List<Fort>(4);
        static float fortsAt = -10f; static ZWorld fortsWorld;
        static int memoFrame = -1; static Vector3 memoMuzzle; static bool memoHit; static Transform memoFort; static Vector3 memoLocal;

        // ------------------------------------------------------------------ the live forts, their guns and roof men (once a second)
        static void Refresh(ZGame g)
        {
            if (g.World == fortsWorld && Time.time - fortsAt < 1f && Time.time >= fortsAt) return;
            fortsWorld = g.World; fortsAt = Time.time; forts.Clear();
            foreach (var o in g.World.Objects)
            {
                if (o == null || !o.IsFort || o.Destroyed || !g.StaticViews.TryGetValue(o.Id, out var go) || !go || !go.activeInHierarchy) continue;
                var f = new Fort { obj = o, view = go.transform };
                var fgv = go.GetComponent<FortGunView>();
                f.bigMuzzle = FindDeep(go.transform, fgv && !string.IsNullOrEmpty(fgv.muzzleNode) ? fgv.muzzleNode : "FortGunMuzzle");
                forts.Add(f);
            }
            if (forts.Count == 0) return;
            foreach (var o in g.World.Objects)
            {
                if (o == null || !o.IsFortDefender || o.Destroyed) continue;
                for (int k = 0; k < forts.Count; k++) if (forts[k].obj.Id == o.DefenderFortId) { forts[k].men.Add(o); break; }
            }
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!v || v.Obj == null || v.Obj.Kind != ZKind.Cannon || v.Obj.Destroyed) continue;
                var p = v.transform.position;
                for (int k = 0; k < forts.Count; k++)
                {
                    var l = forts[k].view.InverseTransformPoint(p);
                    if (Mathf.Abs(l.x) <= FortHalfX && Mathf.Abs(l.z) <= FortHalfZ) { forts[k].guns.Add(v); break; }
                }
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        // ------------------------------------------------------------------ the test
        /// <summary>OzK394RoofShots.RoofFort, after the deck band: this round starts at the drawn muzzle of a live fort roof defender (not
        /// at a gun of his fort). fort = his fort's view, local = the muzzle in its frame. One answer per muzzle and frame.</summary>
        public static bool Defender(Vector3 muzzle, out Transform fort, out Vector3 local)
        {
            fort = null; local = default;
            if (!On) return false;
            int frame = Time.frameCount;
            if (frame == memoFrame && muzzle == memoMuzzle) { fort = memoFort; local = memoLocal; return memoHit; }
            memoFrame = frame; memoMuzzle = muzzle; memoHit = false; memoFort = null; memoLocal = default;
            var g = ZGame.I;
            if (!g || g.World == null) return false;
            Refresh(g);
            float m2 = MatchM * MatchM;
            for (int k = 0; k < forts.Count; k++)
            {
                var f = forts[k]; if (!f.view || f.obj.Destroyed) continue;
                var l = f.view.InverseTransformPoint(muzzle);
                if (Mathf.Abs(l.x) > BoxHalfX || l.z < BoxZ0 || l.z > BoxZ1 || l.y < BoxMinY || l.y > BoxMaxY) continue;
                var man = His(g, f, muzzle, m2, out float manD2);
                if (man == null) continue;
                if (AtGun(f, muzzle, manD2)) { GunRefused++; continue; }
                Matches++;
                if (!OzK394RoofShots.InDeckBand(l)) { OverBand++; NoteOverBand(g, f, man, l); }
                memoHit = true; memoFort = f.view; memoLocal = l;
                fort = f.view; local = l;
                return true;
            }
            return false;
        }

        /// <summary>FortGunView.TopAt, after OnRoofDeck: this muzzle is a roof defender's and fortView is his own fort (no block for his
        /// rounds; OzK394RoofShots.OverParapet takes his line over its real parapet). Off with K394's switch.</summary>
        public static bool OwnFort(Transform fortView, Vector3 muzzle)
        {
            if (!OzK394RoofShots.On || !fortView || !Defender(muzzle, out var t, out _) || !t) return false;
            return t == fortView || fortView.IsChildOf(t) || t.IsChildOf(fortView);
        }

        /// <summary>The fort's roof man whose drawn muzzle is within MatchM of this start (d2 = its squared distance), or null.</summary>
        static ZObject His(ZGame g, Fort f, Vector3 muzzle, float m2, out float d2)
        {
            d2 = float.MaxValue;
            var cur = f.obj.FortDefenderMan;
            if (cur != null && !cur.Destroyed) { d2 = (WeaponsV2Runner.DrawnMuzzle(g, cur) - muzzle).sqrMagnitude; if (d2 <= m2) return cur; }
            for (int i = 0; i < f.men.Count; i++)
            {
                var m = f.men[i]; if (m == null || m == cur || m.Destroyed) continue;
                d2 = (WeaponsV2Runner.DrawnMuzzle(g, m) - muzzle).sqrMagnitude;
                if (d2 <= m2) return m;
            }
            d2 = float.MaxValue;
            return null;
        }

        /// <summary>The start is at least as close (squared distance manD2) to a muzzle of his fort's big gun or tower guns: their round.</summary>
        static bool AtGun(Fort f, Vector3 muzzle, float manD2)
        {
            if (f.bigMuzzle && (f.bigMuzzle.position - muzzle).sqrMagnitude <= manD2) return true;
            for (int i = 0; i < f.guns.Count; i++)
            {
                var v = f.guns[i]; if (!v || !v.isActiveAndEnabled) continue;
                if ((v.MuzzlePos() - muzzle).sqrMagnitude <= manD2) return true;
            }
            return false;
        }

        static void NoteOverBand(ZGame g, Fort f, ZObject man, Vector3 l)
        {
            string soles = "-", stance = "-";
            if (g.Views.TryGetValue(man.Id, out var v) && v)
            {
                soles = f.view.InverseTransformPoint(v.transform.position).y.ToString("0.00");
                var pose = v.GetComponent<RobotWeaponPose>();
                if (pose) stance = pose.Current + " (weight " + pose.PoseWeight.ToString("0.00") + ")";
            }
            LastOverBand = $"#{man.Id} {man.Type} of fort #{f.obj.Id}: muzzle fort-local ({l.x:0.00}, {l.y:0.00}, {l.z:0.00}), soles {soles} m, " +
                           $"stance {stance}, deck node {man.FortDefenderNode}, at {Time.time:0.0} s";
        }
    }
}
