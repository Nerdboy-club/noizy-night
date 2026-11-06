namespace SilentNight.Core
{
    public static class GameConstants
    {
        // Scene names (must match files in Assets/_Project/Scenes and Build Settings)
        public const string SceneBoot = "Boot";
        public const string SceneHub = "Hub";
        public const string SceneHouse = "House";

        // Layers (created by Editor/ProjectBootstrap)
        public const string LayerPlayer = "Player";
        public const string LayerGift = "Gift";
        public const string LayerNPC = "NPC";
        public const string LayerInteractable = "Interactable";
        public const string LayerCameraIgnore = "CameraIgnore";

        // Tags
        public const string TagPlayer = "Player";
        public const string TagSpawn = "PlayerSpawn";

        // Noise baseline values (0..100) from the three.js prototype
        public const float NoiseIdle = 10f;
        public const float NoiseWalk = 30f;
        public const float NoiseRun = 55f;
        public const float NoiseCrouch = 12f;
        public const float NoiseLanding = 60f;
        public const float NoiseJetpack = 70f;
        public const float NoiseCreakyFloorBonus = 25f;

        /// <summary>Grandpa wakes up when he hears this much (after distance falloff).</summary>
        public const float GrandpaWakeThreshold = 45f;
    }
}
