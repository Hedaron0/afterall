using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Top-down gizmo drawing for WallSpec segments — mirrors GeometrySpawner layout
    /// without spawning meshes. Used by BspDebugger for live MapConfig preview.
    /// </summary>
    public static class WallGizmoDrawer
    {
        public static void DrawChunk(
            Vector3 origin,
            ChunkSpec spec,
            float wallThickness,
            Color wallColor,
            Color openingColor,
            bool drawOpenings,
            float wallHeight = 0f)
        {
            float halfT = wallThickness * 0.5f;

            foreach (var wall in spec.Walls)
                DrawWall(origin, wall, halfT, wallColor, openingColor, drawOpenings, wallHeight);
        }

        private static void DrawWall(
            Vector3 origin,
            WallSpec wall,
            float halfT,
            Color wallColor,
            Color openingColor,
            bool drawOpenings,
            float wallHeight)
        {
            float totalLength = wall.Boundary.Length;
            float cursor      = 0f;
            var   openings    = wall.Openings;

            for (int i = 0; i <= openings.Count; i++)
            {
                float segEnd    = i < openings.Count ? openings[i].Offset : totalLength;
                float segLength = segEnd - cursor;

                if (segLength > 0.05f)
                {
                    Gizmos.color = wallColor;
                    DrawSolidSegment(origin, wall.Boundary, cursor, segLength, halfT, wallHeight);
                }

                if (drawOpenings && i < openings.Count)
                {
                    Gizmos.color = openingColor;
                    DrawOpening(origin, wall.Boundary, openings[i], halfT, wallHeight);
                }

                if (i < openings.Count)
                    cursor = openings[i].EndOffset;
            }
        }

        private static void DrawSolidSegment(
            Vector3 origin,
            BspBoundary boundary,
            float segOffset,
            float segLength,
            float halfT,
            float wallHeight)
        {
            if (wallHeight > 0f)
            {
                DrawSegmentVolume(origin, boundary, segOffset, segLength, halfT, wallHeight);
                return;
            }

            DrawSegmentFootprint(origin, boundary, segOffset, segLength, halfT);
        }

        private static void DrawSegmentFootprint(
            Vector3 origin,
            BspBoundary boundary,
            float segOffset,
            float segLength,
            float halfT)
        {
            if (boundary.IsVertical)
            {
                float x  = boundary.Start.x;
                float z0 = boundary.Start.y + segOffset;
                float z1 = z0 + segLength;
                DrawThickRect(origin, x - halfT, z0, x + halfT, z1);
            }
            else
            {
                float z  = boundary.Start.y;
                float x0 = boundary.Start.x + segOffset;
                float x1 = x0 + segLength;
                DrawThickRect(origin, x0, z - halfT, x1, z + halfT);
            }
        }

        private static void DrawSegmentVolume(
            Vector3 origin,
            BspBoundary boundary,
            float segOffset,
            float segLength,
            float halfT,
            float wallHeight)
        {
            if (boundary.IsVertical)
            {
                float x  = boundary.Start.x;
                float z0 = boundary.Start.y + segOffset;
                var center = new Vector3(
                    origin.x + x,
                    origin.y + wallHeight * 0.5f,
                    origin.z + z0 + segLength * 0.5f);
                Gizmos.DrawWireCube(center, new Vector3(halfT * 2f, wallHeight, segLength));
            }
            else
            {
                float z  = boundary.Start.y;
                float x0 = boundary.Start.x + segOffset;
                var center = new Vector3(
                    origin.x + x0 + segLength * 0.5f,
                    origin.y + wallHeight * 0.5f,
                    origin.z + z);
                Gizmos.DrawWireCube(center, new Vector3(segLength, wallHeight, halfT * 2f));
            }
        }

        private static void DrawOpening(
            Vector3 origin,
            BspBoundary boundary,
            OpeningSpec opening,
            float halfT,
            float wallHeight)
        {
            float margin = halfT * 1.5f;

            if (boundary.IsVertical)
            {
                float x  = boundary.Start.x;
                float z0 = boundary.Start.y + opening.Offset;
                float z1 = z0 + opening.Width;
                DrawThickRect(origin, x - margin, z0, x + margin, z1, wallHeight * 0.15f);
            }
            else
            {
                float z  = boundary.Start.y;
                float x0 = boundary.Start.x + opening.Offset;
                float x1 = x0 + opening.Width;
                DrawThickRect(origin, x0, z - margin, x1, z + margin, wallHeight * 0.15f);
            }
        }

        private static void DrawThickRect(
            Vector3 origin, float xMin, float zMin, float xMax, float zMax, float y = 0.04f)
        {
            var center = origin + new Vector3((xMin + xMax) * 0.5f, y, (zMin + zMax) * 0.5f);
            Gizmos.DrawCube(center, new Vector3(xMax - xMin, y * 2f, zMax - zMin));
        }
    }
}
