using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Finds the best interactable / loose gift near the elf every frame and handles [E] and [R].
    /// [E]: interact with focus (gift pickup, stations...) or drop the held item when nothing is focused.
    /// [R]: store held gift in the sack, or (hands empty) pack a nearby gift straight into the sack, or take one out.
    /// </summary>
    [RequireComponent(typeof(ElfCarry))]
    [DisallowMultipleComponent]
    public class ElfInteractor : MonoBehaviour, IInteractor
    {
        [SerializeField] private float radius = 1.0f;
        [SerializeField] private float probeHeight = 0.33f;
        [SerializeField] private float probeForwardOffset = 0.22f;
        [Tooltip("How much being in front of the elf is preferred over pure distance (metres of bonus at dot = 1).")]
        [SerializeField] private float facingWeight = 0.6f;
        [Tooltip("Candidates behind the elf (dot below this) are ignored unless very close.")]
        [SerializeField] private float minFacingDot = -0.4f;
        [SerializeField] private float closeRange = 0.4f;
        [SerializeField] private LayerMask mask = ~0;

        private ElfCarry _carry;
        private readonly Collider[] _overlaps = new Collider[32];
        private CarryablePrompt _carryablePrompt;

        public IInteractable FocusedInteractable { get; private set; }
        /// <summary>Loose gift currently focused (also exposed via FocusedInteractable through a prompt adapter).</summary>
        public ICarryable FocusedCarryable { get; private set; }

        // ---------------- IInteractor ----------------
        public Transform Transform => transform;
        public ICarrier Carrier => _carry;

        private void Awake()
        {
            _carry = GetComponent<ElfCarry>();
            _carryablePrompt = new CarryablePrompt(this);
            int playerLayer = LayerMask.NameToLayer(GameConstants.LayerPlayer);
            if (playerLayer >= 0) mask = mask.value & ~(1 << playerLayer);
        }

        private ElfInput _input;

        private void Start() => _input = GetComponent<ElfInput>();

        private void OnDisable()
        {
            FocusedInteractable = null;
            FocusedCarryable = null;
        }

        private void Update()
        {
            if (_input == null) _input = GetComponent<ElfInput>();
            if (!PlayerGate.CanControl)
            {
                FocusedInteractable = null;
                FocusedCarryable = null;
                return;
            }

            UpdateFocus();
            if (_input == null) return;
            if (_input.InteractPressed) OnInteract();
            if (_input.SackPressed) OnSack();
        }

        // ---------------- focus ----------------

        private void UpdateFocus()
        {
            bool holding = _carry.IsHoldingSomething;
            Vector3 fwd = transform.forward;
            Vector3 origin = transform.position + Vector3.up * probeHeight + fwd * probeForwardOffset;
            int n = Physics.OverlapSphereNonAlloc(origin, radius, _overlaps, mask, QueryTriggerInteraction.Collide);

            IInteractable bestInteractable = null;
            ICarryable bestCarryable = null;
            float bestScore = float.MaxValue;
            Vector3 self = transform.position;

            for (int i = 0; i < n; i++)
            {
                Collider c = _overlaps[i];
                if (c == null) continue;
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue;

                IInteractable interactable = c.GetComponentInParent<IInteractable>();
                ICarryable carryable = c.GetComponentInParent<ICarryable>();

                if (carryable != null)
                {
                    if (!PlayerGate.IsAlive(carryable)) carryable = null;
                    else if (ReferenceEquals(carryable, _carry.Held) || _carry.SackContains(carryable)) continue;
                }
                if (interactable != null && !PlayerGate.IsAlive(interactable)) interactable = null;

                // While holding something, loose gifts are ignored so [E] drops instead.
                if (holding && carryable != null) continue;
                if (interactable != null && !interactable.CanInteract(this)) interactable = null;
                if (interactable == null && carryable == null) continue;

                Vector3 point;
                if (interactable != null && interactable.InteractionPoint != null) point = interactable.InteractionPoint.position;
                else if (carryable != null) point = carryable.GameObject.transform.position;
                else point = c.bounds.center;

                Vector3 to = point - self;
                to.y = 0f;
                float dist = to.magnitude;
                float dot = dist > 0.001f ? Vector3.Dot(fwd, to / dist) : 1f;
                if (dot < minFacingDot && dist > closeRange) continue;

                float score = dist - dot * facingWeight;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestInteractable = interactable;
                    bestCarryable = carryable; // may be both (a Gift can implement IInteractable and ICarryable)
                }
            }

            FocusedCarryable = bestCarryable;
            if (bestInteractable != null)
            {
                FocusedInteractable = bestInteractable;
            }
            else if (bestCarryable != null)
            {
                _carryablePrompt.Target = bestCarryable;
                FocusedInteractable = _carryablePrompt;
            }
            else
            {
                FocusedInteractable = null;
            }
        }

        // ---------------- actions ----------------

        private void OnInteract()
        {
            IInteractable focus = FocusedInteractable;
            if (focus != null && focus.CanInteract(this))
            {
                focus.Interact(this);
                return;
            }
            if (_carry.IsHoldingSomething) _carry.Drop();
        }

        private void OnSack()
        {
            if (_carry.IsHoldingSomething)
            {
                if (!_carry.Held.FitsInSack)
                {
                    GameEvents.RaiseToast("Огромный подарок не влезет в мешок! Несите его в руках.");
                    return;
                }
                if (_carry.IsSackFull)
                {
                    int cap = _carry.SackCapacity;
                    GameEvents.RaiseToast($"Мешок полон ({cap}/{cap})! Улучшите мешок на верстаке.");
                    return;
                }
                _carry.TryStoreHeldInSack();
                return;
            }

            // Hands empty: pack a nearby loose gift directly into the sack (prototype behaviour).
            ICarryable near = FocusedCarryable;
            if (PlayerGate.IsAlive(near))
            {
                if (!near.FitsInSack)
                {
                    GameEvents.RaiseToast("Огромный подарок не влезет в мешок! Несите его в руках.");
                    return;
                }
                if (!_carry.IsSackFull)
                {
                    if (_carry.TryPickUp(near)) _carry.TryStoreHeldInSack();
                    return;
                }
                if (_carry.SackCount == 0) return;
            }

            if (_carry.SackCount > 0)
            {
                _carry.TryTakeFromSack();
                return;
            }

            GameEvents.RaiseToast("Мешок пуст.");
        }

        // ---------------- prompt adapter for loose gifts ----------------

        /// <summary>Lets the HUD show a prompt for an ICarryable that is not itself an IInteractable. Reused, no allocations.</summary>
        private sealed class CarryablePrompt : IInteractable
        {
            private readonly ElfInteractor _owner;
            public ICarryable Target;

            public CarryablePrompt(ElfInteractor owner) { _owner = owner; }

            public string Prompt
            {
                get
                {
                    if (!PlayerGate.IsAlive(Target)) return string.Empty;
                    return Target.Kind == GiftKind.Huge ? "Поднять огромный подарок" : "Взять подарок";
                }
            }

            public Transform InteractionPoint => PlayerGate.IsAlive(Target) ? Target.GameObject.transform : null;

            public bool CanInteract(IInteractor interactor) =>
                PlayerGate.IsAlive(Target) && interactor != null && interactor.Carrier != null && !interactor.Carrier.IsHoldingSomething;

            public void Interact(IInteractor interactor)
            {
                if (CanInteract(interactor)) interactor.Carrier.TryPickUp(Target);
            }
        }
    }
}
