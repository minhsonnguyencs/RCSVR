using System;

[Serializable]
public class RoadGraphData
{
    public RoadNodeData[] nodes;
    public RoadEdgeData[] edges;
}

[Serializable]
public class RoadNodeData
{
    public long id;
    public Vector3Data position;
}

[Serializable]
public class RoadEdgeData
{
    public string id;
    public long from;
    public long to;
    public Vector3Data[] centerline;
    public string highway;
    public int lanes;
    public float maxspeed;
    public bool oneway;
    public float length;
    public bool simulate;
}

[Serializable]
public class Vector3Data
{
    public float x;
    public float y;
    public float z;
}