using System;
using Unity.Netcode;
using UnityEngine;

namespace MechaChameleon
{
    public enum GamePhase
    {
        Lobby = 0,
        Paint = 1,
        Hunt = 2,
        Result = 3
    }

    public enum PlayerRole
    {
        Hider = 0,
        Seeker = 1
    }

    public enum PoseId
    {
        Stand = 0,
        Crouch = 1,
        Lie = 2
    }

    public enum PaintPart : byte
    {
        Head = 0,
        Body = 1
    }

    public static class ChameleonPalette
    {
        public static readonly Color32[] Colors =
        {
            new(255, 255, 255, 255),
            new(30, 30, 30, 255),
            new(218, 56, 50, 255),
            new(237, 135, 45, 255),
            new(238, 214, 67, 255),
            new(68, 160, 80, 255),
            new(54, 119, 220, 255),
            new(88, 70, 185, 255),
            new(180, 75, 170, 255),
            new(119, 102, 78, 255)
        };
    }

    public struct PaintStroke : INetworkSerializable, IEquatable<PaintStroke>
    {
        public PaintPart Part;
        public byte StartU;
        public byte StartV;
        public byte EndU;
        public byte EndV;
        public byte Red;
        public byte Green;
        public byte Blue;
        public byte Radius;
        public ushort Sequence;

        public PaintStroke(
            PaintPart part,
            Vector2 startUv,
            Vector2 endUv,
            Color32 color,
            byte radius,
            ushort sequence)
        {
            Part = part;
            StartU = Quantize(startUv.x);
            StartV = Quantize(startUv.y);
            EndU = Quantize(endUv.x);
            EndV = Quantize(endUv.y);
            Red = color.r;
            Green = color.g;
            Blue = color.b;
            Radius = radius;
            Sequence = sequence;
        }

        public Vector2 StartUv => new(StartU / 255f, StartV / 255f);
        public Vector2 EndUv => new(EndU / 255f, EndV / 255f);
        public Color32 Color => new(Red, Green, Blue, 255);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Part);
            serializer.SerializeValue(ref StartU);
            serializer.SerializeValue(ref StartV);
            serializer.SerializeValue(ref EndU);
            serializer.SerializeValue(ref EndV);
            serializer.SerializeValue(ref Red);
            serializer.SerializeValue(ref Green);
            serializer.SerializeValue(ref Blue);
            serializer.SerializeValue(ref Radius);
            serializer.SerializeValue(ref Sequence);
        }

        public bool Equals(PaintStroke other)
        {
            return Part == other.Part &&
                   StartU == other.StartU &&
                   StartV == other.StartV &&
                   EndU == other.EndU &&
                   EndV == other.EndV &&
                   Red == other.Red &&
                   Green == other.Green &&
                   Blue == other.Blue &&
                   Radius == other.Radius &&
                   Sequence == other.Sequence;
        }

        static byte Quantize(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Repeat(value, 1f) * 255f);
        }
    }
}
