using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct VehiclePrefabEntry
{
    public CargoType cargoType;
    public GameObject prefab;
}

public class VehicleManager : MonoBehaviour
{
    [SerializeField] private Transform vehiclesParent;

    [SerializeField] private List<VehiclePrefabEntry> vehiclePrefabs = new();

    [SerializeField] private Vector3 spawnPosition;
    [SerializeField] private bool useManagerPositionAsSpawn = true;
    [SerializeField] private float spawnY = 0.02f;

    private readonly Dictionary<CargoType, GameObject> prefabByCargo = new();
    private readonly Dictionary<int, VehicleAgent> vehiclesById = new();
    private int nextVehicleId = 1;

    public IReadOnlyDictionary<int, VehicleAgent> VehiclesById => vehiclesById;

    public event Action<VehicleAgent> VehicleSpawned;
    public event Action<VehicleAgent> VehicleRemoved;

    private void Awake()
    {
        RebuildPrefabLookup();
    }

    private void OnValidate()
    {
        RebuildPrefabLookup();
    }

    public int SpawnVehicle(CargoType cargoType)
    {
        Vector3 position = useManagerPositionAsSpawn ? transform.position : spawnPosition;
        position.y = spawnY;
        return SpawnVehicleAt(cargoType, position, Quaternion.identity);
    }

    public int SpawnVehicleAt(CargoType cargoType, Vector3 position, Quaternion rotation)
    {
        if (cargoType == CargoType.None)
        {
            return -1;
        }

        if (!prefabByCargo.TryGetValue(cargoType, out GameObject prefab) || prefab == null)
        {
            return -1;
        }

        GameObject instance = Instantiate(prefab, position, rotation, ResolveRuntimeParent());
        VehicleAgent agent = instance.GetComponent<VehicleAgent>();
        if (agent == null)
        {
            agent = instance.AddComponent<VehicleAgent>();
        }

        int vehicleId = nextVehicleId++;
        agent.Initialize(vehicleId, cargoType);

        vehiclesById[vehicleId] = agent;
        VehicleSpawned?.Invoke(agent);
        return vehicleId;
    }

    public bool RemoveVehicle(int vehicleId)
    {
        if (!vehiclesById.TryGetValue(vehicleId, out VehicleAgent vehicle))
        {
            return false;
        }

        vehiclesById.Remove(vehicleId);

        if (vehicle != null)
        {
            VehicleRemoved?.Invoke(vehicle);
            Destroy(vehicle.gameObject);
        }

        return true;
    }

    public void RemoveAllVehicles()
    {
        List<int> ids = new(vehiclesById.Keys);
        for (int i = 0; i < ids.Count; i++)
        {
            RemoveVehicle(ids[i]);
        }
    }

    public bool TryGetVehicle(int vehicleId, out VehicleAgent vehicle)
    {
        return vehiclesById.TryGetValue(vehicleId, out vehicle);
    }

    public bool TryGetVehiclePrefab(CargoType cargoType, out GameObject prefab)
    {
        return prefabByCargo.TryGetValue(cargoType, out prefab) && prefab != null;
    }

    private void RebuildPrefabLookup()
    {
        prefabByCargo.Clear();
        for (int i = 0; i < vehiclePrefabs.Count; i++)
        {
            VehiclePrefabEntry entry = vehiclePrefabs[i];
            if (entry.cargoType == CargoType.None || entry.prefab == null)
            {
                continue;
            }

            prefabByCargo[entry.cargoType] = entry.prefab;
        }
    }

    private Transform ResolveRuntimeParent()
    {
        Transform candidate = vehiclesParent != null ? vehiclesParent : transform;
        if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded)
        {
            return candidate;
        }

        return transform;
    }
}
