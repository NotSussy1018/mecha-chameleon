using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MechaChameleon
{
    public sealed class LocalMultiplayerSmokeRunner : MonoBehaviour
    {
        const string ArgName = "-mechaSmoke";
        const float TimeoutSeconds = 20f;

        string mode;
        bool started;
        float startedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateFromCommandLine()
        {
            var mode = GetArgValue(ArgName);
            if (string.IsNullOrWhiteSpace(mode)) return;

            var runner = new GameObject("Local Multiplayer Smoke Runner").AddComponent<LocalMultiplayerSmokeRunner>();
            runner.mode = mode.Trim().ToLowerInvariant();
            DontDestroyOnLoad(runner.gameObject);
        }

        void Update()
        {
            if (started) return;
            if (SceneManager.GetActiveScene().name != "Mvp") return;

            var connector = FindFirstObjectByType<RoomConnector>();
            if (connector == null) return;

            started = true;
            startedAt = Time.realtimeSinceStartup;

            if (mode == "host")
            {
                Debug.Log("[Smoke] Host starting.");
                connector.HostLocal();
                StartCoroutine(WaitForHostSuccess());
            }
            else if (mode == "client")
            {
                Debug.Log("[Smoke] Client starting.");
                connector.JoinLocal();
                StartCoroutine(WaitForClientSuccess());
            }
            else
            {
                Fail($"Unknown smoke mode: {mode}");
            }
        }

        IEnumerator WaitForHostSuccess()
        {
            while (Time.realtimeSinceStartup - startedAt < TimeoutSeconds)
            {
                var manager = NetworkManager.Singleton;
                if (manager != null && manager.IsHost && manager.ConnectedClientsIds.Count >= 2)
                {
                    var round = ChameleonRoundManager.Instance;
                    round.StartPaintPhase();
                    yield return null;

                    foreach (var candidate in FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None))
                    {
                        if (candidate.Role.Value != PlayerRole.Hider) continue;

                        candidate.Paint.Strokes.Add(new PaintStroke(
                            PaintPart.Body,
                            new Vector2(0.2f, 0.3f),
                            new Vector2(0.4f, 0.5f),
                            color: ChameleonPalette.Colors[2],
                            radius: 4,
                            sequence: 1));
                        yield return Pass("Host published paint state with two connected players.");
                        yield break;
                    }

                    Fail("Host could not find a hider paint surface.");
                }

                yield return null;
            }

            Fail("Host timed out waiting for client.");
        }

        IEnumerator WaitForClientSuccess()
        {
            while (Time.realtimeSinceStartup - startedAt < TimeoutSeconds)
            {
                var manager = NetworkManager.Singleton;
                if (manager != null && manager.IsConnectedClient && ChameleonPlayer.Local != null)
                {
                    foreach (var candidate in FindObjectsByType<ChameleonPlayer>(FindObjectsSortMode.None))
                    {
                        if (candidate.Paint != null && candidate.Paint.StrokeCount > 0)
                        {
                            yield return Pass("Client received synchronized paint state.");
                            yield break;
                        }
                    }
                }

                yield return null;
            }

            Fail("Client timed out waiting for local player spawn.");
        }

        IEnumerator Pass(string message)
        {
            Debug.Log($"[Smoke] PASS: {message}");
            yield return new WaitForSeconds(1f);
            Application.Quit(0);
        }

        static void Fail(string message)
        {
            Debug.LogError($"[Smoke] FAIL: {message}");
            Application.Quit(1);
        }

        static string GetArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }

            return "";
        }
    }
}
