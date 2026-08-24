using UnityEditor;

namespace MechaChameleon.Editor
{
    public static class DeploymentEnvironmentMenu
    {
        const string Root = "Mecha Chameleon/Environment/";

        [MenuItem(Root + "Development (Local LAN)")]
        public static void SelectDevelopment() => Select(DeploymentEnvironment.Development);

        [MenuItem(Root + "Staging (MPS Relay)")]
        public static void SelectStaging() => Select(DeploymentEnvironment.Staging);

        [MenuItem(Root + "Production (MPS Relay)")]
        public static void SelectProduction() => Select(DeploymentEnvironment.Production);

        [MenuItem(Root + "Development (Local LAN)", true)]
        static bool ValidateDevelopment() => SetCheck(DeploymentEnvironment.Development);

        [MenuItem(Root + "Staging (MPS Relay)", true)]
        static bool ValidateStaging() => SetCheck(DeploymentEnvironment.Staging);

        [MenuItem(Root + "Production (MPS Relay)", true)]
        static bool ValidateProduction() => SetCheck(DeploymentEnvironment.Production);

        static void Select(DeploymentEnvironment environment)
        {
            EditorPrefs.SetString(DeploymentEnvironmentSettings.EditorPreferenceKey, environment.ToString());
            UnityEngine.Debug.Log($"[Environment] Selected {environment}. Restart Play Mode before testing.");
        }

        static bool SetCheck(DeploymentEnvironment environment)
        {
            Menu.SetChecked(Root + Label(environment), DeploymentEnvironmentSettings.Current == environment);
            return true;
        }

        static string Label(DeploymentEnvironment environment)
        {
            return environment switch
            {
                DeploymentEnvironment.Development => "Development (Local LAN)",
                DeploymentEnvironment.Staging => "Staging (MPS Relay)",
                _ => "Production (MPS Relay)"
            };
        }
    }
}
