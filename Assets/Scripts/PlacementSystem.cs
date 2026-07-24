using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlacementSystem : MonoBehaviour
{
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Grid grid;
    [SerializeField] private ObjectDatabaseSO database;
    [SerializeField] private GameObject gridVisualization;
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private TrafficLightManager trafficLightManager;
    [SerializeField] private VehicleManager vehicleManager;
    [SerializeField] private ForestSpreadManager forestSpreadManager;
    [SerializeField] private LayerMask obstacleLayerMask;
    [SerializeField] private LayerMask noBuildLayerMask;
    [SerializeField] private float previewScale = 0.5f;
    [SerializeField] private float objectY = 0.01f;
    [SerializeField] private float previewY = 0.1f;
    [SerializeField] private float footprintPadding = 0.8f;
    [SerializeField] private float obstacleCheckHalfHeight = 2f;
    [SerializeField] private bool moveGridVisualizationWithCursor;
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.5f;

    private readonly List<GameObject> placedGameObjects = new();
    private readonly HashSet<Vector3Int> occupiedCells = new();
    private readonly Dictionary<Vector3Int, PlacementRecord> placementsByCell = new();
    private readonly List<Material> previewMaterials = new();

    private ObjectData selectedObject;
    private GameObject previewObject;
    private int currentRotation;
    private bool dragPlacementHasCell;
    private Vector3Int dragPlacementCell;
    private DragMode dragMode;
    private int lastDragPlacedFrame = -1;
    private Vector3Int lastDragPlacedCell;

    private sealed class PlacementRecord
    {
        public GameObject Instance;
        public int ObjectId;
        public Vector3Int RootCell;
        public Vector2Int Size;
        public bool RegisteredAsRoad;
    }

    private enum DragMode
    {
        None,
        Place,
        Remove
    }

    public bool IsPlacing => selectedObject != null;

    private void Awake()
    {
        CoreUtility.ResolveIfNull(ref roadNetworkManager);
        CoreUtility.ResolveIfNull(ref stopManager);
        CoreUtility.ResolveIfNull(ref trafficLightManager);
        CoreUtility.ResolveIfNull(ref vehicleManager);
        CoreUtility.ResolveIfNull(ref forestSpreadManager);

        if (obstacleLayerMask.value == 0)
        {
            obstacleLayerMask = LayerMask.GetMask("Obstacle");
        }

        if (noBuildLayerMask.value == 0)
        {
            noBuildLayerMask = LayerMask.GetMask("Selectable");
        }
    }

    private void Start()
    {
        StopPlacement();
    }

    private void OnDisable()
    {
        StopPlacement();
    }

    public void StartPlacement(int id)
    {
        StopPlacement();

        if (!ValidateReferences())
        {
            return;
        }

        if (!database.TryGetObjectDataById(id, out selectedObject))
        {
            selectedObject = null;
            return;
        }

        if (selectedObject.Prefab == null)
        {
            selectedObject = null;
            return;
        }

        SetPlacementVisualsActive(true);
        CreatePreviewObject();

        inputManager.onClicked += PlaceStructure;
        inputManager.onExit += StopPlacement;
        inputManager.onRotate += RotateObject;
    }

    public void StopPlacement()
    {
        selectedObject = null;
        currentRotation = 0;
        dragPlacementHasCell = false;
        dragMode = DragMode.None;
        lastDragPlacedFrame = -1;

        SetPlacementVisualsActive(false);
        DestroyPreviewObject();

        if (inputManager != null)
        {
            inputManager.onClicked -= PlaceStructure;
            inputManager.onExit -= StopPlacement;
            inputManager.onRotate -= RotateObject;
        }
    }

    private void Update()
    {
        if (selectedObject == null || inputManager == null || grid == null)
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mapPosition))
        {
            PreviewVisualUtility.UpdatePreviewColor(
                previewMaterials,
                PreviewVisualUtility.DefaultValidColor,
                PreviewVisualUtility.DefaultInvalidColor,
                previewAlpha,
                false);
            return;
        }

        Vector3Int gridPosition = grid.WorldToCell(mapPosition);
        Vector3 snappedPos = grid.GetCellCenterWorld(gridPosition);
        UpdateVisualPositions(snappedPos);

        Vector2Int occupiedSize = selectedObject.GetSizeForRotation(currentRotation);
        bool isRoadObject = roadNetworkManager != null && roadNetworkManager.IsRoadObjectId(selectedObject.ID);
        bool isPlacementValid = CheckPlacementValidity(gridPosition, occupiedSize, isRoadObject);
        bool previewCanPlace = isPlacementValid && !inputManager.IsPointerOverUI();
        PreviewVisualUtility.UpdatePreviewColor(
            previewMaterials,
            PreviewVisualUtility.DefaultValidColor,
            PreviewVisualUtility.DefaultInvalidColor,
            previewAlpha,
            previewCanPlace);
        HandleDragPlacement(gridPosition);
    }

    private void RotateObject()
    {
        currentRotation = (currentRotation + 90) % 360;
    }

    private void PlaceStructure()
    {
        if (selectedObject == null || inputManager == null || grid == null)
        {
            return;
        }

        if (inputManager.IsPointerOverUI())
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mousePosition))
        {
            return;
        }

        Vector3Int gridPosition = grid.WorldToCell(mousePosition);
        if (lastDragPlacedFrame == Time.frameCount && lastDragPlacedCell == gridPosition)
        {
            return;
        }

        if (TryPlaceStructureAtCell(gridPosition, allowRemovalOnExistingMatch: true))
        {
            lastDragPlacedFrame = Time.frameCount;
            lastDragPlacedCell = gridPosition;
        }
    }

    private void HandleDragPlacement(Vector3Int gridPosition)
    {
        if (Mouse.current == null || !Mouse.current.leftButton.isPressed || inputManager.IsPointerOverUI())
        {
            dragPlacementHasCell = false;
            dragMode = DragMode.None;
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragMode = CanRemoveAtCell(gridPosition) ? DragMode.Remove : DragMode.Place;
            dragPlacementHasCell = false;
        }

        if (lastDragPlacedFrame == Time.frameCount && lastDragPlacedCell == gridPosition)
        {
            dragPlacementHasCell = true;
            dragPlacementCell = gridPosition;
            return;
        }

        if (dragPlacementHasCell && dragPlacementCell == gridPosition)
        {
            return;
        }

        dragPlacementHasCell = true;
        dragPlacementCell = gridPosition;

        bool changed = dragMode switch
        {
            DragMode.Remove => TryRemovePlacedObjectAtCell(gridPosition),
            _ => TryPlaceStructureAtCell(gridPosition, allowRemovalOnExistingMatch: false)
        };

        if (changed)
        {
            lastDragPlacedFrame = Time.frameCount;
            lastDragPlacedCell = gridPosition;
        }
    }

    private bool TryPlaceStructureAtCell(Vector3Int gridPosition, bool allowRemovalOnExistingMatch)
    {
        if (allowRemovalOnExistingMatch && TryRemovePlacedObjectAtCell(gridPosition))
        {
            return true;
        }

        Vector2Int occupiedSize = selectedObject.GetSizeForRotation(currentRotation);
        bool isRoadObject = roadNetworkManager != null && roadNetworkManager.IsRoadObjectId(selectedObject.ID);

        if (!CheckPlacementValidity(gridPosition, occupiedSize, isRoadObject))
        {
            return false;
        }

        int additionalRoadCost = isRoadObject && forestSpreadManager != null
            ? forestSpreadManager.GetRoadClearCostForFootprint(gridPosition, occupiedSize)
            : 0;

        if (EconomyManager.HasInstance && !EconomyManager.Instance.TrySpendForRoadPlacement(selectedObject.ID, additionalRoadCost))
        {
            return false;
        }

        Vector3 finalPosition = grid.GetCellCenterWorld(gridPosition);
        finalPosition.y = objectY;

        Transform parent = ResolvePlacementParent(isRoadObject);
        GameObject newObject = Instantiate(
            selectedObject.Prefab,
            finalPosition,
            Quaternion.Euler(0f, currentRotation, 0f),
            parent);

        newObject.transform.localScale = Vector3.one * previewScale;

        placedGameObjects.Add(newObject);

        bool registeredAsRoad = isRoadObject && roadNetworkManager.RegisterRoad(selectedObject.ID, gridPosition, currentRotation);

        PlacementRecord record = new PlacementRecord
        {
            Instance = newObject,
            ObjectId = selectedObject.ID,
            RootCell = gridPosition,
            Size = occupiedSize,
            RegisteredAsRoad = registeredAsRoad
        };

        MarkCellsOccupied(gridPosition, occupiedSize, record);

        if (isRoadObject && forestSpreadManager != null)
        {
            forestSpreadManager.ClearInfectedTreesInFootprint(gridPosition, occupiedSize);
        }

        return true;
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, Vector2Int occupiedSize, bool isRoadObject)
    {
        if (selectedObject == null || grid == null)
        {
            return false;
        }

        if (IsAnyFootprintCellOccupied(gridPosition, occupiedSize))
        {
            return false;
        }

        if (isRoadObject && IsFootprintOnProtectedForest(gridPosition, occupiedSize))
        {
            return false;
        }

        return !IsFootprintBlocked(gridPosition, occupiedSize, isRoadObject);
    }

    private bool IsFootprintBlocked(Vector3Int gridPosition, Vector2Int occupiedSize, bool isRoadObject)
    {
        int blockedLayers = obstacleLayerMask.value | noBuildLayerMask.value;
        if (blockedLayers == 0)
        {
            return false;
        }

        float halfX = (grid.cellSize.x * footprintPadding) * 0.5f;
        float halfZ = (grid.cellSize.z * footprintPadding) * 0.5f;
        Vector3 halfExtents = new Vector3(halfX, obstacleCheckHalfHeight, halfZ);

        for (int x = 0; x < occupiedSize.x; x++)
        {
            for (int z = 0; z < occupiedSize.y; z++)
            {
                Vector3Int cell = gridPosition + new Vector3Int(x, 0, z);
                Vector3 center = grid.GetCellCenterWorld(cell);

                if (Physics.CheckBox(center, halfExtents, Quaternion.identity, blockedLayers, QueryTriggerInteraction.Collide))
                {
                    if (isRoadObject && forestSpreadManager != null && forestSpreadManager.IsInfectedCell(cell))
                    {
                        continue;
                    }

                    return true;
                }
            }
        }

        return false;
    }

    private bool IsFootprintOnProtectedForest(Vector3Int gridPosition, Vector2Int occupiedSize)
    {
        if (forestSpreadManager == null)
        {
            return false;
        }

        bool overlapsProtectedForest = false;
        ForEachFootprintCell(gridPosition, occupiedSize, cell =>
        {
            if (!overlapsProtectedForest && forestSpreadManager.IsProtectedForestCell(cell))
            {
                overlapsProtectedForest = true;
            }
        });

        return overlapsProtectedForest;
    }

    private void CreatePreviewObject()
    {
        previewObject = Instantiate(selectedObject.Prefab);
        previewObject.transform.localScale = Vector3.one * previewScale;
        PreviewVisualUtility.InitializePreviewObject(
            previewObject,
            previewMaterials,
            PreviewVisualUtility.DefaultValidColor,
            PreviewVisualUtility.DefaultInvalidColor,
            previewAlpha);
    }

    private void DestroyPreviewObject()
    {
        if (previewObject != null)
        {
            DestroySafely(previewObject);
            previewObject = null;
        }

        previewMaterials.Clear();
    }

    private void UpdateVisualPositions(Vector3 snappedPos)
    {
        if (previewObject != null)
        {
            previewObject.transform.position = new Vector3(snappedPos.x, previewY, snappedPos.z);
            previewObject.transform.rotation = Quaternion.Euler(0f, currentRotation, 0f);
        }

        if (moveGridVisualizationWithCursor && gridVisualization != null)
        {
            gridVisualization.transform.position = new Vector3(snappedPos.x, 0.005f, snappedPos.z);
        }
    }

    private void SetPlacementVisualsActive(bool isActive)
    {
        if (gridVisualization != null)
        {
            gridVisualization.SetActive(isActive);
        }
    }

    private bool ValidateReferences()
    {
        if (inputManager == null || grid == null || database == null)
        {
            return false;
        }

        return true;
    }

    private bool IsAnyFootprintCellOccupied(Vector3Int gridPosition, Vector2Int occupiedSize)
    {
        bool foundOccupiedCell = false;
        ForEachFootprintCell(gridPosition, occupiedSize, cell =>
        {
            if (!foundOccupiedCell && occupiedCells.Contains(cell))
            {
                foundOccupiedCell = true;
            }
        });
        return foundOccupiedCell;
    }

    private void MarkCellsOccupied(Vector3Int gridPosition, Vector2Int occupiedSize, PlacementRecord record)
    {
        ForEachFootprintCell(gridPosition, occupiedSize, cell =>
        {
            occupiedCells.Add(cell);
            placementsByCell[cell] = record;
        });
    }

    private void UnmarkCellsOccupied(Vector3Int gridPosition, Vector2Int occupiedSize)
    {
        ForEachFootprintCell(gridPosition, occupiedSize, cell =>
        {
            occupiedCells.Remove(cell);
            placementsByCell.Remove(cell);
        });
    }

    private bool CanRemoveAtCell(Vector3Int gridPosition)
    {
        if (selectedObject == null)
        {
            return false;
        }

        return placementsByCell.TryGetValue(gridPosition, out PlacementRecord record)
            && record != null
            && record.ObjectId == selectedObject.ID;
    }

    private bool TryRemovePlacedObjectAtCell(Vector3Int gridPosition)
    {
        if (!CanRemoveAtCell(gridPosition))
        {
            return false;
        }

        PlacementRecord record = placementsByCell[gridPosition];
        RemovePlacementRecord(record);
        return true;
    }

    private void RemovePlacementRecord(PlacementRecord record)
    {
        if (record == null)
        {
            return;
        }

        if (stopManager != null)
        {
            RemoveStopsOnFootprint(record.RootCell, record.Size);
        }

        if (trafficLightManager != null)
        {
            RemoveTrafficLightsOnFootprint(record.RootCell, record.Size);
        }

        RemoveVehiclesUsingRoadsOnFootprint(record.RootCell, record.Size);

        UnmarkCellsOccupied(record.RootCell, record.Size);

        if (record.Instance != null)
        {
            placedGameObjects.Remove(record.Instance);
            DestroySafely(record.Instance);
        }

        if (record.RegisteredAsRoad && roadNetworkManager != null)
        {
            roadNetworkManager.UnregisterRoad(record.RootCell);
            if (EconomyManager.HasInstance)
            {
                EconomyManager.Instance.RefundForRoadRemoval(record.ObjectId);
            }
        }
    }

    private void RemoveStopsOnFootprint(Vector3Int rootCell, Vector2Int occupiedSize)
    {
        ForEachFootprintCell(rootCell, occupiedSize, cell => stopManager.TryRemoveStopAtCell(cell));
    }

    private void RemoveTrafficLightsOnFootprint(Vector3Int rootCell, Vector2Int occupiedSize)
    {
        ForEachFootprintCell(rootCell, occupiedSize, cell => trafficLightManager.TryRemoveTrafficLightAtCell(cell));
    }

    private void RemoveVehiclesUsingRoadsOnFootprint(Vector3Int rootCell, Vector2Int occupiedSize)
    {
        if (vehicleManager == null)
        {
            return;
        }

        HashSet<Vector3Int> footprintCells = new();
        ForEachFootprintCell(rootCell, occupiedSize, cell => footprintCells.Add(cell));
        HashSet<int> vehiclesToRemove = new();

        foreach (KeyValuePair<int, VehicleAgent> pair in vehicleManager.VehiclesById)
        {
            VehicleAgent vehicle = pair.Value;
            if (vehicle == null)
            {
                continue;
            }

            bool shouldRemove = false;
            foreach (Vector3Int cell in footprintCells)
            {
                if (vehicle.UsesRoadCell(cell))
                {
                    shouldRemove = true;
                    break;
                }
            }

            if (shouldRemove)
            {
                vehiclesToRemove.Add(pair.Key);
            }
        }

        foreach (int vehicleId in vehiclesToRemove)
        {
            vehicleManager.RemoveVehicle(vehicleId);
        }
    }

    private static void DestroySafely(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
            return;
        }

        DestroyImmediate(target);
    }

    private static void ForEachFootprintCell(Vector3Int rootCell, Vector2Int occupiedSize, System.Action<Vector3Int> action)
    {
        if (action == null)
        {
            return;
        }

        int width = Mathf.Max(1, occupiedSize.x);
        int height = Mathf.Max(1, occupiedSize.y);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                action(rootCell + new Vector3Int(x, 0, z));
            }
        }
    }

    private Transform ResolvePlacementParent(bool isRoadObject)
    {
        if (isRoadObject)
        {
            if (roadNetworkManager != null)
            {
                return roadNetworkManager.GetRoadsParent();
            }
        }

        return transform;
    }
}
