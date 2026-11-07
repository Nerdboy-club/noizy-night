using System.Collections;
using SilentNight.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SilentNight.Player
{
    /// <summary>
    /// Local co-op drop-in / drop-out (COOP_PLAN step 1). Lives on player 1's elf (PlayerSetup adds it; ElfInput adds it
    /// at runtime to elves baked into scenes before co-op).
    ///
    /// Join: during gameplay (PlayerGate.CanControl), press Start or South (A / Cross) on a gamepad that player 1 is not
    /// using → a new elf appears 1 m next to player 1, sweater colour = player index, that pad is its only device.
    /// "Not used by player 1" = not claimed by another elf, not player 1's last-used gamepad, and player 1 has already
    /// used keyboard/mouse or another pad (so a solo gamepad player pressing Start for the first time just pauses).
    /// Leave: unplugging the pad removes that elf (its held item and sack contents are dropped on the spot).
    ///
    /// Shared screen: extra elves have no camera rig; player 1's ThirdPersonCameraRig frames the whole group and
    /// extra elves move relative to Camera.main (ElfController.GetCameraYaw fallback).
    ///
    /// TODO (later steps):
    /// - Roster (PlayerSlot index/devices/theme) that survives scene loads (DontDestroyOnLoad) + PlayerSpawner;
    ///   right now joined elves are scene objects and must re-join after every scene change.
    /// - Split-screen option (per-player Camera/Brain/channel), per-player HUD (HudView shows LocalPlayer only),
    ///   per-player PlayerGate.CanControl / UI modal owner, ElfRagdoll debug key per player, leave via menu button,
    ///   re-join with the same slot after a pad reconnects, and a runtime model source for builds (ElfRagdoll uses
    ///   AssetDatabase in the editor only).
    /// </summary>
    [DefaultExecutionOrder(-200)] // before ElfInput (-100), so the join press can be hidden from player 1 this frame
    [DisallowMultipleComponent]
    public class LocalCoopJoin : MonoBehaviour
    {
        public const int MaxLocalPlayers = 4;

        [SerializeField] private bool allowJoin = true;
        [Tooltip("Distance (m) from player 1 at which a joining elf appears.")]
        [SerializeField] private float spawnOffset = 1f;

        private ElfInput _host;
        private int _limitToastFrame = -1000;

        /// <summary>Raised after an extra elf joined (player index 1..3).</summary>
        public static event System.Action<int, GameObject> PlayerJoined;
        /// <summary>Raised just before an extra elf is removed (player index 1..3).</summary>
        public static event System.Action<int> PlayerLeft;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlayerJoined = null;
            PlayerLeft = null;
        }

        private void Awake() => _host = GetComponent<ElfInput>();

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;

        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void Update()
        {
            if (!allowJoin) return;
            if (_host == null) _host = GetComponent<ElfInput>();
            if (_host == null || !_host.isActiveAndEnabled || _host.PlayerIndex != 0) return;
            if (!PlayerGate.CanControl) return;

            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
            {
                Gamepad pad = pads[i];
                if (pad == null) continue;
                if (!pad.startButton.wasPressedThisFrame && !pad.buttonSouth.wasPressedThisFrame) continue;
                if (ElfInput.IsGamepadClaimed(pad)) continue;
                if (pad == _host.LastUsedGamepad) continue;
                // Player 1 has touched nothing but (maybe) this pad: the press belongs to player 1, not a newcomer.
                if (_host.LastUsedGamepad == null && !_host.HasUsedKeyboardMouse) continue;

                if (CountLocalElves() >= MaxLocalPlayers)
                {
                    if (Time.frameCount - _limitToastFrame > 60)
                    {
                        _limitToastFrame = Time.frameCount;
                        GameEvents.RaiseToast("Больше четырёх эльфов нельзя.");
                    }
                    continue;
                }

                Join(pad);
                break; // one join per frame
            }
        }

        private static int CountLocalElves()
        {
            int n = 0;
            var all = ElfInput.All;
            for (int i = 0; i < all.Count; i++) if (all[i] != null) n++;
            return n;
        }

        /// <summary>Smallest free player index in 1..3, or -1.</summary>
        private static int FreePlayerIndex()
        {
            var all = ElfInput.All;
            for (int idx = 1; idx < MaxLocalPlayers; idx++)
            {
                bool used = false;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].PlayerIndex == idx) { used = true; break; }
                if (!used) return idx;
            }
            return -1;
        }

        /// <summary>Spawns an extra elf for <paramref name="pad"/> next to player 1. Returns null if no slot is free.</summary>
        public GameObject Join(Gamepad pad)
        {
            if (pad == null || ElfInput.IsGamepadClaimed(pad)) return null;
            int index = FreePlayerIndex();
            if (index < 0) return null;

            // The Start/South press also reached player 1 (it listens to unclaimed pads): no pause/jump this frame.
            if (_host != null) _host.SuppressPresses();

            Vector3 pos = FindSpawnPoint();
            Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            GameObject elf = PlayerSetup.CreatePlayer(pos, index, index, pad);
            elf.transform.rotation = rot;
            StartCoroutine(PlaceAfterStart(elf, pos, rot));

            GameEvents.RaiseToast($"Эльф {index + 1} присоединился!");
            PlayerJoined?.Invoke(index, elf);
            return elf;
        }

        /// <summary>
        /// ElfRagdoll builds its puppet in Start; teleport once after that so the puppet snaps to the root (and so the
        /// elf is placed correctly even if ElfController still moved it to PlayerSpawn).
        /// </summary>
        private static IEnumerator PlaceAfterStart(GameObject elf, Vector3 pos, Quaternion rot)
        {
            yield return null;
            if (elf == null) yield break;
            var ctrl = elf.GetComponent<ElfController>();
            if (ctrl != null) ctrl.Teleport(pos, rot);
        }

        private Vector3 FindSpawnPoint()
        {
            Transform t = transform;
            Vector3 origin = t.position + Vector3.up * 0.05f;
            Vector3[] dirs = { t.right, -t.right, -t.forward, t.forward };
            int mask = BuildBlockMask();
            float r = PlayerSetup.ControllerRadius;
            float h = PlayerSetup.ControllerHeight;
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 d = dirs[i];
                d.y = 0f;
                if (d.sqrMagnitude < 1e-4f) continue;
                d.Normalize();
                // Line of sight from player 1 (no spawning through a wall) and room for the capsule.
                if (Physics.Raycast(origin + Vector3.up * (h * 0.5f), d, spawnOffset + r, mask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 p = origin + d * spawnOffset;
                if (Physics.CheckCapsule(p + Vector3.up * (r + 0.02f), p + Vector3.up * (h - r), r, mask, QueryTriggerInteraction.Ignore)) continue;
                return p;
            }
            return origin; // crowded: spawn on top of player 1, the CharacterControllers push apart
        }

        private static int BuildBlockMask()
        {
            int mask = ~0;
            mask = Exclude(mask, GameConstants.LayerPlayer);
            mask = Exclude(mask, GameConstants.LayerGift);
            mask = Exclude(mask, "Ignore Raycast");
            return mask;
        }

        private static int Exclude(int mask, string layerName)
        {
            int l = LayerMask.NameToLayer(layerName);
            return l >= 0 ? mask & ~(1 << l) : mask;
        }

        // ---------------- leave ----------------

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected) return;
            if (!(device is Gamepad)) return;

            var all = ElfInput.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                ElfInput e = all[i];
                if (e == null || e.PlayerIndex == 0 || e.AssignedGamepad != device) continue;
                Leave(e);
            }
        }

        /// <summary>Removes an extra elf (never player 1). Held item and sack contents are dropped where it stood.</summary>
        public static void Leave(ElfInput elf)
        {
            if (elf == null || elf.PlayerIndex == 0) return;
            int index = elf.PlayerIndex;
            GameObject go = elf.gameObject;

            var carry = go.GetComponent<ElfCarry>();
            if (carry != null)
            {
                ICarryable[] items = carry.TakeAll();
                Vector3 basePos = go.transform.position + Vector3.up * 0.3f;
                for (int i = 0; i < items.Length; i++)
                {
                    ICarryable item = items[i];
                    if (!PlayerGate.IsAlive(item) || item.GameObject == null) continue;
                    Vector2 jitter = Random.insideUnitCircle * 0.2f;
                    item.GameObject.transform.position = basePos + new Vector3(jitter.x, 0.15f * i, jitter.y);
                    item.OnDropped(Vector3.zero);
                }
            }

            PlayerLeft?.Invoke(index);
            Destroy(go);
            GameEvents.RaiseToast($"Эльф {index + 1} покинул игру: геймпад отключён.");
        }
    }
}
