using System.Collections.Generic;
using MechaChameleon.Rooms;
using Unity.Netcode;
using UnityEngine;

namespace MechaChameleon
{
    public sealed class ChameleonRoundManager : NetworkBehaviour
    {
        static readonly Vector3 DefaultHunterPlatformPosition = new(0f, 0.15f, -1.5f);
        static readonly Vector3 DefaultHunterPlatformSize = new(4f, 3f, 3f);
        static readonly Vector3 DefaultHunterSpawnPosition = new(0f, 1f, 20.8f);
        static readonly Vector3[] DefaultHiderSpawnPositions =
        {
            new(-4f, 1f, 24f),
            new(-1.5f, 1f, 24f),
            new(1f, 1f, 24f),
            new(3.5f, 1f, 24f)
        };

        [SerializeField] private ChameleonPlayer playerPrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private Transform[] hiderSpawnPoints;
        [SerializeField] private Transform hunterSpawnPoint;
        [SerializeField] private Transform hunterPlatform;
        [SerializeField] private Vector3 hunterPlatformSize = DefaultHunterPlatformSize;
        [SerializeField] private RoomModule[] roomModules = { };
        [SerializeField] private int startingRoomIndex;
        [SerializeField] private float paintSeconds = 30f;
        [SerializeField] private float huntSeconds = 60f;
        [SerializeField] private float resultSeconds = 3f;

        public NetworkVariable<GamePhase> Phase { get; } = new(GamePhase.Lobby);
        public NetworkVariable<double> PhaseEndsAt { get; } = new(0);
        public NetworkVariable<bool> HidersWonLastRound { get; } = new(false);
        public NetworkVariable<byte> ActiveRoomIndex { get; } = new(0);
        public float ResultSeconds => resultSeconds;
        public RoomModule ActiveRoom => GetRoomModule(ActiveRoomIndex.Value);

        readonly Dictionary<ulong, ChameleonPlayer> players = new();
        readonly List<ChameleonPlayer> practiceHiders = new();

        public static ChameleonRoundManager Instance { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            GameDiagnostics.Info("round", "manager_spawned",
                $"isServer={IsServer} isClient={IsClient} networkObjectId={NetworkObjectId}");
            ActiveRoomIndex.OnValueChanged += OnActiveRoomChanged;
            if (IsServer && roomModules != null && roomModules.Length > 0)
                ActiveRoomIndex.Value = (byte)Mathf.Clamp(startingRoomIndex, 0, Mathf.Min(255, roomModules.Length - 1));
            ApplyRoomActivation(ActiveRoomIndex.Value);
            EnsureHunterPlatform();
            EnsureRoomSpawns();

            if (!IsServer) return;
            NetworkManager.Singleton.OnClientConnectedCallback += SpawnPlayer;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
                SpawnPlayer(clientId);
        }

        public override void OnNetworkDespawn()
        {
            GameDiagnostics.Info("round", "manager_despawned",
                $"isServer={IsServer} players={players.Count} practiceHiders={practiceHiders.Count}");
            ActiveRoomIndex.OnValueChanged -= OnActiveRoomChanged;
            if (Instance == this) Instance = null;
            players.Clear();
            practiceHiders.Clear();

            if (!IsServer || NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnClientConnectedCallback -= SpawnPlayer;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        void Update()
        {
            if (!IsServer) return;
            if (Phase.Value == GamePhase.Paint && NetworkManager.ServerTime.Time >= PhaseEndsAt.Value)
                BeginHunt();
            else if (Phase.Value == GamePhase.Hunt && NetworkManager.ServerTime.Time >= PhaseEndsAt.Value)
                EndRound(hidersWon: true);
            else if (Phase.Value == GamePhase.Result &&
                     PhaseEndsAt.Value > 0 &&
                     NetworkManager.ServerTime.Time >= PhaseEndsAt.Value)
                ResetToLobby();
        }

        public int RemainingSeconds
        {
            get
            {
                if (Phase.Value != GamePhase.Paint && Phase.Value != GamePhase.Hunt) return 0;
                var manager = NetworkManager.Singleton;
                var now = manager != null && manager.IsListening ? manager.ServerTime.Time : Time.timeAsDouble;
                return Mathf.Max(0, Mathf.CeilToInt((float)(PhaseEndsAt.Value - now)));
            }
        }

        public void StartRound()
        {
            if (!IsServer) return;

            if (Phase.Value == GamePhase.Result)
                ResetToLobby();

            if (Phase.Value == GamePhase.Lobby)
                StartPaintPhase();
        }

        public void StartPaintPhase()
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby) return;

            if (players.Count == 1 && practiceHiders.Count == 0)
                SpawnPracticeHider();

            AssignRoles();
            SendPlayersToPaintPositions();
            Phase.Value = GamePhase.Paint;
            PhaseEndsAt.Value = NetworkManager.ServerTime.Time + paintSeconds;
            GameDiagnostics.Info("round", "phase_changed",
                $"phase={Phase.Value} duration={paintSeconds:0.##} players={players.Count}");
        }

