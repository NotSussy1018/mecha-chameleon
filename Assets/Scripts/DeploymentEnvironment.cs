using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MechaChameleon
{
    public enum DeploymentEnvironment
    {
        Development,
        Staging,
        Production
    }

    public static class DeploymentEnvironmentSettings
    {
        public const string EditorPreferenceKey = "MechaChameleon.DeploymentEnvironment";

        static DeploymentEnvironment? testOverride;

        public static DeploymentEnvironment Current
        {
            get
            {
                if (testOverride.HasValue) return testOverride.Value;

#if MECHA_DEVELOPMENT
                return DeploymentEnvironment.Development;
#elif MECHA_PRODUCTION
                return DeploymentEnvironment.Production;
#elif MECHA_STAGING
                return DeploymentEnvironment.Staging;
#elif UNITY_EDITOR
                return Parse(EditorPrefs.GetString(EditorPreferenceKey, "development"));
#elif DEVELOPMENT_BUILD
                return DeploymentEnvironment.Development;
#else
                return DeploymentEnvironment.Production;
#endif
            }
        }

        public static string UgsEnvironmentName => Current.ToString().ToLowerInvariant();
        public static bool UsesOnlineServices => Current != DeploymentEnvironment.Development;

        public static DeploymentEnvironment Parse(string value)
        {
            return Enum.TryParse(value, true, out DeploymentEnvironment environment)
                ? environment
                : DeploymentEnvironment.Development;
        }

        public static void SetTestOverride(DeploymentEnvironment? environment)
        {
            testOverride = environment;
        }
    }
}
