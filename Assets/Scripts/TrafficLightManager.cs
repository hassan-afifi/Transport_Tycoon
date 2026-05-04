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
    [SerializeField] private VehicleBuildToolUI vehicleBuildToolUIToDisable;
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
    [SerializeField] private string selectionLayerName = "Selectable";

    private readonly Dictionary<int, TrafficLightNode> lightsById = new();
    private readonly Dictionary<Vector3Int, TrafficLightNode> lightsByCell = new();
    private readonly Dictionary<Vector3Int, List<IntersectionReservation>> reservationsByCell = new();
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

    private enum IntersectionMovementType
    {
        Unknown,
        Straight,
        Right,
        Left
    }

    private struct IntersectionReservation
    {
        public VehicleAgent vehicle;
        public Vector3Int fromCell;
        public Vector3Int toCell;
        public RoadDirectionMask incomingDirection;
        public RoadDirectionMask outgoingDirection;
        public IntersectionMovementType movementType;
    }

    private static readonly Vector3[] LightDirections =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.right,
        Vector3.left
    };

    private int selectionLayer = -1;

    public bool IsPlacementActive { get; private set; }
    public IReadOnlyDictionary<int, TrafficLightNode> LightsById => lightsById;

    public event Action<TrafficLightNode> TrafficLightPlaced;
    public event Action TrafficLightsChanged;

    private void Awake()
    {
        CoreUtility.ResolveIfNull(ref inputManager);
        CoreUtility.ResolveIfNull(ref grid);
        CoreUtility.ResolveIfNull(ref roadNetworkManager);
        CoreUtility.ResolveIfNull(ref placementSystemToDisable);
        CoreUtility.ResolveIfNull(ref stopManagerToDisable);
        CoreUtility.ResolveIfNull(ref vehicleBuildToolUIToDisable);
        selectionLayer = LayerMask.NameToLayer(selectionLayerName);
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
                PreviewVisualUtility.DefaultValidColor,
                PreviewVisualUtility.DefaultInvalidColor,
                previewAlpha,
                false);
            return;
        }

        Vector3Int gridCell = grid.WorldToCell(mapPos);
        Vector3 snappedPos = grid.GetCellCenterWorld(gridCell);

        if (previewObject != null)
        {
            previewObject.transform.position = new Vector3(snappedPos.x, previewY, snappedPos.z);
            if (roadNetworkManager != null && roadNetworkManager.TryGetRoad(gridCell, out RoadTileData previewRoadTile))
            {
                SetLightHeadsActiveForMask(previewObject.transform, previewRoadTile.connections);
            }
            else
            {
                SetLightHeadsActiveForMask(previewObject.transform, RoadDirectionMask.None);
            }
        }

        bool canPlace = CanPlaceTrafficLightAtCell(gridCell) && !inputManager.IsPointerOverUI();
        PreviewVisualUtility.UpdatePreviewColor(
            previewMaterials,
            PreviewVisualUtility.DefaultValidColor,
            PreviewVisualUtility.DefaultInvalidColor,
            previewAlpha,
            canPlace);
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

        if (vehicleBuildToolUIToDisable != null)
        {
            vehicleBuildToolUIToDisable.EndPlacement();
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

        if (!roadNetworkManager.TryGetRoad(gridCell, out RoadTileData placedRoadTile))
        {
            return false;
        }

        if (EconomyManager.HasInstance && !EconomyManager.Instance.TrySpendForTrafficLightPlacement())
        {
            return false;
        }

        RoadDirectionMask allowedMask = placedRoadTile.connections;
        Vector3 worldPos = grid.GetCellCenterWorld(gridCell);
        worldPos.y = lightY;
        Transform parent = CoreUtility.ResolveRuntimeParent(trafficLightsParent, transform);
        int lightId = nextLightId++;
        string lightName = $"{lightNamePrefix} {lightId}";

        GameObject lightObject = new(lightName);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = worldPos;

        CreateTrafficLightSet(lightObject.transform);
        SetLightHeadsActiveForMask(lightObject.transform, allowedMask);
        EnsureSelectable(lightObject);

        TrafficLightNode node = lightObject.AddComponent<TrafficLightNode>();

        node.Initialize(lightId, gridCell, lightName, false);
        node.ConfigureAllowedDirections(allowedMask);

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

    public bool HasTrafficLightAtCell(Vector3Int gridCell)
    {
        return lightsByCell.ContainsKey(gridCell);
    }

    public bool TryReserveIntersection(Vector3Int gridCell, VehicleAgent vehicle, Vector3Int fromCell, Vector3Int toCell)
    {
        if (vehicle == null)
        {
            return false;
        }

        if (!IsReservableIntersectionCell(gridCell))
        {
            return true;
        }

        if (roadNetworkManager == null)
        {
            return false;
        }

        RoadDirectionMask incomingDirection = roadNetworkManager.GetDirectionBetweenCells(fromCell, gridCell);
        RoadDirectionMask outgoingDirection = roadNetworkManager.GetDirectionBetweenCells(gridCell, toCell);
        if (incomingDirection == RoadDirectionMask.None || outgoingDirection == RoadDirectionMask.None)
        {
            return false;
        }

        IntersectionMovementType movementType = GetMovementType(incomingDirection, outgoingDirection);
        if (movementType == IntersectionMovementType.Unknown)
        {
            return false;
        }

        if (lightsByCell.TryGetValue(gridCell, out TrafficLightNode controlledNode)
            && controlledNode != null
            && !controlledNode.IsDirectionGreen(RoadUtility.Opposite(incomingDirection)))
        {
            return false;
        }

        List<IntersectionReservation> reservations = GetOrCreateReservations(gridCell);
        PruneStaleReservations(gridCell, reservations);

        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            IntersectionReservation existing = reservations[i];
            if (existing.vehicle != vehicle)
            {
                continue;
            }

            if (existing.fromCell == fromCell && existing.toCell == toCell)
            {
                return true;
            }

            reservations.RemoveAt(i);
        }

        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            IntersectionReservation existing = reservations[i];
            if (!DoMovementsConflict(
                    incomingDirection,
                    outgoingDirection,
                    movementType,
                    existing.incomingDirection,
                    existing.outgoingDirection,
                    existing.movementType))
            {
                continue;
            }

            if (CanPreemptExistingReservation(
                    gridCell,
                    movementType,
                    incomingDirection,
                    existing,
                    controlledNode))
            {
                reservations.RemoveAt(i);
                continue;
            }

            return false;
        }

        reservations.Add(new IntersectionReservation
        {
            vehicle = vehicle,
            fromCell = fromCell,
            toCell = toCell,
            incomingDirection = incomingDirection,
            outgoingDirection = outgoingDirection,
            movementType = movementType
        });
        return true;
    }

    public bool HasIntersectionReservation(Vector3Int gridCell, VehicleAgent vehicle)
    {
        if (vehicle == null || !reservationsByCell.TryGetValue(gridCell, out List<IntersectionReservation> reservations))
        {
            return false;
        }

        PruneStaleReservations(gridCell, reservations);
        for (int i = 0; i < reservations.Count; i++)
        {
            if (reservations[i].vehicle == vehicle)
            {
                return true;
            }
        }

        if (reservations.Count == 0)
        {
            reservationsByCell.Remove(gridCell);
        }

        return false;
    }

    public void ReleaseIntersection(Vector3Int gridCell, VehicleAgent vehicle)
    {
        if (!reservationsByCell.TryGetValue(gridCell, out List<IntersectionReservation> reservations))
        {
            return;
        }

        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            VehicleAgent holder = reservations[i].vehicle;
            if (holder == null || vehicle == null || holder == vehicle)
            {
                reservations.RemoveAt(i);
            }
        }

        if (reservations.Count == 0)
        {
            reservationsByCell.Remove(gridCell);
        }
    }

    private bool IsReservableIntersectionCell(Vector3Int gridCell)
    {
        if (lightsByCell.ContainsKey(gridCell))
        {
            return true;
        }

        if (roadNetworkManager == null || !roadNetworkManager.TryGetRoad(gridCell, out RoadTileData tile))
        {
            return false;
        }

        return RoadUtility.CountConnectedDirections(tile.connections) >= 3;
    }

    private static bool ShouldClearStaleReservation(IntersectionReservation reservation, Vector3Int reservedCell)
    {
        VehicleAgent holder = reservation.vehicle;
        if (holder == null)
        {
            return true;
        }

        if (!holder.TryGetLaneOccupancy(
            out Vector3Int currentRoadCell,
            out Vector3Int nextRoadCell,
            out bool hasNextRoadCell,
            out _))
        {
            return true;
        }

        if (currentRoadCell == reservedCell)
        {
            return false;
        }

        return !hasNextRoadCell || nextRoadCell != reservedCell;
    }

    public bool IsApproachBlockedByRedLight(Vector3Int fromCell, Vector3Int controlledIntersectionCell)
    {
        if (roadNetworkManager == null)
        {
            return false;
        }

        if (!lightsByCell.TryGetValue(controlledIntersectionCell, out TrafficLightNode node) || node == null)
        {
            return false;
        }

        RoadDirectionMask incomingDirection = roadNetworkManager.GetDirectionBetweenCells(fromCell, controlledIntersectionCell);
        if (incomingDirection == RoadDirectionMask.None)
        {
            return false;
        }

        return !node.IsDirectionGreen(RoadUtility.Opposite(incomingDirection));
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
        reservationsByCell.Remove(gridCell);

        if (EconomyManager.HasInstance)
        {
            EconomyManager.Instance.RefundForTrafficLightRemoval();
        }

        if (Application.isPlaying)
        {
            Destroy(node.gameObject);
        }
        else
        {
            DestroyImmediate(node.gameObject);
        }
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
            && IsIntersectionMask(roadTile.connections);
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
        if (roadNetworkManager != null && roadNetworkManager.TryGetRoad(cell, out RoadTileData roadTile))
        {
            node.ConfigureAllowedDirections(roadTile.connections);
            SetLightHeadsActiveForMask(node.transform, roadTile.connections);
        }

        EnsureSelectable(node.gameObject);
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
        PreviewVisualUtility.InitializePreviewObject(
            previewObject,
            previewMaterials,
            PreviewVisualUtility.DefaultValidColor,
            PreviewVisualUtility.DefaultInvalidColor,
            previewAlpha);
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
            TrafficLightHead head = light.GetComponent<TrafficLightHead>();
            if (head == null)
            {
                head = light.AddComponent<TrafficLightHead>();
            }

            head.AutoAssignMissingLights();

            Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
            Vector3 localPos = (direction * lightSideOffset) + (right * lightRightOffset);
            light.transform.localPosition = new Vector3(localPos.x, lightLocalY, localPos.z);
            light.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            light.name = $"{trafficLightPrefab.name}_{i + 1}";
        }
    }

    private void EnsureSelectable(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        if (selectionLayer >= 0)
        {
            PreviewVisualUtility.SetLayerRecursively(root, selectionLayer);
        }

        Collider rootCollider = root.GetComponent<Collider>();
        if (rootCollider == null)
        {
            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = Mathf.Max(0.1f, fallbackColliderRadius);
            sphere.center = Vector3.up * sphere.radius;
            rootCollider = sphere;
        }

        rootCollider.enabled = true;
    }

    private void SetLightHeadsActiveForMask(Transform root, RoadDirectionMask allowedMask)
    {
        if (root == null)
        {
            return;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            RoadDirectionMask direction = RoadUtility.GetClosestCardinalDirection(child.forward);
            bool isActive = direction != RoadDirectionMask.None && (allowedMask & direction) != 0;
            child.gameObject.SetActive(isActive);
        }
    }

    private static bool IsIntersectionMask(RoadDirectionMask mask)
    {
        return RoadUtility.CountConnectedDirections(mask) >= 3;
    }

    private List<IntersectionReservation> GetOrCreateReservations(Vector3Int gridCell)
    {
        if (!reservationsByCell.TryGetValue(gridCell, out List<IntersectionReservation> reservations))
        {
            reservations = new List<IntersectionReservation>();
            reservationsByCell[gridCell] = reservations;
        }

        return reservations;
    }

    private void PruneStaleReservations(Vector3Int gridCell, List<IntersectionReservation> reservations)
    {
        if (reservations == null)
        {
            return;
        }

        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            if (ShouldClearStaleReservation(reservations[i], gridCell))
            {
                reservations.RemoveAt(i);
            }
        }

        if (reservations.Count == 0)
        {
            reservationsByCell.Remove(gridCell);
        }
    }

    private bool CanPreemptExistingReservation(
        Vector3Int intersectionCell,
        IntersectionMovementType candidateType,
        RoadDirectionMask candidateIncoming,
        IntersectionReservation existing,
        TrafficLightNode controlledNode)
    {
        if (candidateType == IntersectionMovementType.Left || existing.movementType != IntersectionMovementType.Left)
        {
            return false;
        }

        if (controlledNode == null
            || !controlledNode.IsDirectionGreen(RoadUtility.Opposite(candidateIncoming))
            || !controlledNode.IsDirectionGreen(RoadUtility.Opposite(existing.incomingDirection)))
        {
            return false;
        }

        if (existing.vehicle == null)
        {
            return true;
        }

        if (!existing.vehicle.TryGetLaneOccupancy(
                out Vector3Int currentRoadCell,
                out _,
                out _,
                out _))
        {
            return true;
        }

        return currentRoadCell != intersectionCell;
    }

    private static bool DoMovementsConflict(
        RoadDirectionMask incomingA,
        RoadDirectionMask outgoingA,
        IntersectionMovementType movementA,
        RoadDirectionMask incomingB,
        RoadDirectionMask outgoingB,
        IntersectionMovementType movementB)
    {
        if (incomingA == incomingB || outgoingA == outgoingB)
        {
            return true;
        }

        bool oppositeApproach = incomingA == RoadUtility.Opposite(incomingB);
        if (!oppositeApproach)
        {
            return true;
        }

        if (movementA == IntersectionMovementType.Left || movementB == IntersectionMovementType.Left)
        {
            return true;
        }

        return false;
    }

    private static IntersectionMovementType GetMovementType(RoadDirectionMask incoming, RoadDirectionMask outgoing)
    {
        if (incoming == RoadDirectionMask.None || outgoing == RoadDirectionMask.None)
        {
            return IntersectionMovementType.Unknown;
        }

        if (outgoing == RoadUtility.Opposite(incoming))
        {
            return IntersectionMovementType.Straight;
        }

        if (outgoing == RotateClockwise(incoming))
        {
            return IntersectionMovementType.Right;
        }

        if (outgoing == RotateCounterClockwise(incoming))
        {
            return IntersectionMovementType.Left;
        }

        return IntersectionMovementType.Unknown;
    }

    private static RoadDirectionMask RotateClockwise(RoadDirectionMask direction)
    {
        switch (direction)
        {
            case RoadDirectionMask.North:
                return RoadDirectionMask.East;
            case RoadDirectionMask.East:
                return RoadDirectionMask.South;
            case RoadDirectionMask.South:
                return RoadDirectionMask.West;
            case RoadDirectionMask.West:
                return RoadDirectionMask.North;
            default:
                return RoadDirectionMask.None;
        }
    }

    private static RoadDirectionMask RotateCounterClockwise(RoadDirectionMask direction)
    {
        switch (direction)
        {
            case RoadDirectionMask.North:
                return RoadDirectionMask.West;
            case RoadDirectionMask.East:
                return RoadDirectionMask.North;
            case RoadDirectionMask.South:
                return RoadDirectionMask.East;
            case RoadDirectionMask.West:
                return RoadDirectionMask.South;
            default:
                return RoadDirectionMask.None;
        }
    }
}
