using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Hub conveyor belt. Spawns the active order's gifts one by one at its start point and pushes
    /// gifts lying on it along transform.forward. Re-checks GameServices.ActiveOrder every few seconds:
    /// when the order changes, old (unheld) gifts are removed, LoadedGifts is reset, and the new order is spawned.
    /// Lost gifts (fell off the world / destroyed) are re-spawned automatically.
    /// </summary>
    public class Conveyor : MonoBehaviour
    {
        [Header("Belt (local space, belt runs along +Z)")]
        [SerializeField] private float beltLength = 6f;
        [SerializeField] private float beltWidth = 1.2f;
        [SerializeField] private float beltHeight = 0.8f;
        [SerializeField] private float beltSpeed = 1.2f;
        [Tooltip("Spawn point. If null, one is created at the -Z end, above the belt.")]
        [SerializeField] private Transform startPoint;

        [Header("Spawning")]
        [SerializeField] private float spawnInterval = 1.6f;
        [SerializeField] private float recheckInterval = 2f;
        [SerializeField] private float clearRadiusSmall = 0.35f;
        [SerializeField] private float clearRadiusHuge = 0.95f;

        [Header("Visual")]
        [Tooltip("Optional real model for the belt. If null and the object has no renderers, a placeholder is built.")]
        [SerializeField] private GameObject modelPrefab;
        [SerializeField] private bool buildPlaceholder = true;
        [SerializeField] private Color beltColor = new Color32(0x2d, 0x2d, 0x33, 0xff);
        [SerializeField] private Color frameColor = new Color32(0x8b, 0x5a, 0x2b, 0xff);
        [SerializeField] private Color slatColor = new Color32(0x55, 0x55, 0x60, 0xff);
        [SerializeField] private int slatCount = 8;

        private Order _order;
        private bool _orderInitialised;
        private readonly List<Gift> _spawned = new List<Gift>(8);
        private readonly int[] _need = new int[8];
        private readonly Collider[] _overlap = new Collider[16];
        private float _recheckTimer;
        private float _spawnTimer;
        private Transform[] _slats;
        private float _slatOffset;

        private void Awake()
        {
            if (startPoint == null)
            {
                startPoint = new GameObject("ConveyorStart").transform;
                startPoint.SetParent(transform, false);
                startPoint.localPosition = new Vector3(0f, beltHeight + 0.05f, -beltLength * 0.5f + 0.6f);
            }

            // Trigger volume above the belt surface that pushes rigidbodies.
            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, beltHeight + 0.4f, 0f);
            trigger.size = new Vector3(beltWidth, 0.8f, beltLength);

            BuildVisual();
        }

        private void OnEnable() => GameEvents.StateChanged += OnStateChanged;
        private void OnDisable() => GameEvents.StateChanged -= OnStateChanged;

        private void Start() => Recheck();

        private void OnStateChanged(GameState prev, GameState next)
        {
            if (next == GameState.Hub || next == GameState.MainMenu) _recheckTimer = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            _recheckTimer -= dt;
            if (_recheckTimer <= 0f)
            {
                _recheckTimer = recheckInterval;
                Recheck();
            }

            // In co-op, only the Host spawns gifts on the belt to prevent duplicate/desynced presents
            bool isClient = SilentNight.Multiplayer.CoopNetworkManager.Instance != null &&
                            SilentNight.Multiplayer.CoopNetworkManager.Instance.IsConnectedClient;

            if (!isClient)
            {
                _spawnTimer -= dt;
                if (_spawnTimer <= 0f && TrySpawnNext()) _spawnTimer = spawnInterval;
            }

            AnimateSlats(dt);
        }

        /// <summary>Force a full respawn of the active order's remaining gifts.</summary>
        public void Refill()
        {
            ClearSpawned(includeCarried: false);
            _spawnTimer = 0f;
            Recheck();
        }

        private void Recheck()
        {
            var order = GameServices.ActiveOrder;
            if (!_orderInitialised || order != _order)
            {
                bool realChange = _orderInitialised;
                _orderInitialised = true;
                _order = order;
                if (realChange)
                {
                    // Gifts loaded for a previous order don't count for the new one.
                    GameServices.LoadedGifts.Clear();
                    GameEvents.RaiseGiftLoaded(0, order != null ? order.Gifts.Count : 0);
                    ClearSpawned(includeCarried: false);
                }
                _spawnTimer = 0f;
            }
            ComputeNeed();
        }

        private void ComputeNeed()
        {
            System.Array.Clear(_need, 0, _need.Length);
            if (_order == null) return;

            var gifts = _order.Gifts;
            for (int i = 0; i < gifts.Count; i++) _need[(int)gifts[i]]++;
            var loaded = GameServices.LoadedGifts;
            for (int i = 0; i < loaded.Count; i++) _need[(int)loaded[i]]--;

            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var g = _spawned[i];
                if (g == null) { _spawned.RemoveAt(i); continue; } // loaded into the sleigh or lost
                _need[(int)g.Kind]--;
            }
        }

        private bool TrySpawnNext()
        {
            if (_order == null || startPoint == null) return false;
            ComputeNeed();

            // Spawn in the order's listed sequence.
            var gifts = _order.Gifts;
            for (int i = 0; i < gifts.Count; i++)
            {
                var kind = gifts[i];
                if (_need[(int)kind] <= 0) continue;

                float radius = kind == GiftKind.Huge ? clearRadiusHuge : clearRadiusSmall;
                Vector3 pos = startPoint.position + Vector3.up * (kind == GiftKind.Huge ? 0.6f : 0.3f);
                if (!IsClear(pos, radius)) return false;

                // Bike's long side (local X) along the belt.
                Quaternion rot = kind == GiftKind.Huge ? transform.rotation * Quaternion.Euler(0f, 90f, 0f) : transform.rotation;
                var net = SilentNight.Multiplayer.CoopNetworkManager.Instance;
                int netId = net != null ? net.GetNextGiftNetId() : -1;
                var gift = GiftFactory.Create(kind, pos, rot, netId);
                if (net != null && net.Role == SilentNight.Multiplayer.CoopNetworkManager.NetRole.Host)
                {
                    net.BroadcastGiftSpawn(gift);
                }
                _spawned.Add(gift);
                _need[(int)kind]--;
                return true;
            }
            return false;
        }

        private bool IsClear(Vector3 pos, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(pos, radius, _overlap, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = _overlap[i].attachedRigidbody;
                if (rb != null && rb.GetComponent<Gift>() != null) return false;
            }
            return true;
        }

        private void ClearSpawned(bool includeCarried)
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var g = _spawned[i];
                if (g == null) { _spawned.RemoveAt(i); continue; }
                if (!includeCarried && (g.IsHeld || g.IsInSack)) continue;
                Destroy(g.gameObject);
                _spawned.RemoveAt(i);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;
            if (rb.GetComponent<Gift>() == null) return; // only gifts ride the belt

            Vector3 dir = transform.forward;
            Vector3 v = rb.linearVelocity;
            float along = Vector3.Dot(v, dir);
            // Replace the along-belt component with belt speed, damp sideways drift a bit.
            Vector3 side = Vector3.ProjectOnPlane(v, dir);
            side.x *= 0.9f; side.z *= 0.9f;
            rb.linearVelocity = side + dir * Mathf.MoveTowards(along, beltSpeed, 10f * Time.fixedDeltaTime);
        }

        // ---------------- visuals ----------------

        private void BuildVisual()
        {
            if (modelPrefab != null)
            {
                Instantiate(modelPrefab, transform, false);
                return;
            }
            if (!buildPlaceholder || GetComponentInChildren<Renderer>() != null) return;

            Material belt = GiftVisual.Mat(beltColor, 0f, 0.2f), frame = GiftVisual.Mat(frameColor), slat = GiftVisual.Mat(slatColor, 0.3f, 0.5f);

            // Solid belt body (keeps its collider so gifts rest on it).
            Box("Belt", belt, new Vector3(0f, beltHeight * 0.5f, 0f), new Vector3(beltWidth, beltHeight, beltLength), true);
            // Side rails + end stopper.
            float railX = beltWidth * 0.5f + 0.05f;
            Box("RailL", frame, new Vector3(-railX, beltHeight + 0.08f, 0f), new Vector3(0.1f, 0.16f, beltLength), true);
            Box("RailR", frame, new Vector3(railX, beltHeight + 0.08f, 0f), new Vector3(0.1f, 0.16f, beltLength), true);
            Box("Stopper", frame, new Vector3(0f, beltHeight + 0.12f, beltLength * 0.5f - 0.05f), new Vector3(beltWidth, 0.24f, 0.1f), true);

            // Moving slats (visual only).
            _slats = new Transform[Mathf.Max(0, slatCount)];
            for (int i = 0; i < _slats.Length; i++)
                _slats[i] = Box("Slat", slat, Vector3.zero, new Vector3(beltWidth * 0.96f, 0.02f, 0.12f), false);
            AnimateSlats(0f);
        }

        private Transform Box(string name, Material m, Vector3 localPos, Vector3 localScale, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) { c.enabled = false; Destroy(c); }
            }
            var t = go.transform;
            t.SetParent(transform, false);
            t.localPosition = localPos;
            t.localScale = localScale;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && m != null) mr.sharedMaterial = m;
            return t;
        }

        private void AnimateSlats(float dt)
        {
            if (_slats == null || _slats.Length == 0) return;
            _slatOffset = Mathf.Repeat(_slatOffset + beltSpeed * dt, beltLength);
            float spacing = beltLength / _slats.Length;
            for (int i = 0; i < _slats.Length; i++)
            {
                if (_slats[i] == null) continue;
                float z = Mathf.Repeat(i * spacing + _slatOffset, beltLength) - beltLength * 0.5f;
                _slats[i].localPosition = new Vector3(0f, beltHeight + 0.011f, z);
            }
        }
    }
}
