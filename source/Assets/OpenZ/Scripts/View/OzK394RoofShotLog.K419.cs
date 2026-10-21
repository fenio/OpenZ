using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K419b, the K394 roof rounds check's staging (test only). Run 28 logged 0 roof rounds in 3+ minutes on Level 16 and Level 17. Why,
    /// from the rules (Sim/ZObject.FortDefender.cs, DV41 / DV111):
    /// <list type="bullet">
    /// <item>A fort sends its roof man up only when it is attacked AND it knows the attack's side (FortDefenderRingFor: -1 = no hatch).
    /// The side comes from a real hit on the fort (NoteFortAttack) or from the threat scan (an enemy unit within 160 px = 20 m of the
    /// fort's centre, or within 250 px and attacking something of the fort's team). The check staged the alarm by writing the fort's
    /// LastEnemyHit every frame: "attacked" was true, but a fresh hit makes the threat scan keep its hands off the side (it only sets
    /// it when no real hit is fresh), and no real hit ever came, so no hatch was ever chosen and nobody came up.</item>
    /// <item>The ship patrolled 25 m (200 px) off the fort's centre, 14 m each side: outside the 20 m threat ring (an unarmed ship attacks
    /// nothing), and outside the sniper's reach (144 px from a deck spot; his deck spots stand at most ~62 px off the centre).</item>
    /// <item>The fort's big gun reached it there and one shell (more than the ship's 3378 hit points) ended the L17 run.</item>
    /// </list>
    /// So the rules are not at fault: the roof man does fire at the flying ship (CanAttackObject; the sight line to an airborne ship is
    /// always clear, DV118; his deck walk picks a spot with a clear line past the towers and the gun block) once it is within the threat
    /// ring and his reach. The staging now: the ship hovers on the fort's best side (the side whose patrol line has the most points
    /// within 150 px of the centre, inside the big gun's no-fire ring of 64 px round the walls, and within the sniper's reach from a deck
    /// spot with a clear line, FortDefenderLineClear), 13.5 m (else 12 or 15 m) off the centre, swinging 7 m each side; the threat scan
    /// calls him up by itself (no staged hit). The ship cannot be shot down (OzProofHoldFire: 100 000 000 hit points, filled up every
    /// frame) and the other teams' units and guns hold their fire, the roof defenders left free. If after 10 s near the fort nobody has
    /// come up (and the cooldown is over), ONE hit from the ship's side is staged and logged as FALLBACK (a rules question for the
    /// report). Every 15 s the defender's state goes into the report's notes.
    /// </summary>
    public partial class OzK394RoofShotLog
    {
        /// <summary>Distances (m) off the fort's centre tried, the swing (m) each side, the largest distance (px) from the centre kept.</summary>
        public static readonly float[] K419StandM = { 13.5f, 12f, 15f };
        public const float K419SwingM = 7f, K419NearPx = 150f, K419FallbackS = 10f, K419StateEveryS = 15f;
        /// <summary>The deck spots the roof man walks through but never fires from (as ZObject.FortDefender's FdPassOnly).</summary>
        static readonly int[] K419PassNodes = { 4, 8, 12, 17, 18, 23, 24, 28 };

        float k419Near = -1f, k419LogT; bool k419Fallback; int k419Spawns0;

        /// <summary>Fort-local metres (the fort view's frame) to map px (ZObject.FdToPx's rule).</summary>
        Vector2 K419Px(float lx, float lz) { int sg = fort.Type == "fort_front" ? -1 : 1; return new Vector2(fort.CenterX + sg * 8f * lx, fort.CenterY - sg * 8f * lz); }

        static bool K419PassOnly(int k) { foreach (var p in K419PassNodes) if (p == k) return true; return false; }

        /// <summary>Instead of PlanPatrol: the guard and the side, the distance and the two goals.</summary>
        void K419PlanPatrol()
        {
            OzProofHoldFire.Attach(gameObject, W, ship, me, true, false, false, Note);
            var u = W.S != null ? W.S.Unit(ZWorld.FortSniperDeviation ? "sniper" : "grunt") : null;
            int range = u != null && u.attack_radius > 0 ? u.attack_radius : 144;
            var mid = ZGame.PxToWorld(W.Map.width * 8, W.Map.height * 8); var fc = ZGame.PxToWorld(fort.CenterX, fort.CenterY);
            var toMid = new Vector2(mid.x - fc.x, mid.z - fc.z);
            Vector2[] sides = { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) };
            int bestScore = -1; float bestPref = float.MinValue, bestD = K419StandM[0]; Vector2 bestN = sides[0]; string table = "";
            foreach (var d in K419StandM)
                foreach (var n in sides)
                {
                    var tg = new Vector2(-n.y, n.x); int score = 0, near = 0, quiet = 0, shot = 0;
                    for (int i = 0; i <= 10; i++)
                    {
                        float f = -1f + i / 5f; var p = n * d + tg * (f * K419SwingM); var px = K419Px(p.x, p.y);
                        var cl = W.ShuttleClamp(Mathf.RoundToInt(px.x), Mathf.RoundToInt(px.y));
                        if (cl.x != Mathf.RoundToInt(px.x) || cl.y != Mathf.RoundToInt(px.y)) continue;           // off the map
                        bool isNear = Vector2.Distance(px, new Vector2(fort.CenterX, fort.CenterY)) <= K419NearPx;
                        bool isQuiet = !fort.FortGunCanReach(Mathf.RoundToInt(px.x), Mathf.RoundToInt(px.y));
                        bool canShoot = false;
                        for (int k = 0; k < ZObject.FortDefenderDeckNodes && !canShoot; k++)
                        {
                            if (K419PassOnly(k)) continue;
                            double nx = ZObject.FortDefenderDeck[k * 2], nz = ZObject.FortDefenderDeck[k * 2 + 1];
                            double ex = p.x - nx, ez = p.y - nz;
                            if (System.Math.Sqrt(ex * ex + ez * ez) * 8.0 > range - 8) continue;
                            canShoot = ZObject.FortDefenderLineClear(nx, nz, p.x, p.y);
                        }
                        if (isNear) near++; if (isQuiet) quiet++; if (canShoot) shot++;
                        if (isNear && canShoot) score += isQuiet ? 2 : 1;
                    }
                    // the side as the world sees it (fort-local -> px -> world), for the tie: toward the map's middle
                    var sp = K419Px(n.x, n.y) - new Vector2(fort.CenterX, fort.CenterY); var sw = new Vector2(sp.x, -sp.y);
                    float pref = Vector2.Dot(sw.normalized, toMid.sqrMagnitude > 1f ? toMid.normalized : Vector2.right);
                    table += $" [{d:0.0} m, side ({n.x:0},{n.y:0}): near {near}, quiet {quiet}, shot {shot} of 11, score {score}]";
                    if (score > bestScore || (score == bestScore && pref > bestPref + 1e-3f)) { bestScore = score; bestPref = pref; bestD = d; bestN = n; }
                }
            var t = new Vector2(-bestN.y, bestN.x);
            var a = K419Px(bestN.x * bestD + t.x * K419SwingM, bestN.y * bestD + t.y * K419SwingM);
            var b = K419Px(bestN.x * bestD - t.x * K419SwingM, bestN.y * bestD - t.y * K419SwingM);
            var ca = W.ShuttleClamp(Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y)); var cb = W.ShuttleClamp(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.y));
            goalA = new Vector2Int(ca.x, ca.y); goalB = new Vector2Int(cb.x, cb.y);
            k419Spawns0 = fort.FortDefenderSpawns;
            Note($"K419b staging: the sniper's reach {range} px from a deck spot; patrol lines tried (fort-local side; points within {K419NearPx:0} px of the centre, inside the big gun's no-fire ring, shootable from a deck spot):{table}");
            Note($"K419b staging: patrol between px {goalA.x},{goalA.y} and {goalB.x},{goalB.y} ({bestD:0.0} m off the fort's centre on its side ({bestN.x:0},{bestN.y:0}), {K419SwingM:0} m each side; score {bestScore} of 22); no staged hit: the threat scan calls the roof man up");
        }

        /// <summary>Instead of the staged LastEnemyHit, every frame: the fallback hit (once, logged) and the defender's state every 15 s.</summary>
        void K419Tick()
        {
            if (fort == null || ship == null || fort.Destroyed || ship.Destroyed) return;
            float d = Vector2.Distance(new Vector2(ship.CenterX, ship.CenterY), new Vector2(fort.CenterX, fort.CenterY));
            if (ship.ShuttleAirborne && d <= ZWorld.FortDefenderNearPx) { if (k419Near < 0f) { k419Near = Time.time; Note($"K419b: the ship is within the fort's threat ring ({d / 8f:0.0} m off its centre, {ship.ShuttleHeightM:0.0} m up) at {Time.time:0.0} s"); } }
            if (!k419Fallback && k419Near >= 0f && Time.time - k419Near > K419FallbackS && fort.FortDefenderMan == null && fort.FortDefenderState == FortDefenderPhase.Down && W.Time >= fort.FortDefenderNextSpawn)
            {
                k419Fallback = true;
                Note($"K419b FALLBACK: {K419FallbackS:0} s in the threat ring and nobody came up (threat scans that found an enemy {fort.FortDefenderThreats}, men sent up {fort.FortDefenderSpawns - k419Spawns0}): one hit from the ship's side staged (a rules question for the report)");
                fort.NoteFortAttack(ship.Id, ship.CenterX, ship.CenterY); fort.LastEnemyHit = W.Time;
            }
            if (Time.time < k419LogT) return;
            k419LogT = Time.time + K419StateEveryS;
            var m = fort.FortDefenderMan;
            Note($"K419b state at {Time.time:0.0} s: fort {fort.FortDefenderState}, ring {fort.FortDefenderRing}, men sent up {fort.FortDefenderSpawns - k419Spawns0}, threat scans {fort.FortDefenderThreats}; " +
                 (m != null ? $"the man #{m.Id} {m.Type} at deck node {m.FortDefenderNode} (goal {m.FortDefenderGoal}), target {(m.AttackObject != null ? "#" + m.AttackObject.Id + (m.AttackObject == ship ? " (the ship)" : "") : "none")}, rifle shots {m.FortDefenderRifleShots} (blocked {m.FortDefenderBlockedShots}), grenades {m.FortDefenderLobs}; " : "nobody up; ") +
                 $"the ship {d / 8f:0.0} m off the centre, {ship.ShuttleHeightM:0.0} m up, hit points {ship.Health}; roof rounds drawn {n} ({snipers} sniper), guard hits topped {OzProofHoldFire.HitsTopped}");
        }
    }
}
