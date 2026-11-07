using SilentNight.Core;
using SilentNight.Player;
using UnityEngine;

namespace SilentNight.Rooftop
{
    /// <summary>
    /// Soft snow drift: a trigger volume over a snow pile. While an airborne elf falls through it faster than
    /// <see cref="catchSpeed"/>, its fall is "absorbed" (ElfController.Teleport in place zeroes vertical speed and the
    /// tracked peak fall speed), so the landing never reaches ElfRagdoll's limp threshold. Plays a snow puff.
    /// Runs before ElfController so the catch happens before that frame's landing check.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(BoxCollider))]
    public class SnowCushion : MonoBehaviour
    {
        [Tooltip("Downward speed (m/s) above which the drift brakes the fall.")]
        [SerializeField] private float catchSpeed = 4f;
        [SerializeField] private float puffCooldown = 0.35f;
        [SerializeField] private float toastCooldown = 6f;
        [SerializeField] private string toast = "Плюх! Эльф мягко ушёл в сугроб.";

        private BoxCollider _zone;
        private ElfController _elf;
        private ElfRagdoll _ragdoll;
        private float _puffTimer;
        private float _toastTimer;

        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        private void Awake()
        {
            _zone = GetComponent<BoxCollider>();
            _zone.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other) => Track(other);

        private void OnTriggerStay(Collider other) => Track(other);

        private void OnTriggerExit(Collider other)
        {
            if (_elf != null && other.GetComponentInParent<ElfController>() == _elf) _elf = null;
        }

        private void Track(Collider other)
        {
            if (_elf != null) return;
            ElfController elf = other.GetComponentInParent<ElfController>();
            if (elf == null) return;
            _elf = elf;
            _ragdoll = elf.GetComponent<ElfRagdoll>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_puffTimer > 0f) _puffTimer -= dt;
            if (_toastTimer > 0f) _toastTimer -= dt;
            if (_elf == null) return;
            if (!_elf.isActiveAndEnabled || !_zone.bounds.Contains(_elf.transform.position + Vector3.up * 0.1f))
            {
                _elf = null; // left the zone (Exit may be skipped while the controller is toggled by Teleport)
                return;
            }
            if (_elf.IsGrounded || _elf.IsJetpacking) return;
            if (_ragdoll != null && _ragdoll.IsLimp) return;

            float down = -_elf.VerticalSpeed;
            if (down < catchSpeed) return;

            Transform t = _elf.transform;
            _elf.Teleport(t.position, t.rotation, false); // in place: kills vertical speed + peak fall speed

            if (_puffTimer <= 0f)
            {
                _puffTimer = puffCooldown;
                SilentNight.FX.LandingDust.Spawn(t.position, Mathf.InverseLerp(catchSpeed, 14f, down));
            }
            if (_toastTimer <= 0f && down >= 8f && !string.IsNullOrEmpty(toast))
            {
                _toastTimer = toastCooldown;
                GameEvents.RaiseToast(toast);
            }
        }
    }
}
