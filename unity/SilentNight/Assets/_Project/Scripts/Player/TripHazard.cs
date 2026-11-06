using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Toy on the floor (toy car, blocks, rubber duck) that trips an elf who walks or runs over it.
    /// Put it on a TRIGGER collider. An elf (ElfController in the parents of the entering collider) moving faster than
    /// minTripSpeed, grounded and not crouching, goes limp, squeaks (noise) and shows a toast. Sneaking (crouch) over it
    /// is safe — that is the counterplay. Optional: the toy's Rigidbody (in parents) gets a small kick.
    /// </summary>
    [DisallowMultipleComponent]
    public class TripHazard : MonoBehaviour
    {
        [SerializeField] private float minTripSpeed = 1.6f;
        [SerializeField] private float cooldown = 3f;
        [SerializeField] private float squeakNoise = 45f;
        [SerializeField] private string toastText = "Пиии! Эльф наступил на игрушечную машинку.";

        [Header("Physics")]
        [Tooltip("Forward/up velocity change given to the elf's pelvis (faceplant along the movement).")]
        [SerializeField] private float tripForwardPush = 2.6f;
        [SerializeField] private float tripUpPush = 1.8f;
        [Tooltip("Velocity change for the toy's own Rigidbody (if any): it shoots away from under the foot.")]
        [SerializeField] private float toyKick = 2.2f;

        private float _lastTripTime = -999f;

        public string ToastText { get => toastText; set => toastText = value; }

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        private void Awake()
        {
            Collider c = GetComponent<Collider>();
            if (c == null || !c.isTrigger)
                Debug.LogWarning("[TripHazard] '" + name + "' needs a trigger collider on the same GameObject.", this);
        }

        private void OnTriggerEnter(Collider other) => TryTrip(other);

        // Stay: an elf can enter crouching and then stand up / start running inside the trigger.
        private void OnTriggerStay(Collider other) => TryTrip(other);

        private void TryTrip(Collider other)
        {
            if (other == null || Time.time - _lastTripTime < cooldown) return;

            ElfController elf = other.GetComponentInParent<ElfController>();
            if (elf == null || !elf.isActiveAndEnabled) return;
            if (elf.InputLocked || elf.IsCrouching || !elf.IsGrounded) return;
            if (elf.PlanarSpeed < minTripSpeed) return;

            ElfRagdoll ragdoll = elf.GetComponent<ElfRagdoll>();
            if (ragdoll != null && ragdoll.IsLimp) return;

            _lastTripTime = Time.time;

            Vector3 v = elf.Velocity;
            Vector3 dir = new Vector3(v.x, 0f, v.z);
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : elf.transform.forward;

            if (ragdoll != null) ragdoll.GoLimp(dir * tripForwardPush + Vector3.up * tripUpPush);

            GameEvents.RaiseNoise(new NoiseEvent(transform.position, squeakNoise, NoiseSource.Object, gameObject));
            if (!string.IsNullOrEmpty(toastText)) GameEvents.RaiseToast(toastText);

            Rigidbody toy = GetComponentInParent<Rigidbody>();
            if (toy != null && !toy.isKinematic)
                toy.AddForce(dir * toyKick + Vector3.up * (toyKick * 0.3f), ForceMode.VelocityChange);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Collider c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.35f);
            Bounds b = c.bounds;
            Gizmos.DrawCube(b.center, b.size);
        }
#endif
    }
}
