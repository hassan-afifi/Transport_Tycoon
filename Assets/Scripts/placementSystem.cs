using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class placementSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Grid grid;
    [SerializeField] private ObjectDatabaseSO database;
    [SerializeField] private GameObject gridVisualization;
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private StopManager stopManager;

    [Header("Placement Settings")]
    [SerializeField] private LayerMask obstacleLayerMask;
    [SerializeField] private LayerMask noBuildLayerMask;
    [SerializeField] private float previewScale = 0.5f;
    [SerializeField] private float objectY = 0.01f;
    [SerializeField] private float previewY = 0.1f;
    [SerializeField] private float footprintPadding = 0.8f;
    [SerializeField] private float obstacleCheckHalfHeight = 2f;
    [SerializeField] private bool moveGridVisualizationWithCursor;
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.5f;
    [SerializeField] private Color previewValidColor = new Color(0f, 0.5f, 0f, 1f);
    [SerializeField] private Color previewInvalidColor = new Color(0.5f, 0f, 0f, 1f);

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
        if (roadNetworkManager == null)
        {
            roadNetworkManager = FindFirstObjectByType<RoadNetworkManager>();
        }

        if (stopManager == null)
        {
            stopManager = FindFirstObjectByType<StopManager>();
        }

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
            UpdatePreviewColor(false);
            return;
        }

        Vector3Int gridPosition = grid.WorldToCell(mapPosition);
        Vector3 snappedPos = grid.GetCellCenterWorld(gridPosition);
        UpdateVisualPositions(snappedPos);

        bool isPlacementValid = CheckPlacementValidity(gridPosition);
        bool canRemoveHere = CanRemoveAtCell(gridPosition);
        UpdatePreviewColor(isPlacementValid || canRemoveHere);
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

        if (!CheckPlacementValidity(gridPosition))
        {
            return false;
        }

        if (EconomyManager.HasInstance && !EconomyManager.Instance.TrySpendForRoadPlacement(selectedObject.ID))
        {
            return false;
        }

        Vector3 finalPosition = grid.GetCellCenterWorld(gridPosition);
        finalPosition.y = objectY;

        GameObject newObject = Instantiate(
            selectedObject.Prefab,
            finalPosition,
            Quaternion.Euler(0f, currentRotation, 0f));

        newObject.transform.localScale = Vector3.one * previewScale;

        placedGameObjects.Add(newObject);

        Vector2Int occupiedSize = selectedObject.GetSizeForRotation(currentRotation);
        bool registeredAsRoad = roadNetworkManager != null && roadNetworkManager.RegisterRoad(selectedObject.ID, gridPosition, currentRotation);

        PlacementRecord record = new PlacementRecord
        {
            Instance = newObject,
            ObjectId = selectedObject.ID,
            RootCell = gridPosition,
            Size = occupiedSize,
            RegisteredAsRoad = registeredAsRoad
        };

        MarkCellsOccupied(gridPosition, occupiedSize, record);
        return true;
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition)
    {
        if (selectedObject == null || grid == null)
        {
            return false;
        }

        Vector2Int occupiedSize = selectedObject.GetSizeForRotation(currentRotation);
        if (IsAnyFootprintCellOccupied(gridPosition, occupiedSize))
        {
            return false;
        }

        return !IsFootprintBlocked(gridPosition, occupiedSize);
    }

    private bool IsFootprintBlocked(Vector3Int gridPosition, Vector2Int occupiedSize)
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
                    return true;
                }
            }
        }

        return false;
    }

    private void CreatePreviewObject()
    {
        previewObject = Instantiate(selectedObject.Prefab);
        previewObject.transform.localScale = Vector3.one * previewScale;

        foreach (Collider collider in previewObject.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        SetLayerRecursively(previewObject, LayerMask.NameToLayer("Ignore Raycast"));
        CacheAndPreparePreviewRenderers();
        UpdatePreviewColor(false);
    }

    private void DestroyPreviewObject()
    {
        if (previewObject != null)
        {
            Destroy(previewObject);
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

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (layer < 0)
        {
            return;
        }

        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private bool IsAnyFootprintCellOccupied(Vector3Int gridPosition, Vector2Int occupiedSize)
    {
        int width = Mathf.Max(1, occupiedSize.x);
        int height = Mathf.Max(1, occupiedSize.y);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3Int cell = gridPosition + new Vector3Int(x, 0, z);
                if (occupiedCells.Contains(cell))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void MarkCellsOccupied(Vector3Int gridPosition, Vector2Int occupiedSize, PlacementRecord record)
    {
        int width = Mathf.Max(1, occupiedSize.x);
        int height = Mathf.Max(1, occupiedSize.y);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3Int cell = gridPosition + new Vector3Int(x, 0, z);
                occupiedCells.Add(cell);
                placementsByCell[cell] = record;
            }
        }
    }

    private void UnmarkCellsOccupied(Vector3Int gridPosition, Vector2Int occupiedSize)
    {
        int width = Mathf.Max(1, occupiedSize.x);
        int height = Mathf.Max(1, occupiedSize.y);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3Int cell = gridPosition + new Vector3Int(x, 0, z);
                occupiedCells.Remove(cell);
                placementsByCell.Remove(cell);
            }
        }
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

        UnmarkCellsOccupied(record.RootCell, record.Size);

        if (record.Instance != null)
        {
            placedGameObjects.Remove(record.Instance);
            Destroy(record.Instance);
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
        int width = Mathf.Max(1, occupiedSize.x);
        int height = Mathf.Max(1, occupiedSize.y);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3Int cell = rootCell + new Vector3Int(x, 0, z);
                stopManager.TryRemoveStopAtCell(cell);
            }
        }
    }

    private void CacheAndPreparePreviewRenderers()
    {
        previewMaterials.Clear();
        if (previewObject == null)
        {
            return;
        }

        Renderer[] renderers = previewObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material[] materials = renderer.materials;
            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null)
                {
                    continue;
                }

                MakeMaterialTransparent(material);
                previewMaterials.Add(material);
            }
        }
    }

    private void UpdatePreviewColor(bool isValid)
    {
        if (previewMaterials.Count == 0)
        {
            return;
        }

        Color color = isValid ? previewValidColor : previewInvalidColor;
        color.a = Mathf.Clamp01(previewAlpha);

        for (int i = 0; i < previewMaterials.Count; i++)
        {
            SetMaterialColor(previewMaterials[i], color);
        }
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static void MakeMaterialTransparent(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }
}
