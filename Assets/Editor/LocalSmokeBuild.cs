using UnityEditor;

namespace MechaChameleon.Editor
{
    public static class LocalSmokeBuild
    {
        public static void BuildMac()
        {
            BuildPipeline.BuildPlayer(
                new[] { "Assets/Scenes/Mvp.unity" },
                "Builds/LocalSmoke/MechaChameleonSmoke.app",
                BuildTarget.StandaloneOSX,
                BuildOptions.Development
            );
        }
    }
}
