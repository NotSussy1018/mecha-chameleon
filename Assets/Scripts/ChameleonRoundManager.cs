using System.Collections.Generic;
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
        [SerializeField] private float paintSeconds = 30f;
        [SerializeField] private float huntSeconds = 60f;

        public NetworkVariable<GamePhase> Phase { get; } = new(GamePhase.Lobby);
        public NetworkVariable<double> PhaseEndsAt { get; } = new(0);
        public NetworkVariable<bool> HidersWonLastRound { get; } = new(false);

        readonly Dictionary<ulong, ChameleonPlayer> players = new();
        readonly List<ChameleonPlayer> practiceHiders = new();

        public static ChameleonRoundManager Instance { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
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
            if (Instance == this) Instance = null;
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
        }

        public void ResetToLobby()
        {
            if (!IsServer) return;
            CancelInvoke(nameof(ReturnPlayersToLobby));
            Phase.Value = GamePhase.Lobby;
            PhaseEndsAt.Value = 0;
            HidersWonLastRound.Value = false;

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

        public Vector3 HunterPlatformCenter => hunterPlatform != null ? hunterPlatform.position : DefaultHunterPlatformPosition;

        public bool IsOnHunterPlatform(Vector3 position)
        {
            if (hunterPlatform == null) return false;

            var size = hunterPlatformSize;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                size = DefaultHunterPlatformSize;

            var local = Quaternion.Inverse(hunterPlatform.rotation) * (position - hunterPlatform.position);
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
        }

        void OnClientDisconnected(ulong clientId)
        {
            players.Remove(clientId);
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
            PhaseEndsAt.Value = 0;
            HidersWonLastRound.Value = hidersWon;

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
            Debug.Log(hidersWon ? "Hiders win." : "Seeker wins.");
        }

        Transform GetSpawn(ulong clientId)
        {
            return GetLobbySpawn(clientId);
        }

        Transform GetLobbySpawn(ulong clientId)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return transform;
            return spawnPoints[(int)(clientId % (ulong)spawnPoints.Length)];
        }

        Transform GetHiderSpawn(ulong clientId)
        {
            if (hiderSpawnPoints == null || hiderSpawnPoints.Length == 0) return GetLobbySpawn(clientId);
            return hiderSpawnPoints[(int)(clientId % (ulong)hiderSpawnPoints.Length)];
        }

        Transform GetHunterSpawn()
        {
            return hunterSpawnPoint != null ? hunterSpawnPoint : GetLobbySpawn(NetworkManager.ServerClientId);
        }

        Transform GetPracticeSpawn()
        {
            if (hiderSpawnPoints != null && hiderSpawnPoints.Length > 0)
                return hiderSpawnPoints[Mathf.Min(1, hiderSpawnPoints.Length - 1)];

            return GetLobbySpawn(NetworkManager.ServerClientId);
        }
    }
}
