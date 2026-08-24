using System;
using Unity.Netcode;

namespace MechaChameleon
{
    public struct PoseId : INetworkSerializable, IEquatable<PoseId>
    {
        public static readonly PoseId Stand = new(0);
        public static readonly PoseId Crouch = new(1);
        public static readonly PoseId Lie = new(2);

        byte value;

        public PoseId(byte value)
        {
            this.value = value;
        }

        public byte Value => value;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref value);
        }

        public bool Equals(PoseId other) => value == other.value;
        public override bool Equals(object obj) => obj is PoseId other && Equals(other);
        public override int GetHashCode() => value;

        public override string ToString()
        {
            if (Equals(Stand)) return nameof(Stand);
            if (Equals(Crouch)) return nameof(Crouch);
            if (Equals(Lie)) return nameof(Lie);
            return $"Pose({value})";
        }

        public static bool operator ==(PoseId left, PoseId right) => left.Equals(right);
        public static bool operator !=(PoseId left, PoseId right) => !left.Equals(right);
    }
}
