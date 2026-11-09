using System.Collections;
using System.Collections.Generic;
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
    /// Roster: every join is remembered in a static roster (player index, sweater theme, gamepad deviceId). Joined elves
    /// are scene objects and die with the scene; when the next scene's player 1 starts, every roster entry whose pad is
    /// still connected is re-spawned next to player 1 in its old slot (one "Эльфы на месте: N" toast instead of
    /// per-elf join toasts). Leave (pad unplugged / Leave()) forgets the entry. Carried items are not carried over.
    ///
    /// Shared screen: extra elves have no camera rig; player 1's ThirdPersonCameraRig frames the whole group and
    /// extra elves move relative to Camera.main (ElfController.GetCameraYaw fallback).
    ///
    /// TODO (later steps):
    /// - PlayerSpawner (spawn per PlayerIndex instead of "next to player 1").
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
        [Tooltip("On scene start, re-spawn the extra elves that joined in an earlier scene (if their pads are still connected).")]
        [SerializeField] private bool restoreRoster = true;

        private ElfInput _host;
        private int _limitToastFrame = -1000;
        private bool _restorePending;

        /// <summary>Raised after an extra elf joined (player index 1..3). Not raised for roster restores.</summary>
        public static event System.Action<int, GameObject> PlayerJoined;
        /// <summary>Raised just before an extra elf is removed (player index 1..3).</summary>
        public static event System.Action<int> PlayerLeft;
        /// <summary>Raised after the roster re-spawned extra elves on scene start (argument: number restored).</summary>
        public static event System.Action<int> RosterRestored;

        // ---------------- roster (survives scene loads) ----------------

        /// <summary>One joined extra player: slot index, sweater theme and its gamepad (or keyboard secondary).</summary>
        private struct RosterEntry
        {
            public int PlayerIndex;
            public int ThemeIndex;
            public int DeviceId;
            // Same-object check on top of the id: InputTestFixture (and Input System resets) can hand out the same
            // deviceId again to a brand-new virtual pad, which must not inherit a stale slot.
            public InputDevice Device;
            public bool IsKeyboardSecondary;
        }

        private static readonly List<RosterEntry> s_Roster = new List<RosterEntry>(MaxLocalPlayers - 1);

        /// <summary>Number of extra players (2..4) remembered across scene loads.</summary>
        public static int RosterCount => s_Roster.Count;

        /// <summary>True if the roster remembers <paramref name="device"/> as some extra player's gamepad.</summary>
        public static bool IsInRoster(InputDevice device)
        {
            if (device == null) return false;
            for (int i = 0; i < s_Roster.Count; i++)
                if (!s_Roster[i].IsKeyboardSecondary && s_Roster[i].DeviceId == device.deviceId && s_Roster[i].Device == device) return true;
            return false;
        }

        /// <summary>Forgets every remembered extra player (tests / "back to main menu").</summary>
        public static void ClearRoster() => s_Roster.Clear();

        private static void RosterAdd(int playerIndex, int themeIndex, InputDevice device, bool keyboardSecondary = false)
        {
            if ((!keyboardSecondary && device == null) || playerIndex <= 0) return;
            for (int i = s_Roster.Count - 1; i >= 0; i--)
                if (s_Roster[i].PlayerIndex == playerIndex ||
                    (keyboardSecondary && s_Roster[i].IsKeyboardSecondary) ||
                    (!keyboardSecondary && device != null && s_Roster[i].DeviceId == device.deviceId))
                    s_Roster.RemoveAt(i);
            s_Roster.Add(new RosterEntry
            {
                PlayerIndex = playerIndex,
                ThemeIndex = themeIndex,
                DeviceId = device != null ? device.deviceId : -1,
                Device = device,
                IsKeyboardSecondary = keyboardSecondary,
            });
            s_Roster.Sort((a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
        }

        private static void RosterRemoveIndex(int playerIndex)
        {
            for (int i = s_Roster.Count - 1; i >= 0; i--)
                if (s_Roster[i].PlayerIndex == playerIndex) s_Roster.RemoveAt(i);
        }

        private static void RosterRemoveDevice(InputDevice device)
        {
            if (device == null) return;
            for (int i = s_Roster.Count - 1; i >= 0; i--)
                if (s_Roster[i].DeviceId == device.deviceId || s_Roster[i].Device == device) s_Roster.RemoveAt(i);
        }

        /// <summary>The roster entry's pad if it is still the same, connected device; otherwise null.</summary>
        private static Gamepad ResolvePad(RosterEntry e)
        {
            var pad = InputSystem.GetDeviceById(e.DeviceId) as Gamepad;
            if (pad == null || !pad.added || pad != e.Device) return null;
            return pad;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlayerJoined = null;
            PlayerLeft = null;
            RosterRestored = null;
            s_Roster.Clear();
        }

        private void Awake() => _host = GetComponent<ElfInput>();

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;

        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void Start()
        {
            if (!restoreRoster || s_Roster.Count == 0) return;
            if (_host == null) _host = GetComponent<ElfInput>();
            if (_host == null || _host.PlayerIndex != 0) return;
            _restorePending = true;
            StartCoroutine(RestoreRosterNextFrame());
        }

        /// <summary>
        /// Waits one frame so player 1's ElfController.Start has already moved it to PlayerSpawn, then re-spawns every
        /// roster entry whose pad is still connected (same slot, same sweater, same pad). Entries whose pad is gone are
        /// forgotten. Entries already present in this scene (pad claimed / slot taken) are skipped.
        /// </summary>
        private IEnumerator RestoreRosterNextFrame()
        {
            yield return null;
            _restorePending = false;
            if (_host == null || _host.PlayerIndex != 0) yield break;

            int restored = 0;
            RosterEntry[] entries = s_Roster.ToArray();
            for (int i = 0; i < entries.Length; i++)
            {
                RosterEntry e = entries[i];
                if (e.IsKeyboardSecondary)
                {
                    if (Keyboard.current == null)
                    {
                        RosterRemoveIndex(e.PlayerIndex);
                        continue;
                    }
                    if (ElfInput.AnyKeyboardSecondaryClaimed() || IsIndexUsed(e.PlayerIndex)) continue;
                    if (CountLocalElves() >= MaxLocalPlayers) break;
                    if (SpawnKeyboardElf(e.PlayerIndex, e.ThemeIndex, e.PlayerIndex - 1) != null) restored++;
                    continue;
                }

                Gamepad pad = ResolvePad(e);
                if (pad == null)
                {
                    RosterRemoveIndex(e.PlayerIndex);
                    continue;
                }
                if (ElfInput.IsGamepadClaimed(pad) || IsIndexUsed(e.PlayerIndex)) continue;
                if (CountLocalElves() >= MaxLocalPlayers) break;

                if (SpawnElf(pad, e.PlayerIndex, e.ThemeIndex, e.PlayerIndex - 1) != null) restored++;
            }

            if (restored > 0)
            {
                GameEvents.RaiseToast($"Эльфы на месте: {CountLocalElves()}");
                RosterRestored?.Invoke(restored);
            }
        }

        private void Update()
        {
            if (!allowJoin) return;
            if (_host == null) _host = GetComponent<ElfInput>();
            if (_host == null || !_host.isActiveAndEnabled || _host.PlayerIndex != 0) return;
            if (!PlayerGate.CanControl) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                // [F2]: Toggle Keyboard Player 2 (Arrows + Enter / RShift / RCtrl)
                if (kb.f2Key.wasPressedThisFrame)
                {
                    ElfInput existingKb = FindKeyboardSecondaryElf();
                    if (existingKb != null)
                    {
                        Leave(existingKb, "Эльф 2 (стрелки) покинул игру.");
                    }
                    else if (CountLocalElves() >= MaxLocalPlayers)
                    {
                        GameEvents.RaiseToast("Больше четырёх эльфов нельзя.");
                    }
                    else
                    {
                        JoinKeyboard();
                    }
                    return;
                }

                // [F3]: Toggle Split-Screen / Shared-Screen camera when 2+ players are active
                if (kb.f3Key.wasPressedThisFrame)
                {
                    if (CountLocalElves() < 2)
                    {
                        GameEvents.RaiseToast("Сначала добавьте второго эльфа ([F2] или геймпад [Start]/[A])!");
                    }
                    else
                    {
                        bool split = ThirdPersonCameraRig.ToggleSplitScreen();
                        GameEvents.RaiseToast(split
                            ? "Камера коопа: Разделённый экран [F3]"
                            : "Камера коопа: Общая камера [F3]");
                    }
                    return;
                }
            }

            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
            {
                Gamepad pad = pads[i];
                if (pad == null) continue;
                if (!pad.startButton.wasPressedThisFrame && !pad.buttonSouth.wasPressedThisFrame) continue;
                if (ElfInput.IsGamepadClaimed(pad)) continue;
                // That friend is about to be re-spawned from the roster in its old slot: not a new join.
                if (_restorePending && IsInRoster(pad)) continue;
                if (pad == _host.LastUsedGamepad) continue;
                // Player 1 has touched nothing but (maybe) this pad and has no keyboard: the press belongs to player 1.
                if (_host.LastUsedGamepad == null && !_host.HasUsedKeyboardMouse && Keyboard.current == null) continue;

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

        private static ElfInput FindKeyboardSecondaryElf()
        {
            var all = ElfInput.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].PlayerIndex > 0 && all[i].IsKeyboardSecondary) return all[i];
            return null;
        }

        private static int CountLocalElves()
        {
            int n = 0;
            var all = ElfInput.All;
            for (int i = 0; i < all.Count; i++) if (all[i] != null) n++;
            return n;
        }

        private static bool IsIndexUsed(int idx)
        {
            var all = ElfInput.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].PlayerIndex == idx) return true;
            return false;
        }

        /// <summary>
        /// Smallest player index in 1..3 that is neither on screen nor reserved by a roster entry whose pad is still
        /// connected (so a newcomer never steals a friend's slot), or -1.
        /// </summary>
        private static int FreePlayerIndex()
        {
            for (int idx = 1; idx < MaxLocalPlayers; idx++)
            {
                if (IsIndexUsed(idx) || IsIndexReserved(idx)) continue;
                return idx;
            }
            // Every free slot is reserved: fall back to any slot not on screen.
            for (int idx = 1; idx < MaxLocalPlayers; idx++)
                if (!IsIndexUsed(idx)) return idx;
            return -1;
        }

        private static bool IsIndexReserved(int idx)
        {
            for (int i = 0; i < s_Roster.Count; i++)
            {
                if (s_Roster[i].PlayerIndex != idx) continue;
                if (s_Roster[i].IsKeyboardSecondary && Keyboard.current != null) return true;
                if (!s_Roster[i].IsKeyboardSecondary && ResolvePad(s_Roster[i]) != null) return true;
            }
            return false;
        }

        /// <summary>Spawns an extra elf controlled by the secondary keyboard layout (Arrow keys + Enter / RShift).</summary>
        public GameObject JoinKeyboard()
        {
            if (ElfInput.AnyKeyboardSecondaryClaimed()) return null;
            int index = FreePlayerIndex();
            if (index < 0) return null;

            GameObject elf = SpawnKeyboardElf(index, index, 0);
            if (elf == null) return null;

            GameEvents.RaiseToast($"Эльф {index + 1} в игре! (Стрелки + Enter / RShift)");
            PlayerJoined?.Invoke(index, elf);
            return elf;
        }

        private GameObject SpawnKeyboardElf(int index, int themeIndex, int firstDirection)
        {
            if (index <= 0 || index >= MaxLocalPlayers) return null;
            if (_host != null) _host.SuppressPresses();

            Vector3 pos = FindSpawnPoint(firstDirection);
            Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            GameObject elf = PlayerSetup.CreateKeyboardPlayer(pos, themeIndex, index);
            elf.transform.rotation = rot;
            StartCoroutine(PlaceAfterStart(elf, pos, rot));

            RosterAdd(index, themeIndex, null, true);
            return elf;
        }

        /// <summary>Spawns an extra elf for <paramref name="pad"/> next to player 1. Returns null if no slot is free.</summary>
        public GameObject Join(Gamepad pad)
        {
            if (pad == null || ElfInput.IsGamepadClaimed(pad)) return null;
            int index = FreePlayerIndex();
            if (index < 0) return null;

            GameObject elf = SpawnElf(pad, index, index, 0);
            if (elf == null) return null;

            GameEvents.RaiseToast($"Эльф {index + 1} присоединился!");
            PlayerJoined?.Invoke(index, elf);
            return elf;
        }

        /// <summary>Creates the extra elf next to player 1 and records it in the roster (no toast, no event).</summary>
        private GameObject SpawnElf(Gamepad pad, int index, int themeIndex, int firstDirection)
        {
            if (pad == null || index <= 0 || index >= MaxLocalPlayers) return null;

            // The Start/South press also reached player 1 (it listens to unclaimed pads): no pause/jump this frame.
            if (_host != null) _host.SuppressPresses();

            Vector3 pos = FindSpawnPoint(firstDirection);
            Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            GameObject elf = PlayerSetup.CreatePlayer(pos, themeIndex, index, pad);
            elf.transform.rotation = rot;
            StartCoroutine(PlaceAfterStart(elf, pos, rot));

            RosterAdd(index, themeIndex, pad);
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

        /// <param name="firstDirection">Which side to try first (0 right, 1 left, 2 behind, 3 front), so several
        /// elves restored in the same frame do not all land on the same spot.</param>
        private Vector3 FindSpawnPoint(int firstDirection = 0)
        {
            Transform t = transform;
            Vector3 origin = t.position + Vector3.up * 0.05f;
            Vector3[] dirs = { t.right, -t.right, -t.forward, t.forward };
            int mask = BuildBlockMask();
            float r = PlayerSetup.ControllerRadius;
            float h = PlayerSetup.ControllerHeight;
            int start = ((firstDirection % dirs.Length) + dirs.Length) % dirs.Length;
            for (int k = 0; k < dirs.Length; k++)
            {
                Vector3 d = dirs[(start + k) % dirs.Length];
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
            // Also forget a remembered friend whose elf is not in this scene (e.g. pad pulled during a load).
            RosterRemoveDevice(device);
        }

        /// <summary>
        /// Removes an extra elf (never player 1) and forgets it in the roster. Held item and sack contents are dropped
        /// where it stood.
        /// </summary>
        public static void Leave(ElfInput elf, string customToast = null)
        {
            if (elf == null || elf.PlayerIndex == 0) return;
            int index = elf.PlayerIndex;
            GameObject go = elf.gameObject;

            RosterRemoveIndex(index);
            if (elf.AssignedGamepad != null) RosterRemoveDevice(elf.AssignedGamepad);

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
            GameEvents.RaiseToast(customToast ?? $"Эльф {index + 1} покинул игру: геймпад отключён.");
        }
    }
}
