using UnityEngine;

namespace SilentNight.Core
{
    /// <summary>Anything the elf can use with the Interact key [E] (gift, conveyor, sleigh, order board, workbench, chimney exit).</summary>
    public interface IInteractable
    {
        /// <summary>Short prompt for the HUD, e.g. "Взять подарок".</summary>
        string Prompt { get; }
        Transform InteractionPoint { get; }
        bool CanInteract(IInteractor interactor);
        void Interact(IInteractor interactor);
    }

    /// <summary>The player-side of an interaction. Implemented by Player/ElfInteractor.</summary>
    public interface IInteractor
    {
        Transform Transform { get; }
        ICarrier Carrier { get; }
    }

    /// <summary>Something the elf holds in hands or stores in the sack. Implemented by Gifts/Gift.</summary>
    public interface ICarryable
    {
        GameObject GameObject { get; }
        GiftKind Kind { get; }
        float Mass { get; }
        bool FitsInSack { get; }
        /// <summary>Packaging integrity 0..100. Affects tips.</summary>
        float Integrity { get; }
        /// <summary>Attach to a hold point (physics disabled / kinematic).</summary>
        void OnPickedUp(Transform holdPoint);
        /// <summary>Release into the world with a velocity (physics re-enabled).</summary>
        void OnDropped(Vector3 velocity);
        /// <summary>Hide inside the sack (disable renderers + physics).</summary>
        void OnStoredInSack();
    }

    /// <summary>Hands + sack of an elf. Implemented by Player/ElfCarry.</summary>
    public interface ICarrier
    {
        ICarryable Held { get; }
        bool IsHoldingSomething { get; }
        int SackCount { get; }
        int SackCapacity { get; }
        bool TryPickUp(ICarryable item);
        void Drop();
        bool TryStoreHeldInSack();
        bool TryTakeFromSack();
        /// <summary>Remove and return everything (held + sack). Used by sleigh loading / tree drop zone.</summary>
        ICarryable[] TakeAll();
    }

    /// <summary>Read-only live player stats for HUD. Implemented by Player/ElfController, registered in GameServices.LocalPlayer.</summary>
    public interface IPlayerStatus
    {
        float Stamina01 { get; }
        float JetpackFuel01 { get; }
        bool HasJetpack { get; }
        bool IsCrouching { get; }
        bool IsGrounded { get; }
        /// <summary>Smoothed noise the player is currently making, 0..100.</summary>
        float CurrentNoise { get; }
        SurfaceType CurrentSurface { get; }
        ICarrier Carrier { get; }
        Transform Transform { get; }
        /// <summary>Interactable currently in range (for HUD prompt), or null.</summary>
        IInteractable FocusedInteractable { get; }
    }
}
