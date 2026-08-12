using UnityEngine;

public class TrafficSpawner : MonoBehaviour
{
    public RoadNetworkManager network;
    public IntersectionManager intersectionManager;

    [Tooltip("Prefab containing exactly one TrafficAgentBase-derived controller: SimpleTrafficVehicle, AwareTrafficAgent_LaneUnaware, AwareTrafficAgent, or AwareTrafficAgent_DeadlockEscape.")]
    public GameObject vehiclePrefab;

    [Header("Traffic")]
    public int vehicleCount = 20;

    void Start()
    {
        SpawnVehicles();
    }

    private void SpawnVehicles()
    {
        if (network == null || vehiclePrefab == null)
        {
            Debug.LogError("TrafficSpawner is not fully configured.");
            return;
        }

        if (network.allLanes == null || network.allLanes.Count == 0)
        {
            Debug.LogError("No lanes available.");
            return;
        }

        TrafficAgentBase prefabController =
            vehiclePrefab.GetComponent<TrafficAgentBase>();

        if (prefabController == null)
        {
            Debug.LogError(
                "Vehicle prefab has no TrafficAgentBase-derived controller. " +
                "Attach exactly one of: SimpleTrafficVehicle, " +
                "AwareTrafficAgent_LaneUnaware, AwareTrafficAgent, " +
                "AwareTrafficAgent_DeadlockEscape."
            );
            return;
        }

        TrafficAgentBase[] allControllers =
            vehiclePrefab.GetComponents<TrafficAgentBase>();

        if (allControllers.Length != 1)
        {
            Debug.LogError(
                "Vehicle prefab must contain exactly one traffic controller, " +
                "but found " + allControllers.Length + "."
            );
            return;
        }

        for (int i = 0; i < vehicleCount; i++)
        {
            SpawnVehicle();
        }
    }

    private void SpawnVehicle()
    {
        Lane lane =
            network.allLanes[
                Random.Range(0, network.allLanes.Count)
            ];

        int spawnPointIndex = 0;

        if (lane.points.Count > 2)
        {
            spawnPointIndex =
                Random.Range(0, lane.points.Count - 1);
        }

        GameObject car = Instantiate(vehiclePrefab, transform);

        TrafficAgentBase controller =
            car.GetComponent<TrafficAgentBase>();

        if (controller == null)
        {
            Debug.LogError(
                "Spawned vehicle has no TrafficAgentBase-derived controller."
            );

            Destroy(car);
            return;
        }

        controller.Initialize(
            network,
            intersectionManager,
            lane,
            spawnPointIndex
        );
    }
}
