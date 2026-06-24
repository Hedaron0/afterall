using System;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Integer grid position of a chunk in the infinite world.
    /// World X = X × chunkSize, World Z = Z × chunkSize (local chunk origin).
    /// </summary>
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int X;
        public readonly int Z;

        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>World-space XZ of this chunk's local (0,0) corner.</summary>
        public Vector2 WorldOrigin(float chunkSize) => new Vector2(X * chunkSize, Z * chunkSize);

        /// <summary>World-space position of the chunk root (Y = 0).</summary>
        public Vector3 WorldPosition(float chunkSize) =>
            new Vector3(X * chunkSize, 0f, Z * chunkSize);

        /// <summary>Which chunk contains <paramref name="worldPosition"/> (XZ).</summary>
        public static ChunkCoord FromWorldPosition(Vector3 worldPosition, float chunkSize)
        {
            int x = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int z = Mathf.FloorToInt(worldPosition.z / chunkSize);
            return new ChunkCoord(x, z);
        }

        public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is ChunkCoord c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(X, Z);
        public override string ToString() => $"({X}, {Z})";

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
    }
}
