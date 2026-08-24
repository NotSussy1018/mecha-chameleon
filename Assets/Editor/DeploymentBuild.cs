using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MechaChameleon.Editor
{
    public static class DeploymentBuild
    {
        const string ScenePath = "Assets/Scenes/Mvp.unity";
        const string BuildPathArgument = "-mechaBuildPath";
        const string DevelopmentDefine = "MECHA_DEVELOPMENT";
        const string StagingDefine = "MECHA_STAGING";
        const string ProductionDefine = "MECHA_PRODUCTION";

        [Serializable]
        sealed class BuildManifest
        {
            public string environment;
            public string networkMode;
            public string unityVersion;
            public string applicationVersion;
            public string sourceRevision;
            public string sourceBranch;
            public bool sourceDirty;
            public string cloudProjectId;
            public string builtAtUtc;
            public string outputPath;
            public ulong outputBytes;
            public double buildSeconds;
        }

        [MenuItem("Mecha Chameleon/Build/macOS/Development (Local LAN)")]
        public static void BuildDevelopment()
        {
            Build(DeploymentEnvironment.Development);
        }

        [MenuItem("Mecha Chameleon/Build/macOS/Staging (MPS Relay)")]
        public static void BuildStaging()
        {
            Build(DeploymentEnvironment.Staging);
        }

        [MenuItem("Mecha Chameleon/Build/macOS/Production (MPS Relay)")]
        public static void BuildProduction()
        {
            Build(DeploymentEnvironment.Production);
        }

        [MenuItem("Mecha Chameleon/Build/Validate/Development")]
        public static void ValidateDevelopment()
        {
            ValidateAndLog(DeploymentEnvironment.Development);
        }

        [MenuItem("Mecha Chameleon/Build/Validate/Staging")]
        public static void ValidateStaging()
        {
            ValidateAndLog(DeploymentEnvironment.Staging);
        }

        [MenuItem("Mecha Chameleon/Build/Validate/Production")]
        public static void ValidateProduction()
        {
            ValidateAndLog(DeploymentEnvironment.Production);
        }

        static void Build(DeploymentEnvironment environment)
        {
            Validate(environment);

            var outputPath = GetOutputPath(environment);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ProjectRoot);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = GetBuildOptions(environment),
                extraScriptingDefines = new[] { GetEnvironmentDefine(environment) }
            };

            UnityEngine.Debug.Log(
                $"[DeploymentBuild] Building {environment} to {outputPath} " +
                $"revision={GetGitValue("rev-parse --short HEAD", "unknown")}");

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"{environment} build failed: {report.summary.result} " +
                    $"errors={report.summary.totalErrors} warnings={report.summary.totalWarnings}");

            WriteManifest(environment, report);
            UnityEngine.Debug.Log(
                $"[DeploymentBuild] {environment} build succeeded: {report.summary.outputPath}");
        }

        static void ValidateAndLog(DeploymentEnvironment environment)
        {
            Validate(environment);
            UnityEngine.Debug.Log(
                $"[DeploymentBuild] {environment} configuration is ready. " +
                $"define={GetEnvironmentDefine(environment)} output={GetOutputPath(environment)}");
        }

        static void Validate(DeploymentEnvironment environment)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Exit Play Mode before building.");

            if (!File.Exists(Path.Combine(ProjectRoot, ScenePath)))
                throw new BuildFailedException($"Required build scene is missing: {ScenePath}");

            var enabledScenes = EditorBuildSettings.scenes;
            if (Array.Find(enabledScenes, scene => scene.enabled && scene.path == ScenePath) == null)
                throw new BuildFailedException($"Required build scene is not enabled: {ScenePath}");

            var globalDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            if (ContainsEnvironmentDefine(globalDefines))
            {
                throw new BuildFailedException(
                    "Remove MECHA_DEVELOPMENT, MECHA_STAGING, and MECHA_PRODUCTION from global " +
                    "Scripting Define Symbols. DeploymentBuild injects exactly one environment per build.");
            }

            if (environment != DeploymentEnvironment.Development &&
                string.IsNullOrWhiteSpace(Application.cloudProjectId))
            {
                throw new BuildFailedException(
                    $"{environment} requires a linked Unity Cloud Project. " +
                    "Link it in Project Settings > Services before building.");
            }

            if (environment == DeploymentEnvironment.Production && IsGitDirty())
                throw new BuildFailedException("Production builds require a clean Git working tree.");

            if (environment == DeploymentEnvironment.Production && PlayerSettings.companyName == "DefaultCompany")
                throw new BuildFailedException("Set Player Settings > Company Name before a Production build.");
        }

        static BuildOptions GetBuildOptions(DeploymentEnvironment environment)
        {
            // Unity 6000.1.1f1 can crash while collecting DetailedBuildReport scene asset data.
            var options = BuildOptions.None;
            if (environment == DeploymentEnvironment.Development)
                return options | BuildOptions.Development | BuildOptions.AllowDebugging;
            if (environment == DeploymentEnvironment.Staging)
                return options | BuildOptions.Development;
            return options | BuildOptions.CompressWithLz4HC;
        }

        static string GetEnvironmentDefine(DeploymentEnvironment environment)
        {
            return environment switch
            {
                DeploymentEnvironment.Development => DevelopmentDefine,
                DeploymentEnvironment.Staging => StagingDefine,
                DeploymentEnvironment.Production => ProductionDefine,
                _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, null)
            };
        }

        static bool ContainsEnvironmentDefine(string defines)
        {
            if (string.IsNullOrEmpty(defines)) return false;
            var values = defines.Split(';');
            return Array.Exists(values, value =>
                value == DevelopmentDefine || value == StagingDefine || value == ProductionDefine);
        }

        static string GetOutputPath(DeploymentEnvironment environment)
        {
            var overridePath = GetCommandLineArgument(BuildPathArgument);
            if (!string.IsNullOrWhiteSpace(overridePath))
                return Path.GetFullPath(overridePath);

            return Path.Combine(ProjectRoot, "Builds", environment.ToString().ToLowerInvariant(),
                "MechaChameleon.app");
        }

        static void WriteManifest(DeploymentEnvironment environment, BuildReport report)
        {
            var manifest = new BuildManifest
            {
                environment = environment.ToString().ToLowerInvariant(),
                networkMode = environment == DeploymentEnvironment.Development
                    ? "local-lan"
                    : "mps-sessions-relay-player-host",
                unityVersion = Application.unityVersion,
                applicationVersion = PlayerSettings.bundleVersion,
                sourceRevision = GetGitValue("rev-parse HEAD", "unknown"),
                sourceBranch = GetGitValue("branch --show-current", "unknown"),
                sourceDirty = IsGitDirty(),
                cloudProjectId = environment == DeploymentEnvironment.Development
                    ? ""
                    : Application.cloudProjectId,
                builtAtUtc = DateTime.UtcNow.ToString("O"),
                outputPath = report.summary.outputPath,
                outputBytes = report.summary.totalSize,
                buildSeconds = report.summary.totalTime.TotalSeconds
            };

            var outputDirectory = Path.GetDirectoryName(report.summary.outputPath) ?? ProjectRoot;
            var manifestPath = Path.Combine(outputDirectory, "build-manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            UnityEngine.Debug.Log($"[DeploymentBuild] Manifest: {manifestPath}");
        }

        static bool IsGitDirty()
        {
            return !string.IsNullOrWhiteSpace(GetGitValue("status --porcelain", "dirty"));
        }

        static string GetGitValue(string arguments, string fallback)
        {
            try
            {
                var startInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = ProjectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process == null) return fallback;
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 ? output : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        static string GetCommandLineArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1];
            }

            return "";
        }

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
