using UnityEngine;
using UnityEngine.UI;

namespace MechaChameleon
{
    public sealed class LocalRoomRowView : MonoBehaviour
    {
        [SerializeField] Text roomNameLabel;
        [SerializeField] Text playerCountLabel;
        [SerializeField] Text accessLabel;
        [SerializeField] Button joinButton;

        public RoomListing Room { get; private set; }
        public Button JoinButton => joinButton;

        public void Configure(Text roomName, Text playerCount, Text access, Button join)
        {
            roomNameLabel = roomName;
            playerCountLabel = playerCount;
            accessLabel = access;
            joinButton = join;
        }

        public void Show(RoomListing room)
        {
            Room = room;
            roomNameLabel.text = room.RoomName;
            playerCountLabel.text = $"{room.PlayerCount} / {room.MaxPlayers}";
            accessLabel.text = room.IsLocked ? "LOCKED" : "OPEN";
            accessLabel.color = room.IsLocked
                ? new Color(1f, 0.82f, 0.28f)
                : new Color(0.55f, 1f, 0.72f);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            Room = null;
            gameObject.SetActive(false);
        }
    }
}
