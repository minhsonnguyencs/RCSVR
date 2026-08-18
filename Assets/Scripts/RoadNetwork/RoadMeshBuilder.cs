using System.Collections.Generic;
using UnityEngine;

public static class RoadMeshBuilder
{
    public static Mesh BuildRoadStrip(
        IReadOnlyList<Vector3> centerline,
        float width,
        float textureLength = 10f,
        float yOffset = 0.02f)
    {
        Mesh mesh = new Mesh
        {
            name = "Procedural Road Mesh"
        };

        if (centerline == null || centerline.Count < 2)
            return mesh;

        int pointCount = centerline.Count;

        var vertices = new Vector3[pointCount * 2];
        var uvs = new Vector2[pointCount * 2];
        var triangles = new int[(pointCount - 1) * 6];

        float accumulatedDistance = 0f;

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 previous =
                i == 0 ? centerline[i] : centerline[i - 1];

            Vector3 next =
                i == pointCount - 1 ? centerline[i] : centerline[i + 1];

            Vector3 direction = next - previous;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector3.forward;

            direction.Normalize();

            Vector3 perpendicular = new Vector3(
                -direction.z,
                0f,
                direction.x
            );

            Vector3 point = centerline[i];
            point.y += yOffset;

            vertices[i * 2] =
                point - perpendicular * (width * 0.5f);

            vertices[i * 2 + 1] =
                point + perpendicular * (width * 0.5f);

            if (i > 0)
                accumulatedDistance +=
                    Vector3.Distance(centerline[i - 1], centerline[i]);

            float v = accumulatedDistance / textureLength;

            uvs[i * 2] = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(1f, v);

            /**
            uvs[i * 2] = new Vector2(0f, accumulatedDistance);
            uvs[i * 2 + 1] = new Vector2(1f, accumulatedDistance);
            **/
        }

        for (int i = 0; i < pointCount - 1; i++)
        {
            int vertex = i * 2;
            int triangle = i * 6;

            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 1;
            triangles[triangle + 2] = vertex + 2;

            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 3;
            triangles[triangle + 5] = vertex + 2;

            /**
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;

            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
            **/
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}