using UnityEngine;
using UnityEngine.UI;

namespace MechaChameleon
{
    public sealed class RoomPlayerRowView : MonoBehaviour
    {
        [SerializeField] Text playerNameLabel;
        [SerializeField] Text badgeLabel;
        [SerializeField] Text roleLabel;

        public void Configure(Text playerName, Text badge, Text role)
        {
            playerNameLabel = playerName;
            badgeLabel = badge;
            roleLabel = role;
        }

        public void Show(string playerName, bool isHost, bool wantsHunter)
        {
            playerNameLabel.text = playerName;
            badgeLabel.text = isHost ? "HOST" : "";
            roleLabel.text = wantsHunter ? "HUNTER" : "HIDER";
            roleLabel.color = wantsHunter
                ? new Color(1f, 0.82f, 0.28f)
                : new Color(0.31f, 0.91f, 0.86f);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
