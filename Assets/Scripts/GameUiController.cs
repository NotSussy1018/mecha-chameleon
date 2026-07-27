using UnityEngine;
using UnityEngine.UI;

namespace MechaChameleon
{
    public sealed class GameUiController : MonoBehaviour
    {
        [Header("Surfaces")]
        [SerializeField] GameObject menuBackground;
        [SerializeField] GameObject homePanel;
        [SerializeField] GameObject createRoomPanel;
        [SerializeField] GameObject joinRoomPanel;
        [SerializeField] GameObject roomPanel;
        [SerializeField] GameObject optionsPanel;
        [SerializeField] GameObject gameHud;
        [SerializeField] GameObject passwordModal;
        [SerializeField] GameObject resultOverlay;

        [Header("Home")]
        [SerializeField] Button createRoomButton;
        [SerializeField] Button joinRoomButton;
        [SerializeField] Button homeOptionsButton;

        [Header("Create Room")]
        [SerializeField] Button createConfirmButton;
        [SerializeField] Button createBackButton;

        [Header("Join Room")]
        [SerializeField] Button joinBackButton;
        [SerializeField] Button joinLockedRoomButton;
        [SerializeField] Button joinOpenRoomButton;

        [Header("Room")]
        [SerializeField] Button startPreviewButton;
        [SerializeField] Button roomOptionsButton;

        [Header("Options")]
        [SerializeField] Button optionsBackButton;
        [SerializeField] Button leaveRoomButton;
        [SerializeField] Button endGameButton;
        [SerializeField] GameObject roomOnlyOptions;

        [Header("Password")]
        [SerializeField] Button passwordJoinButton;
        [SerializeField] Button passwordCloseButton;

        [Header("Game HUD")]
        [SerializeField] Button hudOptionsButton;

        bool roomPreview;
        bool gamePreview;

        void Awake()
        {
            createRoomButton?.onClick.AddListener(ShowCreateRoom);
            joinRoomButton?.onClick.AddListener(ShowJoinRoom);
            homeOptionsButton?.onClick.AddListener(ShowOptions);
            createConfirmButton?.onClick.AddListener(ShowRoomPreview);
            createBackButton?.onClick.AddListener(ShowHome);
            joinBackButton?.onClick.AddListener(ShowHome);
            joinLockedRoomButton?.onClick.AddListener(ShowPassword);
            joinOpenRoomButton?.onClick.AddListener(ShowRoomPreview);
            startPreviewButton?.onClick.AddListener(ShowGamePreview);
            roomOptionsButton?.onClick.AddListener(ShowOptions);
            optionsBackButton?.onClick.AddListener(ReturnFromOptions);
            leaveRoomButton?.onClick.AddListener(ShowHome);
            endGameButton?.onClick.AddListener(ShowHome);
            passwordJoinButton?.onClick.AddListener(ShowRoomPreview);
            passwordCloseButton?.onClick.AddListener(HidePassword);
            hudOptionsButton?.onClick.AddListener(ShowOptions);

            ShowHome();
        }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            if (passwordModal != null && passwordModal.activeSelf)
                HidePassword();
            else if (optionsPanel != null && optionsPanel.activeSelf)
                ReturnFromOptions();
            else if (createRoomPanel != null && createRoomPanel.activeSelf ||
                     joinRoomPanel != null && joinRoomPanel.activeSelf)
                ShowHome();
            else
                ShowOptions();
        }

        public void ShowHome()
        {
            roomPreview = false;
            gamePreview = false;
            SetExclusivePanel(homePanel);
            SetMenuBackground(true);
            HidePassword();
        }

        public void ShowCreateRoom()
        {
            SetExclusivePanel(createRoomPanel);
            SetMenuBackground(true);
        }

        public void ShowJoinRoom()
        {
            SetExclusivePanel(joinRoomPanel);
            SetMenuBackground(true);
        }

        public void ShowRoomPreview()
        {
            roomPreview = true;
            gamePreview = false;
            SetExclusivePanel(roomPanel);
            SetMenuBackground(false);
            HidePassword();
        }

        public void ShowGamePreview()
        {
            roomPreview = true;
            gamePreview = true;
            SetExclusivePanel(gameHud);
            SetMenuBackground(false);
        }

        public void ShowOptions()
        {
            SetExclusivePanel(optionsPanel);
            SetMenuBackground(!roomPreview);
            if (roomOnlyOptions != null)
                roomOnlyOptions.SetActive(roomPreview);
        }

        public void ReturnFromOptions()
        {
            if (gamePreview)
                ShowGamePreview();
            else if (roomPreview)
                ShowRoomPreview();
            else
                ShowHome();
        }

        public void ShowPassword()
        {
            if (passwordModal != null)
                passwordModal.SetActive(true);
        }

        public void HidePassword()
        {
            if (passwordModal != null)
                passwordModal.SetActive(false);
        }

        public void ShowResultPreview(bool won)
        {
            if (resultOverlay == null) return;

            var label = resultOverlay.GetComponentInChildren<Text>(includeInactive: true);
            if (label != null)
            {
                label.text = won ? "YOU WON" : "YOU LOST";
                label.color = won
                    ? new Color(0.45f, 1f, 0.68f)
                    : new Color(1f, 0.45f, 0.38f);
            }

            resultOverlay.SetActive(true);
        }

        void SetExclusivePanel(GameObject activePanel)
        {
            SetActive(homePanel, activePanel);
            SetActive(createRoomPanel, activePanel);
            SetActive(joinRoomPanel, activePanel);
            SetActive(roomPanel, activePanel);
            SetActive(optionsPanel, activePanel);
            SetActive(gameHud, activePanel);
            if (resultOverlay != null)
                resultOverlay.SetActive(false);
        }

        void SetMenuBackground(bool visible)
        {
            if (menuBackground != null)
                menuBackground.SetActive(visible);
        }

        static void SetActive(GameObject panel, GameObject activePanel)
        {
            if (panel != null)
                panel.SetActive(panel == activePanel);
        }
    }
}