        public void BeginHunt()
        {
            if (!IsServer || Phase.Value == GamePhase.Hunt) return;
            if (Phase.Value == GamePhase.Result) return;

            if (Phase.Value == GamePhase.Lobby)
            {
                if (players.Count == 1 && practiceHiders.Count == 0)
                    SpawnPracticeHider();

                AssignRoles();
                SendPlayersToPaintPositions();
            }

            SendHuntersToHuntPositions();
            Phase.Value = GamePhase.Hunt;
            PhaseEndsAt.Value = NetworkManager.ServerTime.Time + huntSeconds;
            GameDiagnostics.Info("round", "phase_changed",
                $"phase={Phase.Value} duration={huntSeconds:0.##} players={players.Count}");
        }

        public void ResetToLobby()
        {
            if (!IsServer) return;
            CancelInvoke(nameof(ReturnPlayersToLobby));
            Phase.Value = GamePhase.Lobby;
            PhaseEndsAt.Value = 0;
            HidersWonLastRound.Value = false;
            GameDiagnostics.Info("round", "phase_changed",
                $"phase={Phase.Value} players={players.Count}");

            foreach (var player in players.Values)
            {
                player.ResetForLobby();
                player.TeleportFromServer(GetLobbySpawn(player.OwnerClientId).position);
            }

            foreach (var hider in practiceHiders)
            {
                if (hider != null && hider.NetworkObject.IsSpawned)
                    hider.NetworkObject.Despawn();
            }

            practiceHiders.Clear();
        }

        public void SpawnPracticeHider()
        {
            if (!IsServer || playerPrefab == null) return;

            var spawn = GetPracticeSpawn();
            var hider = Instantiate(playerPrefab, spawn.position, spawn.rotation);
            hider.NetworkObject.SpawnWithOwnership(NetworkManager.ServerClientId);
            hider.SetServerState(
                PlayerRole.Hider,
                alive: true,
                head: new Color32(119, 102, 78, 255),
                body: new Color32(68, 104, 72, 255),
                pose: PoseId.Crouch
            );

            practiceHiders.Add(hider);
            GameDiagnostics.Info("round", "practice_hider_spawned",
                $"networkObjectId={hider.NetworkObjectId}");
        }

        public Vector3 GetRespawnPosition(ulong clientId)
        {
            if (players.TryGetValue(clientId, out var player))
            {
                if (Phase.Value == GamePhase.Paint && player.Role.Value == PlayerRole.Hider)
                    return GetHiderSpawn(clientId).position;

                if (Phase.Value == GamePhase.Hunt && player.Role.Value == PlayerRole.Seeker)
                    return GetHunterSpawn().position;
            }

            return GetLobbySpawn(clientId).position;
        }

        public Vector3 HunterPlatformCenter
        {
            get
            {
                var platform = ActiveRoom != null ? ActiveRoom.HunterPlatform : hunterPlatform;
                return platform != null ? platform.position : DefaultHunterPlatformPosition;
            }
        }

