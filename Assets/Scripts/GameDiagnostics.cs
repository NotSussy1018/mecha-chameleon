using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MechaChameleon
{
    public static class GameDiagnostics
    {
        const string FolderName = "MechaChameleonDiagnostics";

        static readonly object Sync = new();
        static StreamWriter writer;
        static bool initialized;
        static bool runtimeSessionActive;
        static string sessionId;
        static string instanceName;
        static int processId;

        public static string LogDirectory
        {
            get
            {
                var directory = Path.Combine(Application.persistentDataPath, FolderName);
                Directory.CreateDirectory(directory);
                return directory;
            }
        }

        public static string CurrentLogPath { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlaySession()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            lock (Sync)
            {
                if (writer != null)
                    WriteUnlocked("INFO", "diagnostics", "session_end", "reason=session_reset");
                CloseWriter();
                initialized = false;
                runtimeSessionActive = false;
                CurrentLogPath = "";
            }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeBeforeScene()
        {
            runtimeSessionActive = true;
            EnsureInitialized();
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string category, string eventName, string details = "")
        {
            Write("INFO", category, eventName, details);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(string category, string eventName, string details = "")
        {
            Write("WARN", category, eventName, details);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Error(string category, string eventName, string details = "")
        {
            Write("ERROR", category, eventName, details);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Flush()
        {
            lock (Sync)
                writer?.Flush();
        }

        static void EnsureInitialized()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            lock (Sync)
            {
                if (initialized) return;

                initialized = true;
                processId = Process.GetCurrentProcess().Id;
                sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
                instanceName = ResolveInstanceName();

                var directory = Path.Combine(Application.persistentDataPath, FolderName);
                Directory.CreateDirectory(directory);
                CurrentLogPath = Path.Combine(directory, $"latest-{instanceName}-{processId}.log");
                writer = new StreamWriter(CurrentLogPath, append: false, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                WriteUnlocked("INFO", "diagnostics", "session_start",
                    $"unity={Application.unityVersion} platform={Application.platform} " +
                    $"product=\"{Application.productName}\" log=\"{CurrentLogPath}\"");

                WriteReadme(directory);
                Application.logMessageReceivedThreaded += CaptureUnityProblem;
                Application.quitting += Shutdown;
            }
#endif
        }

        static void Write(string level, string category, string eventName, string details)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#if UNITY_EDITOR
            if (!runtimeSessionActive) return;
#endif
            EnsureInitialized();
            lock (Sync)
                WriteUnlocked(level, category, eventName, details);
#endif
        }

        static void WriteUnlocked(string level, string category, string eventName, string details)
        {
            if (writer == null) return;

            writer.Write(DateTime.UtcNow.ToString("O"));
            writer.Write(" | level=");
            writer.Write(Clean(level));
            writer.Write(" | pid=");
            writer.Write(processId);
            writer.Write(" | instance=");
            writer.Write(instanceName);
            writer.Write(" | session=");
            writer.Write(sessionId);
            writer.Write(" | thread=");
            writer.Write(Thread.CurrentThread.ManagedThreadId);
            writer.Write(" | category=");
            writer.Write(Clean(category));
            writer.Write(" | event=");
            writer.Write(Clean(eventName));
            if (!string.IsNullOrWhiteSpace(details))
            {
                writer.Write(" | ");
                writer.Write(Clean(details));
            }
            writer.WriteLine();
        }

        static void CaptureUnityProblem(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;

            if (type == LogType.Warning &&
                condition.StartsWith("The Progressive CPU lightmapper is not available for Apple Silicon",
                    StringComparison.Ordinal))
            {
                lock (Sync)
                    WriteUnlocked("INFO", "unity", "editor_notice", $"message=\"{condition}\"");
                return;
            }

            var level = type == LogType.Warning ? "WARN" : "ERROR";
            lock (Sync)
            {
                WriteUnlocked(level, "unity", type.ToString().ToLowerInvariant(),
                    $"message=\"{condition}\" stack=\"{stackTrace}\"");
            }
        }

        static void Shutdown()
        {
            lock (Sync)
            {
                runtimeSessionActive = false;
                if (!initialized) return;
                WriteUnlocked("INFO", "diagnostics", "session_end", "reason=application_quit");
                CloseWriter();
                initialized = false;
            }
        }

        static void CloseWriter()
        {
            Application.logMessageReceivedThreaded -= CaptureUnityProblem;
            Application.quitting -= Shutdown;
            writer?.Dispose();
            writer = null;
        }

        static string ResolveInstanceName()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-name")
                    return FileSafe(args[i + 1]);
            }

            return Application.isEditor ? "MainEditor" : "Player";
        }

        static string FileSafe(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unknown";

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
                builder.Append(char.IsLetterOrDigit(character) ? character : '_');
            return builder.ToString();
        }

        static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", " ");
        }

        static void WriteReadme(string directory)
        {
            var path = Path.Combine(directory, "README.txt");
            if (File.Exists(path)) return;

            File.WriteAllText(path,
                "Mecha Chameleon development diagnostics\n" +
                "\n" +
                "Each Unity process writes latest-<instance>-<pid>.log.\n" +
                "The file is replaced when that process starts a new play session.\n" +
                "Search for level=ERROR, level=WARN, category=network, category=discovery, " +
                "category=ui, or category=round.\n" +
                "Passwords and paint payloads are never logged.\n");
        }
    }
}
