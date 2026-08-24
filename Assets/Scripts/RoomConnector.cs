using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MechaChameleon
{
    public sealed class RoomConnector : MonoBehaviour
    {
        public const ushort LocalPort = 7778;
        public const int MaxRoomNameLength = 32;
        public const int MaxPasswordLength = 32;
        public const int MinOnlinePasswordLength = 8;
        public const ushort OnlineProtocolVersion = 1;
        const string ProtocolProperty = "protocol";

        [SerializeField] int maxPlayers = 8;
        [SerializeField] string sessionName = "Paint Hideout";
        [SerializeField] string relayRegion = "";
        [SerializeField] bool useRelay = true;

        public event Action StatusChanged;
        public event Action RoomEntered;
        public event Action RoomLeft;
        public event Action RoomsChanged;

        public string JoinCode { get; private set; } = "";
        public string Status { get; private set; } = "Idle";
        public RoomListing CurrentRoom { get; private set; }
        public bool IsBusy { get; private set; }
        public bool UsesOnlineServices => DeploymentEnvironmentSettings.UsesOnlineServices;
        public string EnvironmentName => DeploymentEnvironmentSettings.UgsEnvironmentName;
        public IReadOnlyList<RoomListing> Rooms => UsesOnlineServices ? onlineRooms : discovery.Rooms;
        public bool IsRoomHost =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && CurrentRoom != null;
        public bool IsLocalRoomHost => IsRoomHost;

        public int ConnectedPlayerCount
        {
            get
            {
                var manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening) return 0;
                if (manager.IsServer) return manager.ConnectedClientsIds.Count;

                var players = FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None);
                var count = 0;
                foreach (var player in players)
                {
                    if (player != null && player.IsSpawned && player.NetworkObject.IsPlayerObject)
                        count++;
                }

                return Mathf.Max(manager.IsConnectedClient ? 1 : 0, count);
            }
        }

        ISession session;
        NetworkManager registeredNetworkManager;
        LocalRoomDiscovery discovery;
        readonly List<RoomListing> onlineRooms = new();
        string hostPassword = "";
        bool pendingLocalJoin;
        bool roomEnteredRaised;
        bool isLeaving;

        void Awake()
        {
            discovery = GetComponent<LocalRoomDiscovery>();
            if (discovery == null)
                discovery = gameObject.AddComponent<LocalRoomDiscovery>();
            discovery.RoomsChanged += OnLocalRoomsChanged;
        }

        void OnDisable()
        {
            UnregisterNetworkCallbacks();
            ShutdownNetwork();
        }

        public async void Host()
        {
            await CreateRoomAsync(sessionName, "");
        }

        public async Task<bool> CreateRoomAsync(string roomName, string password)
        {
            if (IsBusy) return false;
            if (!UsesOnlineServices) return CreateLocalRoom(roomName, password);
            return await CreateOnlineRoomAsync(roomName, password);
        }

        public bool CreateLocalRoom(string roomName, string password)
        {
            roomName = roomName?.Trim() ?? "";
            password ??= "";

            GameDiagnostics.Info("network", "create_room_requested",
                $"room=\"{roomName}\" locked={password.Length > 0} maxPlayers={maxPlayers}");

            if (roomName.Length == 0)
            {
                SetStatus("Enter a room name.");
                return false;
            }

            if (roomName.Length > MaxRoomNameLength)
            {
                SetStatus($"Room names can be up to {MaxRoomNameLength} characters.");
                return false;
            }

            if (password.Length > MaxPasswordLength)
            {
                SetStatus($"Passwords can be up to {MaxPasswordLength} characters.");
                return false;
            }

            if (!PrepareNetworkManager() || !PrepareDirectConnection("127.0.0.1", LocalPort))
                return false;

            var manager = NetworkManager.Singleton;
            CurrentRoom = new RoomListing
            {
                RoomId = Guid.NewGuid().ToString("N"),
                IsOnline = false,
                HostAddress = "127.0.0.1",
                Port = LocalPort,
                RoomName = roomName,
                IsLocked = password.Length > 0,
                PlayerCount = 1,
                MaxPlayers = maxPlayers
            };
            hostPassword = password;
            pendingLocalJoin = false;
            roomEnteredRaised = false;
            manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            ConfigureConnectionApproval(manager);
            RegisterNetworkCallbacks();

            if (!manager.StartHost())
            {
                GameDiagnostics.Error("network", "host_start_failed",
                    $"room=\"{roomName}\" port={LocalPort}");
                CleanupFailedStart();
                ClearLocalRoom();
                SetStatus($"Could not start local host. UDP port {LocalPort} may already be in use.");
                return false;
            }

            JoinCode = "LOCAL";
            discovery.StartAdvertising(CreateAdvertisement);
            GameDiagnostics.Info("network", "host_started",
                $"roomId={CurrentRoom.RoomId} room=\"{roomName}\" port={LocalPort}");
            SetStatus($"Hosting {roomName} locally.");
            return true;
        }

        public void HostLocal()
        {
            CreateLocalRoom("Local Room", "");
        }

        public async void Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("Enter a join code.");
                return;
            }

            await JoinOnlineByCodeAsync(code.Trim(), "");
        }

        public async Task<bool> JoinRoomAsync(RoomListing room, string password)
        {
            if (IsBusy) return false;
            if (room == null) return false;
            if (!room.IsOnline) return JoinLocal(room, password);
            return await JoinOnlineRoomAsync(room, password);
        }

        public bool JoinLocal(RoomListing room, string password)
        {
            if (room == null || string.IsNullOrWhiteSpace(room.HostAddress))
            {
                SetStatus("That room is no longer available.");
                return false;
            }

            if (room.Port <= 0 || room.Port > ushort.MaxValue)
            {
                SetStatus("That room has an invalid network port.");
                return false;
            }

            if (!PrepareNetworkManager() ||
                !PrepareDirectConnection(room.HostAddress, (ushort)room.Port))
                return false;

            password ??= "";
            GameDiagnostics.Info("network", "join_room_requested",
                $"roomId={room.RoomId} room=\"{room.RoomName}\" " +
                $"host={room.HostAddress}:{room.Port} passwordProvided={password.Length > 0}");
            if (password.Length > MaxPasswordLength)
            {
                SetStatus($"Passwords can be up to {MaxPasswordLength} characters.");
                return false;
            }

            var manager = NetworkManager.Singleton;
            CurrentRoom = room.Copy();
            hostPassword = "";
            pendingLocalJoin = true;
            roomEnteredRaised = false;
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(password);
            RegisterNetworkCallbacks();
            discovery.StopAll();

            if (manager.StartClient())
            {
                GameDiagnostics.Info("network", "client_started",
                    $"roomId={CurrentRoom.RoomId} host={CurrentRoom.HostAddress}:{CurrentRoom.Port}");
                SetStatus($"Joining {room.RoomName}...");
                return true;
            }

            CleanupFailedStart();
            GameDiagnostics.Error("network", "client_start_failed",
                $"roomId={room.RoomId} host={room.HostAddress}:{room.Port}");
            ClearLocalRoom();
            SetStatus("Could not start local client.");
            return false;
        }

        public void JoinLocal()
        {
            JoinLocal(new RoomListing
            {
                RoomId = "LOCAL",
                IsOnline = false,
                HostAddress = "127.0.0.1",
                Port = LocalPort,
                RoomName = "Local Room",
                MaxPlayers = maxPlayers
            }, "");
        }

        public async void Leave()
        {
            await LeaveAsync();
        }

        public async Task LeaveAsync()
        {
            if (isLeaving) return;
            isLeaving = true;
            IsBusy = true;
            GameDiagnostics.Info("network", "leave_requested",
                $"isHost={IsRoomHost} online={CurrentRoom?.IsOnline == true} " +
                $"roomId={CurrentRoom?.RoomId ?? "none"}");
            discovery?.StopAll();

            try
            {
                if (session != null)
                {
                    var activeSession = session;
                    UnbindSessionEvents(activeSession);
                    if (activeSession.IsHost)
                        await activeSession.AsHost().DeleteAsync();
                    else
                        await activeSession.LeaveAsync();
                    session = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RoomConnector] Leave failed: {ex.Message}");
            }

            var manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
                manager.Shutdown();

            ClearLocalRoom();
            JoinCode = "";
            IsBusy = false;
            isLeaving = false;
            SetStatus("Left room.");
            RoomLeft?.Invoke();
        }

        public void EndLocalRoom()
        {
            if (!IsRoomHost)
            {
                SetStatus("Only the room host can end the room.");
                return;
            }

            Leave();
        }

        public void StartRoomSearch()
        {
            if (!UsesOnlineServices)
                discovery.StartListening();
        }

        public void StopRoomSearch()
        {
            if (!UsesOnlineServices)
                discovery.StopAll();
        }

        public async Task RefreshRoomsAsync()
        {
            if (!UsesOnlineServices)
            {
                discovery.Refresh();
                return;
            }

            if (IsBusy || CurrentRoom != null) return;
            IsBusy = true;
            SetStatus($"Searching {EnvironmentName} rooms...");

            try
            {
                await UgsBootstrap.EnsureReadyAsync();
                var query = new QuerySessionsOptions { Count = 20 };
                query.FilterOptions.Add(new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater));
                query.FilterOptions.Add(new FilterOption(FilterField.IsLocked, "false", FilterOperation.Equal));
                query.FilterOptions.Add(new FilterOption(
                    FilterField.StringIndex1,
                    OnlineProtocolVersion.ToString(),
                    FilterOperation.Equal));
                query.SortOptions.Add(new SortOption
                {
                    Field = SortField.LastUpdated,
                    Order = SortOrder.Descending
                });

                var results = await MultiplayerService.Instance.QuerySessionsAsync(query);
                onlineRooms.Clear();
                foreach (var info in results.Sessions)
                {
                    onlineRooms.Add(new RoomListing
                    {
                        RoomId = info.Id,
                        IsOnline = true,
                        RoomName = info.Name,
                        IsLocked = info.HasPassword,
                        IsJoinLocked = info.IsLocked,
                        PlayerCount = info.MaxPlayers - info.AvailableSlots,
                        MaxPlayers = info.MaxPlayers
                    });
                }

                SetStatus(onlineRooms.Count == 0
                    ? $"No {EnvironmentName} rooms found."
                    : $"Found {onlineRooms.Count} {EnvironmentName} room{(onlineRooms.Count == 1 ? "" : "s")}.");
                RoomsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                HandleOnlineFailure($"Could not search {EnvironmentName} rooms.", ex, clearRoom: false);
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        async Task<bool> CreateOnlineRoomAsync(string roomName, string password)
        {
            roomName = roomName?.Trim() ?? "";
            password ??= "";
            if (!ValidateRoomInput(roomName, password, online: true)) return false;

            IsBusy = true;
            SetStatus($"Creating room in {EnvironmentName}...");

            try
            {
                await UgsBootstrap.EnsureReadyAsync();
                if (!PrepareOnlineNetwork()) return false;

                CurrentRoom = new RoomListing
                {
                    IsOnline = true,
                    RoomName = roomName,
                    IsLocked = password.Length > 0,
                    PlayerCount = 1,
                    MaxPlayers = maxPlayers
                };
                roomEnteredRaised = false;
                pendingLocalJoin = false;

                var options = new SessionOptions
                {
                    Name = roomName,
                    MaxPlayers = maxPlayers,
                    IsPrivate = false,
                    Password = password.Length == 0 ? null : password,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        [ProtocolProperty] = new(
                            OnlineProtocolVersion.ToString(),
                            VisibilityPropertyOptions.Public,
                            PropertyIndex.String1)
                    }
                };

                if (useRelay)
                    options.WithRelayNetwork(string.IsNullOrWhiteSpace(relayRegion) ? null : relayRegion);
                else
                    options.WithDirectNetwork(port: 7777);

                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                BindSessionEvents(session);
                CurrentRoom.RoomId = session.Id;
                CurrentRoom.PlayerCount = session.PlayerCount;
                JoinCode = session.Code;
                EnterConnectedRoom();
                SetStatus($"Hosting {roomName} in {EnvironmentName}. Code: {JoinCode}");
                return true;
            }
            catch (Exception ex)
            {
                HandleOnlineFailure($"Could not create {EnvironmentName} room.", ex, clearRoom: true);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        async Task<bool> JoinOnlineRoomAsync(RoomListing room, string password)
        {
            return await JoinOnlineAsync(
                room,
                password,
                () => MultiplayerService.Instance.JoinSessionByIdAsync(
                    room.RoomId,
                    new JoinSessionOptions { Password = string.IsNullOrEmpty(password) ? null : password }));
        }

        async Task<bool> JoinOnlineByCodeAsync(string code, string password)
        {
            var room = new RoomListing
            {
                RoomId = code,
                IsOnline = true,
                RoomName = "Relay Room",
                MaxPlayers = maxPlayers
            };
            return await JoinOnlineAsync(
                room,
                password,
                () => MultiplayerService.Instance.JoinSessionByCodeAsync(
                    code,
                    new JoinSessionOptions { Password = string.IsNullOrEmpty(password) ? null : password }));
        }

        async Task<bool> JoinOnlineAsync(RoomListing room, string password, Func<Task<ISession>> join)
        {
            password ??= "";
            if (room.IsLocked && !IsOnlinePasswordValid(password))
            {
                SetStatus($"Online room passwords must be {MinOnlinePasswordLength}-{MaxPasswordLength} characters.");
                return false;
            }

            IsBusy = true;
            SetStatus($"Joining {room.RoomName}...");

            try
            {
                await UgsBootstrap.EnsureReadyAsync();
                if (!PrepareOnlineNetwork()) return false;

                CurrentRoom = room.Copy();
                pendingLocalJoin = true;
                roomEnteredRaised = false;
                session = await join();
                BindSessionEvents(session);
                CurrentRoom.RoomId = session.Id;
                CurrentRoom.RoomName = session.Name;
                CurrentRoom.PlayerCount = session.PlayerCount;
                CurrentRoom.MaxPlayers = session.MaxPlayers;
                CurrentRoom.IsLocked = session.HasPassword;
                CurrentRoom.IsJoinLocked = session.IsLocked;
                JoinCode = session.Code;
                EnterConnectedRoom();
                SetStatus($"Joined {CurrentRoom.RoomName} in {EnvironmentName}.");
                return true;
            }
            catch (Exception ex)
            {
                HandleOnlineFailure($"Could not join {EnvironmentName} room.", ex, clearRoom: true);
                return false;
            }
            finally
            {
                IsBusy = false;
                StatusChanged?.Invoke();
            }
        }

        public async Task<bool> SetRoomLockedAsync(bool locked)
        {
            if (!UsesOnlineServices || session == null || !session.IsHost) return true;

            try
            {
                var hostSession = session.AsHost();
                hostSession.IsLocked = locked;
                await hostSession.SavePropertiesAsync();
                if (CurrentRoom != null) CurrentRoom.IsJoinLocked = locked;
                return true;
            }
            catch (Exception ex)
            {
                HandleOnlineFailure("Could not update room access.", ex, clearRoom: false);
                return false;
            }
        }

        public static bool IsOnlinePasswordValid(string password)
        {
            return string.IsNullOrEmpty(password) ||
                   password.Length >= MinOnlinePasswordLength && password.Length <= MaxPasswordLength;
        }

        bool ValidateRoomInput(string roomName, string password, bool online)
        {
            if (roomName.Length == 0)
            {
                SetStatus("Enter a room name.");
                return false;
            }

            if (roomName.Length > MaxRoomNameLength)
            {
                SetStatus($"Room names can be up to {MaxRoomNameLength} characters.");
                return false;
            }

            if (online && !IsOnlinePasswordValid(password))
            {
                SetStatus($"Online room passwords must be empty or {MinOnlinePasswordLength}-{MaxPasswordLength} characters.");
                return false;
            }

            if (!online && password.Length > MaxPasswordLength)
            {
                SetStatus($"Passwords can be up to {MaxPasswordLength} characters.");
                return false;
            }

            return true;
        }

        void SetStatus(string message)
        {
            Status = message;
            GameDiagnostics.Info("network", "status", $"message=\"{message}\"");
            Debug.Log($"[RoomConnector] {message}");
            StatusChanged?.Invoke();
        }

        bool PrepareNetworkManager()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                Debug.LogError("[RoomConnector] No NetworkManager in scene.");
                SetStatus("Network manager is missing.");
                return false;
            }

            if (manager.IsListening)
            {
                Debug.LogWarning("[RoomConnector] NetworkManager is already running.");
                SetStatus("A room connection is already running.");
                return false;
            }

            manager.NetworkConfig.ForceSamePrefabs = false;
            return true;
        }

        void ConfigureConnectionApproval(NetworkManager manager)
        {
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = ApproveConnection;
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            var manager = NetworkManager.Singleton;
            var isHostClient = request.ClientNetworkId == NetworkManager.ServerClientId;
            var password = Encoding.UTF8.GetString(request.Payload ?? Array.Empty<byte>());
            var phase = ChameleonRoundManager.Instance != null
                ? ChameleonRoundManager.Instance.Phase.Value
                : GamePhase.Lobby;

            response.CreatePlayerObject = false;
            response.Pending = false;

            if (!isHostClient && manager != null && manager.ConnectedClientsIds.Count >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "The room is full.";
            }
            else if (!isHostClient && phase != GamePhase.Lobby)
            {
                response.Approved = false;
                response.Reason = "The match has already started.";
            }
            else if (!isHostClient && password != hostPassword)
            {
                response.Approved = false;
                response.Reason = "Incorrect room password.";
            }
            else
            {
                response.Approved = true;
            }

            GameDiagnostics.Info("network", "connection_approval",
                $"clientId={request.ClientNetworkId} hostClient={isHostClient} " +
                $"approved={response.Approved} phase={phase} reason=\"{response.Reason}\"");
        }

        void RegisterNetworkCallbacks()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || registeredNetworkManager == manager) return;

            UnregisterNetworkCallbacks();
            registeredNetworkManager = manager;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        void UnregisterNetworkCallbacks()
        {
            if (registeredNetworkManager == null) return;

            registeredNetworkManager.OnClientConnectedCallback -= OnClientConnected;
            registeredNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (!registeredNetworkManager.IsListening)
                registeredNetworkManager.ConnectionApprovalCallback = null;
            registeredNetworkManager = null;
        }

        void OnDestroy()
        {
            if (discovery != null)
                discovery.RoomsChanged -= OnLocalRoomsChanged;
            UnbindSessionEvents(session);
            UnregisterNetworkCallbacks();
        }

        void OnClientConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            GameDiagnostics.Info("network", "client_connected",
                $"clientId={clientId} local={clientId == manager.LocalClientId} " +
                $"isServer={manager.IsServer} players={ConnectedPlayerCount}");

            if (CurrentRoom != null)
                CurrentRoom.PlayerCount = ConnectedPlayerCount;

            if (clientId == manager.LocalClientId)
            {
                pendingLocalJoin = false;
                SetStatus(manager.IsHost
                    ? $"Hosting {CurrentRoom?.RoomName ?? "room"}. Players: {ConnectedPlayerCount}"
                    : $"Joined {CurrentRoom?.RoomName ?? "room"}.");
                EnterConnectedRoom();
                return;
            }

            if (manager.IsServer)
                SetStatus($"Player joined. Players: {ConnectedPlayerCount}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            var disconnectDetails =
                $"clientId={clientId} local={clientId == manager.LocalClientId} " +
                $"isServer={manager.IsServer} reason=\"{manager.DisconnectReason}\"";
            if (string.IsNullOrWhiteSpace(manager.DisconnectReason))
                GameDiagnostics.Info("network", "client_disconnected", disconnectDetails);
            else
                GameDiagnostics.Warning("network", "client_disconnected", disconnectDetails);

            if (CurrentRoom != null)
                CurrentRoom.PlayerCount = ConnectedPlayerCount;

            if (clientId == manager.LocalClientId)
            {
                var reason = manager.DisconnectReason;
                var wasJoining = pendingLocalJoin;
                if (!isLeaving && session != null)
                    _ = CleanupSessionAfterDisconnectAsync();
                ClearLocalRoom();
                SetStatus(string.IsNullOrWhiteSpace(reason)
                    ? "Disconnected from host."
                    : reason);
                if (!wasJoining)
                    RoomLeft?.Invoke();
                return;
            }

            if (manager.IsServer)
                SetStatus($"Player left. Players: {ConnectedPlayerCount}");
        }

        RoomListing CreateAdvertisement()
        {
            if (CurrentRoom == null) return null;
            var listing = CurrentRoom.Copy();
            listing.PlayerCount = ConnectedPlayerCount;
            return listing;
        }

        void ClearLocalRoom()
        {
            discovery?.StopAll();
            CurrentRoom = null;
            hostPassword = "";
            pendingLocalJoin = false;
            roomEnteredRaised = false;
            var manager = NetworkManager.Singleton;
            if (manager != null && !manager.IsListening)
                manager.ConnectionApprovalCallback = null;
        }

        static bool PrepareDirectConnection(string address, ushort port)
        {
            var manager = NetworkManager.Singleton;
            var transport = manager != null ? manager.GetComponent<UnityTransport>() : null;
            if (transport == null)
            {
                Debug.LogError("[RoomConnector] NetworkManager needs a UnityTransport component.");
                return false;
            }

            transport.SetConnectionData(address, port);
            return true;
        }

        bool PrepareOnlineNetwork()
        {
            if (!PrepareNetworkManager()) return false;

            var manager = NetworkManager.Singleton;
            manager.NetworkConfig.ProtocolVersion = OnlineProtocolVersion;
            manager.NetworkConfig.ForceSamePrefabs = true;
            manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            hostPassword = "";
            ConfigureConnectionApproval(manager);
            RegisterNetworkCallbacks();
            discovery.StopAll();
            return true;
        }

        void EnterConnectedRoom()
        {
            var manager = NetworkManager.Singleton;
            if (roomEnteredRaised || manager == null || !manager.IsListening) return;
            roomEnteredRaised = true;
            pendingLocalJoin = false;
            RoomEntered?.Invoke();
        }

        void BindSessionEvents(ISession activeSession)
        {
            if (activeSession == null) return;
            activeSession.Deleted += OnOnlineSessionClosed;
            activeSession.RemovedFromSession += OnOnlineSessionClosed;
            activeSession.SessionHostChanged += OnOnlineSessionHostChanged;
        }

        void UnbindSessionEvents(ISession activeSession)
        {
            if (activeSession == null) return;
            activeSession.Deleted -= OnOnlineSessionClosed;
            activeSession.RemovedFromSession -= OnOnlineSessionClosed;
            activeSession.SessionHostChanged -= OnOnlineSessionHostChanged;
        }

        void OnOnlineSessionClosed()
        {
            if (isLeaving) return;
            if (session == null && CurrentRoom == null) return;
            UnbindSessionEvents(session);
            session = null;
            ShutdownNetwork();
            ClearLocalRoom();
            JoinCode = "";
            SetStatus("The room was closed by its host.");
            RoomLeft?.Invoke();
        }

        async void OnOnlineSessionHostChanged(string newHostPlayerId)
        {
            if (isLeaving || session == null) return;

            try
            {
                if (AuthenticationService.Instance.PlayerId == newHostPlayerId && session.IsHost)
                    await session.AsHost().DeleteAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RoomConnector] Could not clean up migrated host session: {ex.Message}");
            }
            finally
            {
                OnOnlineSessionClosed();
            }
        }

        async Task CleanupSessionAfterDisconnectAsync()
        {
            var disconnectedSession = session;
            if (disconnectedSession == null) return;
            UnbindSessionEvents(disconnectedSession);
            session = null;

            try
            {
                if (disconnectedSession.IsHost)
                    await disconnectedSession.AsHost().DeleteAsync();
                else
                    await disconnectedSession.LeaveAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RoomConnector] Session cleanup after disconnect failed: {ex.Message}");
            }
        }

        void OnLocalRoomsChanged()
        {
            if (!UsesOnlineServices)
                RoomsChanged?.Invoke();
        }

        void HandleOnlineFailure(string userMessage, Exception exception, bool clearRoom)
        {
            GameDiagnostics.Error("network", "online_operation_failed",
                $"environment={EnvironmentName} type={exception.GetType().Name} message=\"{exception.Message}\"");
            Debug.LogException(exception);

            if (clearRoom)
            {
                UnbindSessionEvents(session);
                session = null;
                CleanupFailedStart();
                ClearLocalRoom();
            }

            SetStatus(exception is InvalidOperationException ? exception.Message : userMessage);
        }

        static void CleanupFailedStart()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.Shutdown();
        }

        void ShutdownNetwork()
        {
            var manager = NetworkManager.Singleton;
            GameDiagnostics.Info("network", "shutdown",
                $"wasListening={manager != null && manager.IsListening} " +
                $"wasHost={manager != null && manager.IsHost}");
            discovery?.StopAll();
            if (manager != null && manager.IsListening)
                manager.Shutdown();
        }
    }
}
