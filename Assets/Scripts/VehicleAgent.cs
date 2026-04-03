using System.Collections.Generic;
using UnityEngine;

public class VehicleAgent : MonoBehaviour
{
    [SerializeField] private int vehicleId;
    [SerializeField] private CargoType cargoType = CargoType.None;
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float turnSpeed = 360f;
    [SerializeField] private float reachDistance = 0.25f;
    [SerializeField, Range(0.5f, 0.95f)] private float turnExitProgressInTile = 0.75f;
    [SerializeField, Min(1)] private int cargoCapacity = 30;
    [SerializeField, Min(0f)] private float stopBaseSeconds = 0.5f;
    [SerializeField, Min(0f)] private float loadSecondsPerUnit = 0.12f;
    [SerializeField, Min(0f)] private float unloadSecondsPerUnit = 0.1f;
    [SerializeField, Min(1)] private int maxTransferUnitsPerStop = 20;

    public int VehicleId => vehicleId;
    public CargoType CargoType => cargoType;
    public IReadOnlyList<int> AssignedStopIds => assignedStopIds;
    public int ActiveRouteId => activeRouteId;
    public bool IsMoving => isMoving;
    public int CargoAmount => cargoAmount;
    public int CargoCapacity => cargoCapacity;

    private RoadNetworkManager roadNetworkManager;
    private GridMap gridMap;
    private Grid grid;
    private Vector3Int currentRoadCell;
    private bool hasCurrentRoadCell;
    private float movementY;
    private float laneOffset;
    private bool hasMovementContext;

    private readonly List<int> assignedStopIds = new();
    private readonly List<Vector3Int> assignedStopRoadCells = new();
    private readonly List<Vector3Int> assignedStopCells = new();
    private readonly List<BuildingEconomy> nearbyBuildings = new();
    private readonly List<Vector3Int> routeCells = new();
    private readonly List<Vector3Int> segmentBuffer = new();
    private int activeRouteId = -1;
    private int loopStartIndex;
    private int nextScheduledStopIndex;
    private bool isTurningInCell;
    private int pendingTargetCellIndex = -1;
    private int targetCellIndex = -1;
    private Vector3 targetPosition;
    private float stopWaitTimer;
    private int cargoAmount;
    private bool isMoving;

    public void Initialize(int id, CargoType type)
    {
        vehicleId = id;
        cargoType = type;
        movementY = transform.position.y;
    }

    public void ConfigureMovementContext(
        RoadNetworkManager networkManager,
        Grid gridReference,
        Vector3Int startCell,
        float laneOffsetAmount,
        float yPosition)
    {
        roadNetworkManager = networkManager;
        grid = gridReference;
        movementY = yPosition;
        laneOffset = Mathf.Max(0f, laneOffsetAmount);
        hasMovementContext = roadNetworkManager != null && grid != null;
        hasCurrentRoadCell = false;

        if (!hasMovementContext)
        {
            return;
        }

        if (TryResolveNearestRoadCell(startCell, out Vector3Int resolvedCell)
            || TryResolveNearestRoadCell(grid.WorldToCell(transform.position), out resolvedCell))
        {
            currentRoadCell = resolvedCell;
            hasCurrentRoadCell = true;
        }
    }

    public bool AssignRoute(RouteData route)
    {
        if (route == null || route.pathCells == null || route.pathCells.Count < 2)
        {
            return false;
        }

        if (!EnsureContext())
        {
            return false;
        }

        activeRouteId = route.routeId;
        assignedStopIds.Clear();
        assignedStopRoadCells.Clear();
        assignedStopCells.Clear();
        nextScheduledStopIndex = 0;
        for (int i = 0; i < route.stopIds.Count; i++)
        {
            int stopId = route.stopIds[i];
            if (stopId > 0)
            {
                assignedStopIds.Add(stopId);
            }
        }

        routeCells.Clear();
        for (int i = 0; i < route.pathCells.Count; i++)
        {
            Vector3Int cell = route.pathCells[i];
            if (i > 0 && route.pathCells[i - 1] == cell)
            {
                continue;
            }

            routeCells.Add(cell);
        }

        if (routeCells.Count < 2)
        {
            return false;
        }

        PrependPathFromCurrentCell(routeCells[0]);
        return StartRouteMovement();
    }

