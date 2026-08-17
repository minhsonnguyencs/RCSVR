using System;

[Serializable]
public class TrafficODMatrixData
{
    public TrafficODRow[] rows;
}

[Serializable]
public class TrafficODRow
{
    public string originZone;
    public TrafficODWeight[] destinations;
}

[Serializable]
public class TrafficODWeight
{
    public string destinationZone;
    public float weight;
}
