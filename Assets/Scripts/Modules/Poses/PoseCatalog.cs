using UnityEngine;

namespace MechaChameleon.Poses
{
    [CreateAssetMenu(menuName = "Mecha Chameleon/Pose Catalog", fileName = "PoseCatalog")]
    public sealed class PoseCatalog : ScriptableObject
    {
        [SerializeField] byte defaultPoseId;
        [SerializeField] PoseDefinition[] definitions = { };

        public PoseId DefaultPoseId => new(defaultPoseId);
        public int Count => definitions?.Length ?? 0;

        public PoseDefinition GetAt(int index) => definitions[index];

        public bool TryGet(PoseId id, out PoseDefinition definition)
        {
            if (definitions != null)
            {
                for (var i = 0; i < definitions.Length; i++)
                {
                    if (definitions[i].Id != id) continue;
                    definition = definitions[i];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public bool HasUniqueIdsAndShortcuts()
        {
            if (definitions == null || definitions.Length == 0) return false;

            for (var i = 0; i < definitions.Length; i++)
            {
                for (var j = i + 1; j < definitions.Length; j++)
                {
                    if (definitions[i].Id == definitions[j].Id ||
                        definitions[i].Shortcut == definitions[j].Shortcut)
                        return false;
                }
            }

            return TryGet(DefaultPoseId, out _);
        }

        public static PoseDefinition Resolve(PoseCatalog catalog, PoseId id)
        {
            return catalog != null && catalog.TryGet(id, out var definition)
                ? definition
                : default;
        }
    }
}
