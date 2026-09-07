using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MechaChameleon
{
    public sealed class GameUiController : MonoBehaviour
    {
        [Header("Surfaces")]
        [SerializeField] GameObject menuBackground;
        [SerializeField] GameObject loginPanel;
        [SerializeField] GameObject homePanel;
        [SerializeField] GameObject createRoomPanel;
        [SerializeField] GameObject joinRoomPanel;
        [SerializeField] GameObject roomPanel;
        [SerializeField] GameObject optionsPanel;
        [SerializeField] GameObject gameHud;
        [SerializeField] GameObject passwordModal;
        [SerializeField] GameObject resultOverlay;

        [Header("Authentication")]
        [SerializeField] Button authSignInTabButton;
        [SerializeField] Button authSignUpTabButton;
        [SerializeField] Button authSubmitButton;

        [Header("Home")]
        [SerializeField] Button createRoomButton;
        [SerializeField] Button joinRoomButton;
        [SerializeField] Button homeOptionsButton;
        [SerializeField] Button homeSignOutButton;

        [Header("Create Room")]
        [SerializeField] Button createConfirmButton;
        [SerializeField] Button createBackButton;

        [Header("Join Room")]
        [SerializeField] Button joinBackButton;
        [SerializeField] Button refreshRoomsButton;

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

        RoomConnector connector;
        LocalRoomRowView[] roomRows = Array.Empty<LocalRoomRowView>();
        RoomPlayerRowView[] playerRows = Array.Empty<RoomPlayerRowView>();
        InputField authUsernameInput;
        InputField authPasswordInput;
        InputField authConfirmPasswordInput;
        InputField createRoomNameInput;
        InputField createRoomPasswordInput;
        InputField joinPasswordInput;
        Text authTitleLabel;
        Text authEnvironmentLabel;
        Text authStatusLabel;
        Text authSubmitLabel;
        Text homeAccountLabel;
        Text homeFooterLabel;
        Text joinDiscoveryLabel;
        Text createStatusLabel;
        Text joinStatusLabel;
        Text passwordRoomLabel;
        Text passwordStatusLabel;
        Text currentRoomNameLabel;
        Text currentRoomMetaLabel;
        Text hudTimerLabel;
        Text hudTimerShadowLabel;
        Text hudRoleLabel;
        Text hudBrushValueLabel;
        Text hudPaintModeLabel;
        GameObject hudPaintTools;
        RectTransform hudColorWheel;
        Slider hudBrushSlider;
        Button hudClearPaintButton;
        GameObject authConfirmGroup;
        RoomListing selectedRoom;
        bool inRoom;
        bool gameActive;
        float nextRoomRefreshAt;
        int lastLoggedRoomCount = -1;
        int lastLoggedVisibleRows = -1;
        bool lastLoggedJoinPanelActive;
        GamePhase lastPhase = (GamePhase)(-1);
        GamePhase lastHudPhase = (GamePhase)(-1);
        PlayerRole lastHudRole = (PlayerRole)(-1);
        int lastHudSeconds = -1;
        int lastHudBrushSize = -1;
        bool lastHudCanPaint;
        bool lastHudPaintMode;
        bool hudStateInitialized;
        bool authSignUpMode;
        bool authBusy;
        bool preserveJoinStatus;

        void Awake()
        {
            connector = GetComponent<RoomConnector>();
            ResolveGeneratedUi();
            RegisterButtons();

            if (connector != null)
            {
                connector.StatusChanged += OnConnectorStatusChanged;
                connector.RoomEntered += EnterRoom;
                connector.RoomLeft += ShowHome;
                connector.RoomsChanged += RefreshRoomRows;
            }

            LogLanUi($"awake rows={roomRows.Length}");
            BeginEntryFlow();
        }

        void OnDestroy()
        {
            if (connector != null)
            {
                connector.StatusChanged -= OnConnectorStatusChanged;
                connector.RoomEntered -= EnterRoom;
                connector.RoomLeft -= ShowHome;
                connector.RoomsChanged -= RefreshRoomRows;
            }

            LogLanUi("destroyed");
        }

        void Update()
        {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.F1)) ShowHomeFromPreview();
            if (Input.GetKeyDown(KeyCode.F2)) ShowCreateRoom();
            if (Input.GetKeyDown(KeyCode.F3)) ShowJoinRoom();
            if (Input.GetKeyDown(KeyCode.F4)) ShowRoomPreview();
            if (Input.GetKeyDown(KeyCode.F5)) ShowOptions();
            if (Input.GetKeyDown(KeyCode.F6)) ShowGamePreview();
            if (Input.GetKeyDown(KeyCode.F7)) ShowResultPreview(true);
            if (Input.GetKeyDown(KeyCode.F8))
            {
                ShowJoinRoom();
                if (connector != null && connector.Rooms.Count > 0)
                    SelectRoom(connector.Rooms[0]);
            }
#endif

            if (loginPanel != null && loginPanel.activeSelf && Input.GetKeyDown(KeyCode.Return))
                SubmitAuthentication();

            UpdateNetworkScreen();
            UpdateHud();
            if (Time.unscaledTime < nextRoomRefreshAt) return;

            nextRoomRefreshAt = Time.unscaledTime + 0.2f;
            if (inRoom)
                RefreshCurrentRoom();
            else if (joinRoomPanel != null && joinRoomPanel.activeSelf)
                RefreshRoomRows();
        }

        public void ShowHome()
        {
            if (connector != null && connector.UsesOnlineServices && !UgsBootstrap.IsSignedIn)
            {
                ShowLogin();
                return;
            }

            inRoom = false;
            gameActive = false;
            selectedRoom = null;
            connector?.StartRoomSearch();
            GameDiagnostics.Info("ui", "screen_changed", "screen=home");
            LogLanUi("show home; listener requested");
            SetExclusivePanel(homePanel);
            SetMenuBackground(true);
            HidePassword();
            RefreshAccountDisplay();
        }

        public void ShowLogin()
        {
            inRoom = false;
            gameActive = false;
            selectedRoom = null;
            connector?.StopRoomSearch();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetExclusivePanel(loginPanel);
            SetMenuBackground(true);
            HidePassword();
            SetAuthMode(authSignUpMode);
            GameDiagnostics.Info("ui", "screen_changed", "screen=login");
        }

        void ShowHomeFromPreview()
        {
            var manager = NetworkManager.Singleton;
            if (connector != null && (connector.CurrentRoom != null || manager != null && manager.IsListening))
                connector.Leave();
            else
                ShowHome();
        }

        public void ShowCreateRoom()
        {
            if (!CanUseOnlineMenus()) return;
            connector?.StopRoomSearch();
            GameDiagnostics.Info("ui", "screen_changed", "screen=create_room");
            SetExclusivePanel(createRoomPanel);
            SetMenuBackground(true);
            SetText(createStatusLabel, "");
        }

        public void ShowJoinRoom()
        {
            if (!CanUseOnlineMenus()) return;
            preserveJoinStatus = false;
            GameDiagnostics.Info("ui", "screen_changed", "screen=join_room");
            SetExclusivePanel(joinRoomPanel);
            SetMenuBackground(true);
            HidePassword();
            SetText(joinStatusLabel, $"SEARCHING {EnvironmentLabel()} ROOMS...");
            SetText(joinDiscoveryLabel, $"{EnvironmentLabel()} ROOMS");
            connector?.StartRoomSearch();
            if (connector != null && connector.UsesOnlineServices)
                _ = connector.RefreshRoomsAsync();
            LogLanUi("show join room; listener requested");
            RefreshRoomRows();
        }

        public void ShowRoomPreview()
        {
            inRoom = true;
            gameActive = false;
            GameDiagnostics.Info("ui", "screen_changed", "screen=room");
            SetExclusivePanel(roomPanel);
            SetMenuBackground(false);
            HidePassword();
            RefreshCurrentRoom();
        }

        public void ShowGamePreview()
        {
            inRoom = true;
            gameActive = true;
            GameDiagnostics.Info("ui", "screen_changed", "screen=game");
            SetExclusivePanel(gameHud);
            SetMenuBackground(false);
            HidePassword();
            hudStateInitialized = false;
        }

        public void ShowOptions()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameDiagnostics.Info("ui", "screen_changed", $"screen=options inRoom={inRoom}");
            SetExclusivePanel(optionsPanel);
            SetMenuBackground(!inRoom);
            if (roomOnlyOptions != null)
                roomOnlyOptions.SetActive(inRoom);
            if (leaveRoomButton != null)
                leaveRoomButton.gameObject.SetActive(inRoom);
            if (endGameButton != null)
                endGameButton.gameObject.SetActive(inRoom && connector != null && connector.IsRoomHost);
        }

        public void ReturnFromOptions()
        {
            if (gameActive)
                ShowGamePreview();
            else if (inRoom)
                ShowRoomPreview();
            else
                ShowHome();
        }

        public void ShowPassword()
        {
            GameDiagnostics.Info("ui", "password_prompt_opened",
                $"roomId={selectedRoom?.RoomId ?? "none"}");
            if (passwordModal != null)
                passwordModal.SetActive(true);
        }

        public void HidePassword()
        {
            if (passwordModal != null)
                passwordModal.SetActive(false);
            if (passwordJoinButton != null)
                passwordJoinButton.interactable = true;
            SetText(passwordStatusLabel, "");
        }

        public void ShowResultPreview(bool won)
        {
            if (resultOverlay == null) return;

            GameDiagnostics.Info("ui", "result_shown", $"won={won}");

            var label = resultOverlay.transform.Find("Result Board/Result")?.GetComponent<Text>();
            if (label != null)
            {
                label.text = won ? "YOU WON" : "YOU LOST";
                label.color = won
                    ? new Color(0.45f, 1f, 0.68f)
                    : new Color(1f, 0.45f, 0.38f);
            }

            resultOverlay.SetActive(true);
        }

        void ResolveGeneratedUi()
        {
            authUsernameInput = FindComponent<InputField>(loginPanel, "Account Board/Username");
            authPasswordInput = FindComponent<InputField>(loginPanel, "Account Board/Password");
            authConfirmPasswordInput = FindComponent<InputField>(loginPanel,
                "Account Board/Confirm Group/Confirm Password");
            authTitleLabel = FindComponent<Text>(loginPanel, "Account Board/Title");
            authEnvironmentLabel = FindComponent<Text>(loginPanel, "Account Board/Environment");
            authStatusLabel = FindComponent<Text>(loginPanel, "Account Board/Status");
            authConfirmGroup = loginPanel != null
                ? loginPanel.transform.Find("Account Board/Confirm Group")?.gameObject
                : null;
            authSubmitLabel = authSubmitButton != null
                ? authSubmitButton.transform.Find("Label")?.GetComponent<Text>()
                : null;
            homeAccountLabel = FindComponent<Text>(homePanel, "Account");
            homeFooterLabel = FindComponent<Text>(homePanel, "Footer");
            createRoomNameInput = FindComponent<InputField>(createRoomPanel, "Create Room Board/Room Name");
            createRoomPasswordInput = FindComponent<InputField>(createRoomPanel, "Create Room Board/Room Password");
            createStatusLabel = FindComponent<Text>(createRoomPanel, "Create Room Board/Status");
            joinStatusLabel = FindComponent<Text>(joinRoomPanel, "Join Room Board/Status");
            joinDiscoveryLabel = FindComponent<Text>(joinRoomPanel, "Join Room Board/Discovery");
            joinPasswordInput = FindComponent<InputField>(passwordModal, "Password Board/Join Password");
            passwordRoomLabel = FindComponent<Text>(passwordModal, "Password Board/Room");
            passwordStatusLabel = FindComponent<Text>(passwordModal, "Password Board/Status");
            currentRoomNameLabel = FindComponent<Text>(roomPanel, "Room Info/Room Name");
            currentRoomMetaLabel = FindComponent<Text>(roomPanel, "Room Info/Room Meta");
            hudTimerLabel = FindComponent<Text>(gameHud, "Timer");
            hudTimerShadowLabel = FindComponent<Text>(gameHud, "Timer Shadow");
            hudRoleLabel = FindComponent<Text>(gameHud, "Role Badge/Label");
            hudPaintTools = gameHud != null ? gameHud.transform.Find("Paint Tools")?.gameObject : null;
            hudColorWheel = gameHud != null
                ? gameHud.transform.Find("Paint Tools/Color Wheel") as RectTransform
                : null;
            hudBrushSlider = FindComponent<Slider>(gameHud, "Paint Tools/Brush Slider");
            hudBrushValueLabel = FindComponent<Text>(gameHud, "Paint Tools/Brush Value");
            hudPaintModeLabel = FindComponent<Text>(gameHud, "Paint Tools/Paint Mode");
            hudClearPaintButton = FindComponent<Button>(gameHud, "Paint Tools/Clear Paint");

            if (hudBrushSlider != null)
            {
                hudBrushSlider.minValue = 0f;
                hudBrushSlider.maxValue = 3f;
                hudBrushSlider.wholeNumbers = true;
            }

            roomRows = joinRoomPanel != null
                ? joinRoomPanel.GetComponentsInChildren<LocalRoomRowView>(true)
                : Array.Empty<LocalRoomRowView>();
            playerRows = roomPanel != null
                ? roomPanel.GetComponentsInChildren<RoomPlayerRowView>(true)
                : Array.Empty<RoomPlayerRowView>();
        }

        void RegisterButtons()
        {
            authSignInTabButton?.onClick.AddListener(() => SetAuthMode(false));
            authSignUpTabButton?.onClick.AddListener(() => SetAuthMode(true));
            authSubmitButton?.onClick.AddListener(SubmitAuthentication);
            createRoomButton?.onClick.AddListener(ShowCreateRoom);
            joinRoomButton?.onClick.AddListener(ShowJoinRoom);
            homeOptionsButton?.onClick.AddListener(ShowOptions);
            homeSignOutButton?.onClick.AddListener(SignOutAccount);
            createConfirmButton?.onClick.AddListener(CreateRoom);
            createBackButton?.onClick.AddListener(ShowHome);
            joinBackButton?.onClick.AddListener(ShowHome);
            refreshRoomsButton?.onClick.AddListener(RefreshDiscovery);
            startPreviewButton?.onClick.AddListener(StartRound);
            roomOptionsButton?.onClick.AddListener(ShowOptions);
            optionsBackButton?.onClick.AddListener(ReturnFromOptions);
            leaveRoomButton?.onClick.AddListener(LeaveRoom);
            endGameButton?.onClick.AddListener(EndRoom);
            passwordJoinButton?.onClick.AddListener(SubmitPassword);
            passwordCloseButton?.onClick.AddListener(HidePassword);
            hudOptionsButton?.onClick.AddListener(ShowOptions);
            hudBrushSlider?.onValueChanged.AddListener(SetHudBrushSize);
            hudClearPaintButton?.onClick.AddListener(ClearHudPaint);

            foreach (var row in roomRows)
            {
                var capturedRow = row;
                capturedRow.JoinButton?.onClick.AddListener(() => SelectRoom(capturedRow.Room));
            }
        }

        async void BeginEntryFlow()
        {
            if (connector == null || !connector.UsesOnlineServices)
            {
                ShowHome();
                return;
            }

            ShowLogin();
            SetAuthBusy(true, "RESTORING SESSION...");
            try
            {
                var restored = await UgsBootstrap.TryRestoreSessionAsync();
                if (this == null) return;
                if (restored)
                    ShowHome();
                else
                    SetAuthBusy(false, "SIGN IN TO PLAY ONLINE");
            }
            catch (Exception exception)
            {
                if (this == null) return;
                SetAuthBusy(false, UgsBootstrap.GetAuthenticationError(exception, signingUp: false));
            }
        }

        void SetAuthMode(bool signingUp)
        {
            authSignUpMode = signingUp;
            if (authConfirmGroup != null)
                authConfirmGroup.SetActive(signingUp);
            SetText(authTitleLabel, signingUp ? "CREATE ACCOUNT" : "WELCOME BACK");
            SetText(authSubmitLabel, signingUp ? "CREATE ACCOUNT" : "SIGN IN");
            SetText(authEnvironmentLabel, $"ONLINE {EnvironmentLabel()}");
            SetText(authStatusLabel, "");
            RefreshAuthControls();
        }

        async void SubmitAuthentication()
        {
            if (authBusy) return;

            var username = authUsernameInput != null ? authUsernameInput.text.Trim() : "";
            var password = authPasswordInput != null ? authPasswordInput.text : "";
            var validation = UgsBootstrap.ValidateCredentials(username, password);
            if (validation.Length > 0)
            {
                SetText(authStatusLabel, validation);
                return;
            }

            if (authSignUpMode &&
                password != (authConfirmPasswordInput != null ? authConfirmPasswordInput.text : ""))
            {
                SetText(authStatusLabel, "Passwords do not match.");
                return;
            }

            SetAuthBusy(true, authSignUpMode ? "CREATING ACCOUNT..." : "SIGNING IN...");
            try
            {
                if (authSignUpMode)
                    await UgsBootstrap.SignUpAsync(username, password);
                else
                    await UgsBootstrap.SignInAsync(username, password);

                if (this == null) return;
                ClearPasswordFields();
                ShowHome();
            }
            catch (Exception exception)
            {
                if (this == null) return;
                GameDiagnostics.Warning("auth", "authentication_failed",
                    $"mode={(authSignUpMode ? "sign_up" : "sign_in")} type={exception.GetType().Name}");
                SetAuthBusy(false, UgsBootstrap.GetAuthenticationError(exception, authSignUpMode));
            }
        }

        async void SignOutAccount()
        {
            if (connector != null && connector.CurrentRoom != null)
                await connector.LeaveAsync();
            UgsBootstrap.SignOut();
            ClearPasswordFields();
            ShowLogin();
            SetText(authStatusLabel, "SIGNED OUT");
        }

        void SetAuthBusy(bool busy, string status)
        {
            authBusy = busy;
            SetText(authStatusLabel, status);
            RefreshAuthControls();
        }

        void RefreshAuthControls()
        {
            if (authSignInTabButton != null)
                authSignInTabButton.interactable = !authBusy && authSignUpMode;
            if (authSignUpTabButton != null)
                authSignUpTabButton.interactable = !authBusy && !authSignUpMode;
            if (authSubmitButton != null)
                authSubmitButton.interactable = !authBusy;
            if (authUsernameInput != null)
                authUsernameInput.interactable = !authBusy;
            if (authPasswordInput != null)
                authPasswordInput.interactable = !authBusy;
            if (authConfirmPasswordInput != null)
                authConfirmPasswordInput.interactable = !authBusy;
        }

        void RefreshAccountDisplay()
        {
            var online = connector != null && connector.UsesOnlineServices;
            var username = UgsBootstrap.Username;
            SetText(homeAccountLabel, online
                ? $"SIGNED IN  |  {(username.Length > 0 ? username : "PLAYER")}"
                : "");
            SetText(homeFooterLabel, online ? "ONLINE MULTIPLAYER" : "LOCAL MULTIPLAYER");
            if (homeSignOutButton != null)
                homeSignOutButton.gameObject.SetActive(online && UgsBootstrap.IsSignedIn);
        }

        void ClearPasswordFields()
        {
            if (authPasswordInput != null) authPasswordInput.text = "";
            if (authConfirmPasswordInput != null) authConfirmPasswordInput.text = "";
        }

        bool CanUseOnlineMenus()
        {
            if (connector == null || !connector.UsesOnlineServices || UgsBootstrap.IsSignedIn)
                return true;
            ShowLogin();
            SetText(authStatusLabel, "SIGN IN TO PLAY ONLINE");
            return false;
        }

        async void CreateRoom()
        {
            if (connector == null) return;

            createConfirmButton.interactable = false;
            await connector.CreateRoomAsync(
                createRoomNameInput != null ? createRoomNameInput.text : "",
                createRoomPasswordInput != null ? createRoomPasswordInput.text : "");
            createConfirmButton.interactable = !connector.IsBusy && connector.CurrentRoom == null;
        }

        async void RefreshDiscovery()
        {
            preserveJoinStatus = false;
            SetText(joinStatusLabel, $"SEARCHING {EnvironmentLabel()} ROOMS...");
            if (connector != null)
                await connector.RefreshRoomsAsync();
        }

        void RefreshRoomRows()
        {
            if (roomRows == null) return;
            var rooms = connector != null ? connector.Rooms : null;

            for (var i = 0; i < roomRows.Length; i++)
            {
                if (roomRows[i] == null) continue;

                if (rooms != null && i < rooms.Count)
                    roomRows[i].Show(rooms[i]);
                else
                    roomRows[i].Hide();
            }

            var roomCount = rooms?.Count ?? 0;
            var visibleRows = 0;
            foreach (var row in roomRows)
            {
                if (row != null && row.gameObject.activeInHierarchy)
                    visibleRows++;
            }

            var joinPanelActive = joinRoomPanel != null && joinRoomPanel.activeSelf;
            if (joinPanelActive && !preserveJoinStatus)
                SetText(joinStatusLabel, rooms != null && rooms.Count > 0
                    ? $"{rooms.Count} {EnvironmentLabel()} ROOM{(rooms.Count == 1 ? "" : "S")} FOUND"
                    : $"NO {EnvironmentLabel()} ROOMS FOUND YET");

            if (roomCount != lastLoggedRoomCount ||
                visibleRows != lastLoggedVisibleRows ||
                joinPanelActive != lastLoggedJoinPanelActive)
            {
                lastLoggedRoomCount = roomCount;
                lastLoggedVisibleRows = visibleRows;
                lastLoggedJoinPanelActive = joinPanelActive;
                LogLanUi(
                    $"refresh rooms={roomCount} visibleRows={visibleRows} " +
                    $"joinPanelActive={joinPanelActive}");
            }
        }

        void LogLanUi(string message)
        {
            GameDiagnostics.Info("ui", "lan_room_state",
                $"ui={GetInstanceID()} environment={connector?.EnvironmentName ?? "unknown"} {message}");
        }

        void SelectRoom(RoomListing room)
        {
            if (room == null) return;
            selectedRoom = room.Copy();

            if (selectedRoom.IsLocked)
            {
                SetText(passwordRoomLabel, selectedRoom.RoomName);
                SetText(passwordStatusLabel, "");
                if (joinPasswordInput != null)
                    joinPasswordInput.text = "";
                ShowPassword();
                joinPasswordInput?.ActivateInputField();
                return;
            }

            JoinSelectedRoom("");
        }

        void SubmitPassword()
        {
            JoinSelectedRoom(joinPasswordInput != null ? joinPasswordInput.text : "");
        }

        async void JoinSelectedRoom(string password)
        {
            if (selectedRoom == null || connector == null) return;
            passwordJoinButton.interactable = false;
            if (!await connector.JoinRoomAsync(selectedRoom, password))
                passwordJoinButton.interactable = true;
        }

        void EnterRoom()
        {
            if (connector != null && connector.IsRoomHost)
                LogLanUi("enter room as host; keeping advertisement active");
            else
                connector?.StopRoomSearch();

            ShowRoomPreview();
        }

        async void LeaveRoom()
        {
            if (connector != null)
                await connector.LeaveAsync();
        }

        async void EndRoom()
        {
            if (connector != null)
                await connector.LeaveAsync();
        }

        async void StartRound()
        {
            if (connector == null || !connector.IsRoomHost) return;
            var round = ChameleonRoundManager.Instance;
            if (round == null || round.Phase.Value != GamePhase.Lobby) return;
            if (!await connector.SetRoomLockedAsync(true)) return;
            round.StartRound();
            ShowGamePreview();
        }

        void OnConnectorStatusChanged()
        {
            if (connector == null) return;
            if (joinRoomPanel != null && joinRoomPanel.activeSelf)
                preserveJoinStatus = true;
            SetText(createStatusLabel, connector.Status);
            SetText(joinStatusLabel, connector.Status);
            SetText(passwordStatusLabel, connector.Status);
            if (createConfirmButton != null)
                createConfirmButton.interactable = !connector.IsBusy && connector.CurrentRoom == null;
            if (refreshRoomsButton != null)
                refreshRoomsButton.interactable = !connector.IsBusy;
            if (passwordJoinButton != null && connector.CurrentRoom == null)
                passwordJoinButton.interactable = !connector.IsBusy;
        }

        void UpdateNetworkScreen()
        {
            if (!inRoom) return;
            var round = ChameleonRoundManager.Instance;
            if (round == null || !round.IsSpawned) return;

            var phase = round.Phase.Value;
            if (phase == lastPhase) return;
            lastPhase = phase;

            if (phase == GamePhase.Lobby)
            {
                _ = connector?.SetRoomLockedAsync(false);
                ShowRoomPreview();
            }
            else if (phase == GamePhase.Paint || phase == GamePhase.Hunt)
            {
                ShowGamePreview();
            }
            else if (phase == GamePhase.Result)
            {
                var local = ChameleonPlayer.Local;
                if (local != null)
                {
                    var hidersWon = round.HidersWonLastRound.Value;
                    ShowResultPreview(local.Role.Value == PlayerRole.Hider ? hidersWon : !hidersWon);
                }
            }
        }

        void UpdateHud()
        {
            if (gameHud == null || !gameHud.activeSelf) return;

            var round = ChameleonRoundManager.Instance;
            var local = ChameleonPlayer.Local;
            if (round == null || local == null) return;

            var phase = round.Phase.Value;
            var seconds = round.RemainingSeconds;
            if (!hudStateInitialized || phase != lastHudPhase || seconds != lastHudSeconds)
            {
                var phaseLabel = phase == GamePhase.Paint ? "HIDE" : phase == GamePhase.Hunt ? "HUNT" : "";
                var timer = phaseLabel.Length > 0 ? $"{phaseLabel}  {seconds:00}" : "";
                SetText(hudTimerLabel, timer);
                SetText(hudTimerShadowLabel, timer);
                lastHudPhase = phase;
                lastHudSeconds = seconds;
            }

            var role = local.Role.Value;
            if (!hudStateInitialized || role != lastHudRole)
            {
                SetText(hudRoleLabel, role == PlayerRole.Seeker ? "HUNTER" : "HIDER");
                lastHudRole = role;
            }

            var paint = local.Paint;
            var canPaint = paint != null &&
                           local.Alive.Value &&
                           role == PlayerRole.Hider &&
                           phase is GamePhase.Paint or GamePhase.Hunt;
            var paintMode = canPaint && paint.IsPaintMode;
            if (!hudStateInitialized || canPaint != lastHudCanPaint || paintMode != lastHudPaintMode)
            {
                if (hudPaintTools != null)
                    hudPaintTools.SetActive(canPaint);
                SetText(hudPaintModeLabel, paintMode ? "PAINT MODE ACTIVE" : "PRESS P TO PAINT");
                if (hudBrushSlider != null)
                    hudBrushSlider.interactable = canPaint;
                if (hudClearPaintButton != null)
                    hudClearPaintButton.interactable = canPaint;
                lastHudCanPaint = canPaint;
                lastHudPaintMode = paintMode;
            }

            if (canPaint && (!hudStateInitialized || paint.BrushSizeIndex != lastHudBrushSize))
            {
                lastHudBrushSize = paint.BrushSizeIndex;
                hudBrushSlider?.SetValueWithoutNotify(lastHudBrushSize);
                SetText(hudBrushValueLabel, lastHudBrushSize switch
                {
                    0 => "SMALL",
                    1 => "MEDIUM",
                    2 => "LARGE",
                    _ => "GIANT"
                });
            }

            if (paintMode)
                UpdateHudColorWheel(paint);

            hudStateInitialized = true;
        }

        void UpdateHudColorWheel(ChameleonPaint paint)
        {
            if (hudColorWheel == null || !Input.GetMouseButton(0)) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(hudColorWheel, Input.mousePosition)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    hudColorWheel,
                    Input.mousePosition,
                    null,
                    out var localPoint))
                return;

            var rect = hudColorWheel.rect;
            var normalized = new Vector2(
                localPoint.x / (rect.width * 0.5f),
                localPoint.y / (rect.height * 0.5f));
            var saturation = normalized.magnitude;
            if (saturation > 1f) return;

            var angle = Mathf.Atan2(normalized.y, normalized.x);
            var hue = Mathf.Repeat(0.5f - angle / (Mathf.PI * 2f), 1f);
            paint.SetBrushColor((Color32)Color.HSVToRGB(hue, saturation, 1f));
        }

        void SetHudBrushSize(float value)
        {
            ChameleonPlayer.Local?.Paint?.SetBrushSizeIndex(Mathf.RoundToInt(value));
            hudStateInitialized = false;
        }

        void ClearHudPaint()
        {
            ChameleonPlayer.Local?.Paint?.RequestClear();
        }

        void RefreshCurrentRoom()
        {
            var room = connector != null ? connector.CurrentRoom : null;
            if (room == null) return;

            var count = connector.ConnectedPlayerCount;
            SetText(currentRoomNameLabel, room.RoomName);
            SetText(currentRoomMetaLabel,
                $"{EnvironmentLabel()}  |  {count} / {room.MaxPlayers} PLAYERS  |  " +
                $"{(room.IsJoinLocked ? "IN MATCH" : room.IsLocked ? "PASSWORD" : "OPEN")}  |  HOST: PLAYER 1");

            var players = FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None);
            Array.Sort(players, (left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));
            var visiblePlayers = 0;
            var round = ChameleonRoundManager.Instance;
            foreach (var player in players)
            {
                if (player == null || !player.IsSpawned || !player.NetworkObject.IsPlayerObject) continue;
                if (visiblePlayers >= playerRows.Length) break;

                var wantsHunter = round != null && round.Phase.Value == GamePhase.Lobby
                    ? round.IsOnHunterPlatform(player.transform.position)
                    : player.Role.Value == PlayerRole.Seeker;
                playerRows[visiblePlayers].Show(
                    $"PLAYER {player.OwnerClientId + 1}",
                    player.OwnerClientId == NetworkManager.ServerClientId,
                    wantsHunter);
                visiblePlayers++;
            }

            for (var i = visiblePlayers; i < playerRows.Length; i++)
                playerRows[i].Hide();

            if (startPreviewButton != null)
            {
                startPreviewButton.gameObject.SetActive(connector.IsRoomHost);
                startPreviewButton.interactable = round != null && round.Phase.Value == GamePhase.Lobby;
            }
        }

        string EnvironmentLabel()
        {
            return connector == null ? "LOCAL" : connector.EnvironmentName.ToUpperInvariant();
        }

        void SetExclusivePanel(GameObject activePanel)
        {
            SetActive(loginPanel, activePanel);
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

        static T FindComponent<T>(GameObject root, string path) where T : Component
        {
            return root != null ? root.transform.Find(path)?.GetComponent<T>() : null;
        }

        static void SetText(Text label, string value)
        {
            if (label != null)
                label.text = value;
        }

        static void SetActive(GameObject panel, GameObject activePanel)
        {
            if (panel != null)
                panel.SetActive(panel == activePanel);
        }
    }
}
