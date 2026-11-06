using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilentNight.Core
{
    /// <summary>
    /// The game's spine. Lives in the Boot scene (DontDestroyOnLoad), owns the state machine and scene loading.
    /// Flow: Boot → MainMenu (Hub scene + menu UI) → Hub → Delivery (House scene) → Results/GameOver → Hub ...
    /// Other modules never load scenes themselves: they raise GameEvents.Request* and GameFlow reacts.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameFlow : MonoBehaviour
    {
        public GameState State { get; private set; } = GameState.Boot;
        public bool IsLoading { get; private set; }

        private GameState _stateBeforePause;
        private readonly List<float> _deliveredIntegrity = new List<float>();

        private void Awake()
        {
            if (GameServices.Flow != null && GameServices.Flow != this)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);
            GameServices.RegisterFlow(this);
            GameServices.RegisterProfile(PlayerProfile.Load());
            GameServices.RegenerateOrders();

            GameEvents.RequestStartDelivery += OnRequestStartDelivery;
            GameEvents.RequestReturnToHub += OnRequestReturnToHub;
            GameEvents.GiftDelivered += OnGiftDelivered;
            GameEvents.PlayerCaught += OnPlayerCaught;
        }

        private void OnDestroy()
        {
            GameEvents.RequestStartDelivery -= OnRequestStartDelivery;
            GameEvents.RequestReturnToHub -= OnRequestReturnToHub;
            GameEvents.GiftDelivered -= OnGiftDelivered;
            GameEvents.PlayerCaught -= OnPlayerCaught;
        }

        private void Start()
        {
            // If the game was started from the Boot scene, go to the hub with the main menu on top.
            if (SceneManager.GetActiveScene().name == GameConstants.SceneBoot)
                StartCoroutine(LoadScene(GameConstants.SceneHub, GameState.MainMenu));
            else
                // Started directly from Hub/House in the editor: infer state.
                SetState(SceneManager.GetActiveScene().name == GameConstants.SceneHouse ? GameState.Delivery : GameState.MainMenu);
        }

        // ---------------- public API (also usable from UI) ----------------

        /// <summary>Main menu "Start shift" button.</summary>
        public void StartShift()
        {
            if (State == GameState.MainMenu) SetState(GameState.Hub);
        }

        public void TogglePause()
        {
            if (State == GameState.Paused)
            {
                Time.timeScale = 1f;
                SetState(_stateBeforePause);
            }
            else if (State == GameState.Hub || State == GameState.Delivery)
            {
                _stateBeforePause = State;
                Time.timeScale = 0f;
                SetState(GameState.Paused);
            }
        }

        public void QuitGame()
        {
            GameServices.Profile?.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------- event handlers ----------------

        private void OnRequestStartDelivery(Order order)
        {
            if (IsLoading || (State != GameState.Hub && State != GameState.MainMenu)) return;
            GameServices.ActiveOrder = order ?? GameServices.ActiveOrder;
            // If the player launched without loading the sleigh, bring the order's gifts anyway (prototype behaviour).
            if (GameServices.LoadedGifts.Count == 0 && GameServices.ActiveOrder != null)
                GameServices.LoadedGifts.AddRange(GameServices.ActiveOrder.Gifts);
            _deliveredIntegrity.Clear();
            StartCoroutine(LoadScene(GameConstants.SceneHouse, GameState.Delivery));
        }

        private void OnRequestReturnToHub()
        {
            if (IsLoading) return;
            Time.timeScale = 1f;
            GameServices.LoadedGifts.Clear();
            StartCoroutine(LoadScene(GameConstants.SceneHub, GameState.Hub));
        }

        private void OnGiftDelivered(ICarryable gift, int delivered, int total)
        {
            if (State != GameState.Delivery) return;
            _deliveredIntegrity.Add(gift != null ? gift.Integrity : 100f);
            if (delivered >= total) CompleteDelivery();
        }

        private void OnPlayerCaught(string reason)
        {
            if (State != GameState.Delivery) return;
            var p = GameServices.Profile;
            if (p != null)
            {
                p.GadgetDurability = Mathf.Max(25f, p.GadgetDurability - 20f);
                p.NotifyChanged();
                p.Save();
            }
            SetState(GameState.GameOver);
        }

        private void CompleteDelivery()
        {
            var order = GameServices.ActiveOrder;
            float avg = 100f;
            if (_deliveredIntegrity.Count > 0)
            {
                float sum = 0f;
                foreach (var v in _deliveredIntegrity) sum += v;
                avg = sum / _deliveredIntegrity.Count;
            }
            int earned = order != null ? Mathf.RoundToInt(order.BaseReward * (avg / 100f) * order.TipMultiplier) : 0;

            var p = GameServices.Profile;
            if (p != null)
            {
                p.Tips += earned;
                p.CompletedRuns++;
                p.NotifyChanged();
                p.Save();
            }

            SetState(GameState.Results);
            GameEvents.RaiseDeliveryCompleted(order, avg, earned);
            GameServices.RegenerateOrders();
        }

        // ---------------- internals ----------------

        private void SetState(GameState next)
        {
            if (next == State) return;
            var prev = State;
            State = next;
            bool gameplay = next == GameState.Hub || next == GameState.Delivery;
            Cursor.lockState = gameplay ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !gameplay;
            GameEvents.RaiseStateChanged(prev, next);
        }

        private IEnumerator LoadScene(string sceneName, GameState stateAfter)
        {
            IsLoading = true;
            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                Debug.LogError($"[GameFlow] Scene '{sceneName}' is not in Build Settings. Run menu: Silent Night ▸ Build Greybox Scenes.");
                IsLoading = false;
                yield break;
            }
            while (!op.isDone) yield return null;
            IsLoading = false;
            SetState(stateAfter);
        }
    }
}