    public bool AssignStops(StopManager stopManager, IReadOnlyList<int> stopIds)
    {
        ClearAssignedStops();
        if (!EnsureContext() || stopManager == null || stopIds == null)
        {
            return false;
        }

        List<Vector3Int> stopRoadCells = new();
        for (int i = 0; i < stopIds.Count; i++)
        {
            int stopId = stopIds[i];
            if (stopId <= 0)
            {
                continue;
            }

            if (!stopManager.TryGetStopById(stopId, out StopNode stopNode) || stopNode == null)
            {
                continue;
            }

            if (!TryResolveNearestRoadCell(stopNode.GridCell, out Vector3Int stopRoadCell))
            {
                continue;
            }

            if (stopRoadCells.Count > 0 && stopRoadCells[stopRoadCells.Count - 1] == stopRoadCell)
            {
                continue;
            }

            stopRoadCells.Add(stopRoadCell);
            assignedStopIds.Add(stopId);
        }

        if (stopRoadCells.Count < 2)
        {
            ClearAssignedStops();
            return false;
        }

        RoadDirectionMask firstLegForbiddenStartExit = RoadDirectionMask.None;
        RoadDirectionMask approachForbiddenStartExit = GetForbiddenStartExitFromCurrentHeading();
        if (TryGetCurrentRoadCellForRouting(out Vector3Int currentCellForRouting)
            && currentCellForRouting != stopRoadCells[0])
        {
            List<Vector3Int> approachPath = new();
            if (roadNetworkManager.FindShortestPath(currentCellForRouting, stopRoadCells[0], approachPath, approachForbiddenStartExit)
                && approachPath.Count >= 2)
            {
                Vector3Int approachPreviousCell = approachPath[approachPath.Count - 2];
                firstLegForbiddenStartExit = roadNetworkManager.GetDirectionBetweenCells(stopRoadCells[0], approachPreviousCell);
            }
        }

        routeCells.Clear();
        segmentBuffer.Clear();
        assignedStopRoadCells.Clear();
        assignedStopCells.Clear();

        for (int i = 0; i < stopRoadCells.Count; i++)
        {
            Vector3Int fromCell = stopRoadCells[i];
            Vector3Int toCell = stopRoadCells[(i + 1) % stopRoadCells.Count];
            RoadDirectionMask forbiddenStartExit = RoadDirectionMask.None;
            if (i == 0 && firstLegForbiddenStartExit != RoadDirectionMask.None)
            {
                forbiddenStartExit = firstLegForbiddenStartExit;
            }
            else if (routeCells.Count >= 2 && routeCells[routeCells.Count - 1] == fromCell)
            {
                Vector3Int previousCell = routeCells[routeCells.Count - 2];
                forbiddenStartExit = roadNetworkManager.GetDirectionBetweenCells(fromCell, previousCell);
            }

            if (!roadNetworkManager.FindShortestPath(fromCell, toCell, segmentBuffer, forbiddenStartExit))
            {
                ClearAssignedStops();
                return false;
            }

            AppendSegment(routeCells, segmentBuffer);
        }

        assignedStopRoadCells.AddRange(stopRoadCells);
        for (int i = 0; i < assignedStopIds.Count; i++)
        {
            if (stopManager.TryGetStopById(assignedStopIds[i], out StopNode assignedStopNode) && assignedStopNode != null)
            {
                assignedStopCells.Add(assignedStopNode.GridCell);
            }
            else
            {
                assignedStopCells.Add(stopRoadCells[Mathf.Clamp(i, 0, stopRoadCells.Count - 1)]);
            }
        }
        nextScheduledStopIndex = 0;

        if (routeCells.Count < 2)
        {
            ClearAssignedStops();
            return false;
        }

        activeRouteId = -1;
        PrependPathFromCurrentCell(routeCells[0]);
        return StartRouteMovement();
    }

    public bool RebuildRouteFromAssignedStops(StopManager stopManager)
    {
        if (assignedStopIds.Count < 2)
        {
            return false;
        }

        List<int> stopIds = new(assignedStopIds);
        return AssignStops(stopManager, stopIds);
    }

