using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Shared base for hub/house stations (sleigh, order board, workbench, tree, chimney).
    /// Gives a serialized prompt + interaction point and makes sure there is a collider the
    /// player's interactor can find.
    /// </summary>
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [Header("Interaction")]
        [SerializeField] protected string prompt = "Взаимодействовать";
        [Tooltip("Point the HUD / interactor measures distance to. Defaults to this transform.")]
        [SerializeField] protected Transform interactionPoint;

        [Tooltip("Add a trigger BoxCollider in Awake if this object has no collider of its own.")]
        [SerializeField] protected bool ensureCollider = true;
        [SerializeField] protected Vector3 defaultColliderSize = new Vector3(1.6f, 1.6f, 1.6f);
        [SerializeField] protected Vector3 defaultColliderCenter = new Vector3(0f, 0.8f, 0f);

        [Tooltip("Put this object on the 'Interactable' layer (only if it is still on Default).")]
        [SerializeField] protected bool useInteractableLayer = true;

        public virtual string Prompt => prompt;
        public Transform InteractionPoint => interactionPoint != null ? interactionPoint : transform;

        public virtual bool CanInteract(IInteractor interactor) => isActiveAndEnabled;
        public abstract void Interact(IInteractor interactor);

        public void SetPrompt(string text) => prompt = text;

        protected virtual void Awake()
        {
            if (ensureCollider && GetComponent<Collider>() == null)
            {
                var bc = gameObject.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = defaultColliderSize;
                bc.center = defaultColliderCenter;
            }

            if (useInteractableLayer && gameObject.layer == 0)
            {
                int layer = LayerMask.NameToLayer(GameConstants.LayerInteractable);
                if (layer >= 0) gameObject.layer = layer;
            }
        }

        /// <summary>Null-safe access to the interactor's hands/sack.</summary>
        protected static ICarrier CarrierOf(IInteractor interactor) => interactor?.Carrier;

        protected static bool HasAnything(ICarrier c) => c != null && (c.IsHoldingSomething || c.SackCount > 0);
    }
}
