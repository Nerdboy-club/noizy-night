using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SilentNight.Core;
using SilentNight.Gifts;
using SilentNight.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SilentNight.Multiplayer
{
    /// <summary>
    /// Self-contained Online & LAN Host/Client Co-op Manager (UDP 7777 + LAN Broadcast 7778 + UPnP Port Mapper).
    /// Supports:
    /// - Hosting directly from your PC (LAN, Radmin VPN / ZeroTier / Tailscale, and direct Internet IP on UDP 7777).
    /// - Connecting by IP or 1-click Auto-Discovery on LAN / VPN.
    /// - Full scene synchronization (Hub -> Rooftop -> House), ActiveOrder & Sleigh gifts sync,
    ///   4-player Elf avatars with procedural ragdoll / walking / crouching / sprinting / knockdown & HelpUp ([E]).
    /// - Toggle the Co-op window anytime with [F4] or the top-center status button.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CoopNetworkManager : MonoBehaviour
    {
        public const int DefaultGamePort = 7777;
        public const int DiscoveryPort = 7778;
        private const float TickRate = 25f;
        private const float BroadcastInterval = 1.25f;
        private const float ClientTimeoutSeconds = 8.0f;

        public enum NetRole
        {
            Offline,
            Host,
            Client
        }

        public static CoopNetworkManager Instance { get; private set; }

        public NetRole Role { get; private set; } = NetRole.Offline;
        public int Port { get; private set; } = DefaultGamePort;
        public int LocalSlot { get; private set; } = 0; // 0 = Host, 1..3 = Client
        public bool IsConnectedClient => Role == NetRole.Client && _clientWelcomed;
        public bool WindowOpen { get; set; }
        public IReadOnlyList<string> LocalIps => _localIps;
        public string PublicIp => _publicIp;
        public int ConnectedPlayerCount => Role == NetRole.Host ? (_clientsBySlot.Count + 1) : (Role == NetRole.Client && _clientWelcomed ? 2 : 1);
        public string StatusMessage => _statusMessage;

        private string _connectIpInput = "127.0.0.1";
        private string _portInput = "7777";
        private string _statusMessage = "Готов к запуску сервера или подключению.";
        private string _upnpStatus = "";
        private string _publicIp = "";
        private readonly List<string> _localIps = new List<string>();
        private string _discoveredHostIp = "";
        private int _discoveredHostPort = DefaultGamePort;
        private int _discoveredHostPlayers = 0;
        private string _discoveredHostScene = "";

        private UdpClient _udp;
        private UdpClient _discoveryUdp;
        private Thread _recvThread;
        private Thread _discoveryThread;
        private volatile bool _running;

        private struct IncomingPacket
        {
            public IPEndPoint Sender;
            public string Payload;
        }

        private readonly ConcurrentQueue<IncomingPacket> _inbox = new ConcurrentQueue<IncomingPacket>();

        private sealed class ConnectedClient
        {
            public int Slot;
            public IPEndPoint EndPoint;
            public float LastSeenTime;
            public Vector3 Pos;
            public float Yaw;
            public Vector3 PlanarVel;
            public float VerticalVel;
            public bool Grounded = true;
            public bool Crouching;
            public bool Sprinting;
            public bool Mantling;
            public bool Jetpacking;
            public float Stamina01 = 1f;
            public bool KnockedDown;
            public bool HasState;
        }

        private readonly Dictionary<int, ConnectedClient> _clientsBySlot = new Dictionary<int, ConnectedClient>();
        private readonly Dictionary<string, int> _slotByEndpoint = new Dictionary<string, int>();

        // Remote avatars spawned in the current scene indexed by slot (0..3)
        private readonly Dictionary<int, GameObject> _remoteAvatars = new Dictionary<int, GameObject>();

        // Client state
        private IPEndPoint _hostEndPoint;
        private bool _clientWelcomed;
        private float _lastHelloSentTime;
        private float _lastPacketFromHostTime;
        private float _sendAccumulator;
        private float _broadcastAccumulator;
        private string _pendingSceneLoad;
        private float _sceneLoadBlockUntil;

        // UI styles
        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _btnPrimaryStyle;
        private GUIStyle _btnSecondaryStyle;
        private GUIStyle _textFieldStyle;
        private Texture2D _bgTex;
        private Texture2D _cardTex;
        private Texture2D _btnRedTex;
        private Texture2D _btnWoodTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("CoopNetworkManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CoopNetworkManager>();
        }

        public static void ToggleWindow()
        {
            if (Instance == null) AutoCreate();
            if (Instance != null)
            {
                Instance.WindowOpen = !Instance.WindowOpen;
                Instance.ApplyModalCursorState();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;

            RefreshLocalIpList();
            StartDiscoveryListener();

            SceneManager.sceneLoaded += OnSceneLoaded;
            ElfHelpUp.PlayerHelpedUp += OnLocalPlayerHelpedUp;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ElfHelpUp.PlayerHelpedUp -= OnLocalPlayerHelpedUp;
            StopNetwork();
            StopDiscoveryListener();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _remoteAvatars.Clear();
            _pendingSceneLoad = null;

            // If we are a client and already assigned a slot (1..3), apply our Elf theme/model immediately
            if (Role == NetRole.Client && LocalSlot > 0)
            {
                ApplyLocalPlayerSlotTheme(LocalSlot);
                StartCoroutine(OffsetLocalPlayerSpawnRoutine());
            }
        }

        private System.Collections.IEnumerator OffsetLocalPlayerSpawnRoutine()
        {
            yield return null;
            yield return null;
            ElfInput primary = ElfInput.Primary;
            if (primary != null)
            {
                Vector3 offset = primary.transform.right * (LocalSlot * 1.5f);
                var ctrl = primary.GetComponent<ElfController>();
                if (ctrl != null) ctrl.Teleport(primary.transform.position + offset, primary.transform.rotation);
                else primary.transform.position += offset;
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f4Key.wasPressedThisFrame)
            {
                WindowOpen = !WindowOpen;
                ApplyModalCursorState();
            }

            DrainInbox();

            float dt = Time.unscaledDeltaTime;
            if (Role == NetRole.Host)
            {
                UpdateHost(dt);
            }
            else if (Role == NetRole.Client)
            {
                UpdateClient(dt);
            }
        }

        private void ApplyModalCursorState()
        {
            if (WindowOpen)
            {
                GameServices.UIModalOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                GameServices.UIModalOpen = false;
                if (GameServices.Flow != null &&
                    (GameServices.Flow.State == GameState.Hub ||
                     GameServices.Flow.State == GameState.Rooftop ||
                     GameServices.Flow.State == GameState.Delivery))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        // =====================================================================
        // PUBLIC HOST / CLIENT API
        // =====================================================================

        public void StartHost(int port = DefaultGamePort)
        {
            StopNetwork();
            RefreshLocalIpList();
            Port = Mathf.Clamp(port, 1024, 65535);
            _portInput = Port.ToString(CultureInfo.InvariantCulture);

            try
            {
                _udp = new UdpClient(Port);
                _udp.Client.ReceiveBufferSize = 262144;
                _udp.Client.SendBufferSize = 262144;
                try
                {
                    // Prevent Windows UDP WSAECONNRESET (10054) from killing the socket when a client closes
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { /* non-Windows or unsupported */ }

                _running = true;
                Role = NetRole.Host;
                LocalSlot = 0;
                ApplyLocalPlayerSlotTheme(0);

                _recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "SN_CoopHostRecv" };
                _recvThread.Start();

                if (GameServices.Flow != null && GameServices.Flow.State == GameState.MainMenu)
                {
                    GameServices.Flow.StartShift();
                }

                _statusMessage = $"Сервер запущен на UDP :{Port}! Ждём друзей...";
                GameEvents.RaiseToast($"Кооп-сервер запущен (порт {Port})! Друзья могут заходить.");

                // Try UPnP port forwarding + Public IP lookup in background for Internet play
                ThreadPool.QueueUserWorkItem(_ => TrySetupUpnpAndPublicIp(Port));
            }
            catch (Exception ex)
            {
                _statusMessage = "Ошибка запуска порта " + Port + ": " + ex.Message;
                Role = NetRole.Offline;
            }
        }

        public void StartClient(string hostIp, int port = DefaultGamePort)
        {
            StopNetwork();
            if (string.IsNullOrWhiteSpace(hostIp)) hostIp = "127.0.0.1";
            hostIp = hostIp.Trim();
            _connectIpInput = hostIp;
            Port = Mathf.Clamp(port, 1024, 65535);
            _portInput = Port.ToString(CultureInfo.InvariantCulture);

            try
            {
                if (!IPAddress.TryParse(hostIp, out IPAddress addr))
                {
                    IPAddress[] resolved = Dns.GetHostAddresses(hostIp);
                    addr = resolved != null && resolved.Length > 0 ? resolved[0] : IPAddress.Loopback;
                }

                _hostEndPoint = new IPEndPoint(addr, Port);
                _udp = new UdpClient(0);
                _udp.Client.ReceiveBufferSize = 262144;
                _udp.Client.SendBufferSize = 262144;
                try
                {
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { }

                _running = true;
                Role = NetRole.Client;
                LocalSlot = 1;
                _clientWelcomed = false;
                _lastHelloSentTime = -10f;
                _lastPacketFromHostTime = Time.unscaledTime;

                _recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "SN_CoopClientRecv" };
                _recvThread.Start();

                if (GameServices.Flow != null && GameServices.Flow.State == GameState.MainMenu)
                {
                    GameServices.Flow.StartShift();
                }

                _statusMessage = $"Подключение к {hostIp}:{Port}...";
                GameEvents.RaiseToast($"Подключаемся к {hostIp}:{Port}...");
            }
            catch (Exception ex)
            {
                _statusMessage = "Ошибка подключения: " + ex.Message;
                Role = NetRole.Offline;
            }
        }

        public void StopNetwork()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            _udp = null;

            foreach (var kv in _remoteAvatars)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _remoteAvatars.Clear();
            _clientsBySlot.Clear();
            _slotByEndpoint.Clear();

            while (_inbox.TryDequeue(out _)) { }

            if (Role != NetRole.Offline)
            {
                Role = NetRole.Offline;
                LocalSlot = 0;
                _clientWelcomed = false;
                ApplyLocalPlayerSlotTheme(0);
                _statusMessage = "Сеть отключена.";
            }
        }

        // =====================================================================
        // LAN / VPN AUTO-DISCOVERY (UDP 7778)
        // =====================================================================

        private void StartDiscoveryListener()
        {
            try
            {
                _discoveryUdp = new UdpClient();
                _discoveryUdp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _discoveryUdp.ExclusiveAddressUse = false;
                _discoveryUdp.EnableBroadcast = true;
                _discoveryUdp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

                _discoveryThread = new Thread(DiscoveryLoop) { IsBackground = true, Name = "SN_CoopDiscovery" };
                _discoveryThread.Start();
            }
            catch
            {
                // Port 7778 may already be bound by another instance on the same PC; that's fine.
            }
        }

        private void StopDiscoveryListener()
        {
            try { _discoveryUdp?.Close(); } catch { }
            _discoveryUdp = null;
        }

        private void DiscoveryLoop()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_discoveryUdp != null)
            {
                try
                {
                    byte[] data = _discoveryUdp.Receive(ref remote);
                    if (data == null || data.Length == 0) continue;
                    string msg = Encoding.UTF8.GetString(data);
                    if (msg.StartsWith("SN_DISCOVER", StringComparison.Ordinal))
                    {
                        if (Role == NetRole.Host && _udp != null)
                        {
                            string reply = $"SN_HOST|{Port}|{_clientsBySlot.Count + 1}|{SceneManager.GetActiveScene().name}";
                            byte[] bytes = Encoding.UTF8.GetBytes(reply);
                            _discoveryUdp.Send(bytes, bytes.Length, remote);
                        }
                    }
                    else if (msg.StartsWith("SN_HOST|", StringComparison.Ordinal))
                    {
                        string[] parts = msg.Split('|');
                        if (parts.Length >= 4 && int.TryParse(parts[1], out int p))
                        {
                            _discoveredHostIp = remote.Address.ToString();
                            _discoveredHostPort = p;
                            int.TryParse(parts[2], out _discoveredHostPlayers);
                            _discoveredHostScene = parts[3];
                        }
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        public void BroadcastLanDiscoveryProbe()
        {
            try
            {
                using (var probe = new UdpClient())
                {
                    probe.EnableBroadcast = true;
                    byte[] bytes = Encoding.UTF8.GetBytes("SN_DISCOVER");
                    probe.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

                    // Also probe common local / VPN / loopback addresses
                    probe.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
                    for (int i = 0; i < _localIps.Count; i++)
                    {
                        string ip = _localIps[i];
                        int lastDot = ip.LastIndexOf('.');
                        if (lastDot > 0)
                        {
                            string subnetBroadcast = ip.Substring(0, lastDot + 1) + "255";
                            if (IPAddress.TryParse(subnetBroadcast, out IPAddress bcast))
                                probe.Send(bytes, bytes.Length, new IPEndPoint(bcast, DiscoveryPort));
                        }
                    }
                }
                _statusMessage = "Отправлен поиск серверов в локальной сети / VPN...";
            }
            catch (Exception ex)
            {
                _statusMessage = "Ошибка поиска: " + ex.Message;
            }
        }

        private void BroadcastHostBeacon()
        {
            if (_discoveryUdp == null) return;
            try
            {
                string msg = $"SN_HOST|{Port}|{_clientsBySlot.Count + 1}|{SceneManager.GetActiveScene().name}";
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                _discoveryUdp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch { }
        }

        // =====================================================================
        // SOCKET RECEIVE & PACKET DISPATCH
        // =====================================================================

        private void ReceiveLoop()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running && _udp != null)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    if (data == null || data.Length == 0) continue;
                    string payload = Encoding.UTF8.GetString(data);
                    _inbox.Enqueue(new IncomingPacket
                    {
                        Sender = new IPEndPoint(remote.Address, remote.Port),
                        Payload = payload
                    });
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void SendRaw(string payload, IPEndPoint target)
        {
            if (_udp == null || target == null) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                _udp.Send(bytes, bytes.Length, target);
            }
            catch { }
        }

        private void DrainInbox()
        {
            int safety = 256;
            while (safety-- > 0 && _inbox.TryDequeue(out IncomingPacket pkt))
            {
                if (string.IsNullOrEmpty(pkt.Payload)) continue;
                if (Role == NetRole.Host) HandleHostPacket(pkt.Sender, pkt.Payload);
                else if (Role == NetRole.Client) HandleClientPacket(pkt.Sender, pkt.Payload);
            }
        }

        // =====================================================================
        // HOST LOGIC
        // =====================================================================

        private void UpdateHost(float dt)
        {
            // Remove timed-out clients
            float now = Time.unscaledTime;
            List<int> expired = null;
            foreach (var kv in _clientsBySlot)
            {
                if (now - kv.Value.LastSeenTime > ClientTimeoutSeconds)
                {
                    if (expired == null) expired = new List<int>();
                    expired.Add(kv.Key);
                }
            }
            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                {
                    int slot = expired[i];
                    if (_clientsBySlot.TryGetValue(slot, out ConnectedClient c))
                    {
                        _slotByEndpoint.Remove(c.EndPoint.ToString());
                        _clientsBySlot.Remove(slot);
                    }
                    if (_remoteAvatars.TryGetValue(slot, out GameObject av) && av != null)
                        Destroy(av);
                    _remoteAvatars.Remove(slot);
                    GameEvents.RaiseToast($"Эльф {slot + 1} отключился.");
                }
            }

            // Apply client states to their remote avatars on Host
            foreach (var kv in _clientsBySlot)
            {
                ConnectedClient c = kv.Value;
                if (!c.HasState) continue;
                GameObject avatar = EnsureRemoteAvatar(c.Slot, c.Pos);
                if (avatar != null)
                {
                    ApplyStateToAvatar(avatar, c.Pos, c.Yaw, c.PlanarVel, c.VerticalVel,
                        c.Grounded, c.Crouching, c.Sprinting, c.Mantling, c.Jetpacking, c.Stamina01, c.KnockedDown);
                }
            }

            _sendAccumulator += dt;
            if (_sendAccumulator >= 1f / TickRate)
            {
                _sendAccumulator = 0f;
                BroadcastWorldStateToClients();
            }

            _broadcastAccumulator += dt;
            if (_broadcastAccumulator >= BroadcastInterval)
            {
                _broadcastAccumulator = 0f;
                BroadcastHostBeacon();
            }
        }

        private void HandleHostPacket(IPEndPoint sender, string payload)
        {
            string key = sender.ToString();
            string[] lines = payload.Split('\n');
            if (lines.Length == 0) return;
            string header = lines[0];

            if (header.StartsWith("HELLO", StringComparison.Ordinal))
            {
                if (!_slotByEndpoint.TryGetValue(key, out int slot))
                {
                    slot = AllocateClientSlot();
                    if (slot < 0)
                    {
                        SendRaw("FULL", sender);
                        return;
                    }
                    var client = new ConnectedClient
                    {
                        Slot = slot,
                        EndPoint = sender,
                        LastSeenTime = Time.unscaledTime,
                        Pos = GetLocalElfPosition() + new Vector3(0.65f * slot, 0f, 0f)
                    };
                    _clientsBySlot[slot] = client;
                    _slotByEndpoint[key] = slot;
                    GameEvents.RaiseToast($"Эльф {slot + 1} подключился к смене!");
                    _statusMessage = $"Игроков в сети: {_clientsBySlot.Count + 1}/4 (последний: {sender.Address})";
                }
                else if (_clientsBySlot.TryGetValue(slot, out ConnectedClient existing))
                {
                    existing.LastSeenTime = Time.unscaledTime;
                }

                SendRaw($"WELCOME|{slot}|{SceneManager.GetActiveScene().name}", sender);
                return;
            }

            if (!_slotByEndpoint.TryGetValue(key, out int clientSlot) ||
                !_clientsBySlot.TryGetValue(clientSlot, out ConnectedClient cl))
            {
                return;
            }

            cl.LastSeenTime = Time.unscaledTime;

            if (header.StartsWith("CSTATE|", StringComparison.Ordinal))
            {
                // CSTATE|slot|x|y|z|yaw|vx|vy|vz| flags |stamina
                string[] p = header.Split('|');
                if (p.Length >= 11)
                {
                    cl.Pos = new Vector3(ParseF(p[2]), ParseF(p[3]), ParseF(p[4]));
                    cl.Yaw = ParseF(p[5]);
                    cl.PlanarVel = new Vector3(ParseF(p[6]), 0f, ParseF(p[8]));
                    cl.VerticalVel = ParseF(p[7]);
                    int flags = ParseI(p[9]);
                    cl.Grounded = (flags & 1) != 0;
                    cl.Crouching = (flags & 2) != 0;
                    cl.Sprinting = (flags & 4) != 0;
                    cl.Mantling = (flags & 8) != 0;
                    cl.Jetpacking = (flags & 16) != 0;
                    cl.KnockedDown = (flags & 32) != 0;
                    cl.Stamina01 = ParseF(p[10]);
                    cl.HasState = true;
                }
            }
            else if (header.StartsWith("EVENT|", StringComparison.Ordinal))
            {
                HandleNetworkEventOnHost(header);
            }
        }

        private int AllocateClientSlot()
        {
            for (int s = 1; s <= 3; s++)
            {
                if (!_clientsBySlot.ContainsKey(s)) return s;
            }
            return -1;
        }

        private void BroadcastWorldStateToClients()
        {
            if (_clientsBySlot.Count == 0) return;

            var sb = new StringBuilder(1024);
            string sceneName = SceneManager.GetActiveScene().name;
            int flowState = GameServices.Flow != null ? (int)GameServices.Flow.State : (int)GameState.Hub;
            int tips = GameServices.Profile != null ? GameServices.Profile.Tips : 0;
            TreeDropZone tree = FindAnyObjectByType<TreeDropZone>();
            int delivered = tree != null ? tree.Delivered : 0;

            // Line 0: WSTATE|scene|flowState|tips|delivered|loadedGiftsCsv
            sb.Append("WSTATE|").Append(sceneName).Append('|')
              .Append(flowState).Append('|')
              .Append(tips).Append('|')
              .Append(delivered).Append('|');

            for (int i = 0; i < GameServices.LoadedGifts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((int)GameServices.LoadedGifts[i]);
            }
            sb.Append('\n');

            // Line 1: ORDER|address|mood|baseReward|tipMul|description|reqGiftsCsv
            Order ord = GameServices.ActiveOrder;
            if (ord != null)
            {
                sb.Append("ORDER|")
                  .Append(Sanitize(ord.Address)).Append('|')
                  .Append((int)ord.Mood).Append('|')
                  .Append(ord.BaseReward).Append('|')
                  .Append(F(ord.TipMultiplier)).Append('|')
                  .Append(Sanitize(ord.Description)).Append('|');
                if (ord.Gifts != null)
                {
                    for (int i = 0; i < ord.Gifts.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append((int)ord.Gifts[i]);
                    }
                }
                sb.Append('\n');
            }
            else
            {
                sb.Append("ORDER|NONE\n");
            }

            // Player lines: P|slot|x|y|z|yaw|vx|vy|vz|flags|stamina
            AppendHostLocalPlayerLine(sb);
            foreach (var kv in _clientsBySlot)
            {
                ConnectedClient c = kv.Value;
                if (!c.HasState) continue;
                int flags = (c.Grounded ? 1 : 0) | (c.Crouching ? 2 : 0) | (c.Sprinting ? 4 : 0) |
                            (c.Mantling ? 8 : 0) | (c.Jetpacking ? 16 : 0) | (c.KnockedDown ? 32 : 0);
                sb.Append("P|").Append(c.Slot).Append('|')
                  .Append(F(c.Pos.x)).Append('|').Append(F(c.Pos.y)).Append('|').Append(F(c.Pos.z)).Append('|')
                  .Append(F(c.Yaw)).Append('|')
                  .Append(F(c.PlanarVel.x)).Append('|').Append(F(c.VerticalVel)).Append('|').Append(F(c.PlanarVel.z)).Append('|')
                  .Append(flags).Append('|').Append(F(c.Stamina01)).Append('\n');
            }

            // Gifts in scene (up to 12 so UDP packet stays well under MTU)
            Gift[] gifts = FindObjectsByType<Gift>();
            if (gifts != null && gifts.Length > 0)
            {
                Array.Sort(gifts, (a, b) => string.CompareOrdinal(a.name, b.name));
                int count = Mathf.Min(gifts.Length, 12);
                for (int i = 0; i < count; i++)
                {
                    Gift g = gifts[i];
                    if (g == null) continue;
                    Vector3 gp = g.transform.position;
                    float gyaw = g.transform.eulerAngles.y;
                    sb.Append("G|").Append(i).Append('|')
                      .Append((int)g.Kind).Append('|')
                      .Append(g.IsHeld ? 1 : 0).Append('|')
                      .Append(F(gp.x)).Append('|').Append(F(gp.y)).Append('|').Append(F(gp.z)).Append('|')
                      .Append(F(gyaw)).Append('\n');
                }
            }

            string packet = sb.ToString();
            foreach (var kv in _clientsBySlot)
            {
                SendRaw(packet, kv.Value.EndPoint);
            }
        }

        private void AppendHostLocalPlayerLine(StringBuilder sb)
        {
            ElfInput primary = ElfInput.Primary;
            if (primary == null) return;
            var ctrl = primary.GetComponent<ElfController>();
            var rag = primary.GetComponent<ElfRagdoll>();
            Vector3 pos = primary.transform.position;
            float yaw = primary.transform.eulerAngles.y;
            Vector3 vel = ctrl != null ? ctrl.Velocity : Vector3.zero;
            bool grounded = ctrl == null || ctrl.IsGrounded;
            bool crouching = ctrl != null && ctrl.IsCrouching;
            bool sprinting = ctrl != null && ctrl.IsSprinting;
            bool mantling = ctrl != null && ctrl.IsMantling;
            bool jetpacking = ctrl != null && ctrl.IsJetpacking;
            bool downed = rag != null && rag.IsKnockedDown;
            float stam = ctrl != null ? ctrl.Stamina01 : 1f;
            int flags = (grounded ? 1 : 0) | (crouching ? 2 : 0) | (sprinting ? 4 : 0) |
                        (mantling ? 8 : 0) | (jetpacking ? 16 : 0) | (downed ? 32 : 0);

            sb.Append("P|0|")
              .Append(F(pos.x)).Append('|').Append(F(pos.y)).Append('|').Append(F(pos.z)).Append('|')
              .Append(F(yaw)).Append('|')
              .Append(F(vel.x)).Append('|').Append(F(vel.y)).Append('|').Append(F(vel.z)).Append('|')
              .Append(flags).Append('|').Append(F(stam)).Append('\n');
        }

        // =====================================================================
        // CLIENT LOGIC
        // =====================================================================

        private void UpdateClient(float dt)
        {
            float now = Time.unscaledTime;
            if (!_clientWelcomed)
            {
                if (now - _lastHelloSentTime >= 0.5f)
                {
                    _lastHelloSentTime = now;
                    SendRaw("HELLO", _hostEndPoint);
                }
                return;
            }

            if (now - _lastPacketFromHostTime > ClientTimeoutSeconds)
            {
                GameEvents.RaiseToast("Потеряно соединение с сервером.");
                StopNetwork();
                return;
            }

            _sendAccumulator += dt;
            if (_sendAccumulator >= 1f / TickRate)
            {
                _sendAccumulator = 0f;
                SendClientStateToHost();
            }
        }

        private void SendClientStateToHost()
        {
            ElfInput primary = ElfInput.Primary;
            if (primary == null || _hostEndPoint == null) return;
            var ctrl = primary.GetComponent<ElfController>();
            var rag = primary.GetComponent<ElfRagdoll>();

            Vector3 pos = primary.transform.position;
            float yaw = primary.transform.eulerAngles.y;
            Vector3 vel = ctrl != null ? ctrl.Velocity : Vector3.zero;
            bool grounded = ctrl == null || ctrl.IsGrounded;
            bool crouching = ctrl != null && ctrl.IsCrouching;
            bool sprinting = ctrl != null && ctrl.IsSprinting;
            bool mantling = ctrl != null && ctrl.IsMantling;
            bool jetpacking = ctrl != null && ctrl.IsJetpacking;
            bool downed = rag != null && rag.IsKnockedDown;
            float stam = ctrl != null ? ctrl.Stamina01 : 1f;
            int flags = (grounded ? 1 : 0) | (crouching ? 2 : 0) | (sprinting ? 4 : 0) |
                        (mantling ? 8 : 0) | (jetpacking ? 16 : 0) | (downed ? 32 : 0);

            string msg = $"CSTATE|{LocalSlot}|{F(pos.x)}|{F(pos.y)}|{F(pos.z)}|{F(yaw)}|{F(vel.x)}|{F(vel.y)}|{F(vel.z)}|{flags}|{F(stam)}";
            SendRaw(msg, _hostEndPoint);
        }

        private void HandleClientPacket(IPEndPoint sender, string payload)
        {
            _lastPacketFromHostTime = Time.unscaledTime;
            string[] lines = payload.Split('\n');
            if (lines.Length == 0) return;

            string first = lines[0];
            if (first.StartsWith("FULL", StringComparison.Ordinal))
            {
                _statusMessage = "Сервер полон (4/4 эльфов).";
                GameEvents.RaiseToast("Сервер полон (4/4)!");
                StopNetwork();
                return;
            }

            if (first.StartsWith("WELCOME|", StringComparison.Ordinal))
            {
                string[] parts = first.Split('|');
                if (parts.Length >= 3 && int.TryParse(parts[1], out int assignedSlot))
                {
                    LocalSlot = Mathf.Clamp(assignedSlot, 1, 3);
                    _clientWelcomed = true;
                    ApplyLocalPlayerSlotTheme(LocalSlot);
                    _statusMessage = $"Подключено! Вы — Эльф {LocalSlot + 1}.";
                    GameEvents.RaiseToast($"Вы подключились как Эльф {LocalSlot + 1}!");
                }
                return;
            }

            if (first.StartsWith("EVENT|", StringComparison.Ordinal))
            {
                HandleNetworkEventOnClient(first);
                return;
            }

            if (!first.StartsWith("WSTATE|", StringComparison.Ordinal)) return;

            if (!_clientWelcomed)
            {
                _clientWelcomed = true;
                ApplyLocalPlayerSlotTheme(LocalSlot);
            }

            // Parse line 0: WSTATE|scene|flowState|tips|delivered|loadedGiftsCsv
            string[] w = first.Split('|');
            if (w.Length >= 6)
            {
                string hostScene = w[1];
                int hostFlowState = ParseI(w[2]);
                int hostTips = ParseI(w[3]);
                string giftsCsv = w[5];

                if (GameServices.Profile != null && GameServices.Profile.Tips != hostTips)
                {
                    int delta = hostTips - GameServices.Profile.Tips;
                    if (delta > 0) GameServices.Profile.AddTips(delta);
                }

                SyncLoadedGifts(giftsCsv);
                SyncSceneWithHost(hostScene, (GameState)hostFlowState);
            }

            HashSet<int> seenPlayerSlots = new HashSet<int>();
            List<Gift> sceneGifts = null;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("ORDER|", StringComparison.Ordinal))
                {
                    SyncOrderFromHost(line);
                }
                else if (line.StartsWith("P|", StringComparison.Ordinal))
                {
                    string[] p = line.Split('|');
                    if (p.Length >= 11)
                    {
                        int slot = ParseI(p[1]);
                        seenPlayerSlots.Add(slot);
                        if (slot == LocalSlot) continue; // That's us!

                        Vector3 pos = new Vector3(ParseF(p[2]), ParseF(p[3]), ParseF(p[4]));
                        float yaw = ParseF(p[5]);
                        Vector3 planarVel = new Vector3(ParseF(p[6]), 0f, ParseF(p[8]));
                        float vertVel = ParseF(p[7]);
                        int flags = ParseI(p[9]);
                        bool grounded = (flags & 1) != 0;
                        bool crouching = (flags & 2) != 0;
                        bool sprinting = (flags & 4) != 0;
                        bool mantling = (flags & 8) != 0;
                        bool jetpacking = (flags & 16) != 0;
                        bool downed = (flags & 32) != 0;
                        float stam = ParseF(p[10]);

                        GameObject avatar = EnsureRemoteAvatar(slot, pos);
                        if (avatar != null)
                        {
                            ApplyStateToAvatar(avatar, pos, yaw, planarVel, vertVel,
                                grounded, crouching, sprinting, mantling, jetpacking, stam, downed);
                        }
                    }
                }
                else if (line.StartsWith("G|", StringComparison.Ordinal))
                {
                    string[] g = line.Split('|');
                    if (g.Length >= 8)
                    {
                        if (sceneGifts == null)
                        {
                            Gift[] found = FindObjectsByType<Gift>();
                            sceneGifts = new List<Gift>(found);
                            sceneGifts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                        }
                        int idx = ParseI(g[1]);
                        bool heldOnHost = ParseI(g[3]) != 0;
                        Vector3 gp = new Vector3(ParseF(g[4]), ParseF(g[5]), ParseF(g[6]));
                        float gyaw = ParseF(g[7]);
                        if (idx >= 0 && idx < sceneGifts.Count && sceneGifts[idx] != null && !sceneGifts[idx].IsHeld && !heldOnHost)
                        {
                            Transform gt = sceneGifts[idx].transform;
                            if (Vector3.Distance(gt.position, gp) > 0.25f)
                            {
                                gt.position = Vector3.Lerp(gt.position, gp, 0.35f);
                                gt.rotation = Quaternion.Euler(0f, gyaw, 0f);
                            }
                        }
                    }
                }
            }

            // Cleanup avatars of slots that left
            List<int> toRemove = null;
            foreach (var kv in _remoteAvatars)
            {
                if (!seenPlayerSlots.Contains(kv.Key))
                {
                    if (toRemove == null) toRemove = new List<int>();
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    int s = toRemove[i];
                    if (_remoteAvatars.TryGetValue(s, out GameObject go) && go != null) Destroy(go);
                    _remoteAvatars.Remove(s);
                }
            }
        }

        private void SyncLoadedGifts(string csv)
        {
            GameServices.LoadedGifts.Clear();
            if (string.IsNullOrEmpty(csv)) return;
            string[] parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int k))
                    GameServices.LoadedGifts.Add((GiftKind)k);
            }
        }

        private void SyncOrderFromHost(string line)
        {
            if (line == "ORDER|NONE") return;
            string[] p = line.Split('|');
            if (p.Length < 7) return;
            string addr = p[1];
            HouseMood mood = (HouseMood)ParseI(p[2]);
            int basePay = ParseI(p[3]);
            float tipMul = ParseF(p[4]);
            string desc = p[5];
            var req = new List<GiftKind>();
            if (!string.IsNullOrEmpty(p[6]))
            {
                string[] items = p[6].Split(',');
                for (int i = 0; i < items.Length; i++)
                {
                    if (int.TryParse(items[i], out int k)) req.Add((GiftKind)k);
                }
            }

            Order cur = GameServices.ActiveOrder;
            if (cur == null || cur.Address != addr || cur.Gifts.Count != req.Count)
            {
                GameServices.ActiveOrder = new Order
                {
                    Address = addr,
                    Mood = mood,
                    BaseReward = basePay,
                    TipMultiplier = tipMul,
                    Description = desc,
                    Gifts = req
                };
            }
        }

        private void SyncSceneWithHost(string hostScene, GameState hostFlowState)
        {
            if (string.IsNullOrEmpty(hostScene)) return;
            string active = SceneManager.GetActiveScene().name;
            if (active != hostScene && Time.unscaledTime > _sceneLoadBlockUntil && _pendingSceneLoad != hostScene)
            {
                _pendingSceneLoad = hostScene;
                _sceneLoadBlockUntil = Time.unscaledTime + 1.5f;
                if (GameServices.Flow != null)
                {
                    if (hostScene == GameConstants.SceneHub) GameEvents.RaiseRequestReturnToHub();
                    else if (hostScene == GameConstants.SceneRooftop) GameEvents.RaiseRequestStartDelivery(GameServices.ActiveOrder);
                    else if (hostScene == GameConstants.SceneHouse) GameEvents.RaiseRequestEnterHouse();
                    else SceneManager.LoadScene(hostScene);
                }
                else
                {
                    SceneManager.LoadScene(hostScene);
                }
            }
            else if (GameServices.Flow != null && GameServices.Flow.State == GameState.MainMenu && hostFlowState != GameState.MainMenu)
            {
                GameServices.Flow.StartShift();
            }
        }

        // =====================================================================
        // REMOTE AVATAR MANAGEMENT & NETWORK EVENTS (HELP UP)
        // =====================================================================

        private GameObject EnsureRemoteAvatar(int slot, Vector3 initialPos)
        {
            if (_remoteAvatars.TryGetValue(slot, out GameObject existing) && existing != null)
                return existing;

            GameObject go = PlayerSetup.CreateNetworkRemotePlayer(initialPos, slot, slot);
            _remoteAvatars[slot] = go;
            ElfNameTag.EnsureAll();
            return go;
        }

        private static void ApplyStateToAvatar(GameObject avatar, Vector3 pos, float yaw, Vector3 planarVel, float verticalVel,
            bool grounded, bool crouching, bool sprinting, bool mantling, bool jetpacking, float stamina01, bool knockedDown)
        {
            var ctrl = avatar.GetComponent<ElfController>();
            if (ctrl != null)
            {
                ctrl.ApplyNetworkState(pos, yaw, planarVel, verticalVel,
                    grounded, crouching, sprinting, mantling, jetpacking, stamina01);
            }

            var rag = avatar.GetComponent<ElfRagdoll>();
            if (rag != null)
            {
                if (knockedDown && !rag.IsLimp)
                {
                    rag.GoLimp(Vector3.up * 0.6f);
                }
                else if (!knockedDown && rag.IsLimp)
                {
                    rag.HelpUp();
                }
            }
        }

        public void ApplyLocalPlayerTheme(int themeIndex)
        {
            ElfInput primary = ElfInput.Primary;
            if (primary == null) return;
            var vis = primary.GetComponent<ElfVisual>();
            if (vis != null) vis.ThemeIndex = Mathf.Clamp(themeIndex, 0, 4);
            var rag = primary.GetComponent<ElfRagdoll>();
            if (rag != null) rag.RefreshThemeModel();
        }

        private void ApplyLocalPlayerSlotTheme(int slot)
        {
            ApplyLocalPlayerTheme(slot);
        }

        private void OnLocalPlayerHelpedUp(int targetSlot)
        {
            string pkt = $"EVENT|HELPUP|{targetSlot}";
            if (Role == NetRole.Host)
            {
                foreach (var kv in _clientsBySlot) SendRaw(pkt, kv.Value.EndPoint);
            }
            else if (Role == NetRole.Client && _hostEndPoint != null)
            {
                SendRaw(pkt, _hostEndPoint);
            }
        }

        private int _nextGiftNetId = 1000;
        public int GetNextGiftNetId() => _nextGiftNetId++;

        public float TransitionCountdownRemaining { get; private set; }
        public string TransitionCountdownLabel { get; private set; }

        private Coroutine _transitionRoutine;

        public void SendEvent(string pkt)
        {
            if (Role == NetRole.Host)
            {
                foreach (var kv in _clientsBySlot) SendRaw(pkt, kv.Value.EndPoint);
            }
            else if (Role == NetRole.Client && _hostEndPoint != null)
            {
                SendRaw(pkt, _hostEndPoint);
            }
        }

        public void NotifyGiftPickedUp(int netId)
        {
            SendEvent($"EVENT|G_PICK|{netId}|{LocalSlot}");
        }

        public void NotifyGiftDropped(int netId, Vector3 pos, Vector3 vel)
        {
            SendEvent($"EVENT|G_DROP|{netId}|{F(pos.x)}|{F(pos.y)}|{F(pos.z)}|{F(vel.x)}|{F(vel.y)}|{F(vel.z)}");
        }

        public void NotifyGiftSacked(int netId)
        {
            SendEvent($"EVENT|G_SACK|{netId}|{LocalSlot}");
        }

        public void NotifyGiftUnsacked(int netId)
        {
            SendEvent($"EVENT|G_UNSACK|{netId}|{LocalSlot}");
        }

        public void BroadcastGiftSpawn(Gift g)
        {
            if (g == null) return;
            Vector3 p = g.transform.position;
            float yaw = g.transform.eulerAngles.y;
            SendEvent($"EVENT|G_SPAWN|{g.NetworkId}|{(int)g.Kind}|{F(p.x)}|{F(p.y)}|{F(p.z)}|{F(yaw)}");
        }

        public void NotifySleighLoaded(GiftKind kind)
        {
            if (Role == NetRole.Host)
            {
                BroadcastSleighSync();
            }
            else if (Role == NetRole.Client)
            {
                SendEvent($"EVENT|REQ_SLEIGH_LOAD|{(int)kind}");
            }
        }

        public void BroadcastSleighSync()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < GameServices.LoadedGifts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((int)GameServices.LoadedGifts[i]);
            }
            SendEvent($"EVENT|SLEIGH_SYNC|{sb}");
        }

        public void BroadcastLadderDropped(int houseNum)
        {
            SendEvent($"EVENT|LADDER_DROPPED|{houseNum}");
        }

        public void BroadcastTreeDelivery(int netId, int slot, int total, GiftKind kind)
        {
            SendEvent($"EVENT|TREE_SYNC|{netId}|{slot}|{total}|{(int)kind}");
        }

        public static void RequestSynchronizedDeparture(Order order)
        {
            if (Instance == null || Instance.Role == NetRole.Offline)
            {
                GameEvents.RaiseRequestStartDelivery(order);
                return;
            }

            if (Instance.Role == NetRole.Client)
            {
                Instance.SendEvent("EVENT|REQ_DEPART");
                GameEvents.RaiseToast("Запрос на вылет отправлен хосту! Занимаем сани...");
                return;
            }

            if (Instance.Role == NetRole.Host)
            {
                Instance.StartSynchronizedTransitionCountdown(GameConstants.SceneRooftop, "Вылет в рейс! Все садятся в сани!");
            }
        }

        public static void RequestSynchronizedHouseEntry()
        {
            if (Instance == null || Instance.Role == NetRole.Offline)
            {
                GameEvents.RaiseRequestEnterHouse();
                return;
            }

            if (Instance.Role == NetRole.Client)
            {
                Instance.SendEvent("EVENT|REQ_ENTER_HOUSE");
                GameEvents.RaiseToast("Готов к спуску в камин! Ожидаем команду...");
                return;
            }

            if (Instance.Role == NetRole.Host)
            {
                Instance.StartSynchronizedTransitionCountdown(GameConstants.SceneHouse, "Оба эльфа ныряют в дымоход!");
            }
        }

        public static void RequestSynchronizedReturnToHub(bool escape)
        {
            if (Instance == null || Instance.Role == NetRole.Offline)
            {
                GameEvents.RaiseRequestReturnToHub();
                return;
            }

            if (Instance.Role == NetRole.Client)
            {
                Instance.SendEvent("EVENT|REQ_RETURN_HUB");
                GameEvents.RaiseToast("Запрос на возвращение отправлен хосту...");
                return;
            }

            if (Instance.Role == NetRole.Host)
            {
                string label = escape ? "Срочная эвакуация на базу!" : "Миссия завершена! Возвращение на базу!";
                Instance.StartSynchronizedTransitionCountdown(GameConstants.SceneHub, label);
            }
        }

        public void StartSynchronizedTransitionCountdown(string targetScene, string label)
        {
            if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
            _transitionRoutine = StartCoroutine(TransitionCountdownRoutine(targetScene, label));
        }

        private System.Collections.IEnumerator TransitionCountdownRoutine(string targetScene, string label)
        {
            float duration = 2.4f;
            float start = Time.unscaledTime;
            TransitionCountdownLabel = label;

            while (Time.unscaledTime - start < duration)
            {
                float remaining = Mathf.Max(0.1f, duration - (Time.unscaledTime - start));
                TransitionCountdownRemaining = remaining;
                string toast = $"[{remaining:F1}с] {label}";
                GameEvents.RaiseToast(toast);
                SendEvent($"EVENT|COUNTDOWN|{F(remaining)}|{Sanitize(label)}");
                yield return new WaitForSecondsRealtime(0.6f);
            }

            TransitionCountdownRemaining = 0f;
            TransitionCountdownLabel = null;
            SendEvent($"EVENT|TRANSITION|{targetScene}");
            ExecuteSynchronizedSceneLoad(targetScene);
            _transitionRoutine = null;
        }

        public void ExecuteSynchronizedSceneLoad(string targetScene)
        {
            _pendingSceneLoad = targetScene;
            _sceneLoadBlockUntil = Time.unscaledTime + 6.0f;
            if (targetScene == GameConstants.SceneHub)
            {
                GameEvents.RaiseRequestReturnToHub();
            }
            else if (targetScene == GameConstants.SceneRooftop)
            {
                GameEvents.RaiseRequestStartDelivery(GameServices.ActiveOrder);
            }
            else if (targetScene == GameConstants.SceneHouse)
            {
                GameEvents.RaiseRequestEnterHouse();
            }
            else
            {
                SceneManager.LoadScene(targetScene);
            }
        }

        public Transform GetAvatarHoldPoint(int slot)
        {
            if (slot == LocalSlot)
            {
                ElfInput primary = ElfInput.Primary;
                var carry = primary != null ? primary.GetComponent<ElfCarry>() : null;
                return carry != null ? carry.HoldPoint : (primary != null ? primary.transform : null);
            }
            if (_remoteAvatars.TryGetValue(slot, out GameObject av) && av != null)
            {
                var carry = av.GetComponent<ElfCarry>();
                if (carry != null && carry.HoldPoint != null) return carry.HoldPoint;
                var vis = av.GetComponent<ElfVisual>();
                if (vis != null && vis.HoldPoint != null) return vis.HoldPoint;
                return av.transform;
            }
            return null;
        }

        private void HandleNetworkEventOnHost(string header)
        {
            string[] p = header.Split('|');
            if (p.Length >= 2)
            {
                string ev = p[1];
                if (ev == "REQ_DEPART")
                {
                    RequestSynchronizedDeparture(GameServices.ActiveOrder);
                    return;
                }
                if (ev == "REQ_ENTER_HOUSE")
                {
                    RequestSynchronizedHouseEntry();
                    return;
                }
                if (ev == "REQ_RETURN_HUB")
                {
                    RequestSynchronizedReturnToHub(false);
                    return;
                }
                if (ev == "REQ_SLEIGH_LOAD" && p.Length >= 3 && int.TryParse(p[2], out int kindInt))
                {
                    GameServices.LoadedGifts.Add((GiftKind)kindInt);
                    BroadcastSleighSync();
                    int total = GameServices.ActiveOrder != null ? GameServices.ActiveOrder.Gifts.Count : 3;
                    GameEvents.RaiseGiftLoaded(GameServices.LoadedGifts.Count, total);
                    return;
                }
            }

            // Relay to all clients and apply locally
            foreach (var kv in _clientsBySlot) SendRaw(header, kv.Value.EndPoint);
            HandleNetworkEventOnClient(header);
        }

        private void HandleNetworkEventOnClient(string header)
        {
            string[] p = header.Split('|');
            if (p.Length < 2) return;
            string ev = p[1];

            switch (ev)
            {
                case "HELPUP":
                    if (p.Length >= 3 && ParseI(p[2]) == LocalSlot)
                    {
                        ElfInput primary = ElfInput.Primary;
                        if (primary != null)
                        {
                            var rag = primary.GetComponent<ElfRagdoll>();
                            if (rag != null && rag.IsLimp)
                            {
                                rag.HelpUp();
                                GameEvents.RaiseToast("Друг поднял вас на ноги!");
                            }
                        }
                    }
                    break;

                case "TRANSITION":
                    TransitionCountdownRemaining = 0f;
                    TransitionCountdownLabel = null;
                    if (p.Length >= 3)
                    {
                        string targetScene = p[2];
                        ExecuteSynchronizedSceneLoad(targetScene);
                    }
                    break;

                case "COUNTDOWN":
                    if (p.Length >= 4)
                    {
                        TransitionCountdownRemaining = ParseF(p[2]);
                        TransitionCountdownLabel = p[3];
                        GameEvents.RaiseToast($"[{p[2]}с] {p[3]}");
                    }
                    break;

                case "TOAST":
                    if (p.Length >= 3)
                    {
                        GameEvents.RaiseToast(p[2]);
                    }
                    break;

                case "G_SPAWN":
                    if (p.Length >= 8)
                    {
                        int netId = ParseI(p[2]);
                        GiftKind kind = (GiftKind)ParseI(p[3]);
                        Vector3 pos = new Vector3(ParseF(p[4]), ParseF(p[5]), ParseF(p[6]));
                        Quaternion rot = Quaternion.Euler(0f, ParseF(p[7]), 0f);
                        if (Gift.FindByNetId(netId) == null)
                        {
                            GiftFactory.Create(kind, pos, rot, netId);
                        }
                    }
                    break;

                case "G_PICK":
                    if (p.Length >= 4)
                    {
                        int netId = ParseI(p[2]);
                        int slot = ParseI(p[3]);
                        if (slot != LocalSlot)
                        {
                            Gift g = Gift.FindByNetId(netId);
                            if (g != null)
                            {
                                Transform anchor = GetAvatarHoldPoint(slot);
                                if (anchor != null) g.OnPickedUp(anchor);
                            }
                        }
                    }
                    break;

                case "G_DROP":
                    if (p.Length >= 9)
                    {
                        int netId = ParseI(p[2]);
                        Vector3 pos = new Vector3(ParseF(p[3]), ParseF(p[4]), ParseF(p[5]));
                        Vector3 vel = new Vector3(ParseF(p[6]), ParseF(p[7]), ParseF(p[8]));
                        Gift g = Gift.FindByNetId(netId);
                        if (g != null)
                        {
                            g.transform.position = pos;
                            g.OnDropped(vel);
                        }
                    }
                    break;

                case "G_SACK":
                    if (p.Length >= 4)
                    {
                        int netId = ParseI(p[2]);
                        int slot = ParseI(p[3]);
                        if (slot != LocalSlot)
                        {
                            Gift g = Gift.FindByNetId(netId);
                            if (g != null) g.OnStoredInSack();
                        }
                    }
                    break;

                case "G_UNSACK":
                    if (p.Length >= 4)
                    {
                        int netId = ParseI(p[2]);
                        int slot = ParseI(p[3]);
                        if (slot != LocalSlot)
                        {
                            Gift g = Gift.FindByNetId(netId);
                            if (g != null)
                            {
                                Transform anchor = GetAvatarHoldPoint(slot);
                                if (anchor != null) g.OnPickedUp(anchor);
                            }
                        }
                    }
                    break;

                case "SLEIGH_SYNC":
                    if (p.Length >= 3)
                    {
                        SyncLoadedGifts(p[2]);
                        int total = GameServices.ActiveOrder != null ? GameServices.ActiveOrder.Gifts.Count : 3;
                        GameEvents.RaiseGiftLoaded(GameServices.LoadedGifts.Count, total);
                    }
                    break;

                case "TREE_SYNC":
                    if (p.Length >= 6)
                    {
                        int netId = ParseI(p[2]);
                        int slot = ParseI(p[3]);
                        int total = ParseI(p[4]);
                        GiftKind kind = (GiftKind)ParseI(p[5]);
                        var tree = FindAnyObjectByType<TreeDropZone>();
                        if (tree != null) tree.RemoteDeliver(netId, slot, total, kind);
                    }
                    break;

                case "LADDER_DROPPED":
                    if (p.Length >= 3)
                    {
                        int hNum = ParseI(p[2]);
                        foreach (var chimney in FindObjectsByType<SilentNight.Rooftop.RooftopChimney>())
                        {
                            if (chimney.HouseNumber == hNum) chimney.DropLadderRemote();
                        }
                    }
                    break;
            }
        }

        private static Vector3 GetLocalElfPosition()
        {
            ElfInput primary = ElfInput.Primary;
            return primary != null ? primary.transform.position : Vector3.zero;
        }

        // =====================================================================
        // IP DETECTION & UPnP PORT FORWARDING HELPER
        // =====================================================================

        private void RefreshLocalIpList()
        {
            _localIps.Clear();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                        string label = ip;
                        if (ip.StartsWith("26.", StringComparison.Ordinal)) label = ip + " (Radmin VPN)";
                        else if (ip.StartsWith("100.", StringComparison.Ordinal)) label = ip + " (Tailscale)";
                        else if (ip.StartsWith("192.168.", StringComparison.Ordinal)) label = ip + " (LAN / Wi-Fi)";
                        if (!_localIps.Contains(label)) _localIps.Add(label);
                    }
                }
            }
            catch { }

            if (_localIps.Count == 0) _localIps.Add("127.0.0.1 (Этот ПК)");
        }

        private void TrySetupUpnpAndPublicIp(int port)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    string pub = wc.DownloadString("https://api.ipify.org").Trim();
                    if (!string.IsNullOrEmpty(pub) && pub.Length <= 45)
                        _publicIp = pub;
                }
            }
            catch { }

            // Attempt UPnP SSDP M-SEARCH for WANIPConnection
            try
            {
                string localIp = "127.0.0.1";
                for (int i = 0; i < _localIps.Count; i++)
                {
                    string raw = _localIps[i].Split(' ')[0];
                    if (raw.StartsWith("192.168.", StringComparison.Ordinal) || raw.StartsWith("10.", StringComparison.Ordinal))
                    {
                        localIp = raw;
                        break;
                    }
                }

                using (var ssdp = new UdpClient())
                {
                    ssdp.Client.ReceiveTimeout = 1800;
                    string search =
                        "M-SEARCH * HTTP/1.1\r\n" +
                        "HOST: 239.255.255.250:1900\r\n" +
                        "ST: urn:schemas-upnp-org:service:WANIPConnection:1\r\n" +
                        "MAN: \"ssdp:discover\"\r\n" +
                        "MX: 1\r\n\r\n";
                    byte[] req = Encoding.ASCII.GetBytes(search);
                    ssdp.Send(req, req.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));

                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = ssdp.Receive(ref ep);
                    string text = Encoding.ASCII.GetString(resp);
                    int locIdx = text.IndexOf("LOCATION:", StringComparison.OrdinalIgnoreCase);
                    if (locIdx >= 0)
                    {
                        int endLine = text.IndexOf("\r\n", locIdx, StringComparison.Ordinal);
                        string locationUrl = text.Substring(locIdx + 9, endLine - (locIdx + 9)).Trim();
                        if (TrySendUpnpAddPortMapping(locationUrl, localIp, port))
                        {
                            _upnpStatus = "UPnP порт " + port + " открыт автоматически!";
                            return;
                        }
                    }
                }
            }
            catch { }

            _upnpStatus = "Для игры через интернет без проброса портов: Radmin VPN / Tailscale или проброс UDP " + port + " на роутере.";
        }

        private static bool TrySendUpnpAddPortMapping(string descUrl, string internalClientIp, int port)
        {
            try
            {
                Uri uri = new Uri(descUrl);
                string xml;
                using (var wc = new WebClient())
                {
                    xml = wc.DownloadString(descUrl);
                }
                string serviceType = "urn:schemas-upnp-org:service:WANIPConnection:1";
                int idx = xml.IndexOf(serviceType, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return false;
                int ctrlStart = xml.IndexOf("<controlURL>", idx, StringComparison.OrdinalIgnoreCase);
                int ctrlEnd = xml.IndexOf("</controlURL>", idx, StringComparison.OrdinalIgnoreCase);
                if (ctrlStart < 0 || ctrlEnd < 0) return false;
                string ctrlPath = xml.Substring(ctrlStart + 12, ctrlEnd - (ctrlStart + 12)).Trim();
                Uri controlUri = new Uri(uri, ctrlPath);

                string soap =
                    "<?xml version=\"1.0\"?>" +
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    "<s:Body>" +
                    "<u:AddPortMapping xmlns:u=\"" + serviceType + "\">" +
                    "<NewRemoteHost></NewRemoteHost>" +
                    "<NewExternalPort>" + port + "</NewExternalPort>" +
                    "<NewProtocol>UDP</NewProtocol>" +
                    "<NewInternalPort>" + port + "</NewInternalPort>" +
                    "<NewInternalClient>" + internalClientIp + "</NewInternalClient>" +
                    "<NewEnabled>1</NewEnabled>" +
                    "<NewPortMappingDescription>SilentNight_Coop_UDP</NewPortMappingDescription>" +
                    "<NewLeaseDuration>0</NewLeaseDuration>" +
                    "</u:AddPortMapping>" +
                    "</s:Body></s:Envelope>";

                using (var wc = new WebClient())
                {
                    wc.Headers.Add("Content-Type", "text/xml; charset=\"utf-8\"");
                    wc.Headers.Add("SOAPAction", "\"" + serviceType + "#AddPortMapping\"");
                    wc.UploadString(controlUri, soap);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        // TACTILE CO-OP OVERLAY UI ([F4] OR TOP STATUS PILL)
        // =====================================================================

        private void EnsureStyles()
        {
            if (_windowStyle != null) return;

            Color outline = new Color(0.16f, 0.08f, 0.04f, 1f);
            _bgTex = MakePlaqueTex(new Color(0.20f, 0.12f, 0.07f, 0.98f), outline, 6);
            _cardTex = MakePlaqueTex(new Color(0.28f, 0.17f, 0.10f, 0.96f), outline, 4);
            _btnRedTex = MakePlaqueTex(new Color(0.78f, 0.18f, 0.16f, 1f), outline, 5);
            _btnWoodTex = MakePlaqueTex(new Color(0.42f, 0.25f, 0.14f, 1f), outline, 5);

            _windowStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _bgTex, textColor = new Color(1f, 0.95f, 0.84f) },
                border = new RectOffset(4, 4, 4, 8),
                padding = new RectOffset(18, 18, 14, 14)
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.86f, 0.35f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.98f, 0.93f, 0.82f) }
            };

            _badgeStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(4, 4, 4, 6),
                normal = { background = _cardTex, textColor = new Color(1f, 0.92f, 0.65f) },
                hover = { background = _btnWoodTex, textColor = Color.white }
            };

            _btnPrimaryStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(4, 4, 4, 7),
                normal = { background = _btnRedTex, textColor = Color.white },
                hover = { background = _btnRedTex, textColor = new Color(1f, 0.92f, 0.55f) }
            };

            _btnSecondaryStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(4, 4, 4, 7),
                normal = { background = _btnWoodTex, textColor = new Color(1f, 0.95f, 0.85f) },
                hover = { background = _cardTex, textColor = Color.white }
            };

            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(4, 4, 4, 6),
                normal = { background = _cardTex, textColor = new Color(1f, 0.96f, 0.85f) }
            };
        }

        private static Texture2D MakePlaqueTex(Color fill, Color border, int bottomBorderExtra = 4)
        {
            int w = 24, h = 24;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isBorder = (x < 2 || x >= w - 2 || y >= h - 2 || y < (2 + bottomBorderExtra));
                    pixels[y * w + x] = isBorder ? border : fill;
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.SetPixels(new[] { c, c, c, c });
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            // Do not show the in-game F4 top pill on the Main Menu screen
            if (SilentNight.Core.GameServices.Flow != null &&
                SilentNight.Core.GameServices.Flow.State == SilentNight.Core.GameState.MainMenu)
            {
                return;
            }

            EnsureStyles();

            // Always show a compact clickable pill at the top-center so the user can open Co-op or check status anytime
            string pillText = Role switch
            {
                NetRole.Host => $"🌐 СЕРВЕР АКТИВЕН (:{Port}) • Эльфов: {_clientsBySlot.Count + 1}/4 [F4]",
                NetRole.Client => _clientWelcomed
                    ? $"🌐 В СЕТИ (Эльф {LocalSlot + 1}) • {_connectIpInput}:{Port} [F4]"
                    : $"⏳ Подключение к {_connectIpInput}:{Port}... [F4]",
                _ => "🌐 Сетевой Кооп (Хост / IP / LAN) [F4]"
            };

            bool inDelivery = SilentNight.Core.GameServices.Flow != null &&
                              SilentNight.Core.GameServices.Flow.State == SilentNight.Core.GameState.Delivery;
            float pillW = 360f;
            float pillY = inDelivery ? Screen.height - 38f : 10f;
            Rect pillRect = new Rect((Screen.width - pillW) * 0.5f, pillY, pillW, 28f);
            if (GUI.Button(pillRect, pillText, _badgeStyle))
            {
                WindowOpen = !WindowOpen;
                ApplyModalCursorState();
            }

            if (!WindowOpen) return;

            float w = Mathf.Min(540f, Screen.width - 32f);
            float h = Mathf.Min(470f, Screen.height - 56f);
            Rect win = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.Box(win, GUIContent.none, _windowStyle);
            GUILayout.BeginArea(new Rect(win.x + 18f, win.y + 14f, win.width - 36f, win.height - 28f));

            GUILayout.Label("ОНЛАЙН И LAN КООП (ДО 4 ЭЛЬФОВ)", _titleStyle);
            GUILayout.Space(6f);

            // Status bar
            GUILayout.Label("Статус: " + _statusMessage, _labelStyle);
            if (!string.IsNullOrEmpty(_upnpStatus) && Role == NetRole.Host)
            {
                GUILayout.Label(_upnpStatus, _labelStyle);
            }

            GUILayout.Space(8f);

            // Section 1: Your IPs for friends to connect
            GUILayout.Label("Ваши IP-адреса (скиньте другу, если вы Хост):", _labelStyle);
            for (int i = 0; i < _localIps.Count; i++)
            {
                string entry = _localIps[i];
                string rawIp = entry.Split(' ')[0];
                GUILayout.BeginHorizontal();
                GUILayout.Label("• " + entry, _labelStyle, GUILayout.Width(w - 175f));
                if (GUILayout.Button("Копировать", _btnSecondaryStyle, GUILayout.Width(120f), GUILayout.Height(24f)))
                {
                    GUIUtility.systemCopyBuffer = rawIp;
                    _statusMessage = "Скопирован IP: " + rawIp;
                }
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(_publicIp))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("• Внешний (Интернет) IP: " + _publicIp, _labelStyle, GUILayout.Width(w - 175f));
                if (GUILayout.Button("Копировать", _btnSecondaryStyle, GUILayout.Width(120f), GUILayout.Height(24f)))
                {
                    GUIUtility.systemCopyBuffer = _publicIp;
                    _statusMessage = "Скопирован внешний IP: " + _publicIp;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);

            // Section 2: Host / Connect controls
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP сервера:", _labelStyle, GUILayout.Width(90f));
            _connectIpInput = GUILayout.TextField(_connectIpInput, _textFieldStyle, GUILayout.Width(210f), GUILayout.Height(28f));
            GUILayout.Label("Порт:", _labelStyle, GUILayout.Width(45f));
            _portInput = GUILayout.TextField(_portInput, _textFieldStyle, GUILayout.Width(80f), GUILayout.Height(28f));
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            int parsedPort = int.TryParse(_portInput, out int pp) ? pp : DefaultGamePort;

            if (Role == NetRole.Offline)
            {
                if (GUILayout.Button("🚀 ЗАПУСТИТЬ СЕРВЕР С КОМПА (ХОСТ :" + parsedPort + ")", _btnPrimaryStyle, GUILayout.Height(38f)))
                {
                    StartHost(parsedPort);
                }

                GUILayout.Space(6f);

                if (GUILayout.Button("🔗 ПОДКЛЮЧИТЬСЯ К ДРУГУ ПО IP (" + _connectIpInput + ":" + parsedPort + ")", _btnSecondaryStyle, GUILayout.Height(34f)))
                {
                    StartClient(_connectIpInput, parsedPort);
                }

                GUILayout.Space(6f);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🔍 Найти сервер в LAN / VPN", _btnSecondaryStyle, GUILayout.Height(30f)))
                {
                    BroadcastLanDiscoveryProbe();
                }

                if (!string.IsNullOrEmpty(_discoveredHostIp))
                {
                    if (GUILayout.Button($"Войти: {_discoveredHostIp}:{_discoveredHostPort} ({_discoveredHostPlayers}/4)", _btnPrimaryStyle, GUILayout.Height(30f)))
                    {
                        StartClient(_discoveredHostIp, _discoveredHostPort);
                    }
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                string modeDesc = Role == NetRole.Host
                    ? $"Вы — ХОСТ СЕРВЕРА (Эльф 1). Подключено клиентов: {_clientsBySlot.Count}"
                    : $"Вы — КЛИЕНТ (Эльф {LocalSlot + 1}). Сервер: {_connectIpInput}:{Port}";
                GUILayout.Label(modeDesc, _labelStyle);
                GUILayout.Space(6f);

                if (GUILayout.Button("❌ ОТКЛЮЧИТЬ СЕТЬ", _btnPrimaryStyle, GUILayout.Height(36f)))
                {
                    StopNetwork();
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Подсказка: по одному Wi-Fi используйте LAN IP (192.168.x.x), через интернет — Radmin VPN / Tailscale или внешний IP (UDP 7777).", _labelStyle);
            if (GUILayout.Button("Закрыть [F4]", _btnSecondaryStyle, GUILayout.Width(115f), GUILayout.Height(28f)))
            {
                WindowOpen = false;
                ApplyModalCursorState();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // =====================================================================
        // STRING / FLOAT SERIALIZATION HELPERS
        // =====================================================================

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static float ParseF(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static int ParseI(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static string Sanitize(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
    }
}