    public void ClearAssignedStops()
    {
        assignedStopIds.Clear();
        routeCells.Clear();
        segmentBuffer.Clear();
        activeRouteId = -1;
        assignedStopRoadCells.Clear();
        assignedStopCells.Clear();
        loopStartIndex = 0;
        nextScheduledStopIndex = 0;
        stopWaitTimer = 0f;
        isTurningInCell = false;
        pendingTargetCellIndex = -1;
        targetCellIndex = -1;
        isMoving = false;
    }

    public bool TryGetLaneOccupancy(out Vector3Int currentRoadCellOut, out Vector3Int nextRoadCell, out bool hasNextRoadCell, out Vector3 laneForward)
    {
        currentRoadCellOut = currentRoadCell;
        nextRoadCell = currentRoadCell;
        hasNextRoadCell = false;
        laneForward = Vector3.zero;

        if (!EnsureContext() || !hasCurrentRoadCell)
        {
            return false;
        }

        if (isMoving && routeCells.Count > 1 && targetCellIndex >= 0)
        {
            nextRoadCell = routeCells[targetCellIndex];
            hasNextRoadCell = nextRoadCell != currentRoadCell;
            if (hasNextRoadCell)
            {
                Vector3 forward = grid.GetCellCenterWorld(nextRoadCell) - grid.GetCellCenterWorld(currentRoadCell);
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                {
                    laneForward = forward.normalized;
                }
            }
        }

        if (laneForward.sqrMagnitude <= 0.0001f)
        {
            laneForward = transform.forward;
            laneForward.y = 0f;
            if (laneForward.sqrMagnitude > 0.0001f)
            {
                laneForward.Normalize();
            }
        }

        return true;
    }

    private void Update()
    {
        if (EconomyManager.HasInstance && EconomyManager.Instance.IsGameOver)
        {
            return;
        }

        if (!isMoving || !EnsureContext() || targetCellIndex < 0 || targetCellIndex >= routeCells.Count)
        {
            return;
        }

        float dt = Time.unscaledDeltaTime * Mathf.Max(0f, Time.timeScale);

        if (stopWaitTimer > 0f)
        {
            stopWaitTimer -= dt;
            if (stopWaitTimer > 0f)
            {
                return;
            }

            stopWaitTimer = 0f;
        }

        Vector3 toTarget = targetPosition - transform.position;
        Vector3 planar = new Vector3(toTarget.x, 0f, toTarget.z);
        if (planar.sqrMagnitude <= reachDistance * reachDistance)
        {
            CompleteStep();
            return;
        }

        if (planar.sqrMagnitude > 0.0001f)
        {
            Quaternion desired = Quaternion.LookRotation(planar.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * dt);
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * dt);
    }

    private bool StartRouteMovement()
    {
        if (routeCells.Count < 2 || !EnsureContext())
        {
            return false;
        }

        if (!hasCurrentRoadCell && !TryResolveNearestRoadCell(grid.WorldToCell(transform.position), out currentRoadCell))
        {
            return false;
        }

        hasCurrentRoadCell = true;
        targetCellIndex = 0;

        for (int i = 0; i < routeCells.Count; i++)
        {
            if (routeCells[targetCellIndex] != currentRoadCell)
            {
                break;
            }

            targetCellIndex = AdvanceIndex(targetCellIndex);
        }

        if (routeCells[targetCellIndex] == currentRoadCell)
        {
            isMoving = false;
            return false;
        }

        isTurningInCell = false;
        pendingTargetCellIndex = -1;
        SetTargetPosition(routeCells[targetCellIndex]);
        isMoving = true;
        return true;
    }

