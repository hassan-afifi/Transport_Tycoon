using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class TrafficLightManager : MonoBehaviour
{
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Grid grid;
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private PlacementSystem placementSystemToDisable;
    [SerializeField] private StopManager stopManagerToDisable;
    [SerializeField] private VehiclePlacementTool vehiclePlacementToolToDisable;
    [SerializeField] private GameObject trafficLightPrefab;
    [SerializeField] private Transform trafficLightsParent;
    [SerializeField] private float lightY = 0.02f;
    [SerializeField, Min(0.1f)] private float lightSideOffset = 4f;
    [SerializeField, Min(0f)] private float lightRightOffset = 2f;
    [SerializeField] private float lightLocalY = 0f;
    [SerializeField] private string lightNamePrefix = "Traffic Light";
    [SerializeField] private bool addSelectionColliderIfMissing = true;
    [SerializeField, Min(0.1f)] private float fallbackColliderRadius = 2f;
    [SerializeField] private float previewY = 0.02f;
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.5f;
    [SerializeField] private Color previewValidColor = new Color(0f, 0.5f, 0f, 1f);
    [SerializeField] private Color previewInvalidColor = new Color(0.5f, 0f, 0f, 1f);

    private readonly Dictionary<int, TrafficLightNode> lightsById = new();
    private readonly Dictionary<Vector3Int, TrafficLightNode> lightsByCell = new();
    private readonly List<Material> previewMaterials = new();
    private int nextLightId = 1;
    private GameObject previewObject;
    private bool dragHasCell;
    private Vector3Int dragCell;
    private DragMode dragMode;
    private int lastDragActionFrame = -1;
    private Vector3Int lastDragActionCell;

    private enum DragMode
    {
        None,
        Place,
        Remove
    }

    private static readonly RoadDirectionMask FourWayMask =
        RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West;

    private static readonly Vector3[] LightDirections =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.right,
        Vector3.left
    };

    public bool IsPlacementActive { get; private set; }
    public IReadOnlyDictionary<int, TrafficLightNode> LightsById => lightsById;

    public event Action<TrafficLightNode> TrafficLightPlaced;
    public event Action TrafficLightsChanged;

    private void Awake()
    {
        SceneReferenceUtility.ResolveIfNull(ref inputManager);
        SceneReferenceUtility.ResolveIfNull(ref grid);
        SceneReferenceUtility.ResolveIfNull(ref roadNetworkManager);
        SceneReferenceUtility.ResolveIfNull(ref placementSystemToDisable);
        SceneReferenceUtility.ResolveIfNull(ref stopManagerToDisable);
        SceneReferenceUtility.ResolveIfNull(ref vehiclePlacementToolToDisable);
    }

    private void OnDisable()
    {
        EndPlacement();
    }

    private void Start()
    {
        RegisterExistingSceneTrafficLights();
        TrafficLightsChanged?.Invoke();
    }

    private void Update()
    {
        if (!IsPlacementActive || inputManager == null || grid == null)
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mapPos))
        {
            PreviewVisualUtility.UpdatePreviewColor(
                previewMaterials,
                previewValidColor,
                previewInvalidColor,
                previewAlpha,
                false);
            return;
        }

        Vector3Int gridCell = grid.WorldToCell(mapPos);
        Vector3 snappedPos = grid.GetCellCenterWorld(gridCell);

        if (previewObject != null)
        {
            previewObject.transform.position = new Vector3(snappedPos.x, previewY, snappedPos.z);
        }

        bool canRemove = CanRemoveTrafficLightAtCell(gridCell) && !inputManager.IsPointerOverUI();
        bool canPlace = CanPlaceTrafficLightAtCell(gridCell) && !inputManager.IsPointerOverUI();
        PreviewVisualUtility.UpdatePreviewColor(
            previewMaterials,
            previewValidColor,
            previewInvalidColor,
            previewAlpha,
            canPlace || canRemove);
        HandleDragPlacement(gridCell);
    }

    public void TogglePlacement()
    {
        if (IsPlacementActive)
        {
            EndPlacement();
            return;
        }

        BeginPlacement();
    }

    public void BeginPlacement()
    {
        if (IsPlacementActive)
        {
            return;
        }

        if (inputManager == null || grid == null || roadNetworkManager == null || trafficLightPrefab == null)
        {
            return;
        }

        if (placementSystemToDisable != null)
        {
            placementSystemToDisable.StopPlacement();
        }

        if (stopManagerToDisable != null)
        {
            stopManagerToDisable.EndStopPlacement();
        }

        if (vehiclePlacementToolToDisable != null)
        {
            vehiclePlacementToolToDisable.EndPlacement();
        }

        CreatePreviewObject();
        IsPlacementActive = true;
        dragHasCell = false;
        dragMode = DragMode.None;
        lastDragActionFrame = -1;
        inputManager.onClicked += HandleMapClickForPlacement;
        inputManager.onExit += EndPlacement;
    }

    public void EndPlacement()
    {
        if (!IsPlacementActive)
        {
            return;
        }

        IsPlacementActive = false;
        if (inputManager != null)
        {
            inputManager.onClicked -= HandleMapClickForPlacement;
            inputManager.onExit -= EndPlacement;
        }

        dragHasCell = false;
        dragMode = DragMode.None;
        lastDragActionFrame = -1;

        PreviewVisualUtility.DestroyPreviewObject(ref previewObject, previewMaterials);
    }

    public bool TryPlaceTrafficLightAtCell(Vector3Int gridCell)
    {
        if (roadNetworkManager == null || grid == null || trafficLightPrefab == null)
        {
            return false;
        }

        if (!CanPlaceTrafficLightAtCell(gridCell))
        {
            return false;
        }

        Vector3 worldPos = grid.GetCellCenterWorld(gridCell);
        worldPos.y = lightY;
        Transform parent = ResolveRuntimeParent();
        int lightId = nextLightId++;
        string lightName = $"{lightNamePrefix} {lightId}";

        GameObject lightObject = new(lightName);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = worldPos;

        CreateTrafficLightSet(lightObject.transform);

        TrafficLightNode node = lightObject.AddComponent<TrafficLightNode>();

        node.Initialize(lightId, gridCell, lightName, false);

        if (addSelectionColliderIfMissing)
        {
            PlacementObjectUtility.EnsureSelectionCollider(lightObject, fallbackColliderRadius);
        }

        lightsById[lightId] = node;
        lightsByCell[gridCell] = node;
        TrafficLightPlaced?.Invoke(node);
        TrafficLightsChanged?.Invoke();
        return true;
    }

    public bool TryGetTrafficLightAtCell(Vector3Int gridCell, out TrafficLightNode node)
    {
        return lightsByCell.TryGetValue(gridCell, out node);
    }

    public bool TryRemoveTrafficLightAtCell(Vector3Int gridCell)
    {
        if (!lightsByCell.TryGetValue(gridCell, out TrafficLightNode node) || node == null)
        {
            return false;
        }

        if (node.IsLockedInPlace)
        {
            return false;
        }

        lightsByCell.Remove(gridCell);
        lightsById.Remove(node.LightId);
        Destroy(node.gameObject);
        TrafficLightsChanged?.Invoke();
        return true;
    }

    private void HandleMapClickForPlacement()
    {
        if (!IsPlacementActive || inputManager == null || grid == null)
        {
            return;
        }

        if (inputManager.IsPointerOverUI())
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mapPos))
        {
            return;
        }

        Vector3Int gridCell = grid.WorldToCell(mapPos);
        if (WasActionAlreadyHandledThisFrame(gridCell))
        {
            return;
        }

        if (TryApplyPrimaryClickAction(gridCell))
        {
            RecordAction(gridCell);
        }
    }

    private void HandleDragPlacement(Vector3Int gridCell)
    {
        if (Mouse.current == null || !Mouse.current.leftButton.isPressed || inputManager.IsPointerOverUI())
        {
            dragHasCell = false;
            dragMode = DragMode.None;
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragMode = CanRemoveTrafficLightAtCell(gridCell) ? DragMode.Remove : DragMode.Place;
            dragHasCell = false;
        }

        if (WasActionAlreadyHandledThisFrame(gridCell))
        {
            dragHasCell = true;
            dragCell = gridCell;
            return;
        }

        if (dragHasCell && dragCell == gridCell)
        {
            return;
        }

        dragHasCell = true;
        dragCell = gridCell;

        bool changed = TryApplyDragAction(gridCell, dragMode);

        if (changed)
        {
            RecordAction(gridCell);
        }
    }

    private bool TryApplyPrimaryClickAction(Vector3Int gridCell)
    {
        return TryRemoveTrafficLightAtCell(gridCell) || TryPlaceTrafficLightAtCell(gridCell);
    }

    private bool TryApplyDragAction(Vector3Int gridCell, DragMode mode)
    {
        return mode == DragMode.Remove
            ? TryRemoveTrafficLightAtCell(gridCell)
            : TryPlaceTrafficLightAtCell(gridCell);
    }

    private void RecordAction(Vector3Int gridCell)
    {
        lastDragActionFrame = Time.frameCount;
        lastDragActionCell = gridCell;
    }

    private bool WasActionAlreadyHandledThisFrame(Vector3Int gridCell)
    {
        return lastDragActionFrame == Time.frameCount && lastDragActionCell == gridCell;
    }

    private bool CanPlaceTrafficLightAtCell(Vector3Int gridCell)
    {
        return IsValidTrafficLightCell(gridCell) && !lightsByCell.ContainsKey(gridCell);
    }

    private bool IsValidTrafficLightCell(Vector3Int gridCell)
    {
        return roadNetworkManager != null
            && roadNetworkManager.TryGetRoad(gridCell, out RoadTileData roadTile)
            && roadTile.connections == FourWayMask;
    }

    private bool CanRemoveTrafficLightAtCell(Vector3Int gridCell)
    {
        return lightsByCell.TryGetValue(gridCell, out TrafficLightNode node)
            && node != null
            && !node.IsLockedInPlace;
    }

    private void RegisterExistingSceneTrafficLights()
    {
        if (grid == null)
        {
            return;
        }

        TrafficLightNode[] existing = FindObjectsByType<TrafficLightNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            RegisterExistingTrafficLight(existing[i]);
        }
    }

    private void RegisterExistingTrafficLight(TrafficLightNode node)
    {
        if (node == null)
        {
            return;
        }

        Vector3Int cell = grid.WorldToCell(node.transform.position);
        if (lightsByCell.ContainsKey(cell))
        {
            return;
        }

        int lightId = node.LightId;
        if (lightId <= 0 || lightsById.ContainsKey(lightId))
        {
            lightId = nextLightId;
        }

        string displayName = string.IsNullOrWhiteSpace(node.LightName) ? $"{lightNamePrefix} {lightId}" : node.LightName;
        node.Initialize(lightId, cell, displayName, true);
        lightsById[lightId] = node;
        lightsByCell[cell] = node;
        nextLightId = Mathf.Max(nextLightId, lightId + 1);
    }

    private void CreatePreviewObject()
    {
        if (trafficLightPrefab == null)
        {
            return;
        }

        previewObject = new GameObject($"{trafficLightPrefab.name}_Preview");
        previewObject.transform.SetParent(transform, false);
        CreateTrafficLightSet(previewObject.transform);

        foreach (Collider collider in previewObject.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        PreviewVisualUtility.SetLayerRecursively(previewObject, LayerMask.NameToLayer("Ignore Raycast"));
        PreviewVisualUtility.CacheAndPreparePreviewMaterials(previewObject, previewMaterials);
        PreviewVisualUtility.UpdatePreviewColor(
            previewMaterials,
            previewValidColor,
            previewInvalidColor,
            previewAlpha,
            false);
    }

    private void CreateTrafficLightSet(Transform root)
    {
        if (root == null || trafficLightPrefab == null)
        {
            return;
        }

        for (int i = 0; i < LightDirections.Length; i++)
        {
            Vector3 direction = LightDirections[i];
            GameObject light = Instantiate(trafficLightPrefab, root);
            PlacementObjectUtility.RemoveComponentsInChildren<TrafficLightNode>(light);

            Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
            Vector3 localPos = (direction * lightSideOffset) + (right * lightRightOffset);
            light.transform.localPosition = new Vector3(localPos.x, lightLocalY, localPos.z);
            light.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            light.name = $"{trafficLightPrefab.name}_{i + 1}";
        }
    }

    private Transform ResolveRuntimeParent()
    {
        Transform candidate = trafficLightsParent != null ? trafficLightsParent : transform;
        if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded)
        {
            return candidate;
        }

        return transform;
    }
}
