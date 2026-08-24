using MechaChameleon;
using MechaChameleon.Poses;
using MechaChameleon.Rooms;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Unity.Netcode;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MechaChameleon.Tests
{
    public sealed class MvpSceneTests
    {
        [TearDown]
        public void TearDown()
        {
            DeploymentEnvironmentSettings.SetTestOverride(null);
        }

        [TestCase("development", DeploymentEnvironment.Development, false)]
        [TestCase("staging", DeploymentEnvironment.Staging, true)]
        [TestCase("production", DeploymentEnvironment.Production, true)]
        public void DeploymentEnvironmentControlsOnlineServices(
            string value,
            DeploymentEnvironment expected,
            bool usesOnlineServices)
        {
            var environment = DeploymentEnvironmentSettings.Parse(value);
            DeploymentEnvironmentSettings.SetTestOverride(environment);

            Assert.AreEqual(expected, DeploymentEnvironmentSettings.Current);
            Assert.AreEqual(value, DeploymentEnvironmentSettings.UgsEnvironmentName);
            Assert.AreEqual(usesOnlineServices, DeploymentEnvironmentSettings.UsesOnlineServices);

        }

        [TestCase("", true)]
        [TestCase("1234567", false)]
        [TestCase("12345678", true)]
        public void OnlineRoomPasswordUsesMpsLengthRules(string password, bool expected)
        {
            Assert.AreEqual(expected, RoomConnector.IsOnlinePasswordValid(password));
        }

        [Test]
        public void RoomListingCopyKeepsPasswordAndJoinLocksSeparate()
        {
            var listing = new RoomListing
            {
                IsLocked = true,
                IsJoinLocked = false
            };

            var copy = listing.Copy();

            Assert.IsTrue(copy.IsLocked);
            Assert.IsFalse(copy.IsJoinLocked);
        }

        [Test]
        public void UsernamePasswordValidationMatchesUnityAuthenticationRules()
        {
            Assert.AreEqual("", UgsBootstrap.ValidateCredentials("Paint_Player.1", "Hide!Game9"));
            StringAssert.Contains("3-20", UgsBootstrap.ValidateCredentials("ab", "Hide!Game9"));
            StringAssert.Contains("dot, dash, @, and underscore",
                UgsBootstrap.ValidateCredentials("bad name", "Hide!Game9"));
        }

        [Test]
        public void PasswordValidationRequiresAllCharacterGroups()
        {
            StringAssert.Contains("8-30", UgsBootstrap.ValidateCredentials("player", "S!1a"));
            StringAssert.Contains("upper, lower, number, and special",
                UgsBootstrap.ValidateCredentials("player", "password1!"));
            StringAssert.Contains("upper, lower, number, and special",
                UgsBootstrap.ValidateCredentials("player", "Password12"));
        }

        [Test]
        public void MvpSceneContainsRequiredNetworkObjects()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Mvp.unity");

            var networkManager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.NotNull(networkManager);
            Assert.NotNull(Object.FindFirstObjectByType<RoomConnector>());
            Assert.NotNull(Object.FindFirstObjectByType<ChameleonRoundManager>());
            Assert.NotNull(Object.FindFirstObjectByType<MvpHud>());
            Assert.NotNull(Resources.FindObjectsOfTypeAll<ChameleonPlayer>());

            Assert.IsNotEmpty(networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists);
            Assert.IsTrue(
                networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists[0]
                    .Contains(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ChameleonPlayer.prefab")),
                "NetworkManager must register ChameleonPlayer so clients can spawn player objects."
            );
        }

        [Test]
        public void MvpSceneDoesNotContainUnspawnedPlayer()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Mvp.unity");

            Assert.IsNull(
                Object.FindFirstObjectByType<ChameleonPlayer>(),
                "The scene should not contain a pre-placed player. Players must be spawned by Netcode after hosting/joining."
            );
        }

        [Test]
        public void PlayerPrefabHasUvPaintSurfaces()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ChameleonPlayer.prefab");
            Assert.NotNull(prefab);
            Assert.NotNull(prefab.GetComponent<ChameleonPaint>());

            var paintColliders = prefab.GetComponentsInChildren<MeshCollider>(includeInactive: true);
            Assert.AreEqual(2, paintColliders.Length);
            foreach (var paintCollider in paintColliders)
            {
                Assert.NotNull(paintCollider.sharedMesh);
                Assert.IsFalse(paintCollider.enabled, "Paint colliders must not affect ordinary player movement.");
            }
        }

        [Test]
        public void PlayerPrefabUsesValidDataDrivenPoseCatalog()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ChameleonPlayer.prefab");
            var player = prefab.GetComponent<ChameleonPlayer>();
            var catalog = player.AvailablePoses;

            Assert.NotNull(catalog);
            Assert.AreEqual(PoseId.Stand, catalog.DefaultPoseId);
            Assert.IsTrue(catalog.HasUniqueIdsAndShortcuts());
            Assert.IsTrue(catalog.TryGet(PoseId.Stand, out _));
            Assert.IsTrue(catalog.TryGet(PoseId.Crouch, out _));
            Assert.IsTrue(catalog.TryGet(PoseId.Lie, out _));
        }

        [Test]
        public void MvpSceneUsesConfiguredRoomModule()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Mvp.unity");

            var round = Object.FindFirstObjectByType<ChameleonRoundManager>();
            var rooms = Object.FindObjectsByType<RoomModule>(FindObjectsSortMode.None);
            Assert.NotNull(round);
            Assert.IsNotEmpty(rooms);

            var roomIds = new HashSet<string>();
            foreach (var room in rooms)
            {
                Assert.IsTrue(room.IsConfigured(), $"Room module is incomplete: {room.name}");
                Assert.IsTrue(roomIds.Add(room.RoomId), $"Duplicate RoomId: {room.RoomId}");
            }

            Assert.AreSame(rooms[0], round.ActiveRoom);
            Assert.AreEqual("house-room", rooms[0].RoomId);
            Assert.AreEqual(4, rooms[0].LobbySpawnCount);
            Assert.AreEqual(4, rooms[0].HiderSpawnCount);
        }

        [Test]
        public void PaintStrokeQuantizesUvsWithinOneBytePrecision()
        {
            var stroke = new PaintStroke(
                PaintPart.Body,
                new Vector2(0.123f, 0.456f),
                new Vector2(0.789f, 0.876f),
                color: new Color32(12, 34, 56, 255),
                radius: 4,
                sequence: 17);

            Assert.AreEqual(0.123f, stroke.StartUv.x, 1f / 255f);
            Assert.AreEqual(0.456f, stroke.StartUv.y, 1f / 255f);
            Assert.AreEqual(0.789f, stroke.EndUv.x, 1f / 255f);
            Assert.AreEqual(0.876f, stroke.EndUv.y, 1f / 255f);
            Assert.AreEqual(new Color32(12, 34, 56, 255), stroke.Color);
            Assert.AreEqual(4, stroke.Radius);
            Assert.AreEqual(17, stroke.Sequence);
        }

        [Test]
        public void MvpSceneContainsCompleteUiDesign()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Mvp.unity");

            var canvas = GameObject.Find("Game UI Canvas");
            Assert.NotNull(canvas);
            Assert.NotNull(Object.FindFirstObjectByType<GameUiController>());
            Assert.NotNull(Object.FindFirstObjectByType<LocalRoomDiscovery>());

            var requiredScreens = new[]
            {
                "Menu Background",
                "LoginPanel",
                "HomePanel",
                "CreateRoomPanel",
                "JoinRoomPanel",
                "RoomPanel",
                "OptionsPanel",
                "GameHud",
                "PasswordModal",
                "ResultOverlay"
            };

            foreach (var screen in requiredScreens)
                Assert.NotNull(canvas.transform.Find(screen), $"Missing UI design screen: {screen}");

            var background = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/UI/Generated/menu_waterfront_background.png");
            var button = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Generated/wood_button.png");
            var panel = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Generated/wood_panel.png");
            var menuFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/LilitaOne-Regular.ttf");
            Assert.NotNull(background);
            Assert.NotNull(button);
            Assert.NotNull(panel);
            Assert.NotNull(menuFont);

            var login = canvas.transform.Find("LoginPanel");
            Assert.NotNull(login.Find("Account Board/Username").GetComponent<InputField>());
            Assert.AreEqual(InputField.ContentType.Password,
                login.Find("Account Board/Password").GetComponent<InputField>().contentType);
            Assert.NotNull(login.Find("Account Board/Sign In Tab").GetComponent<Button>());
            Assert.NotNull(login.Find("Account Board/Sign Up Tab").GetComponent<Button>());
            Assert.NotNull(login.Find("Account Board/Auth Submit").GetComponent<Button>());

            var homeTitle = canvas.transform.Find("HomePanel/Title").GetComponent<Text>();
            var loginTitle = login.Find("Account Board/Title").GetComponent<Text>();
            var gameTimer = canvas.transform.Find("GameHud/Timer").GetComponent<Text>();
            Assert.AreSame(menuFont, homeTitle.font);
            Assert.AreSame(menuFont, loginTitle.font);
            Assert.AreNotSame(menuFont, gameTimer.font);
            Assert.AreEqual(4, canvas.GetComponentsInChildren<LocalRoomRowView>(true).Length);
            Assert.AreEqual(8, canvas.GetComponentsInChildren<RoomPlayerRowView>(true).Length);

            foreach (var roomRow in canvas.GetComponentsInChildren<LocalRoomRowView>(true))
                Assert.IsFalse(roomRow.gameObject.activeSelf, "Generated room slots must not contain fake rooms.");
            foreach (var playerRow in canvas.GetComponentsInChildren<RoomPlayerRowView>(true))
                Assert.IsFalse(playerRow.gameObject.activeSelf, "Player slots must be hidden until someone connects.");
        }
    }
}
