using System;
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
        const ushort LocalPort = 7778;

        [SerializeField] private int maxPlayers = 8;
        [SerializeField] private string sessionName = "Paint Hideout";
        [SerializeField] private string relayRegion = "";
        [SerializeField] private bool useRelay = true;

        public string JoinCode { get; private set; } = "";
        public string Status { get; private set; } = "Idle";
        public int ConnectedPlayerCount
        {
            get
            {
                var manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening) return 0;
                if (manager.IsServer) return manager.ConnectedClientsIds.Count;
                return manager.IsConnectedClient ? 1 : 0;
            }
        }

        ISession session;
        NetworkManager registeredNetworkManager;

        public async void Host()
        {
            await RunAsync(HostAsync);
        }

        public void HostLocal()
        {
            if (!PrepareNetworkManager()) return;

            if (!PrepareDirectConnection("127.0.0.1")) return;
            RegisterNetworkCallbacks();

            if (NetworkManager.Singleton.StartHost())
            {
                JoinCode = "LOCAL";
                SetStatus($"Hosting locally on 127.0.0.1:{LocalPort}. Use Join Local in another editor/player.");
            }
            else
            {
                CleanupFailedStart();
                SetStatus($"Could not start local host. UDP port {LocalPort} may already be in use.");
            }
        }

        public async void Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("Enter a join code.");
                return;
            }

            await RunAsync(() => JoinAsync(code.Trim()));
        }

        public void JoinLocal()
        {
            if (!PrepareNetworkManager()) return;

            if (!PrepareDirectConnection("127.0.0.1")) return;
            RegisterNetworkCallbacks();

            if (NetworkManager.Singleton.StartClient())
                SetStatus($"Joining local host at 127.0.0.1:{LocalPort}...");
            else
            {
                CleanupFailedStart();
                SetStatus("Could not start local client.");
            }
        }

        public async void Leave()
        {
            try
            {
                if (session != null)
                {
                    await session.LeaveAsync();
                    session = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RoomConnector] Leave failed: {ex.Message}");
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            JoinCode = "";
            SetStatus("Left room.");
        }

        async Task RunAsync(Func<Task> work)
        {
            try
            {
                await EnsureSignedInAsync();
                await work();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                Debug.LogException(ex);
            }
        }

        async Task HostAsync()
        {
            SetStatus("Creating room...");

            var options = new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = maxPlayers,
                IsPrivate = false
            };

            if (useRelay) options.WithRelayNetwork(string.IsNullOrWhiteSpace(relayRegion) ? null : relayRegion);
            else options.WithDirectNetwork(port: 7777);

            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            JoinCode = session.Code;
            SetStatus($"Hosting. Code: {JoinCode}");
        }

        async Task JoinAsync(string code)
        {
            SetStatus("Joining room...");
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            JoinCode = code;
            SetStatus($"Joined. Code: {JoinCode}");
        }

        static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        void SetStatus(string message)
        {
            Status = message;
            Debug.Log($"[RoomConnector] {message}");
        }

        bool PrepareNetworkManager()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[RoomConnector] No NetworkManager in scene.");
                return false;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[RoomConnector] NetworkManager is already running.");
                return false;
            }

            NetworkManager.Singleton.NetworkConfig.ForceSamePrefabs = false;
            return true;
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
            registeredNetworkManager = null;
        }

        void OnDestroy()
        {
            UnregisterNetworkCallbacks();
        }

        void OnClientConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            if (clientId == manager.LocalClientId)
            {
                SetStatus(manager.IsHost
                    ? $"Hosting locally. Players: {ConnectedPlayerCount}"
                    : $"Connected to local host. Client: {clientId}");
                return;
            }

            if (manager.IsServer)
                SetStatus($"Client {clientId} connected. Players: {ConnectedPlayerCount}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            if (clientId == manager.LocalClientId)
            {
                SetStatus("Disconnected from host.");
                return;
            }

            if (manager.IsServer)
                SetStatus($"Client {clientId} disconnected. Players: {ConnectedPlayerCount}");
        }

        static bool PrepareDirectConnection(string address)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[RoomConnector] NetworkManager needs a UnityTransport component.");
                return false;
            }

            // A failed or interrupted play session can leave UTP initialized even
            // while NetworkManager.IsListening is false.
            transport.Shutdown();
            transport.SetConnectionData(address, LocalPort);
            return true;
        }

        static void CleanupFailedStart()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.Shutdown();
            manager.GetComponent<UnityTransport>()?.Shutdown();
        }
    }
}