    private void CompleteStep()
    {
        transform.position = targetPosition;
        if (isTurningInCell)
        {
            isTurningInCell = false;
            if (pendingTargetCellIndex < 0 || pendingTargetCellIndex >= routeCells.Count)
            {
                isMoving = false;
                pendingTargetCellIndex = -1;
                return;
            }

            targetCellIndex = pendingTargetCellIndex;
            pendingTargetCellIndex = -1;
            SetTargetPosition(routeCells[targetCellIndex]);
            return;
        }

        Vector3Int previousRoadCell = currentRoadCell;
        currentRoadCell = routeCells[targetCellIndex];
        hasCurrentRoadCell = true;

        int nextIndex = AdvanceIndex(targetCellIndex);
        for (int i = 0; i < routeCells.Count; i++)
        {
            if (nextIndex < 0 || nextIndex >= routeCells.Count)
            {
                isMoving = false;
                return;
            }

            if (routeCells[nextIndex] != currentRoadCell)
            {
                break;
            }

            nextIndex = AdvanceIndex(nextIndex);
        }

        if (nextIndex < 0 || nextIndex >= routeCells.Count || routeCells[nextIndex] == currentRoadCell)
        {
            isMoving = false;
            return;
        }

        Vector3Int nextRoadCell = routeCells[nextIndex];
        RoadDirectionMask incomingDirection = roadNetworkManager != null
            ? roadNetworkManager.GetDirectionBetweenCells(previousRoadCell, currentRoadCell)
            : RoadDirectionMask.None;
        RoadDirectionMask outgoingDirection = roadNetworkManager != null
            ? roadNetworkManager.GetDirectionBetweenCells(currentRoadCell, nextRoadCell)
            : RoadDirectionMask.None;

        if (incomingDirection != RoadDirectionMask.None
            && outgoingDirection != RoadDirectionMask.None
            && incomingDirection != outgoingDirection)
        {
            Vector3 turnExitPoint = GetTurnExitPoint(currentRoadCell, nextRoadCell, outgoingDirection);
            Vector3 planarDelta = turnExitPoint - transform.position;
            planarDelta.y = 0f;
            if (planarDelta.sqrMagnitude > 0.0001f)
            {
                isTurningInCell = true;
                pendingTargetCellIndex = nextIndex;
                targetPosition = turnExitPoint;
                targetPosition.y = movementY;
                return;
            }
        }

        targetCellIndex = nextIndex;
        HandleScheduledStopTransfer();
        SetTargetPosition(nextRoadCell);
    }

    private void SetTargetPosition(Vector3Int targetCell)
    {
        targetPosition = grid.GetCellCenterWorld(targetCell);
        Vector3 from = grid.GetCellCenterWorld(currentRoadCell);
        Vector3 segment = targetPosition - from;
        segment.y = 0f;
        if (segment.sqrMagnitude > 0.0001f && laneOffset > 0f)
        {
            Vector3 laneRight = Vector3.Cross(Vector3.up, segment.normalized);
            targetPosition += laneRight * laneOffset;
        }

        targetPosition.y = movementY;
    }

    private void PrependPathFromCurrentCell(Vector3Int routeStartCell)
    {
        loopStartIndex = 0;

        if (!EnsureContext())
        {
            return;
        }

        if (!hasCurrentRoadCell && !TryResolveNearestRoadCell(grid.WorldToCell(transform.position), out currentRoadCell))
        {
            return;
        }

        hasCurrentRoadCell = true;
        if (currentRoadCell == routeStartCell)
        {
            return;
        }

        RoadDirectionMask forbiddenStartExit = GetForbiddenStartExitFromCurrentHeading();
        if (!roadNetworkManager.FindShortestPath(currentRoadCell, routeStartCell, segmentBuffer, forbiddenStartExit) || segmentBuffer.Count < 2)
        {
            return;
        }

        List<Vector3Int> merged = new(segmentBuffer.Count + routeCells.Count);
        merged.AddRange(segmentBuffer);

        int routeStartIndex = 0;
        if (merged[merged.Count - 1] == routeCells[0])
        {
            routeStartIndex = 1;
        }

        for (int i = routeStartIndex; i < routeCells.Count; i++)
        {
            merged.Add(routeCells[i]);
        }

        routeCells.Clear();
        routeCells.AddRange(merged);
        loopStartIndex = Mathf.Clamp(segmentBuffer.Count - 1, 0, routeCells.Count - 1);
    }

    private int AdvanceIndex(int index)
    {
        if (routeCells.Count == 0)
        {
            return -1;
        }

        int next = index + 1;
        if (next < routeCells.Count)
        {
            return next;
        }

        return Mathf.Clamp(loopStartIndex, 0, routeCells.Count - 1);
    }

