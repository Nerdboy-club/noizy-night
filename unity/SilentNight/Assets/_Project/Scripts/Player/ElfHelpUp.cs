using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Co-op "help a fallen friend" (PEAK-style): while this elf lies limp, another elf can press [E] next to it to
    /// get it back on its feet at once (ElfRagdoll.HelpUp). In co-op a hard fall keeps the elf down longer
    /// (ElfRagdoll.IsDowned), which leaves friends time to come over.
    ///
    /// ElfInteractor ignores the Player layer and the elf's own children, so this component builds a small trigger
    /// child ("HelpUpTrigger") on the Interactable layer. The trigger is only enabled while the elf is knocked down
    /// (so it never disturbs raycasts / overlaps otherwise) and never collides with the elf's own puppet.
    /// </summary>
    [DefaultExecutionOrder(70)] // after ElfRagdoll (60), so its puppet exists when we set up the trigger
    [DisallowMultipleComponent]
    public class ElfHelpUp : MonoBehaviour, IInteractable
    {
        public const string PromptText = "Поднять друга";
        public const string TriggerName = "HelpUpTrigger";

        [SerializeField] private float triggerRadius = 0.4f; // ~0.8 m across
        [SerializeField] private float triggerHeight = 0.25f;
        [SerializeField] private float helpNoise = 15f;

        private ElfRagdoll _ragdoll;
        private ElfInput _input;
        private SphereCollider _trigger;

        public string Prompt => PromptText;
        public Transform InteractionPoint => _trigger != null ? _trigger.transform : transform;
        /// <summary>The trigger collider ElfInteractor finds (null before Start).</summary>
        public Collider Trigger => _trigger;

        private void Start()
        {
            _ragdoll = GetComponent<ElfRagdoll>();
            _input = GetComponent<ElfInput>();
            EnsureTrigger();
            SyncTrigger();
        }

        private void EnsureTrigger()
        {
            if (_trigger != null) return;
            Transform existing = transform.Find(TriggerName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(TriggerName);
            int layer = LayerMask.NameToLayer(GameConstants.LayerInteractable);
            go.layer = layer >= 0 ? layer : 0; // Default is also seen by ElfInteractor
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, triggerHeight, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _trigger = go.GetComponent<SphereCollider>();
            if (_trigger == null) _trigger = go.AddComponent<SphereCollider>();
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

        /// <summary>Trigger on only while knocked down; re-ignore the own body each time (Unity resets it on disable).</summary>
        private void SyncTrigger()
        {
            if (_trigger == null) return;
            bool want = _ragdoll != null && _ragdoll.IsKnockedDown;
            if (want == _trigger.enabled) return;
            _trigger.enabled = want;
            if (want && _ragdoll != null) _ragdoll.IgnoreOwnBody(_trigger);
        }

        // ---------------- IInteractable ----------------

        public bool CanInteract(IInteractor interactor)
        {
            if (_ragdoll == null || !_ragdoll.IsKnockedDown) return false;
            if (interactor == null || interactor.Transform == null) return false;
            Transform helper = interactor.Transform;
            if (helper == transform || helper.IsChildOf(transform)) return false; // not yourself
            ElfRagdoll helperBody = helper.GetComponent<ElfRagdoll>();
            if (helperBody == null || helperBody == _ragdoll) return false; // only another elf
            if (helperBody.IsLimp) return false; // a fallen elf can't lift anyone
            return true;
        }

        public void Interact(IInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            if (!_ragdoll.HelpUp()) return;
            SyncTrigger();

            int me = _input != null ? _input.PlayerIndex : 0;
            ElfInput helperInput = interactor.Transform.GetComponent<ElfInput>();
            int helper = helperInput != null ? helperInput.PlayerIndex : 0;
            GameEvents.RaiseToast($"Эльф {helper + 1} поднял эльфа {me + 1}!");
            GameEvents.RaiseNoise(new NoiseEvent(transform.position, helpNoise, NoiseSource.Footstep, interactor.Transform.gameObject));
        }
    }
}
