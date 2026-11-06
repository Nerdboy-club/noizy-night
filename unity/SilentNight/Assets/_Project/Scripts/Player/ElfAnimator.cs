using System.Collections.Generic;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Drives an optional Animator from ElfController/ElfCarry state.
    /// Parameters (all optional, each write is skipped if the controller lacks it):
    /// floats Speed, VerticalSpeed; bools Grounded, Crouch, Carrying, Jetpack; trigger Jump.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class ElfAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float speedDampTime = 0.1f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int CrouchHash = Animator.StringToHash("Crouch");
        private static readonly int CarryingHash = Animator.StringToHash("Carrying");
        private static readonly int JetpackHash = Animator.StringToHash("Jetpack");
        private static readonly int JumpHash = Animator.StringToHash("Jump");

        private readonly HashSet<int> _params = new HashSet<int>();
        private RuntimeAnimatorController _cachedFor;
        private bool _cacheValid;

        private ElfController _controller;
        private ElfCarry _carry;
        private bool _subscribed;

        public Animator Animator => animator;

        /// <summary>Called by ElfVisual when a model prefab with an Animator is instantiated.</summary>
        public void SetAnimator(Animator a)
        {
            animator = a;
            _cacheValid = false;
        }

        private void OnEnable()
        {
            _controller = GetComponent<ElfController>();
            _carry = GetComponent<ElfCarry>();
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void Start()
        {
            if (_controller == null) _controller = GetComponent<ElfController>();
            if (_carry == null) _carry = GetComponent<ElfCarry>();
            Subscribe();
            if (animator == null)
            {
                ElfVisual visual = GetComponent<ElfVisual>();
                if (visual != null && visual.ModelAnimator != null) SetAnimator(visual.ModelAnimator);
                else SetAnimator(GetComponentInChildren<Animator>());
            }
        }

        private void Subscribe()
        {
            if (_subscribed || _controller == null) return;
            _controller.Jumped += OnJumped;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _controller == null) { _subscribed = false; return; }
            _controller.Jumped -= OnJumped;
            _subscribed = false;
        }

        private void RebuildCacheIfNeeded()
        {
            if (animator == null) { _cacheValid = false; return; }
            RuntimeAnimatorController rac = animator.runtimeAnimatorController;
            if (_cacheValid && rac == _cachedFor) return;

            _params.Clear();
            _cachedFor = rac;
            if (rac == null || !animator.isActiveAndEnabled) { _cacheValid = false; return; }

            AnimatorControllerParameter[] ps = animator.parameters; // allocates: only on (re)build
            for (int i = 0; i < ps.Length; i++) _params.Add(ps[i].nameHash);
            _cacheValid = true;
        }

        private bool Has(int hash) => _params.Contains(hash);

        private void Update()
        {
            if (animator == null || _controller == null) return;
            RebuildCacheIfNeeded();
            if (!_cacheValid) return;

            float dt = Time.deltaTime;
            if (Has(SpeedHash)) animator.SetFloat(SpeedHash, _controller.PlanarSpeed, speedDampTime, dt);
            if (Has(VerticalSpeedHash)) animator.SetFloat(VerticalSpeedHash, _controller.VerticalSpeed);
            if (Has(GroundedHash)) animator.SetBool(GroundedHash, _controller.IsGrounded);
            if (Has(CrouchHash)) animator.SetBool(CrouchHash, _controller.IsCrouching);
            if (Has(CarryingHash)) animator.SetBool(CarryingHash, _carry != null && _carry.IsHoldingSomething);
            if (Has(JetpackHash)) animator.SetBool(JetpackHash, _controller.IsJetpacking);
        }

        private void OnJumped()
        {
            if (animator == null) return;
            RebuildCacheIfNeeded();
            if (_cacheValid && Has(JumpHash)) animator.SetTrigger(JumpHash);
        }
    }
}
