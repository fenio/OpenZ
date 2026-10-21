using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// impact + metal land, sparks, a dust cloud, a scorch where it lands, a small camera kick; dust and kick only when on screen),
    /// later bounces clank lighter (dst_debris_metal, at most 4, 0.3 s apart). The body freezes (kinematic, collider off) once it
    /// sleeps or after 6 s, so the burning wreck stays put until UnitView pops it away.
    /// </summary>
    public class WreckContact : MonoBehaviour
    {
        int id, contacts; bool heavy, crashed, frozen; float age, last = -10f; Rigidbody rb;

        public void Init(int objId, bool heavyCrash) { id = objId; heavy = heavyCrash; rb = GetComponent<Rigidbody>(); }

        void Update()
        {
            age += Time.deltaTime;
            if (!frozen && rb && age > 0.5f && (rb.IsSleeping() || age > 6f))
            {
                frozen = true; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
                var hull = transform.Find("OZ_WreckHull"); if (hull) { var c = hull.GetComponent<Collider>(); if (c) c.enabled = false; }
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (frozen || age < 0.15f) return;
            float sp = c.relativeVelocity.magnitude;
            var at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (OzWaterLanding.UnderSurface(at)) return;                                               // b27: the flipped wreck touched the bed under a drawn water surface: no crash, sparks, scorch or shake there (its plunge is made at the surface)
            if (!crashed && sp > 2.5f)
            {
                crashed = true; last = age;
                var au = CombatAudio.I;
                if (au)
                {
                    au.PlayPitched("wreck_crash", at, heavy ? 1f : 0.8f, 0f, id, Random.Range(0.85f, 1f), LegacyMatch.BankTankImpact);
                    au.Play("turret", at, 0.8f, 0, id, OzSoundV3.BankMetalHeavy);   // sound_v3
                }
                OpenZHitSparks.Burst(at + Vector3.up * 0.2f, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 1.5f, false);
                ScorchDecals.Add(new Vector3(at.x, at.y, at.z), 1.6f, ScorchKind.Wreck);
                if (WreckFx.OnScreen(at))
                {
                    var fxp = OpenZFxPlayer.I;
                    if (fxp && fxp.isActiveAndEnabled) { fxp.Dust(at, 1.6f); fxp.DemolitionDust(at, 2.6f, 2.6f, 0.5f); }
                    else FxManager.I?.Dust(at, 1.3f);
                    var rc = FindFirstObjectByType<RtsCamera>(); if (rc) rc.Shake(0.3f, at, "wreck_slam");
                }
                return;
            }
            if (contacts >= 4 || age - last < 0.3f || sp < 1.6f) return;
            contacts++; last = age;
            AudioDirector.Fire("dst_debris_metal", at, id, Mathf.Clamp01(0.4f + sp * 0.06f));
        }
    }
}
