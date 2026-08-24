using System.Collections;
using System.IO;
using MechaChameleon;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MechaChameleon.Tests
{
    public sealed class LocalHostTests
    {
        [SetUp]
        public void SetUp()
        {
            DeploymentEnvironmentSettings.SetTestOverride(DeploymentEnvironment.Development);
        }

        [UnityTest]
        public IEnumerator DiagnosticsWritesSearchablePerProcessLog()
        {
            yield return null;
            GameDiagnostics.Info("test", "diagnostic_probe", "value=works");
            GameDiagnostics.Flush();

            Assert.IsTrue(File.Exists(GameDiagnostics.CurrentLogPath));
            var contents = File.ReadAllText(GameDiagnostics.CurrentLogPath);
            StringAssert.Contains("category=test", contents);
            StringAssert.Contains("event=diagnostic_probe", contents);
            StringAssert.Contains("value=works", contents);
        }

        [TearDown]
        public void TearDown()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            foreach (var discovery in Object.FindObjectsByType<LocalRoomDiscovery>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                discovery.StopAll();
            }
            DeploymentEnvironmentSettings.SetTestOverride(null);
        }

        [UnityTest]
        public IEnumerator HostLocalSpawnsOwnedPlayer()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            var connector = Object.FindFirstObjectByType<RoomConnector>();
            Assert.NotNull(connector);

            connector.HostLocal();

            var timeout = Time.realtimeSinceStartup + 5f;
            ChameleonPlayer player = null;

            while (Time.realtimeSinceStartup < timeout)
            {
                player = Object.FindFirstObjectByType<ChameleonPlayer>();
                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsHost &&
                    player != null &&
                    player.IsSpawned &&
                    player.IsOwner)
                {
                    break;
                }

                yield return null;
            }

            Assert.NotNull(NetworkManager.Singleton);
            Assert.IsTrue(NetworkManager.Singleton.IsHost);
            Assert.NotNull(player);
            Assert.IsTrue(player.IsSpawned);
            Assert.IsTrue(player.IsOwner);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator JoinLocalUsesDiscoveredRoomPort()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            var connector = Object.FindFirstObjectByType<RoomConnector>();
            var manager = NetworkManager.Singleton;
            const ushort advertisedPort = 7789;
            Assert.NotNull(connector);
            Assert.NotNull(manager);
            var transport = manager.GetComponent<UnityTransport>();
            Assert.NotNull(transport);

            Assert.IsTrue(connector.JoinLocal(new RoomListing
            {
                RoomId = "CUSTOM_PORT_TEST",
                HostAddress = "127.0.0.1",
                Port = advertisedPort,
                RoomName = "Custom Port Room",
                MaxPlayers = 8
            }, ""));

            Assert.AreEqual(advertisedPort, transport.ConnectionData.Port);
            manager.Shutdown();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreateRoomUiUsesSubmittedRoomDataAndShowsOnlyHost()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            var ui = Object.FindFirstObjectByType<GameUiController>();
            var connector = Object.FindFirstObjectByType<RoomConnector>();
            Assert.NotNull(ui);
            Assert.NotNull(connector);

            ui.ShowCreateRoom();
            var canvas = GameObject.Find("Game UI Canvas").transform;
            var board = canvas.Find("CreateRoomPanel/Create Room Board");
            var roomName = board.Find("Room Name").GetComponent<InputField>();
            var password = board.Find("Room Password").GetComponent<InputField>();
            roomName.text = "Moonlight Hideout";
            password.text = "paint";
            board.Find("Create Confirm").GetComponent<Button>().onClick.Invoke();

            yield return WaitForHostAndPlayer();

            Assert.AreEqual("Moonlight Hideout", connector.CurrentRoom.RoomName);
            Assert.IsTrue(connector.CurrentRoom.IsLocked);
            Assert.AreEqual(1, connector.ConnectedPlayerCount);
            Assert.AreEqual(
                "Moonlight Hideout",
                canvas.Find("RoomPanel/Room Info/Room Name").GetComponent<Text>().text);

            var visiblePlayers = 0;
            foreach (var row in canvas.GetComponentsInChildren<RoomPlayerRowView>(true))
            {
                if (row.gameObject.activeSelf)
                    visiblePlayers++;
            }

            Assert.AreEqual(1, visiblePlayers);

            connector.Leave();
            yield return null;
            yield return null;
            Assert.IsFalse(NetworkManager.Singleton.IsListening);
            Assert.IsTrue(connector.CreateLocalRoom("Restarted Room", ""),
                "Leaving a room must release UDP 7778 for the next host.");
            yield return WaitForHostAndPlayer();
            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator SoloPracticeCanSpawnTargetAndStartHunt()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            var connector = Object.FindFirstObjectByType<RoomConnector>();
            connector.HostLocal();

            yield return WaitForHostAndPlayer();

            var round = ChameleonRoundManager.Instance;
            Assert.NotNull(round);

            round.SpawnPracticeHider();
            yield return null;

            round.StartPaintPhase();
            yield return null;

            Assert.AreEqual(GamePhase.Paint, round.Phase.Value);
            Assert.AreEqual(PlayerRole.Seeker, ChameleonPlayer.Local.Role.Value);
            var canvas = GameObject.Find("Game UI Canvas").transform;
            Assert.That(canvas.Find("GameHud/Timer").GetComponent<Text>().text, Does.StartWith("HIDE"));
            Assert.AreEqual("HUNTER", canvas.Find("GameHud/Role Badge/Label").GetComponent<Text>().text);
            Assert.IsFalse(canvas.Find("GameHud/Paint Tools").gameObject.activeSelf);

            round.BeginHunt();
            yield return null;

            Assert.AreEqual(GamePhase.Hunt, round.Phase.Value);
            Assert.That(canvas.Find("GameHud/Timer").GetComponent<Text>().text, Does.StartWith("HUNT"));
            Assert.GreaterOrEqual(Object.FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None).Length, 2);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator BeginHuntNowWorksFromLobby()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var round = ChameleonRoundManager.Instance;
            round.BeginHunt();
            yield return null;

            Assert.AreEqual(GamePhase.Hunt, round.Phase.Value);
            Assert.AreEqual(PlayerRole.Seeker, ChameleonPlayer.Local.Role.Value);
            Assert.GreaterOrEqual(Object.FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None).Length, 2);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator PlayerOnHunterPlatformBecomesSeekerAndWaitsInLobby()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var round = ChameleonRoundManager.Instance;
            ChameleonPlayer.Local.transform.position = round.HunterPlatformCenter + Vector3.up;
            round.SpawnPracticeHider();
            yield return null;

            round.StartPaintPhase();
            yield return null;

            Assert.AreEqual(PlayerRole.Seeker, ChameleonPlayer.Local.Role.Value);
            Assert.Less(ChameleonPlayer.Local.transform.position.z, 10f);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator HidingPhaseSendsHidersToHidingRoomThenHunterEntersOnHunt()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var round = ChameleonRoundManager.Instance;
            round.SpawnPracticeHider();
            yield return null;

            round.StartPaintPhase();
            yield return null;

            Assert.AreEqual(GamePhase.Paint, round.Phase.Value);
            Assert.AreEqual(PlayerRole.Seeker, ChameleonPlayer.Local.Role.Value);
            Assert.Less(ChameleonPlayer.Local.transform.position.z, 10f);

            ChameleonPlayer practiceHider = null;
            foreach (var player in Object.FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None))
            {
                if (player != ChameleonPlayer.Local && player.Role.Value == PlayerRole.Hider)
                    practiceHider = player;
            }

            Assert.NotNull(practiceHider);
            Assert.Greater(practiceHider.transform.position.z, 15f);

            round.BeginHunt();
            yield return null;

            Assert.AreEqual(GamePhase.Hunt, round.Phase.Value);
            Assert.Greater(ChameleonPlayer.Local.transform.position.z, 15f);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator HunterShotEliminatesHiderAndReturnsToLobby()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var round = ChameleonRoundManager.Instance;
            round.SpawnPracticeHider();
            round.BeginHunt();
            yield return null;

            var hunter = ChameleonPlayer.Local;
            ChameleonPlayer target = null;
            foreach (var player in Object.FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None))
            {
                if (player != hunter && player.Role.Value == PlayerRole.Hider)
                    target = player;
            }

            Assert.NotNull(target);

            hunter.SetServerState(PlayerRole.Seeker, true, Color.white, Color.white, PoseId.Lie);
            hunter.transform.position = new Vector3(0f, 1f, -4f);
            target.transform.position = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();

            var origin = hunter.transform.position + Vector3.up * 0.7f;
            var direction = (target.transform.position + Vector3.up * 0.45f - origin).normalized;

            Assert.IsTrue(hunter.ShootFromServer(origin, direction));
            Assert.IsFalse(target.Alive.Value);
            Assert.AreEqual(GamePhase.Result, round.Phase.Value);
            Assert.AreEqual(-4f, hunter.transform.position.z, 0.01f);

            yield return new WaitForSeconds(0.45f);
            Assert.Greater(Mathf.Abs(hunter.transform.position.z + 4f), 0.01f);

            yield return new WaitForSeconds(round.ResultSeconds);
            Assert.AreEqual(GamePhase.Lobby, round.Phase.Value);
            Assert.AreEqual(PlayerRole.Hider, hunter.Role.Value);
            Assert.IsTrue(hunter.Alive.Value);
            Assert.AreEqual(PoseId.Stand, hunter.Pose.Value);
            Assert.IsTrue(GameObject.Find("Game UI Canvas").transform.Find("RoomPanel").gameObject.activeSelf);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator HidingPosesRotateCharacterWithoutChangingPlayerScale()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var player = ChameleonPlayer.Local;
            Transform body = null;
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer.name == "Body")
                    body = renderer.transform;
            }

            Assert.NotNull(body);
            var poseRoot = body.parent;
            Assert.NotNull(poseRoot);
            var controller = player.GetComponent<CharacterController>();
            Assert.NotNull(controller);

            player.SetServerState(PlayerRole.Hider, true, Color.white, Color.white, PoseId.Stand);
            yield return null;
            Assert.AreEqual(Vector3.one, player.transform.localScale);
            Assert.That(Quaternion.Angle(Quaternion.identity, poseRoot.localRotation), Is.LessThan(0.1f));
            Assert.That(controller.radius, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(poseRoot.localPosition, Is.EqualTo(Vector3.zero));

            player.SetServerState(PlayerRole.Hider, true, Color.white, Color.white, PoseId.Crouch);
            yield return null;
            Assert.AreEqual(Vector3.one, player.transform.localScale);
            Assert.That(Mathf.Abs(poseRoot.localEulerAngles.z), Is.GreaterThan(45f));
            Assert.That(controller.radius, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(controller.height, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(poseRoot.localPosition.x, Is.LessThan(-0.4f));

            player.SetServerState(PlayerRole.Hider, true, Color.white, Color.white, PoseId.Lie);
            yield return null;
            Assert.AreEqual(Vector3.one, player.transform.localScale);
            Assert.That(poseRoot.localEulerAngles.x, Is.GreaterThan(60f));
            Assert.That(controller.radius, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(controller.height, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(poseRoot.localPosition.z, Is.LessThan(-0.6f));

            player.SetServerState(PlayerRole.Hider, true, Color.white, Color.white, PoseId.Stand);
            yield return null;
            Assert.That(controller.radius, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(poseRoot.localPosition, Is.EqualTo(Vector3.zero));

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator PracticeHiderDoesNotBecomeLocalPlayer()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var local = ChameleonPlayer.Local;
            ChameleonRoundManager.Instance.SpawnPracticeHider();
            yield return null;

            Assert.AreSame(local, ChameleonPlayer.Local);
            Assert.IsTrue(ChameleonPlayer.Local.NetworkObject.IsLocalPlayer);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator LocalPlayerRespawnsAfterFalling()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var player = ChameleonPlayer.Local;
            player.transform.position = new Vector3(0f, -20f, 0f);
            yield return null;
            yield return null;

            Assert.Greater(player.transform.position.y, 0f);

            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator PaintStateInitializesAndClearsWithLobbyReset()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            Object.FindFirstObjectByType<RoomConnector>().HostLocal();
            yield return WaitForHostAndPlayer();

            var player = ChameleonPlayer.Local;
            Assert.NotNull(player.Paint);
            Assert.IsTrue(player.Paint.IsReady);
            var round = ChameleonRoundManager.Instance;
            player.SetServerState(PlayerRole.Hider, true, Color.white, Color.white, PoseId.Stand);
            round.Phase.Value = GamePhase.Paint;
            var visualRoot = player.transform.Find("Visual Root");
            var rotationBeforePaint = visualRoot.localRotation;
            player.Paint.TogglePaintMode();
            Assert.IsTrue(player.Paint.IsPaintMode);
            Assert.That(Quaternion.Angle(rotationBeforePaint, visualRoot.localRotation), Is.LessThan(0.01f));
            var brushOutline = player.transform.Find("Brush Outline")?.GetComponent<LineRenderer>();
            Assert.NotNull(brushOutline);
            Assert.AreEqual(48, brushOutline.positionCount);
            var customBrushColor = new Color32(23, 117, 204, 255);
            player.Paint.SetBrushColor(customBrushColor);
            Assert.AreEqual(customBrushColor, player.Paint.SelectedColor);
            player.Paint.CycleBrushSize();
            player.Paint.CycleBrushSize();
            Assert.AreEqual(28, player.Paint.BrushRadius);
            player.Paint.CycleBrushSize();
            Assert.AreEqual(2, player.Paint.BrushRadius);

            var playerCamera = player.transform.Find("Player Camera");
            var cameraPositionBeforeOrbit = playerCamera.position;
            player.SetPaintCameraOrbit(90f);
            Assert.That(Vector3.Distance(cameraPositionBeforeOrbit, playerCamera.position), Is.GreaterThan(1f));
            player.SetPaintCameraOrbit(0f);
            Assert.That(Vector3.Distance(cameraPositionBeforeOrbit, playerCamera.position), Is.LessThan(0.01f));

            player.Paint.Strokes.Add(new PaintStroke(
                PaintPart.Body,
                new Vector2(0.2f, 0.3f),
                new Vector2(0.4f, 0.5f),
                color: new Color32(23, 117, 204, 255),
                radius: 4,
                sequence: 1));
            yield return null;

            Assert.AreEqual(1, player.Paint.StrokeCount);
            var bodyRenderer = player.transform.Find("Visual Root/Body").GetComponent<Renderer>();
            var bodyTexture = (Texture2D)bodyRenderer.material.mainTexture;
            Assert.AreSame(bodyTexture, bodyRenderer.material.GetTexture("_EmissionMap"));
            Assert.IsTrue(bodyRenderer.material.IsKeywordEnabled("_EMISSION"));
            Assert.That(bodyRenderer.material.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(0.2f));
            var paintedPixel = (Color32)bodyTexture.GetPixel(
                Mathf.RoundToInt(0.4f * 127f),
                Mathf.RoundToInt(0.5f * 127f));
            Assert.AreEqual(new Color32(23, 117, 204, 255), paintedPixel);

            player.Paint.TogglePaintMode();
            yield return null;

            Assert.IsFalse(player.Paint.IsPaintMode);
            var persistentPixel = (Color32)bodyTexture.GetPixel(
                Mathf.RoundToInt(0.4f * 127f),
                Mathf.RoundToInt(0.5f * 127f));
            Assert.AreEqual(new Color32(23, 117, 204, 255), persistentPixel);

            round.Phase.Value = GamePhase.Hunt;
            player.Paint.TogglePaintMode();
            Assert.IsTrue(player.Paint.IsPaintMode, "Hiders should be able to paint during the Hunt phase.");
            player.Paint.TogglePaintMode();

            player.ResetForLobby();
            yield return null;

            Assert.AreEqual(0, player.Paint.StrokeCount);
            var clearedPixel = (Color32)bodyTexture.GetPixel(
                Mathf.RoundToInt(0.4f * 127f),
                Mathf.RoundToInt(0.5f * 127f));
            Assert.AreEqual(new Color32(255, 255, 255, 255), clearedPixel);
            NetworkManager.Singleton.Shutdown();
        }

        [UnityTest]
        public IEnumerator LocalRoomDiscoveryDeduplicatesLocalAdvertisements()
        {
            var listenerObject = new GameObject("Test Room Listener");
            var advertiserObject = new GameObject("Test Room Advertiser");
            var listener = listenerObject.AddComponent<LocalRoomDiscovery>();
            var advertiser = advertiserObject.AddComponent<LocalRoomDiscovery>();

            Assert.IsTrue(Application.runInBackground,
                "Local multiplayer must keep advertising while another player window has focus.");
            listener.StartListening();
            advertiser.StartAdvertising(() => new RoomListing
            {
                RoomId = "LOOPBACK_TEST",
                HostAddress = "127.0.0.1",
                Port = RoomConnector.LocalPort,
                RoomName = "Loopback Room",
                MaxPlayers = 8
            });

            var timeout = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < timeout && listener.Rooms.Count == 0)
                yield return null;

            Assert.AreEqual(1, listener.Rooms.Count);
            Assert.AreEqual("Loopback Room", listener.Rooms[0].RoomName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(listener.Rooms[0].HostAddress));

            yield return new WaitForSecondsRealtime(3.5f);
            Assert.AreEqual(1, listener.Rooms.Count,
                "A discovered room must stay listed while its host keeps advertising.");

            Object.Destroy(listenerObject);
            Object.Destroy(advertiserObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator JoinRoomShowsRoomDiscoveredWhileOnHomeScreen()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            var ui = Object.FindFirstObjectByType<GameUiController>();
            var discovery = Object.FindFirstObjectByType<LocalRoomDiscovery>();
            Assert.NotNull(ui);
            Assert.NotNull(discovery);

            var advertiserObject = new GameObject("Test UI Room Advertiser");
            var advertiser = advertiserObject.AddComponent<LocalRoomDiscovery>();
            advertiser.StartAdvertising(() => new RoomListing
            {
                RoomId = "UI_ROOM_TEST",
                HostAddress = "127.0.0.1",
                Port = RoomConnector.LocalPort,
                RoomName = "Visible Room",
                PlayerCount = 1,
                MaxPlayers = 8
            });

            var timeout = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < timeout && discovery.Rooms.Count == 0)
                yield return null;

            Assert.AreEqual(1, discovery.Rooms.Count);
            ui.ShowJoinRoom();
            yield return null;

            var visibleRows = 0;
            foreach (var row in Object.FindObjectsByType<LocalRoomRowView>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (!row.gameObject.activeInHierarchy) continue;
                visibleRows++;
                Assert.AreEqual("Visible Room", row.Room.RoomName);
            }

            Assert.AreEqual(1, visibleRows);
            Object.Destroy(advertiserObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreatedRoomKeepsAdvertisingAfterHostEntersRoom()
        {
            SceneManager.LoadScene("Mvp");
            yield return null;
            yield return null;

            var connector = Object.FindFirstObjectByType<RoomConnector>();
            var ui = Object.FindFirstObjectByType<GameUiController>();
            Assert.NotNull(connector);
            Assert.NotNull(ui);
            Assert.IsTrue(connector.CreateLocalRoom("Persistent Room", ""));

            ui.SendMessage("EnterRoom");

            var listenerObject = new GameObject("Post Enter Room Listener");
            var listener = listenerObject.AddComponent<LocalRoomDiscovery>();
            listener.StartListening();

            var timeout = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < timeout && listener.Rooms.Count == 0)
                yield return null;

            Assert.AreEqual(1, listener.Rooms.Count);
            yield return new WaitForSecondsRealtime(3.5f);
            Assert.AreEqual(1, listener.Rooms.Count,
                "Entering the room must not stop the host's LAN advertisement.");

            connector.Leave();
            Object.Destroy(listenerObject);
            yield return null;
        }

        static IEnumerator WaitForHostAndPlayer()
        {
            var timeout = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < timeout)
            {
                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsHost &&
                    ChameleonPlayer.Local != null &&
                    ChameleonPlayer.Local.gameObject.scene == SceneManager.GetActiveScene() &&
                    ChameleonPlayer.Local.IsSpawned)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Timed out waiting for local host player to spawn.");
        }
    }
}
