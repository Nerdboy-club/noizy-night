using UnityEngine;

namespace SilentNight.Core
{
    /// <summary>Put on floor colliders (or trigger volumes) to mark their surface type.
    /// Player raycasts down and reads this; missing tag = SurfaceType.Default.</summary>
    public class SurfaceTag : MonoBehaviour
    {
        public SurfaceType Surface = SurfaceType.Default;

        /// <summary>Extra friction loss 0..1 (Tile ~0.4, Ice ~0.85). Silent boots reduce it.</summary>
        public float Slipperiness => Surface switch
        {
            SurfaceType.Tile => 0.4f,
            SurfaceType.Ice => 0.85f,
            _ => 0f
        };

        /// <summary>Footstep noise multiplier.</summary>
        public float NoiseMultiplier => Surface switch
        {
            SurfaceType.Carpet => 0.5f,
            SurfaceType.Wood => 1.0f,
            SurfaceType.CreakyWood => 1.0f, // + GameConstants.NoiseCreakyFloorBonus added by player
            SurfaceType.Tile => 1.2f,
            SurfaceType.Ice => 0.8f,
            _ => 1.0f
        };

        public static SurfaceType Get(Collider c, out SurfaceTag tag)
        {
            tag = c ? c.GetComponentInParent<SurfaceTag>() : null;
            return tag ? tag.Surface : SurfaceType.Default;
        }
    }
}
