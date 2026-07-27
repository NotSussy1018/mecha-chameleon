using System.IO;
using MechaChameleon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MechaChameleon.Editor
{
    public static class UiDesignBuilder
    {
        const string ScenePath = "Assets/Scenes/Mvp.unity";
        const string BackgroundPath = "Assets/UI/Generated/menu_waterfront_background.png";
        const string ButtonPath = "Assets/UI/Generated/wood_button.png";
        const string PanelPath = "Assets/UI/Generated/wood_panel.png";
        const string ColorWheelPath = "Assets/UI/Generated/color_wheel.png";

        static readonly Color Cream = new(1f, 0.94f, 0.78f);
        static readonly Color DeepTeal = new(0.035f, 0.16f, 0.19f);
        static readonly Color Cyan = new(0.31f, 0.91f, 0.86f);
        static readonly Color Yellow = new(1f, 0.82f, 0.28f);
        static readonly Color Coral = new(0.92f, 0.31f, 0.27f);
        static readonly Color Muted = new(0.58f, 0.68f, 0.67f);

        static Font font;
        static Sprite buttonSprite;
        static Sprite panelSprite;
        static Sprite backgroundSprite;
        static Sprite colorWheelSprite;

        [MenuItem("Mecha Chameleon/Build UI Design")]
        public static void Build()
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            ConfigureSprite(BackgroundPath, Vector4.zero);
            ConfigureSprite(ButtonPath, new Vector4(92f, 64f, 92f, 64f));
            ConfigureSprite(PanelPath, new Vector4(118f, 105f, 118f, 105f));
            CreateColorWheel();
            ConfigureSprite(ColorWheelPath, Vector4.zero);

            AssetDatabase.Refresh();
            backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath);
            panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
            colorWheelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ColorWheelPath);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var previous = GameObject.Find("Game UI Canvas");
            if (previous != null)
                Object.DestroyImmediate(previous);

            var canvasObject = new GameObject("Game UI Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            var app = GameObject.Find("App");
            if (app == null)
                app = new GameObject("App");

            var controller = app.GetComponent<GameUiController>();
            if (controller == null)
                controller = app.AddComponent<GameUiController>();

            var oldHud = app.GetComponent<MvpHud>();
            if (oldHud != null)
                oldHud.enabled = false;

            var background = BuildMenuBackground(canvasObject.transform);
            var home = BuildHome(canvasObject.transform, out var homeCreate, out var homeJoin, out var homeOptions);
            var create = BuildCreateRoom(canvasObject.transform, out var createConfirm, out var createBack);
            var join = BuildJoinRoom(canvasObject.transform, out var joinBack, out var joinLocked, out var joinOpen);
            var room = BuildRoom(canvasObject.transform, out var roomStart, out var roomOptions);
            var options = BuildOptions(canvasObject.transform, out var optionsBack, out var leaveRoom,
                out var endGame, out var roomOnlyOptions);
            var hud = BuildGameHud(canvasObject.transform, out var hudOptions);
            var password = BuildPasswordModal(canvasObject.transform, out var passwordJoin, out var passwordClose);
            var result = BuildResultOverlay(canvasObject.transform);

            var serialized = new SerializedObject(controller);
            SetReference(serialized, "menuBackground", background);
            SetReference(serialized, "homePanel", home);
            SetReference(serialized, "createRoomPanel", create);
            SetReference(serialized, "joinRoomPanel", join);
            SetReference(serialized, "roomPanel", room);
            SetReference(serialized, "optionsPanel", options);
            SetReference(serialized, "gameHud", hud);
            SetReference(serialized, "passwordModal", password);
            SetReference(serialized, "resultOverlay", result);
            SetReference(serialized, "createRoomButton", homeCreate);
            SetReference(serialized, "joinRoomButton", homeJoin);
            SetReference(serialized, "homeOptionsButton", homeOptions);
            SetReference(serialized, "createConfirmButton", createConfirm);
            SetReference(serialized, "createBackButton", createBack);
            SetReference(serialized, "joinBackButton", joinBack);
            SetReference(serialized, "joinLockedRoomButton", joinLocked);
            SetReference(serialized, "joinOpenRoomButton", joinOpen);
            SetReference(serialized, "startPreviewButton", roomStart);
            SetReference(serialized, "roomOptionsButton", roomOptions);
            SetReference(serialized, "optionsBackButton", optionsBack);
            SetReference(serialized, "leaveRoomButton", leaveRoom);
            SetReference(serialized, "endGameButton", endGame);
            SetReference(serialized, "roomOnlyOptions", roomOnlyOptions);
            SetReference(serialized, "passwordJoinButton", passwordJoin);
            SetReference(serialized, "passwordCloseButton", passwordClose);
            SetReference(serialized, "hudOptionsButton", hudOptions);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            home.SetActive(true);
            create.SetActive(false);
            join.SetActive(false);
            room.SetActive(false);
            options.SetActive(false);
            hud.SetActive(false);
            password.SetActive(false);
            result.SetActive(false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("Completed Mecha Chameleon UI design build.");
        }

        static GameObject BuildMenuBackground(Transform parent)
        {
            var root = Rect("Menu Background", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var image = root.AddComponent<Image>();
            image.sprite = backgroundSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;

            var wash = Rect("Contrast Wash", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var washImage = wash.AddComponent<Image>();
            washImage.color = new Color(0.01f, 0.12f, 0.14f, 0.18f);
            washImage.raycastTarget = false;
            return root;
        }

        static GameObject BuildHome(Transform parent, out Button create, out Button join, out Button options)
        {
            var root = FullPanel("HomePanel", parent);
            AddText("Title", root.transform, "MECHA\nCHAMELEON", 82, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(900f, 215f),
                FontStyle.Bold);
            AddText("Subtitle", root.transform, "PAINT.  HIDE.  HUNT.", 27, TextAnchor.MiddleCenter, Cyan,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -265f), new Vector2(700f, 52f),
                FontStyle.Bold);

            var menu = Rect("Main Menu", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-280f, -275f), new Vector2(280f, 135f));
            var layout = menu.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 12, 12);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            create = AddWoodButton("Create Room Button", menu.transform, "CREATE ROOM", 31);
            join = AddWoodButton("Join Room Button", menu.transform, "JOIN ROOM", 31);
            options = AddWoodButton("Options Button", menu.transform, "OPTIONS", 31);

            AddText("Footer", root.transform, "LOCAL MULTIPLAYER", 18, TextAnchor.MiddleCenter,
                new Color(1f, 1f, 1f, 0.88f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 30f), new Vector2(420f, 42f), FontStyle.Bold);
            return root;
        }

        static GameObject BuildCreateRoom(Transform parent, out Button confirm, out Button back)
        {
            var root = FullPanel("CreateRoomPanel", parent);
            var board = AddBoard("Create Room Board", root.transform, new Vector2(930f, 850f), Vector2.zero);
            AddSectionTitle(board.transform, "CREATE ROOM");

            AddLabel(board.transform, "ROOM NAME", new Vector2(0f, 215f));
            AddInputField(board.transform, "Room Name", "SUNNY HIDEOUT", new Vector2(0f, 150f), false);
            AddLabel(board.transform, "ROOM PASSWORD", new Vector2(0f, 55f));
            AddInputField(board.transform, "Room Password", "OPTIONAL", new Vector2(0f, -10f), true);

            AddFutureField(board.transform, "MAP", "COZY HOUSE", new Vector2(0f, -125f));
            AddFutureField(board.transform, "CAPACITY", "8 PLAYERS", new Vector2(0f, -225f));

            confirm = AddWoodButtonAt("Create Confirm", board.transform, "CREATE", new Vector2(125f, -345f),
                new Vector2(360f, 90f), 29);
            back = AddWoodButtonAt("Create Back", board.transform, "BACK", new Vector2(-260f, -345f),
                new Vector2(260f, 90f), 27);
            return root;
        }

        static GameObject BuildJoinRoom(Transform parent, out Button back, out Button locked, out Button open)
        {
            var root = FullPanel("JoinRoomPanel", parent);
            var board = AddBoard("Join Room Board", root.transform, new Vector2(1240f, 850f), Vector2.zero);
            AddSectionTitle(board.transform, "JOIN ROOM");

            AddText("Discovery", board.transform, "LOCAL ROOMS", 20, TextAnchor.MiddleLeft, Cyan,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-425f, 238f),
                new Vector2(360f, 45f), FontStyle.Bold);
            AddText("Refresh", board.transform, "REFRESH", 18, TextAnchor.MiddleRight, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(420f, 238f),
                new Vector2(250f, 45f), FontStyle.Bold);

            open = AddRoomRow(board.transform, "SUNNY HIDEOUT", "2 / 8", "OPEN", 140f);
            locked = AddRoomRow(board.transform, "LIVING ROOM", "4 / 8", "LOCKED", 5f);
            AddRoomRow(board.transform, "PAINT PARTY", "1 / 8", "OPEN", -130f);

            AddText("List Hint", board.transform, "ROOMS ON THIS DEVICE OR LAN", 17, TextAnchor.MiddleCenter, Muted,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -252f),
                new Vector2(700f, 40f), FontStyle.Normal);
            back = AddWoodButtonAt("Join Back", board.transform, "BACK", new Vector2(0f, -355f),
                new Vector2(300f, 88f), 27);
            return root;
        }

        static GameObject BuildRoom(Transform parent, out Button start, out Button options)
        {
            var root = FullPanel("RoomPanel", parent);

            var roomInfo = AddBoard("Room Info", root.transform, new Vector2(820f, 150f), new Vector2(0f, 420f));
            AddText("Room Name", roomInfo.transform, "SUNNY HIDEOUT", 35, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f),
                new Vector2(650f, 55f), FontStyle.Bold);
            AddText("Room Meta", roomInfo.transform, "LOCAL  |  2 / 8 PLAYERS  |  HOST: PLAYER 1", 17,
                TextAnchor.MiddleCenter, Cyan, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -30f), new Vector2(680f, 40f), FontStyle.Bold);

            var playerBoard = AddBoard("Player Board", root.transform, new Vector2(470f, 620f),
                new Vector2(-690f, 20f));
            AddText("Players Header", playerBoard.transform, "PLAYERS", 32, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 235f),
                new Vector2(350f, 55f), FontStyle.Bold);
            AddPlayerRow(playerBoard.transform, "PLAYER 1", "HOST", "HUNTER", 140f, Yellow);
            AddPlayerRow(playerBoard.transform, "PLAYER 2", "", "HIDER", 30f, Cyan);
            AddPlayerRow(playerBoard.transform, "WAITING...", "", "", -80f, Muted);
            AddText("Hunter Hint", playerBoard.transform, "STAND ON THE YELLOW PAD\nTO VOLUNTEER AS HUNTER", 17,
                TextAnchor.MiddleCenter, Muted, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -205f), new Vector2(340f, 80f), FontStyle.Bold);

            start = AddWoodButtonAt("Start Button", root.transform, "START", new Vector2(0f, -425f),
                new Vector2(430f, 105f), 36);
            options = AddWoodButtonAt("Room Options", root.transform, "OPTIONS", new Vector2(-165f, -65f),
                new Vector2(250f, 72f), 22, new Vector2(1f, 1f));
            return root;
        }

        static GameObject BuildOptions(Transform parent, out Button back, out Button leave, out Button end,
            out GameObject roomOnly)
        {
            var root = FullPanel("OptionsPanel", parent);
            var board = AddBoard("Options Board", root.transform, new Vector2(930f, 880f), Vector2.zero);
            AddSectionTitle(board.transform, "OPTIONS");

            AddLabel(board.transform, "GRAPHICS", new Vector2(0f, 220f));
            var quality = Rect("Quality Buttons", board.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-340f, 125f), new Vector2(340f, 205f));
            AddSegment(quality.transform, "LOW", -220f, false);
            AddSegment(quality.transform, "MEDIUM", 0f, true);
            AddSegment(quality.transform, "HIGH", 220f, false);

            AddLabel(board.transform, "MASTER VOLUME", new Vector2(0f, 70f));
            AddSlider(board.transform, "Master Volume", new Vector2(0f, 5f), 0.78f);
            AddLabel(board.transform, "SFX VOLUME", new Vector2(0f, -105f));
            AddSlider(board.transform, "SFX Volume", new Vector2(0f, -170f), 0.64f);

            roomOnly = Rect("Room Only Options", board.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-380f, -330f), new Vector2(380f, -235f));
            leave = AddWoodButtonAt("Leave Room", roomOnly.transform, "LEAVE ROOM", new Vector2(-195f, 0f),
                new Vector2(350f, 82f), 24);
            end = AddWoodButtonAt("End Game", roomOnly.transform, "END GAME", new Vector2(195f, 0f),
                new Vector2(350f, 82f), 24, null, new Color(0.82f, 0.42f, 0.34f));

            back = AddWoodButtonAt("Options Back", board.transform, "BACK", new Vector2(0f, -375f),
                new Vector2(310f, 85f), 27);
            return root;
        }

        static GameObject BuildGameHud(Transform parent, out Button options)
        {
            var root = FullPanel("GameHud", parent);

            AddText("Timer Shadow", root.transform, "HIDE  30", 64, TextAnchor.MiddleCenter, DeepTeal,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(3f, -39f),
                new Vector2(560f, 95f), FontStyle.Bold, false);
            AddText("Timer", root.transform, "HIDE  30", 64, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -35f),
                new Vector2(560f, 95f), FontStyle.Bold, false);

            var role = AddWoodButtonAt("Role Badge", root.transform, "HIDER", new Vector2(-175f, -65f),
                new Vector2(265f, 78f), 27, new Vector2(1f, 1f));
            role.interactable = false;

            options = AddWoodButtonAt("HUD Options", root.transform, "OPTIONS", new Vector2(150f, -65f),
                new Vector2(235f, 70f), 21, new Vector2(0f, 1f));

            var paint = AddBoard("Paint Tools", root.transform, new Vector2(390f, 700f),
                new Vector2(-735f, -20f));
            AddText("Paint Title", paint.transform, "PAINT", 30, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 275f),
                new Vector2(280f, 50f), FontStyle.Bold);

            var wheel = Rect("Color Wheel", paint.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-110f, 35f), new Vector2(110f, 255f));
            var wheelImage = wheel.AddComponent<Image>();
            wheelImage.sprite = colorWheelSprite;
            wheelImage.preserveAspect = true;
            wheelImage.raycastTarget = false;

            AddText("Brush Size", paint.transform, "BRUSH SIZE", 18, TextAnchor.MiddleLeft, Cyan,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-20f, -20f),
                new Vector2(285f, 40f), FontStyle.Bold);
            AddSlider(paint.transform, "Brush Slider", new Vector2(0f, -78f), 0.48f, 280f);
            AddText("Brush Value", paint.transform, "MEDIUM", 18, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f),
                new Vector2(250f, 36f), FontStyle.Bold);

            AddWoodButtonAt("Clear Paint", paint.transform, "CLEAR", new Vector2(0f, -215f),
                new Vector2(260f, 72f), 21);
            AddText("Paint Mode", paint.transform, "PAINT MODE", 17, TextAnchor.MiddleCenter, Yellow,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -290f),
                new Vector2(260f, 38f), FontStyle.Bold);
            return root;
        }

        static GameObject BuildPasswordModal(Transform parent, out Button join, out Button close)
        {
            var root = FullPanel("PasswordModal", parent);
            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(0.01f, 0.05f, 0.06f, 0.62f);

            var board = AddBoard("Password Board", root.transform, new Vector2(720f, 500f), Vector2.zero);
            AddText("Title", board.transform, "ROOM LOCKED", 38, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 155f),
                new Vector2(520f, 60f), FontStyle.Bold);
            AddText("Room", board.transform, "LIVING ROOM", 20, TextAnchor.MiddleCenter, Cyan,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 103f),
                new Vector2(450f, 40f), FontStyle.Bold);
            AddInputField(board.transform, "Join Password", "ENTER PASSWORD", new Vector2(0f, 25f), true, 500f);
            close = AddWoodButtonAt("Password Back", board.transform, "BACK", new Vector2(-165f, -145f),
                new Vector2(270f, 80f), 24);
            join = AddWoodButtonAt("Password Join", board.transform, "JOIN", new Vector2(165f, -145f),
                new Vector2(270f, 80f), 24);
            return root;
        }

        static GameObject BuildResultOverlay(Transform parent)
        {
            var root = FullPanel("ResultOverlay", parent);
            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(0.01f, 0.05f, 0.06f, 0.55f);
            var board = AddBoard("Result Board", root.transform, new Vector2(760f, 330f), Vector2.zero);
            AddText("Result", board.transform, "YOU WON", 72, TextAnchor.MiddleCenter,
                new Color(0.45f, 1f, 0.68f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 30f), new Vector2(620f, 100f), FontStyle.Bold);
            AddText("Return", board.transform, "RETURNING TO THE LOBBY", 19, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -65f),
                new Vector2(500f, 45f), FontStyle.Bold);
            return root;
        }

        static Button AddRoomRow(Transform parent, string roomName, string players, string access, float y)
        {
            var row = Rect(roomName + " Row", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-510f, y - 48f), new Vector2(510f, y + 48f));
            var image = row.AddComponent<Image>();
            image.color = new Color(0.04f, 0.23f, 0.26f, 0.86f);
            var outline = row.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.79f, 0.72f, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);

            AddText("Name", row.transform, roomName, 25, TextAnchor.MiddleLeft, Cream,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(34f, 0f), new Vector2(390f, 96f),
                FontStyle.Bold);
            AddText("Players", row.transform, players, 20, TextAnchor.MiddleCenter, Cyan,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(80f, 0f), new Vector2(170f, 96f),
                FontStyle.Bold);
            AddText("Access", row.transform, access, 16, TextAnchor.MiddleCenter,
                access == "LOCKED" ? Yellow : new Color(0.55f, 1f, 0.72f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(260f, 0f), new Vector2(150f, 96f),
                FontStyle.Bold);
            return AddWoodButtonAt("Join", row.transform, "JOIN", new Vector2(-100f, 0f),
                new Vector2(180f, 62f), 20, new Vector2(1f, 0.5f));
        }

        static void AddPlayerRow(Transform parent, string playerName, string badge, string role, float y, Color roleColor)
        {
            var row = Rect(playerName + " Row", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-170f, y - 40f), new Vector2(170f, y + 40f));
            var image = row.AddComponent<Image>();
            image.color = new Color(0.04f, 0.23f, 0.26f, playerName == "WAITING..." ? 0.42f : 0.9f);

            AddText("Name", row.transform, playerName, 20, TextAnchor.MiddleLeft,
                playerName == "WAITING..." ? Muted : Cream, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(20f, 0f), new Vector2(210f, 80f), FontStyle.Bold);
            if (!string.IsNullOrEmpty(badge))
                AddText("Badge", row.transform, badge, 13, TextAnchor.MiddleCenter, Yellow,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(40f, 0f),
                    new Vector2(90f, 80f), FontStyle.Bold);
            if (!string.IsNullOrEmpty(role))
                AddText("Role", row.transform, role, 17, TextAnchor.MiddleRight, roleColor,
                    new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-18f, 0f),
                    new Vector2(130f, 80f), FontStyle.Bold);
        }

        static GameObject AddBoard(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var board = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position - size * 0.5f, position + size * 0.5f);
            var image = board.AddComponent<Image>();
            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            return board;
        }

        static void AddSectionTitle(Transform parent, string title)
        {
            AddText(title, parent, title, 45, TextAnchor.MiddleCenter, Cream,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 325f),
                new Vector2(680f, 70f), FontStyle.Bold);
            AddText("Divider", parent, "◆", 20, TextAnchor.MiddleCenter, Yellow,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 282f),
                new Vector2(120f, 32f), FontStyle.Bold);
        }

        static void AddLabel(Transform parent, string text, Vector2 position)
        {
            AddText(text + " Label", parent, text, 19, TextAnchor.MiddleLeft, Cyan,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position + new Vector2(-325f, 0f),
                new Vector2(650f, 38f), FontStyle.Bold);
        }

        static InputField AddInputField(Transform parent, string name, string placeholder, Vector2 position,
            bool password, float width = 650f)
        {
            var fieldObject = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position - new Vector2(width * 0.5f, 38f), position + new Vector2(width * 0.5f, 38f));
            var background = fieldObject.AddComponent<Image>();
            background.color = new Color(1f, 0.96f, 0.82f, 0.97f);
            var outline = fieldObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.47f, 0.26f, 0.10f);
            outline.effectDistance = new Vector2(3f, -3f);

            var input = fieldObject.AddComponent<InputField>();
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            input.caretColor = DeepTeal;
            input.selectionColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f);

            var text = AddText("Text", fieldObject.transform, "", 23, TextAnchor.MiddleLeft, DeepTeal,
                Vector2.zero, Vector2.one, new Vector2(24f, 0f), new Vector2(-24f, 0f), FontStyle.Bold, false);
            var placeholderText = AddText("Placeholder", fieldObject.transform, placeholder, 21,
                TextAnchor.MiddleLeft, new Color(0.19f, 0.34f, 0.35f, 0.55f), Vector2.zero, Vector2.one,
                new Vector2(24f, 0f), new Vector2(-24f, 0f), FontStyle.Bold, false);
            input.textComponent = text;
            input.placeholder = placeholderText;
            return input;
        }

        static void AddFutureField(Transform parent, string label, string value, Vector2 position)
        {
            AddText(label, parent, label, 18, TextAnchor.MiddleLeft, Muted,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position + new Vector2(-325f, 0f),
                new Vector2(180f, 50f), FontStyle.Bold);
            AddText(value, parent, value + "  ·  COMING LATER", 18, TextAnchor.MiddleRight,
                new Color(Muted.r, Muted.g, Muted.b, 0.72f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position + new Vector2(120f, 0f), new Vector2(430f, 50f),
                FontStyle.Bold);
        }

        static void AddSegment(Transform parent, string text, float x, bool selected)
        {
            var segment = Rect(text, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x - 102f, -32f), new Vector2(x + 102f, 32f));
            var image = segment.AddComponent<Image>();
            image.color = selected ? new Color(0.18f, 0.68f, 0.62f) : new Color(0.05f, 0.26f, 0.29f);
            var outline = segment.AddComponent<Outline>();
            outline.effectColor = selected ? Yellow : new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);
            AddText("Label", segment.transform, text, 19, TextAnchor.MiddleCenter,
                selected ? DeepTeal : Cream, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                FontStyle.Bold);
        }

        static Slider AddSlider(Transform parent, string name, Vector2 position, float value, float width = 650f)
        {
            var root = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position - new Vector2(width * 0.5f, 22f), position + new Vector2(width * 0.5f, 22f));
            var slider = root.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;

            var track = Rect("Track", root.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -7f), new Vector2(0f, 7f));
            track.AddComponent<Image>().color = new Color(0.01f, 0.09f, 0.11f);

            var fillArea = Rect("Fill Area", root.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(5f, -6f), new Vector2(-5f, 6f));
            var fill = Rect("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fill.AddComponent<Image>().color = Cyan;

            var handleArea = Rect("Handle Slide Area", root.transform, Vector2.zero, Vector2.one,
                new Vector2(10f, 0f), new Vector2(-10f, 0f));
            var handle = Rect("Handle", handleArea.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-15f, -15f), new Vector2(15f, 15f));
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = Yellow;

            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        static Button AddWoodButton(string name, Transform parent, string text, int fontSize)
        {
            var button = AddWoodButtonAt(name, parent, text, Vector2.zero, new Vector2(500f, 94f), fontSize);
            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 94f;
            element.minHeight = 94f;
            return button;
        }

        static Button AddWoodButtonAt(string name, Transform parent, string text, Vector2 position, Vector2 size,
            int fontSize, Vector2? anchor = null, Color? tint = null)
        {
            var anchorPoint = anchor ?? new Vector2(0.5f, 0.5f);
            var buttonObject = Rect(name, parent, anchorPoint, anchorPoint, position - size * 0.5f,
                position + size * 0.5f);
            var image = buttonObject.AddComponent<Image>();
            image.sprite = buttonSprite;
            image.type = Image.Type.Sliced;
            image.color = tint ?? Color.white;

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = tint ?? Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.03f);
            colors.pressedColor = new Color(0.78f, 0.82f, 0.83f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            AddText("Label", buttonObject.transform, text, fontSize, TextAnchor.MiddleCenter, DeepTeal,
                Vector2.zero, Vector2.one, new Vector2(30f, 8f), new Vector2(-30f, -5f), FontStyle.Bold, false);
            return button;
        }

        static Text AddText(string name, Transform parent, string value, int fontSize, TextAnchor alignment,
            Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size,
            FontStyle style, bool addOutline = true)
        {
            var root = Rect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            var rect = root.GetComponent<RectTransform>();
            if (anchorMin == anchorMax)
            {
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }
            else
            {
                rect.offsetMin = position;
                rect.offsetMax = size;
            }

            var text = root.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;

            if (addOutline)
            {
                var outline = root.AddComponent<Outline>();
                outline.effectColor = new Color(0.01f, 0.08f, 0.09f, 0.88f);
                outline.effectDistance = new Vector2(2f, -2f);
            }

            return text;
        }

        static GameObject FullPanel(string name, Transform parent)
        {
            return Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        static GameObject Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return root;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetAsLastSibling();
        }

        static void SetReference(SerializedObject target, string property, Object value)
        {
            target.FindProperty(property).objectReferenceValue = value;
        }

        static void ConfigureSprite(string path, Vector4 border)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        static void CreateColorWheel()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var point = new Vector2(
                        (x + 0.5f) / size * 2f - 1f,
                        (y + 0.5f) / size * 2f - 1f);
                    var saturation = point.magnitude;
                    if (saturation > 1f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    var hue = Mathf.Repeat(0.5f - Mathf.Atan2(point.y, point.x) / (Mathf.PI * 2f), 1f);
                    pixels[y * size + x] = Color.HSVToRGB(hue, saturation, 1f);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(ColorWheelPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
