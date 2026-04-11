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

    [SerializeField] private InputManager inputManager;
    [SerializeField] private Grid grid;
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private RouteManager routeManager;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private VehicleStopAssignPanel vehicleStopAssignPanel;

    [SerializeField, Min(0.1f)] private float laneOffset = 2f;
    [SerializeField] private float previewHeightOffset = 0.08f;
    [SerializeField] private bool openStopAssignmentPanelOnSpawn = true;
    [SerializeField] private bool autoAssignLatestRoute = true;
    [SerializeField] private bool autoAssignSortedStopsWhenNoRoute = true;
    [SerializeField, Min(2)] private int minimumStopsForAutoAssign = 2;
    [SerializeField] private bool allowTaggedRoadFallback = true;
    [SerializeField] private string roadTag = "Road";
    [SerializeField] private LayerMask taggedRoadLayerMask = ~0;
    [SerializeField, Range(0.1f, 1f)] private float taggedRoadCheckScale = 0.45f;
    [SerializeField, Min(0.1f)] private float taggedRoadCheckHeight = 6f;
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.5f;

    private readonly Dictionary<CargoType, GameObject> prefabByCargo = new();
    private readonly Dictionary<int, VehicleAgent> vehiclesById = new();
    private readonly List<Material> previewMaterials = new();
    private readonly Collider[] taggedRoadOverlapBuffer = new Collider[64];
    private readonly List<int> cachedStopIds = new();

    private int nextVehicleId = 1;
    private CargoType selectedCargoType = CargoType.None;
    private GameObject previewObject;
    private Vector3Int currentCell;
    private Quaternion currentRotation = Quaternion.identity;
    private Vector3 currentSpawnPosition;
    private int currentLaneIndex;
    private bool hasCurrentCell;
    private bool canPlaceCurrentCell;

    public IReadOnlyDictionary<int, VehicleAgent> VehiclesById => vehiclesById;
    public bool IsPlacementActive => selectedCargoType != CargoType.None;
    public CargoType SelectedCargoType => selectedCargoType;

    public event Action<VehicleAgent> VehicleSpawned;
    public event Action<VehicleAgent> VehicleRemoved;

    public Transform GetVehiclesParent()
    {
        return CoreUtility.ResolveRuntimeParent(vehiclesParent, transform);
    }

    private void Awake()
    {
        RebuildPrefabLookup();
        CoreUtility.ResolveIfNull(ref inputManager);
        CoreUtility.ResolveIfNull(ref grid);
        CoreUtility.ResolveIfNull(ref roadNetworkManager);
        CoreUtility.ResolveIfNull(ref routeManager);
        CoreUtility.ResolveIfNull(ref stopManager);
        CoreUtility.ResolveIfNull(ref vehicleStopAssignPanel);
    }

    private void OnValidate()
    {
        RebuildPrefabLookup();
    }

    private void OnDisable()
    {
        EndPlacement();
    }

    private void Update()
    {
        if (!IsPlacementActive || inputManager == null || grid == null)
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mapPosition))
        {
            hasCurrentCell = false;
            canPlaceCurrentCell = false;
            PreviewVisualUtility.UpdatePreviewColor(
                previewMaterials,
                PreviewVisualUtility.DefaultValidColor,
                PreviewVisualUtility.DefaultInvalidColor,
                previewAlpha,
                false);
            return;
        }

        Vector3Int cell = grid.WorldToCell(mapPosition);
        hasCurrentCell = true;
        currentCell = cell;

        bool hasValidRoad = TryBuildPlacementPose(cell, currentLaneIndex, out Vector3 placementSpawnPosition, out Quaternion spawnRotation);
        bool occupied = hasValidRoad && IsSlotOccupied(cell, spawnRotation);
        bool pointerOverUi = inputManager.IsPointerOverUI();
        canPlaceCurrentCell = hasValidRoad && !occupied && !pointerOverUi;

        currentSpawnPosition = placementSpawnPosition;
        currentRotation = spawnRotation;
        UpdatePreviewTransform(placementSpawnPosition, spawnRotation, hasValidRoad);
        PreviewVisualUtility.UpdatePreviewColor(
            previewMaterials,
            PreviewVisualUtility.DefaultValidColor,
            PreviewVisualUtility.DefaultInvalidColor,
            previewAlpha,
            canPlaceCurrentCell);
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

        if (EconomyManager.HasInstance && !EconomyManager.Instance.TrySpendForVehicle(cargoType))
        {
            return -1;
        }

        GameObject instance = Instantiate(prefab, position, rotation, GetVehiclesParent());
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
            if (EconomyManager.HasInstance)
            {
                EconomyManager.Instance.RefundForVehicle(vehicle.CargoType);
            }

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

    public void TogglePlacement(CargoType cargoType)
    {
        if (IsPlacementActive && selectedCargoType == cargoType)
        {
            EndPlacement();
            return;
        }

        BeginPlacement(cargoType);
    }

    public void BeginPlacement(CargoType cargoType)
    {
        EndPlacement();

        if (cargoType == CargoType.None
            || inputManager == null
            || grid == null
            || !TryGetVehiclePrefab(cargoType, out _))
        {
            return;
        }

        roadNetworkManager?.ImportPresetRoadsFromScene();

        selectedCargoType = cargoType;
        currentLaneIndex = 0;
        hasCurrentCell = false;
        canPlaceCurrentCell = false;

        CreatePreviewObject();
        inputManager.onClicked += HandleMapClick;
        inputManager.onExit += EndPlacement;
        inputManager.onRotate += SwitchLane;
    }

    public void EndPlacement()
    {
        if (!IsPlacementActive)
        {
            return;
        }

        selectedCargoType = CargoType.None;
        currentLaneIndex = 0;
        hasCurrentCell = false;
        canPlaceCurrentCell = false;

        if (inputManager != null)
        {
            inputManager.onClicked -= HandleMapClick;
            inputManager.onExit -= EndPlacement;
            inputManager.onRotate -= SwitchLane;
        }

        PreviewVisualUtility.DestroyPreviewObject(ref previewObject, previewMaterials);
    }

    public void AssignLatestRouteToAllVehicles()
    {
        if (routeManager == null
            || routeManager.Routes == null
            || routeManager.Routes.Count == 0
            || grid == null)
        {
            return;
        }

        RouteData latestRoute = routeManager.Routes[routeManager.Routes.Count - 1];
        if (latestRoute == null)
        {
            return;
        }

        ForEachVehicle(vehicle =>
        {
            vehicle.ConfigureMovementContext(roadNetworkManager, grid, grid.WorldToCell(vehicle.transform.position), laneOffset, spawnY);
            vehicle.AssignRoute(latestRoute);
        });
    }

    public void AssignAllStopsToAllVehicles()
    {
        if (stopManager == null || grid == null)
        {
            return;
        }

        cachedStopIds.Clear();
        stopManager.GetSortedStopIds(cachedStopIds);
        if (cachedStopIds.Count < minimumStopsForAutoAssign)
        {
            return;
        }

        ForEachVehicle(vehicle =>
        {
            vehicle.ConfigureMovementContext(roadNetworkManager, grid, grid.WorldToCell(vehicle.transform.position), laneOffset, spawnY);
            vehicle.AssignStops(stopManager, cachedStopIds);
        });
    }

    private void HandleMapClick()
    {
        if (!IsPlacementActive || !hasCurrentCell || !canPlaceCurrentCell)
        {
            return;
        }

        if (inputManager != null && inputManager.IsPointerOverUI())
        {
            return;
        }

        int vehicleId = SpawnVehicleAt(selectedCargoType, currentSpawnPosition, currentRotation);
        if (vehicleId <= 0)
        {
            return;
        }

        if (!TryGetVehicle(vehicleId, out VehicleAgent vehicle) || vehicle == null)
        {
            return;
        }

        vehicle.ConfigureMovementContext(roadNetworkManager, grid, currentCell, laneOffset, spawnY);

        if (openStopAssignmentPanelOnSpawn && vehicleStopAssignPanel != null)
        {
            vehicleStopAssignPanel.OpenForVehicle(vehicle, true);
            return;
        }

        TryAssignInitialRoute(vehicle);
    }

    private void TryAssignInitialRoute(VehicleAgent vehicle)
    {
        if (vehicle == null)
        {
            return;
        }

        if (autoAssignLatestRoute
            && routeManager != null
            && routeManager.Routes != null
            && routeManager.Routes.Count > 0)
        {
            RouteData latestRoute = routeManager.Routes[routeManager.Routes.Count - 1];
            if (latestRoute != null && vehicle.AssignRoute(latestRoute))
            {
                return;
            }
        }

        if (!autoAssignSortedStopsWhenNoRoute || stopManager == null)
        {
            return;
        }

        cachedStopIds.Clear();
        stopManager.GetSortedStopIds(cachedStopIds);
        if (cachedStopIds.Count >= minimumStopsForAutoAssign)
        {
            vehicle.AssignStops(stopManager, cachedStopIds);
        }
    }

    private void SwitchLane()
    {
        if (!IsPlacementActive)
        {
            return;
        }

        currentLaneIndex = 1 - currentLaneIndex;
    }

    private bool TryBuildPlacementPose(Vector3Int cell, int laneIndex, out Vector3 placementSpawnPosition, out Quaternion spawnRotation)
    {
        placementSpawnPosition = grid != null ? grid.GetCellCenterWorld(cell) : Vector3.zero;
        placementSpawnPosition.y = spawnY;
        spawnRotation = Quaternion.identity;

        Vector3 forward = Vector3.zero;

        if (roadNetworkManager != null)
        {
            if (roadNetworkManager.TryGetRoad(cell, out RoadTileData roadTile))
            {
                if (!IsStraightRoadConnections(roadTile.connections))
                {
                    return false;
                }

                forward = GetForwardVector(roadTile.connections);
            }
            else if (TryGetTaggedRoadForwardAtCell(cell, out Vector3 taggedForward, out string taggedRoadName)
                     && IsTaggedRoadStraightName(taggedRoadName))
            {
                forward = taggedForward;
            }
            else
            {
                return false;
            }
        }
        else if (TryGetTaggedRoadForwardAtCell(cell, out Vector3 taggedFallbackForward, out _))
        {
            forward = taggedFallbackForward;
        }

        if (forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        if (laneIndex == 1)
        {
            forward = -forward;
        }

        Vector3 laneRight = Vector3.Cross(Vector3.up, forward).normalized;
        placementSpawnPosition += laneRight * laneOffset;
        spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
        return true;
    }

    private static bool IsStraightRoadConnections(RoadDirectionMask connections)
    {
        RoadDirectionMask northSouth = RoadDirectionMask.North | RoadDirectionMask.South;
        RoadDirectionMask eastWest = RoadDirectionMask.East | RoadDirectionMask.West;
        return connections == northSouth || connections == eastWest;
    }

    private bool TryGetTaggedRoadForwardAtCell(Vector3Int cell, out Vector3 forward, out string roadName)
    {
        forward = Vector3.zero;
        roadName = string.Empty;

        if (!allowTaggedRoadFallback
            || grid == null
            || string.IsNullOrWhiteSpace(roadTag)
            || taggedRoadLayerMask.value == 0)
        {
            return false;
        }

        Vector3 center = grid.GetCellCenterWorld(cell);
        float halfY = Mathf.Max(0.05f, taggedRoadCheckHeight * 0.5f);
        Vector3 halfExtents = new(
            Mathf.Max(0.05f, grid.cellSize.x * taggedRoadCheckScale * 0.5f),
            halfY,
            Mathf.Max(0.05f, grid.cellSize.z * taggedRoadCheckScale * 0.5f));
        Vector3 overlapCenter = center + Vector3.up * halfY;

        int hitCount = Physics.OverlapBoxNonAlloc(
            overlapCenter,
            halfExtents,
            taggedRoadOverlapBuffer,
            Quaternion.identity,
            taggedRoadLayerMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = taggedRoadOverlapBuffer[i];
            if (hit == null)
            {
                continue;
            }

            Transform taggedRoadTransform = FindTaggedRoadTransform(hit.transform);
            if (taggedRoadTransform == null)
            {
                continue;
            }

            Vector3 taggedForward = GetPlanarForward(taggedRoadTransform.forward);
            if (taggedForward.sqrMagnitude < 0.0001f)
            {
                taggedForward = GetPlanarForward(taggedRoadTransform.right);
            }

            if (taggedForward.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            forward = taggedForward.normalized;
            roadName = taggedRoadTransform.name;
            return true;
        }

        return false;
    }

    private static bool IsTaggedRoadStraightName(string roadName)
    {
        return !string.IsNullOrWhiteSpace(roadName)
               && roadName.Trim().ToLowerInvariant().Contains("lane");
    }

    private Transform FindTaggedRoadTransform(Transform source)
    {
        Transform current = source;
        while (current != null)
        {
            if (current.CompareTag(roadTag))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private static Vector3 GetPlanarForward(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }

    private static Vector3 GetForwardVector(RoadDirectionMask connections)
    {
        if ((connections & RoadDirectionMask.North) != 0)
        {
            return Vector3.forward;
        }

        if ((connections & RoadDirectionMask.East) != 0)
        {
            return Vector3.right;
        }

        if ((connections & RoadDirectionMask.South) != 0)
        {
            return Vector3.back;
        }

        if ((connections & RoadDirectionMask.West) != 0)
        {
            return Vector3.left;
        }

        return Vector3.zero;
    }

    private bool IsSlotOccupied(Vector3Int cell, Quaternion spawnRotation)
    {
        Vector3Int desiredLaneStep = GetCardinalStep(spawnRotation * Vector3.forward);
        if (desiredLaneStep == Vector3Int.zero)
        {
            return false;
        }

        foreach (KeyValuePair<int, VehicleAgent> pair in vehiclesById)
        {
            VehicleAgent vehicle = pair.Value;
            if (vehicle == null
                || !vehicle.TryGetLaneOccupancy(out Vector3Int currentRoadCell, out Vector3Int nextRoadCell, out bool hasNextRoadCell, out Vector3 laneForward))
            {
                continue;
            }

            Vector3Int occupiedLaneStep = GetCardinalStep(laneForward);
            if (occupiedLaneStep == Vector3Int.zero || occupiedLaneStep != desiredLaneStep)
            {
                continue;
            }

            if (currentRoadCell == cell || (hasNextRoadCell && nextRoadCell == cell))
            {
                return true;
            }
        }

        return false;
    }

    private static Vector3Int GetCardinalStep(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return Vector3Int.zero;
        }

        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
        {
            return direction.x >= 0f ? Vector3Int.right : Vector3Int.left;
        }

        return direction.z >= 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
    }

    private void CreatePreviewObject()
    {
        if (!TryGetVehiclePrefab(selectedCargoType, out GameObject prefab))
        {
            return;
        }

        previewObject = Instantiate(prefab);
        PreviewVisualUtility.InitializePreviewObject(
            previewObject,
            previewMaterials,
            PreviewVisualUtility.DefaultValidColor,
            PreviewVisualUtility.DefaultInvalidColor,
            previewAlpha);
    }

    private void ForEachVehicle(Action<VehicleAgent> action)
    {
        if (action == null)
        {
            return;
        }

        foreach (KeyValuePair<int, VehicleAgent> pair in vehiclesById)
        {
            VehicleAgent vehicle = pair.Value;
            if (vehicle != null)
            {
                action(vehicle);
            }
        }
    }

    private void UpdatePreviewTransform(Vector3 placementSpawnPosition, Quaternion spawnRotation, bool hasRoad)
    {
        if (previewObject == null)
        {
            return;
        }

        Vector3 position = hasRoad ? placementSpawnPosition : (grid != null ? grid.GetCellCenterWorld(currentCell) : placementSpawnPosition);
        position.y = spawnY + previewHeightOffset;
        previewObject.transform.position = position;
        previewObject.transform.rotation = spawnRotation;
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
}
