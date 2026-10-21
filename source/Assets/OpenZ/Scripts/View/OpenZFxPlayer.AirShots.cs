using UnityEngine;

namespace OpenZ
{
    /// <summary>K337: the drawn projectiles' ends (View/OzAirShots.cs, OzAirShots.Core.cs). Three hooks in OpenZFxPlayer.cs call this:
    /// Launch (the pool full: the oldest ends first; the new projectile bound to its round at the flying ship, its arc kept under the
    /// ceiling), StepProjectiles (each step: the hard cap, and the air burst of a sure miss at its cap or of a salvo's extra with its round).</summary>
    public partial class OpenZFxPlayer
    {
        /// <summary>Proof: the projectiles drawn now (rockets, shells, grenade fallbacks of this pool).</summary>
        public int LiveProjectiles { get { int n = 0; foreach (var p in projs) if (p.active) n++; return n; } }
        /// <summary>Proof: the pool's size (it grows to OzAirShots.MaxLive and is reused from then on).</summary>
        public int ProjectilePool => projs.Count;

        /// <summary>Launch, the pool full: the oldest projectile ends (it is the nearest to its end) and its slot draws the new one.</summary>
        Proj OldestProjectile()
        {
            Proj old = null;
            foreach (var x in projs) if (x.active && (old == null || x.index < old.index)) old = x;
            if (old == null) return null;
            OzAirShots.Unbind(old.index); old.active = false; OzAirShots.Evictions++;
            return old;
        }

        /// <summary>Launch, the new projectile p: bound to its round when it is one at the flying ship (or a salvo's extra beside it); its arc
        /// then kept under the ceiling.</summary>
        void OnAirLaunch(Proj p, Vector3 to)
        {
            if (OzAirShots.BindLaunch(p.index, to, Time.time)) KeepUnderCeiling(p);
            int n = LiveProjectiles; if (n > OzAirShots.MaxLiveSeen) OzAirShots.MaxLiveSeen = n;
        }

        /// <summary>A round at the flying ship (or its salvo's extra): the top of its arc at most OzAirShots.CeilingM over the ground (the
        /// lowest of the ground under its start, its middle and its end), OzAirShots.ArcGravity. Its end points and flight time stay, so it
        /// still lands on its blast; only a long flight is flattened (before: a 12.3 s lead flight at 1x rose to about 110 m over the ground and
        /// its air burst came out of sight). Rounds at anything else keep their arc.</summary>
        void KeepUnderCeiling(Proj p)
        {
            float T = p.life; if (T <= 0.05f || p.g <= 0f) return;
            var mid = (p.from + p.to) * 0.5f;
            float top = Mathf.Min(Ground(p.from.x, p.from.z), Mathf.Min(Ground(mid.x, mid.z), Ground(p.to.x, p.to.z))) + OzAirShots.CeilingM;
            float g = OzAirShots.ArcGravity(p.g, T, top - p.from.y, top - p.to.y);
            if (g >= p.g - 1e-4f) return;
            float k = p.ease ? RocketEaseStart / (1f - RocketEaseRamp * (1f - RocketEaseStart) * 0.5f) : 1f;   // Launch's start speed of an eased rocket
            p.vel.y += (g - p.g) * T * 0.5f * k;                                                     // the same launch, with the lower gravity
            p.g = g; OzAirShots.ArcsFlattened++;
        }

        /// <summary>StepProjectiles, each projectile after its age step: true = it ended here (the hard cap; a sure miss at its cap or a salvo's
        /// extra with its round: in a small air burst at its drawn place).</summary>
        bool AirShotEnd(Proj p)
        {
            if (!OzAirShots.On) return false;
            if (p.age > OzAirShots.MaxAgeEnded) OzAirShots.MaxAgeEnded = p.age;
            var g = ZGame.I; float gs = g ? Mathf.Max(0.05f, g.gameSpeed) : 1f;
            bool cap = p.age >= OzAirShots.LifeCap(p.life, gs);
            if (!cap && !OzAirShots.IsBound(p.index)) return false;                                 // a round at anything else: its own end, as before
            bool pop; float gy = Ground(p.pos.x, p.pos.z);
            if (cap) pop = OzAirShots.CapEnded(p.index, p.pos, gy, Time.time);
            else if (!OzAirShots.PopNow(p.index, p.age, p.life, p.pos, gy, Time.time, g ? g.World : null)) return false;
            else pop = true;
            if (pop) OzAirShots.PopProjectile(this, p.w, p.index, p.pos);
            OzAirShots.Unbind(p.index);
            p.active = false;
            return true;
        }
    }
}
