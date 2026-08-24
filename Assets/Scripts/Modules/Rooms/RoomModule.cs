using UnityEngine;

namespace MechaChameleon.Rooms
{
    public sealed class RoomModule : MonoBehaviour
    {
        [SerializeField] string roomId = "room";
        [SerializeField] string displayName = "Room";
        [SerializeField] GameObject contentRoot;
        [SerializeField] Transform[] lobbySpawnPoints = { };
        [SerializeField] Transform[] hiderSpawnPoints = { };
        [SerializeField] Transform hunterSpawnPoint;
        [SerializeField] Transform hunterPlatform;
        [SerializeField] Vector3 hunterPlatformSize = new(4f, 3f, 3f);

        public string RoomId => roomId;
        public string DisplayName => displayName;
        public int LobbySpawnCount => lobbySpawnPoints?.Length ?? 0;
        public int HiderSpawnCount => hiderSpawnPoints?.Length ?? 0;
        public Transform HunterSpawnPoint => hunterSpawnPoint;
        public Transform HunterPlatform => hunterPlatform;
        public Vector3 HunterPlatformSize => hunterPlatformSize;

        public Transform GetLobbySpawn(ulong playerId)
        {
            return GetSpawn(lobbySpawnPoints, playerId);
        }

        public Transform GetHiderSpawn(ulong playerId)
        {
            return GetSpawn(hiderSpawnPoints, playerId);
        }

        public void SetContentActive(bool active)
        {
            if (contentRoot != null && contentRoot != gameObject)
                contentRoot.SetActive(active);
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(roomId) &&
                   HasAll(lobbySpawnPoints) &&
                   HasAll(hiderSpawnPoints) &&
                   hunterSpawnPoint != null &&
                   hunterPlatform != null &&
                   hunterPlatformSize.x > 0f &&
                   hunterPlatformSize.y > 0f &&
                   hunterPlatformSize.z > 0f;
        }

        public void Configure(
            string id,
            string name,
            GameObject root,
            Transform[] lobbySpawns,
            Transform[] hiderSpawns,
            Transform hunterSpawn,
            Transform platform,
            Vector3 platformSize)
        {
            roomId = id;
            displayName = name;
            contentRoot = root;
            lobbySpawnPoints = lobbySpawns;
            hiderSpawnPoints = hiderSpawns;
            hunterSpawnPoint = hunterSpawn;
            hunterPlatform = platform;
            hunterPlatformSize = platformSize;
        }

        static Transform GetSpawn(Transform[] spawnPoints, ulong playerId)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return null;
            return spawnPoints[(int)(playerId % (ulong)spawnPoints.Length)];
        }

        static bool HasAll(Transform[] transforms)
        {
            if (transforms == null || transforms.Length == 0) return false;
            for (var i = 0; i < transforms.Length; i++)
                if (transforms[i] == null) return false;
            return true;
        }
    }
}
