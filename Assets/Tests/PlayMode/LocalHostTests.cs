using System.Collections;
using MechaChameleon;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MechaChameleon.Tests
{
    public sealed class LocalHostTests
    {
        [TearDown]
        public void TearDown()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
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

            round.BeginHunt();
            yield return null;

            Assert.AreEqual(GamePhase.Hunt, round.Phase.Value);
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
        public IEnumerator HunterShotEliminatesHider()
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
