using System.Collections.Generic;
using UnityEngine;

public static class LaneGeometry
{
    public static List<Vector3> BuildOffsetLane(
        Vector3Data[] centerline,
        float offset)
    {
        List<Vector3> result =
            new List<Vector3>(centerline.Length);

        for (int i = 0; i < centerline.Length; i++)
        {
            Vector3 current = new Vector3(
                centerline[i].x,
                centerline[i].y,
                centerline[i].z
            );

            Vector3 previous;

            if (i == 0)
            {
                previous = current;
            }
            else
            {
                previous = new Vector3(
                    centerline[i - 1].x,
                    centerline[i - 1].y,
                    centerline[i - 1].z
                );
            }

            Vector3 next;

            if (i == centerline.Length - 1)
            {
                next = current;
            }
            else
            {
                next = new Vector3(
                    centerline[i + 1].x,
                    centerline[i + 1].y,
                    centerline[i + 1].z
                );
            }

            Vector3 direction = next - previous;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                result.Add(current);
                continue;
            }

            direction.Normalize();

            // Right-hand side of the road
            Vector3 right = new Vector3(
                direction.z,
                0f,
                -direction.x
            );

            result.Add(
                current + right * offset
            );
        }

        return result;
    }
}