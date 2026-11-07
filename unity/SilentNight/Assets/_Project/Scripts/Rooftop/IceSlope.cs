using SilentNight.Player;
using UnityEngine;

namespace SilentNight.Rooftop
{
    /// <summary>
    /// Trigger volume laid over a tilted icy slab (its +Z points uphill). ElfController has no slope sliding, so while a
    /// grounded elf stands inside, this drags it downhill with a speed that builds up like gravity on ice
    /// (extra CharacterController.Move). Walking still beats the drift, standing still does not: comedic sliding.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class IceSlope : MonoBehaviour
    {
        [Tooltip("Downhill acceleration (m/s²) while standing on the slope.")]
        [SerializeField] private float slideAcceleration = 2.6f;
        [Tooltip("Cap of the downhill drift (m/s). Walk speed is 2.5, so the elf can still climb, slowly.")]
        [SerializeField] private float maxSlideSpeed = 1.7f;
        [SerializeField] private float releaseDeceleration = 6f;

        private BoxCollider _zone;
        private ElfController _elf;
        private CharacterController _cc;
        private float _slide;

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
            _cc = elf.GetComponent<CharacterController>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool sliding = false;
            if (_elf != null)
            {
                if (!_elf.isActiveAndEnabled || !_zone.bounds.Contains(_elf.transform.position + Vector3.up * 0.1f))
                    _elf = null;
                else
                    sliding = _cc != null && _cc.enabled && _elf.IsGrounded && !_elf.InputLocked && !_elf.IsJetpacking;
            }

            if (!sliding)
            {
                _slide = Mathf.MoveTowards(_slide, 0f, releaseDeceleration * dt);
                return;
            }

            _slide = Mathf.MoveTowards(_slide, maxSlideSpeed, slideAcceleration * dt);
            Vector3 downhill = -transform.forward; // along the slab surface, pointing down the slope
            _cc.Move(downhill * (_slide * dt));
        }
    }
}
