using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
public class CityRoadNetworkImporter : MonoBehaviour
{
    [Tooltip("StreamingAssets-relative path to the road centerline JSON (extracted from CityRoad.obj).")]
    public string fileName = "ingolstadt_road_centerlines.json";

    void OnEnable()
    {
        var container = GetComponent<SplineContainer>();
        if (container.Splines.Count == 0) Import();
    }

    [ContextMenu("Import Road Network")]
    public void Import()
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogError("Road centerline JSON not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path);
        RoadCenterlineData data = JsonUtility.FromJson<RoadCenterlineData>(json);

        var container = GetComponent<SplineContainer>();
        var splines = new List<Spline>(data.ways.Length);

        foreach (RoadWayData way in data.ways)
        {
            if (way.points == null || way.points.Length < 2) continue;

            var positions = new float3[way.points.Length];
            for (int i = 0; i < way.points.Length; i++)
                positions[i] = new float3(way.points[i].x, 0f, way.points[i].z);

            splines.Add(SplineFactory.CreateLinear(positions));
        }

        container.Splines = splines;
        Debug.Log($"Imported {splines.Count} road ways into SplineContainer");

        var roadSpline = GetComponent<CityRoadSpline>();
        if (roadSpline != null) roadSpline.Rebuild();
    }
}

[System.Serializable]
public class RoadCenterlineData
{
    public RoadWayData[] ways;
}

[System.Serializable]
public class RoadWayData
{
    public string id;
    public Point2D[] points;
}

[System.Serializable]
public class Point2D
{
    public float x;
    public float z;
}
