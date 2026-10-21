using UnityEngine;

namespace OpenZ
{
    /// <summary>Version 1.1 item 202: a drawn rocket that follows a moving target (MlrsSalvo's marked ripple rockets, MlrsSalvo.Marked.cs).
    /// An eased rocket is drawn along from -> to by its eased progress (StepProjectiles), so moving its end point a few cm a frame bends
    /// its path smoothly and it lands where the target is, on the same frame as before.</summary>
    public partial class OpenZFxPlayer
    {
        /// <summary>The serial the next drawn projectile gets (read it before a Launch, pass it to NewestProjectile after).</summary>
        public int ProjectileSerial => serial;

        /// <summary>The index of the projectile drawn since `serialBefore` (the newest active one), -1 = none was drawn (pool full).</summary>
        public int NewestProjectile(int serialBefore)
        {
            int best = -1;
            foreach (var p in projs) if (p.active && p.index >= serialBefore && p.index > best) best = p.index;
            return best;
        }

        /// <summary>Moves the end point of the eased rocket `index` in flight; false = it has landed / gone, or it is not eased.</summary>
        public bool Steer(int index, Vector3 to)
        {
            foreach (var p in projs)
            {
                if (!p.active || p.index != index) continue;
                if (!p.ease) return false;
                p.to = to;
                return true;
            }
            return false;
        }
    }
}
