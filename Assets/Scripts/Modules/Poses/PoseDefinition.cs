using System;
using UnityEngine;

namespace MechaChameleon.Poses
{
    [Serializable]
    public struct PoseDefinition
    {
        [SerializeField] byte networkId;
        [SerializeField] string displayName;
        [SerializeField] KeyCode shortcut;
        [SerializeField] Vector3 visualPosition;
        [SerializeField] Vector3 visualEulerAngles;
        [SerializeField] bool constrainMovement;
        [SerializeField] Vector3 collisionCenter;
        [SerializeField] Vector3 collisionHalfExtents;

        public PoseId Id => new(networkId);
        public string DisplayName => displayName;
        public KeyCode Shortcut => shortcut;
        public Vector3 VisualPosition => visualPosition;
        public Quaternion VisualRotation => Quaternion.Euler(visualEulerAngles);
        public bool ConstrainMovement => constrainMovement;
        public Vector3 CollisionCenter => collisionCenter;
        public Vector3 CollisionHalfExtents => collisionHalfExtents;

    }
}
