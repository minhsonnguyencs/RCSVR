using System.Collections.Generic;
using UnityEngine;

public class TrafficSpawner : MonoBehaviour
{
    public RoadNetworkManager network;
    public IntersectionManager intersectionManager;

    [Tooltip("Prefab containing exactly one TrafficAgentBase-derived controller.")]
    public GameObject vehiclePrefab;

    [Header("Traffic")]
    [Min(0)] public int vehicleCount = 20;

    [Header("Random top speed (km/h)")]
    [Min(1f)] public float minimumTopSpeedKmh = 35f;
    [Min(1f)] public float maximumTopSpeedKmh = 50f;

    [Header("Spawn bookkeeping")]
    [Tooltip("If enabled, spawned cars are parented under this spawner for a clean hierarchy.")]
    public bool parentVehiclesToSpawner = true;

    private readonly List<GameObject> spawnedVehicles = new List<GameObject>();

    public int CurrentSpawnedVehicleCount => spawnedVehicles.Count;

    void Start()
    {
        RespawnVehicles(vehicleCount);
    }

    public void RespawnVehicles()
    {
        RespawnVehicles(vehicleCount);
    }

    /// <summary>
    /// Main runtime/UI entry point: destroy the currently spawned traffic and
    /// repopulate it with exactly newCount vehicles.
    /// </summary>
    public void RespawnVehicles(int newCount)
    {
        vehicleCount = Mathf.Max(0, newCount);
        ClearVehicles();
        SpawnVehicles(vehicleCount);
    }

    /// <summary>
    /// Convenient for TMP_InputField/InputField dynamic string events. Wire an
    /// input's End Edit event to this method and the traffic is immediately
    /// respawned with the entered number.
    /// </summary>
    public void SetVehicleCountFromString(string value)
    {
        if (!int.TryParse(value, out int parsed))
        {
            Debug.LogWarning("TrafficSpawner: could not parse vehicle count: " + value);
            return;
        }

        vehicleCount = Mathf.Max(0, parsed);
    }

    public void RespawnVehiclesFromString(string value)
    {
        if (!int.TryParse(value, out int parsed))
        {
            Debug.LogWarning("TrafficSpawner: could not parse vehicle count: " + value);
            return;
        }

        RespawnVehicles(parsed);
    }

    public void ClearVehicles()
    {
        if (intersectionManager != null)
            intersectionManager.ClearAllRegistrations();

        for (int i = spawnedVehicles.Count - 1; i >= 0; i--)
        {
            GameObject car = spawnedVehicles[i];
            if (car != null)
            {
                car.SetActive(false);
                Destroy(car);
            }
        }

        spawnedVehicles.Clear();

        if (network != null && network.occupancyManager != null)
            network.occupancyManager.ResetState();
    }

    private bool ValidateConfiguration()
    {
        if (network == null || vehiclePrefab == null)
        {
            Debug.LogError("TrafficSpawner is not fully configured.");
            return false;
        }

        if (network.allLanes == null || network.allLanes.Count == 0)
        {
            Debug.LogError("No lanes available.");
            return false;
        }

        TrafficAgentBase[] controllers = vehiclePrefab.GetComponents<TrafficAgentBase>();
        if (controllers.Length != 1)
        {
            Debug.LogError(
                "Vehicle prefab must contain exactly one TrafficAgentBase-derived controller, " +
                "but found " + controllers.Length + "."
            );
            return false;
        }

        return true;
    }

    private void SpawnVehicles(int count)
    {
        if (!ValidateConfiguration())
            return;

        float minSpeed = Mathf.Min(minimumTopSpeedKmh, maximumTopSpeedKmh);
        float maxSpeed = Mathf.Max(minimumTopSpeedKmh, maximumTopSpeedKmh);

        for (int i = 0; i < count; i++)
            SpawnVehicle(minSpeed, maxSpeed);
    }

    private void SpawnVehicle(float minSpeedKmh, float maxSpeedKmh)
    {
        Lane lane = network.allLanes[Random.Range(0, network.allLanes.Count)];

        int spawnPointIndex = 0;
        if (lane.points.Count > 2)
            spawnPointIndex = Random.Range(0, lane.points.Count - 1);

        Transform parent = parentVehiclesToSpawner ? transform : null;
        GameObject car = Instantiate(vehiclePrefab, parent);
        spawnedVehicles.Add(car);

        TrafficAgentBase controller = car.GetComponent<TrafficAgentBase>();
        if (controller == null)
        {
            Debug.LogError("Spawned vehicle has no TrafficAgentBase-derived controller.");
            spawnedVehicles.Remove(car);
            Destroy(car);
            return;
        }

        float topSpeedKmh = Random.Range(minSpeedKmh, maxSpeedKmh);
        controller.SetTopSpeedKmh(topSpeedKmh);
        controller.Initialize(network, intersectionManager, lane, spawnPointIndex);
    }
}