        public bool IsOnHunterPlatform(Vector3 position)
        {
            var room = ActiveRoom;
            var platform = room != null ? room.HunterPlatform : hunterPlatform;
            if (platform == null) return false;

            var size = room != null ? room.HunterPlatformSize : hunterPlatformSize;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                size = DefaultHunterPlatformSize;

            var local = Quaternion.Inverse(platform.rotation) * (position - platform.position);
            return Mathf.Abs(local.x) <= size.x * 0.5f &&
                   local.y >= -0.5f &&
                   local.y <= size.y &&
                   Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        public void ReportHit(ChameleonPlayer target)
        {
            if (!IsServer || Phase.Value != GamePhase.Hunt || target == null) return;
            if (target.Role.Value != PlayerRole.Hider || !target.Alive.Value) return;

            target.Alive.Value = false;
            GameDiagnostics.Info("round", "hider_hit",
                $"targetOwner={target.OwnerClientId} networkObjectId={target.NetworkObjectId}");

            foreach (var hider in practiceHiders)
            {
                if (hider != null && hider.Alive.Value)
                    return;
            }

            foreach (var player in players.Values)
            {
                if (player.Role.Value == PlayerRole.Hider && player.Alive.Value)
                    return;
            }

            EndRound(hidersWon: false);
        }

        void SpawnPlayer(ulong clientId)
        {
            if (players.ContainsKey(clientId) || playerPrefab == null) return;

            var spawn = GetSpawn(clientId);
            var player = Instantiate(playerPrefab, spawn.position, spawn.rotation);
            player.NetworkObject.SpawnAsPlayerObject(clientId);
            player.ResetForLobby();
            players[clientId] = player;
            GameDiagnostics.Info("round", "player_spawned",
                $"clientId={clientId} networkObjectId={player.NetworkObjectId} players={players.Count}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            players.Remove(clientId);
            GameDiagnostics.Info("round", "player_removed",
                $"clientId={clientId} players={players.Count}");
        }

        void AssignRoles()
        {
            var shouldHaveSeeker = players.Count > 1 || practiceHiders.Count > 0;
            var seekerClientId = shouldHaveSeeker ? SelectSeekerClientId() : ulong.MaxValue;

            foreach (var pair in players)
            {
                pair.Value.Role.Value = pair.Key == seekerClientId ? PlayerRole.Seeker : PlayerRole.Hider;
                pair.Value.Alive.Value = true;
            }

            GameDiagnostics.Info("round", "roles_assigned",
                $"seekerClientId={seekerClientId} players={players.Count} " +
                $"practiceHiders={practiceHiders.Count}");
        }

        ulong SelectSeekerClientId()
        {
            var candidates = new List<ulong>();
            foreach (var pair in players)
            {
                if (IsOnHunterPlatform(pair.Value.transform.position))
                    candidates.Add(pair.Key);
            }

            if (candidates.Count > 0)
                return candidates[Random.Range(0, candidates.Count)];

            var index = Random.Range(0, players.Count);
            foreach (var clientId in players.Keys)
            {
                if (index == 0) return clientId;
                index--;
            }

            return ulong.MaxValue;
        }

        void SendPlayersToPaintPositions()
        {
            foreach (var pair in players)
            {
                if (pair.Value.Role.Value == PlayerRole.Hider)
                    pair.Value.TeleportFromServer(GetHiderSpawn(pair.Key).position);
                else
                    pair.Value.TeleportFromServer(GetLobbySpawn(pair.Key).position);
            }
        }

        void SendHuntersToHuntPositions()
        {
            foreach (var pair in players)
            {
                if (pair.Value.Role.Value == PlayerRole.Seeker)
                    pair.Value.TeleportFromServer(GetHunterSpawn().position);
            }
        }

        void EnsureHunterPlatform()
        {
            if (ActiveRoom != null && ActiveRoom.HunterPlatform != null) return;
            if (hunterPlatform != null) return;

            var existing = GameObject.Find("Hunter Choice Platform");
            if (existing != null)
            {
                hunterPlatform = existing.transform;
                return;
            }

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "Hunter Choice Platform";
            platform.transform.position = DefaultHunterPlatformPosition;
            platform.transform.localScale = new Vector3(DefaultHunterPlatformSize.x, 0.15f, DefaultHunterPlatformSize.z);

            var renderer = platform.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(0.95f, 0.78f, 0.18f);

            hunterPlatform = platform.transform;
        }

        void EnsureRoomSpawns()
        {
            if (ActiveRoom != null && ActiveRoom.IsConfigured()) return;
            if (hiderSpawnPoints == null || hiderSpawnPoints.Length == 0)
            {
                hiderSpawnPoints = new Transform[DefaultHiderSpawnPositions.Length];
                for (var i = 0; i < hiderSpawnPoints.Length; i++)
                {
                    var spawn = new GameObject($"Runtime Hider Spawn {i + 1}").transform;
                    spawn.position = DefaultHiderSpawnPositions[i];
                    hiderSpawnPoints[i] = spawn;
                }
            }

            if (hunterSpawnPoint == null)
            {
                hunterSpawnPoint = new GameObject("Runtime Hunter Spawn").transform;
                hunterSpawnPoint.position = DefaultHunterSpawnPosition;
            }
        }

        void EndRound(bool hidersWon)
        {
            Phase.Value = GamePhase.Result;
            PhaseEndsAt.Value = NetworkManager.ServerTime.Time + Mathf.Max(0.5f, resultSeconds);
            HidersWonLastRound.Value = hidersWon;
            GameDiagnostics.Info("round", "round_ended",
                $"hidersWon={hidersWon} resultDuration={resultSeconds:0.##}");

            if (hidersWon)
                ReturnPlayersToLobby();
            else
                Invoke(nameof(ReturnPlayersToLobby), 0.4f);

            AnnounceResultClientRpc(hidersWon);
        }

        void ReturnPlayersToLobby()
        {
            if (!IsServer) return;

            foreach (var player in players.Values)
                player.TeleportFromServer(GetLobbySpawn(player.OwnerClientId).position);
        }

        [ClientRpc]
        void AnnounceResultClientRpc(bool hidersWon)
        {
            GameDiagnostics.Info("round", "result_received", $"hidersWon={hidersWon}");
            Debug.Log(hidersWon ? "Hiders win." : "Seeker wins.");
        }

        Transform GetSpawn(ulong clientId)
        {
            return GetLobbySpawn(clientId);
        }

        Transform GetLobbySpawn(ulong clientId)
        {
            var modularSpawn = ActiveRoom?.GetLobbySpawn(clientId);
            if (modularSpawn != null) return modularSpawn;
            if (spawnPoints == null || spawnPoints.Length == 0) return transform;
            return spawnPoints[(int)(clientId % (ulong)spawnPoints.Length)];
        }

        Transform GetHiderSpawn(ulong clientId)
        {
            var modularSpawn = ActiveRoom?.GetHiderSpawn(clientId);
            if (modularSpawn != null) return modularSpawn;
            if (hiderSpawnPoints == null || hiderSpawnPoints.Length == 0) return GetLobbySpawn(clientId);
            return hiderSpawnPoints[(int)(clientId % (ulong)hiderSpawnPoints.Length)];
        }

        Transform GetHunterSpawn()
        {
            if (ActiveRoom != null && ActiveRoom.HunterSpawnPoint != null)
                return ActiveRoom.HunterSpawnPoint;
            return hunterSpawnPoint != null ? hunterSpawnPoint : GetLobbySpawn(NetworkManager.ServerClientId);
        }

        Transform GetPracticeSpawn()
        {
            if (hiderSpawnPoints != null && hiderSpawnPoints.Length > 0)
                return hiderSpawnPoints[Mathf.Min(1, hiderSpawnPoints.Length - 1)];

            return GetLobbySpawn(NetworkManager.ServerClientId);
        }

        public bool TrySelectRoom(string roomId)
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby || string.IsNullOrWhiteSpace(roomId) || roomModules == null)
                return false;

            for (var i = 0; i < roomModules.Length && i <= byte.MaxValue; i++)
            {
                if (roomModules[i] == null || roomModules[i].RoomId != roomId) continue;
                ActiveRoomIndex.Value = (byte)i;
                foreach (var player in players.Values)
                    player.TeleportFromServer(GetLobbySpawn(player.OwnerClientId).position);
                return true;
            }

            return false;
        }

        RoomModule GetRoomModule(int index)
        {
            if (roomModules == null || index < 0 || index >= roomModules.Length) return null;
            var room = roomModules[index];
            return room != null && room.IsConfigured() ? room : null;
        }

        void OnActiveRoomChanged(byte previous, byte current)
        {
            ApplyRoomActivation(current);
            GameDiagnostics.Info("round", "room_module_changed",
                $"previous={previous} current={current} roomId={ActiveRoom?.RoomId ?? "fallback"}");
        }

        void ApplyRoomActivation(int activeIndex)
        {
            if (roomModules == null) return;

            for (var i = 0; i < roomModules.Length; i++)
            {
                if (roomModules[i] != null)
                    roomModules[i].SetContentActive(i == activeIndex);
            }
        }
    }
}