    private Vector3 GetTurnExitPoint(Vector3Int currentCell, Vector3Int nextCell, RoadDirectionMask outgoingDirection)
    {
        Vector3 currentCenter = grid.GetCellCenterWorld(currentCell);
        Vector3 nextCenter = grid.GetCellCenterWorld(nextCell);
        Vector3 point = Vector3.Lerp(currentCenter, nextCenter, Mathf.Clamp01(turnExitProgressInTile));

        Vector3 forward = DirectionToVector(outgoingDirection);
        if (forward.sqrMagnitude > 0.0001f && laneOffset > 0f)
        {
            Vector3 laneRight = Vector3.Cross(Vector3.up, forward.normalized);
            point += laneRight * laneOffset;
        }

        point.y = movementY;
        return point;
    }

    private bool TryResolveNearestRoadCell(Vector3Int sourceCell, out Vector3Int roadCell)
    {
        roadCell = sourceCell;
        if (roadNetworkManager == null)
        {
            return false;
        }

        return roadNetworkManager.TryResolveNearestRoadCell(sourceCell, out roadCell);
    }

    private bool TryGetCurrentRoadCellForRouting(out Vector3Int roadCell)
    {
        roadCell = default;
        if (!EnsureContext())
        {
            return false;
        }

        if (hasCurrentRoadCell)
        {
            roadCell = currentRoadCell;
            return true;
        }

        if (!TryResolveNearestRoadCell(grid.WorldToCell(transform.position), out roadCell))
        {
            return false;
        }

        currentRoadCell = roadCell;
        hasCurrentRoadCell = true;
        return true;
    }

    private RoadDirectionMask GetForbiddenStartExitFromCurrentHeading()
    {
        if (!TryGetCurrentHeadingDirection(out RoadDirectionMask headingDirection) || headingDirection == RoadDirectionMask.None)
        {
            return RoadDirectionMask.None;
        }

        return RoadDirectionUtility.Opposite(headingDirection);
    }

