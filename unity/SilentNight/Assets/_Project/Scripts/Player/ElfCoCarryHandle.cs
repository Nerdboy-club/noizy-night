using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Co-op "grab the other end" of the bike (Gang Beasts / PEAK style): while this elf carries a Huge gift alone,
    /// another elf with empty hands can press [E] next to it to start a CoCarry (this elf = front/holder, the other =
    /// rear/helper). While co-carrying, the same trigger sits at the helper's end of the bike and lets the helper
    /// press [E] again to let go.
    ///
    /// Same pattern as ElfHelpUp: ElfInteractor ignores the Player layer and the interactor's own children, so the
    /// trigger lives on a child ("CoCarryHandle") on the Interactable layer. This component sits ON that child (so
    /// GetComponentInParent&lt;IInteractable&gt; from the trigger finds it before ElfHelpUp on the elf root) and is only
    /// enabled while relevant: carrying the bike solo with more than one player, or co-carrying as the holder.
    /// </summary>
    [DefaultExecutionOrder(70)] // after ElfRagdoll (60), so its puppet exists before we ignore it
    [DisallowMultipleComponent]
    public class ElfCoCarryHandle : MonoBehaviour, IInteractable
    {
        public const string PromptText = "Взяться за другой конец";
        public const string ReleasePromptText = "Отпустить велосипед";
        public const string ChildName = "CoCarryHandle";

        [SerializeField] private float triggerRadius = 0.45f;
        [Tooltip("Max distance between the two elves (m) to start carrying together.")]
        [SerializeField] private float maxStartDistance = 2.2f;
        [SerializeField] private float grabNoise = 12f;

        private ElfCarry _owner;
        private ElfRagdoll _ragdoll;
        private SphereCollider _trigger;

        /// <summary>The elf whose bike this handle belongs to.</summary>
        public ElfCarry Owner => _owner;
        /// <summary>The trigger collider ElfInteractor finds.</summary>
        public Collider Trigger => _trigger;

        public string Prompt => _owner != null && _owner.IsCoCarrying ? ReleasePromptText : PromptText;
        public Transform InteractionPoint => transform;

        /// <summary>Find or create the handle child of <paramref name="carry"/>'s elf (works in edit mode too).</summary>
        public static ElfCoCarryHandle Ensure(ElfCarry carry)
        {
            if (carry == null) return null;
            Transform root = carry.transform;
            Transform existing = root.Find(ChildName);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(ChildName);
                go.transform.SetParent(root, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }
            int layer = LayerMask.NameToLayer(GameConstants.LayerInteractable);
            go.layer = layer >= 0 ? layer : 0; // Default is also seen by ElfInteractor

            ElfCoCarryHandle h = go.GetComponent<ElfCoCarryHandle>();
            if (h == null) h = go.AddComponent<ElfCoCarryHandle>();
            h._owner = carry;
            h.EnsureTrigger();
            carry.SetCoCarryHandle(h);
            return h;
        }

        private void Awake()
        {
            if (_owner == null) _owner = GetComponentInParent<ElfCarry>();
            EnsureTrigger();
        }

        private void Start()
        {
            if (_owner == null) _owner = GetComponentInParent<ElfCarry>();
            if (_owner != null)
            {
                _ragdoll = _owner.GetComponent<ElfRagdoll>();
                _owner.SetCoCarryHandle(this);
            }
            int layer = LayerMask.NameToLayer(GameConstants.LayerInteractable);
            gameObject.layer = layer >= 0 ? layer : 0;
            SyncTrigger();
        }

        private void EnsureTrigger()
        {
            if (_trigger != null) return;
            _trigger = GetComponent<SphereCollider>();
            if (_trigger == null) _trigger = gameObject.AddComponent<SphereCollider>();
            _trigger.isTrigger = true;
            _trigger.radius = triggerRadius;
            _trigger.center = Vector3.zero;
            _trigger.enabled = false;
        }

        private void Update() => SyncTrigger();

        private void OnDisable()
        {
            if (_trigger != null) _trigger.enabled = false;
        }

        /// <summary>Trigger on only while relevant; follows the bike centre (solo) or the helper's end (co-carry).</summary>
        private void SyncTrigger()
        {
            if (_trigger == null || _owner == null) return;
            bool want = WantTrigger(out Vector3 at);
            if (want) transform.position = at;
            if (want == _trigger.enabled) return;
            if (want && _ragdoll != null && _ragdoll.PelvisTransform == null) return; // puppet not built yet
            _trigger.enabled = want;
            if (want && _ragdoll != null) _ragdoll.IgnoreOwnBody(_trigger); // re-ignore: Unity resets it on disable
        }

        private bool WantTrigger(out Vector3 at)
        {
            at = transform.position;
            if (!_owner.isActiveAndEnabled) return false;
            CoCarry c = _owner.ActiveCoCarry;
            if (c != null)
            {
                if (c.Holder != _owner) return false; // the helper's own handle stays off
                at = c.HelperGrip;
                return true;
            }
            if (!_owner.IsCarryingHuge || GameServices.Players.Count < 2) return false;
            if (_ragdoll != null && _ragdoll.IsLimp) return false;
            Transform hp = _owner.HoldPoint;
            at = hp != null ? hp.position : _owner.transform.position;
            return true;
        }

        // ---------------- IInteractable ----------------

        private ElfCarry OtherElf(IInteractor interactor)
        {
            if (_owner == null || interactor == null || interactor.Transform == null) return null;
            Transform t = interactor.Transform;
            if (t == _owner.transform || t.IsChildOf(_owner.transform)) return null; // not yourself
            ElfCarry other = t.GetComponent<ElfCarry>();
            return other != null && other != _owner ? other : null;
        }

        public bool CanInteract(IInteractor interactor)
        {
            ElfCarry other = OtherElf(interactor);
            if (other == null) return false;

            CoCarry c = _owner.ActiveCoCarry;
            if (c != null) return c.Holder == _owner && c.Helper == other; // the helper lets go

            if (!CoCarry.CanBegin(_owner, other)) return false;
            Vector3 d = other.transform.position - _owner.transform.position;
            return d.sqrMagnitude <= maxStartDistance * maxStartDistance;
        }

        public void Interact(IInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            ElfCarry other = OtherElf(interactor);

            CoCarry active = _owner.ActiveCoCarry;
            if (active != null)
            {
                active.Release();
                SyncTrigger();
                return;
            }

            if (CoCarry.Begin(_owner, other) == null) return;
            SyncTrigger();

            ElfInput mine = _owner.GetComponent<ElfInput>();
            ElfInput theirs = other.GetComponent<ElfInput>();
            int me = mine != null ? mine.PlayerIndex : 0;
            int helper = theirs != null ? theirs.PlayerIndex : 0;
            GameEvents.RaiseToast($"Эльфы {me + 1} и {helper + 1} несут велосипед вдвоём!");
            if (grabNoise > 0f)
                GameEvents.RaiseNoise(new NoiseEvent(transform.position, grabNoise, NoiseSource.Footstep, other.gameObject));
        }
    }
}
