namespace SilentNight.Core
{
    /// <summary>Global game flow state. Owned by GameFlow (Core/GameFlow.cs).</summary>
    public enum GameState
    {
        Boot,
        MainMenu,
        Hub,          // Santa's workshop: pick order, load gifts, upgrade
        Rooftop,      // city rooftops: find the right chimney
        Delivery,     // inside a house
        Results,      // delivery summary screen
        GameOver,     // caught by grandpa
        Paused
    }

    /// <summary>Mood of a house for an order. Spooky = dark, rare, tips x2.</summary>
    public enum HouseMood { Cozy, Spooky }

    /// <summary>Gift class. Small/Noisy/Fragile fit in the sack, Huge must be carried (bike). Fragile = glass snow globe, breaks on hard impacts.</summary>
    public enum GiftKind { Small, Noisy, Huge, Fragile }

    /// <summary>What produced a noise. Used for UI/audio flavour and NPC reactions.</summary>
    public enum NoiseSource
    {
        Footstep,
        Landing,
        CreakyFloor,
        GiftImpact,   // gift hit a wall/floor
        NoisyGift,    // wind-up toy inside a gift
        Jetpack,
        Cat,
        Object        // knocked-over prop
    }

    /// <summary>Floor material under the elf. Drives footstep noise and slipperiness.</summary>
    public enum SurfaceType { Default, Carpet, Wood, CreakyWood, Tile, Ice }

    public enum UpgradeType { Jetpack, SilentBoots, Sack, Grip }

    /// <summary>Grandpa awareness level, broadcast via GameEvents.GrandpaAlertChanged.</summary>
    public enum AlertLevel { Asleep, Stirring, Suspicious, Searching, Chasing }
}