    private bool TryGetCurrentHeadingDirection(out RoadDirectionMask headingDirection)
    {
        headingDirection = RoadDirectionMask.None;
        if (!EnsureContext())
        {
            return false;
        }

        if (isMoving
            && hasCurrentRoadCell
            && targetCellIndex >= 0
            && targetCellIndex < routeCells.Count
            && routeCells[targetCellIndex] != currentRoadCell)
        {
            headingDirection = roadNetworkManager.GetDirectionBetweenCells(currentRoadCell, routeCells[targetCellIndex]);
            if (headingDirection != RoadDirectionMask.None)
            {
                return true;
            }
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        if (Mathf.Abs(forward.x) > Mathf.Abs(forward.z))
        {
            headingDirection = forward.x >= 0f ? RoadDirectionMask.East : RoadDirectionMask.West;
        }
        else
        {
            headingDirection = forward.z >= 0f ? RoadDirectionMask.North : RoadDirectionMask.South;
        }

        return headingDirection != RoadDirectionMask.None;
    }

    private static Vector3 DirectionToVector(RoadDirectionMask direction)
    {
        switch (direction)
        {
            case RoadDirectionMask.North:
                return Vector3.forward;
            case RoadDirectionMask.East:
                return Vector3.right;
            case RoadDirectionMask.South:
                return Vector3.back;
            case RoadDirectionMask.West:
                return Vector3.left;
            default:
                return Vector3.zero;
        }
    }

    private bool EnsureContext()
    {
        if (roadNetworkManager == null)
        {
            roadNetworkManager = FindFirstObjectByType<RoadNetworkManager>();
        }

        if (gridMap == null)
        {
            gridMap = GridMap.EnsureInstance();
        }

        if (grid == null)
        {
            grid = FindFirstObjectByType<Grid>();
        }

        hasMovementContext = roadNetworkManager != null && grid != null;
        if (movementY <= 0f)
        {
            movementY = transform.position.y;
        }

        return hasMovementContext;
    }

    private void HandleScheduledStopTransfer()
    {
        if (!EnsureContext() || assignedStopRoadCells.Count == 0)
        {
            return;
        }

        if (nextScheduledStopIndex < 0 || nextScheduledStopIndex >= assignedStopRoadCells.Count)
        {
            nextScheduledStopIndex = 0;
        }

        int transferStopIndex = nextScheduledStopIndex;
        Vector3Int expectedStopCell = assignedStopRoadCells[nextScheduledStopIndex];
        if (currentRoadCell != expectedStopCell)
        {
            transferStopIndex = -1;
            for (int i = 0; i < assignedStopRoadCells.Count; i++)
            {
                if (assignedStopRoadCells[i] == currentRoadCell)
                {
                    transferStopIndex = i;
                    break;
                }
            }

            if (transferStopIndex < 0)
            {
                return;
            }
        }

        int loadedUnits = 0;
        int unloadedUnits = 0;

        Vector3Int transferCell = currentRoadCell;
        if (transferStopIndex >= 0 && transferStopIndex < assignedStopCells.Count)
        {
            transferCell = assignedStopCells[transferStopIndex];
        }

        GetNearbyBuildingsForStopCell(transferCell, nearbyBuildings);
        if (nearbyBuildings.Count > 0)
        {
            unloadedUnits = UnloadCargoAtStop(nearbyBuildings);
            loadedUnits = LoadCargoAtStop(nearbyBuildings);
        }

        float dwellSeconds = stopBaseSeconds
            + (loadedUnits * loadSecondsPerUnit)
            + (unloadedUnits * unloadSecondsPerUnit);
        if (dwellSeconds > 0f)
        {
            stopWaitTimer = Mathf.Max(stopWaitTimer, dwellSeconds);
        }

        nextScheduledStopIndex = (transferStopIndex + 1) % assignedStopRoadCells.Count;
    }

    private int UnloadCargoAtStop(List<BuildingEconomy> buildings)
    {
        if (cargoAmount <= 0 || cargoType == CargoType.None)
        {
            return 0;
        }

        int unloaded = 0;
        int transferBudget = Mathf.Max(1, maxTransferUnitsPerStop);

        for (int i = 0; i < buildings.Count; i++)
        {
            if (cargoAmount <= 0 || unloaded >= transferBudget)
            {
                break;
            }

            BuildingEconomy building = buildings[i];
            if (building == null || !building.CanReceiveCargo(cargoType))
            {
                continue;
            }

            int requestAmount = Mathf.Min(cargoAmount, transferBudget - unloaded);
            int delivered = building.ReceiveCargo(cargoType, requestAmount);
            if (delivered <= 0)
            {
                continue;
            }

            delivered = Mathf.Min(delivered, cargoAmount);
            cargoAmount -= delivered;
            unloaded += delivered;
        }

        cargoAmount = Mathf.Clamp(cargoAmount, 0, cargoCapacity);
        return unloaded;
    }

    private int LoadCargoAtStop(List<BuildingEconomy> buildings)
    {
        if (cargoType == CargoType.None || cargoAmount >= cargoCapacity)
        {
            return 0;
        }

        int loaded = 0;
        int transferBudget = Mathf.Max(1, maxTransferUnitsPerStop);

        for (int i = 0; i < buildings.Count; i++)
        {
            int freeCapacity = cargoCapacity - cargoAmount;
            if (freeCapacity <= 0 || loaded >= transferBudget)
            {
                break;
            }

            BuildingEconomy building = buildings[i];
            if (building == null || !building.CanProvideCargo(cargoType))
            {
                continue;
            }

            int requestAmount = Mathf.Min(freeCapacity, transferBudget - loaded);
            int taken = building.TakeCargo(cargoType, requestAmount);
            if (taken <= 0)
            {
                continue;
            }

            taken = Mathf.Min(taken, freeCapacity);
            cargoAmount += taken;
            loaded += taken;
        }

        cargoAmount = Mathf.Clamp(cargoAmount, 0, cargoCapacity);
        return loaded;
    }

    private void GetNearbyBuildingsForStopCell(Vector3Int stopCell, List<BuildingEconomy> results)
    {
        results.Clear();
        if (gridMap == null)
        {
            return;
        }

        gridMap.GetBuildingsAtOrAdjacentCardinal(stopCell, results);
    }

    private static void AppendSegment(List<Vector3Int> fullPath, List<Vector3Int> segment)
    {
        if (segment == null || segment.Count == 0)
        {
            return;
        }

        int startIndex = 0;
        if (fullPath.Count > 0 && fullPath[fullPath.Count - 1] == segment[0])
        {
            startIndex = 1;
        }

        for (int i = startIndex; i < segment.Count; i++)
        {
            fullPath.Add(segment[i]);
        }
    }
}
