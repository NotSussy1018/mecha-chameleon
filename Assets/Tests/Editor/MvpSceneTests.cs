using MechaChameleon;
using NUnit.Framework;
using UnityEditor;
using Unity.Netcode;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MechaChameleon.Tests
{
    public sealed class MvpSceneTests
    {
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

            var requiredScreens = new[]
            {
                "Menu Background",
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
            Assert.NotNull(background);
            Assert.NotNull(button);
            Assert.NotNull(panel);
        }
    }
}
