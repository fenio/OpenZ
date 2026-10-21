using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// K248b (the sniper's stance, view only; plain C# without UnityEngine, so the headless check runs this very code). What made a
    /// drawn sniper lie down and stand up again and again in one fight with nothing to dodge:
    /// <list type="bullet">
    /// <item>BS01Stance got it up the moment its drawn body walked (0.3 m/s for 0.25 s: one small step of the sim, a squad nudge, a
    /// spacing push) and put it down again the moment it stood still and had a target;</item>
    /// <item>RobotWeaponPose picked stand / kneel / prone anew for every new TARGET while it stood (MirrorExternal reports the pose
    /// weight 0 whenever BS01Stance has it standing, and "pose weight under 0.05" counted as a new engagement), so a standing sniper
    /// rolled again after each kill and went down for the next one.</item>
    /// </list>
    /// Now (<see cref="On"/>): the stance is picked once per FIGHT (a new pick only after <see cref="FightGap"/> s without any target;
    /// a target switch keeps it); it goes down only after it has stood still with a target for <see cref="DownStill"/> s; once down it
    /// gets up only for a real walk: the sim moving it for <see cref="RealWalkTime"/> s, or its drawn body
    /// <see cref="RealWalkDist"/> m from where it went down. A nudge or a push slides the kneeling / lying body a little instead. A
    /// sniper that got up for a walk fights on standing until the fight is over and stays up at least <see cref="MinUpTime"/> s (down
    /// at most once a fight: never down-up-down).
    /// It stays down until the fight is over (<see cref="Linger"/>: FightGap s without a target, was 2.5 s); "never down in water" stays
    /// as it was. On = false: exactly the old rules.
    /// </summary>
    public sealed class SniperStanceGate
    {
        /// <summary>The rules above (false = the old rules, for an A/B look in Play).</summary>
        public static bool On = true;
        public static float FightGap = 6f, DownStill = 0.3f, RealWalkTime = 0.35f, RealWalkDist = 0.6f, MinUpTime = 3f;
        /// <summary>Proof counters (Play menu, headless model): lie-downs; get-ups for a real walk / after the last target (linger) /
        /// for a grenade throw; get-ups for a walk while an enemy stood in range; walks of the drawn body that did NOT get it up;
        /// stance picks (new fights).</summary>
        public static int LieDowns, GetUpsWalk, GetUpsLinger, GetUpsThrow, GetUpsWalkInRange, NudgesHeld, Picks;

        float still, simWalk, x, z, downX, downZ, sinceTarget = 999f, sinceUp = 999f;
        bool walkedThisFight, orderWalk;

        // K248d: down only where the fight needs no more walking (ZObject.SniperFightSettled, Sim/ZObject.SniperSettle.cs)
        bool settled = true; float lookIn;
        /// <summary>K248d: s between two looks while the answer is "not yet".</summary>
        public static float LookEvery = 0.25f;
        /// <summary>K248d proof counter: a sniper that could have gone down was kept standing because its fight leads on (each time).</summary>
        public static int HeldStanding;

        /// <summary>K248d, every frame right after <see cref="Step"/>: while it could go down now (still with a target long enough, not
        /// after a walk in this fight), it asks whether its fight needs no more walking from where it stands
        /// (<see cref="ZObject.SniperFightSettled"/>: no order or chase leads on, no enemy it would chase stands outside its range near
        /// it, nobody out of range shoots at its squad). Not yet: it fights standing (it would have to get up for that walk), looked at
        /// again every <see cref="LookEvery"/> s. Off (<see cref="On"/> or ZWorld.SniperDownToStay false): K248b's rules.</summary>
        public void Look(ZObject o)
        {
            if (!On || !ZWorld.SniperDownToStay || o == null) { settled = true; return; }
            if (still < DownStill || walkedThisFight || sinceUp < MinUpTime) { lookIn = 0f; return; }   // it could not go down now anyway
            if (!settled && lookIn > 0f) return;
            bool was = settled;
            settled = o.SniperFightSettled();
            lookIn = settled ? 0f : LookEvery;
            if (was && !settled) HeldStanding++;
        }

        /// <summary>Every frame, before the stance's own decision: engaged = it has a target; walking = its drawn body walks;
        /// simMoving = the sim moves it; order = the sim walks it on an order or a chase (<see cref="OrderWalk"/>); (px, pz) = its drawn
        /// ground position (m).</summary>
        public void Step(float dt, bool engaged, bool walking, bool simMoving, bool order, float px, float pz)
        {
            x = px; z = pz; orderWalk = order;
            still = engaged && !walking && !simMoving ? still + dt : 0f;
            simWalk = simMoving ? simWalk + dt : 0f;
            sinceUp += dt; lookIn -= dt;
            if (engaged) { if (sinceTarget > FightGap) walkedThisFight = false; sinceTarget = 0f; }   // a target after a pause of FightGap s: a new fight, it may go down again
            else sinceTarget += dt;
        }

        /// <summary>Standing: it may go down now (it has stood still with a target long enough, and it has not got up for a walk in this
        /// fight: a sniper that moved on fights standing until the fight is over, never down-up-down).</summary>
        public bool MayGoDown => !On || (still >= DownStill && !walkedThisFight && sinceUp >= MinUpTime && settled);   // K248d: and its fight needs no more walking (Look)

        /// <summary>It got up for a walk (the stance code calls it): down no more in this fight, and not within MinUpTime s anyway.</summary>
        public void GotUpForWalk() { walkedThisFight = true; sinceUp = 0f; }

        /// <summary>Down (kneeling / lying, aiming, firing): it has to get up for this walk (at once when the sim walks it on an order or a
        /// chase: it gets up as it sets off, not a moment later on the way).</summary>
        public bool MustGetUp(bool walking)
        {
            if (!On) return walking;
            if (orderWalk) return true;
            if (!walking) return false;
            float dx = x - downX, dz = z - downZ;
            return simWalk >= RealWalkTime || dx * dx + dz * dz >= RealWalkDist * RealWalkDist;
        }

        /// <summary>How long it stays down after its last target (BS01Stance's own linger, 2.5 s): until the fight is over, FightGap s
        /// (a pause in the fight is not the end of it: no get-up and lie-down again for the next target).</summary>
        public static float Linger(float own) => On ? (own > FightGap ? own : FightGap) : own;

        /// <summary>It went down here (the spot the drawn-body distance is measured from).</summary>
        public void WentDown() { downX = x; downZ = z; }

        /// <summary>RobotWeaponPose.EngagementStance, on a target that differs from the last one: true = pick a new stance.
        /// external = BS01Stance drives the stance; poseWeight = the weapon pose's weight; sinceTarget = s since it last had a target.</summary>
        public static bool NewEngagement(bool external, float poseWeight, float sinceTarget)
        {
            bool fresh = On && external ? sinceTarget > FightGap : poseWeight < 0.05f || sinceTarget > 1.5f;
            if (fresh) Picks++;
            return fresh;
        }

        /// <summary>The sim walks this robot on an order or a chase: any waypoint but the spacing layer's own small moves (a nudge, a step
        /// aside, a back-off, a regroup, a waiting bay: an unordered MOVE with a negative mark).</summary>
        public static bool OrderWalk(ZObject o)
        {
            if (o == null || !o.IsMoving || o.Waypoints.Count == 0) return false;
            var wp = o.Waypoints[0];
            return !(wp.mode == WpMode.Move && !wp.playerGiven && wp.refId < 0);
        }

        public static void ResetCounters() { LieDowns = GetUpsWalk = GetUpsLinger = GetUpsThrow = GetUpsWalkInRange = NudgesHeld = Picks = HeldStanding = 0; ZWorld.SniperStoodToWalkOn = 0; }
    }
}
