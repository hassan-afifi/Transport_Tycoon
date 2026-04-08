using System.Collections.Generic;
using UnityEngine;

public class VehicleAgent : MonoBehaviour
{
    [SerializeField] private int vehicleId;
    [SerializeField] private CargoType cargoType = CargoType.None;
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float turnSpeed = 280f;
    [SerializeField, Range(0.35f, 0.95f)] private float turnEntryProgress = 0.82f;
    [SerializeField] private float turnRotationStartProgress = 0f;
    [SerializeField] private float turnRotationEndProgress = 0.5f;
    [SerializeField] private float reachDistance = 0.25f;
    [SerializeField, Min(1)] private int cargoCapacity = 30;
    [SerializeField, Min(0f)] private float stopBaseSeconds = 0.5f;
    [SerializeField, Min(0f)] private float loadSecondsPerUnit = 0.12f;
    [SerializeField, Min(0f)] private float unloadSecondsPerUnit = 0.1f;
    [SerializeField, Min(1)] private int maxTransferUnitsPerStop = 20;
    [SerializeField, Min(0.02f)] private float redLightRetrySeconds = 0.1f;

    public int VehicleId => vehicleId;
    public CargoType CargoType => cargoType;
    public IReadOnlyList<int> AssignedStopIds => assignedStopIds;
    public int ActiveRouteId => activeRouteId;
    public bool IsMoving => isMoving;
    public int CargoAmount => cargoAmount;
    public int CargoCapacity => cargoCapacity;

    private RoadNetworkManager roadNetworkManager;
    private TrafficLightManager trafficLightManager;
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
    private readonly List<int> pendingAssignedStopIds = new();
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
    private Vector3 turnStartPosition;
    private Vector3 turnControlPosition;
    private float turnProgress;
    private float stopWaitTimer;
    private int cargoAmount;
    private bool isMoving;
    private RoadDirectionMask pendingTurnInDirection = RoadDirectionMask.None;
    private RoadDirectionMask pendingTurnOutDirection = RoadDirectionMask.None;
    private float activeTurnDistance = 0f;
    private bool hasReservedIntersection;
    private Vector3Int reservedIntersectionCell;
    private StopManager pendingStopManager;
    private bool hasPendingStopAssignment;

    private void OnDisable()
    {
        ReleaseIntersectionReservation();
    }

    private void OnDestroy()
    {
        ReleaseIntersectionReservation();
    }

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
            else
            {
                firstLegForbiddenStartExit = approachForbiddenStartExit;
            }
        }
        else
        {
            firstLegForbiddenStartExit = approachForbiddenStartExit;
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

            RoadUtility.AppendSegment(routeCells, segmentBuffer);
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

    public bool RequestAssignStops(StopManager stopManager, IReadOnlyList<int> stopIds)
    {
        if (stopManager == null || stopIds == null)
        {
            return false;
        }

        if (!isMoving || IsSafeToRebuildRouteNow())
        {
            ClearPendingStopAssignment();
            return AssignStops(stopManager, stopIds);
        }

        pendingAssignedStopIds.Clear();
        for (int i = 0; i < stopIds.Count; i++)
        {
            int stopId = stopIds[i];
            if (stopId > 0)
            {
                pendingAssignedStopIds.Add(stopId);
            }
        }

        pendingStopManager = stopManager;
        hasPendingStopAssignment = pendingAssignedStopIds.Count >= 2;
        return hasPendingStopAssignment;
    }

    public bool CanReachStop(StopManager stopManager, int stopId)
    {
        if (stopManager == null || stopId <= 0 || !EnsureContext() || roadNetworkManager == null)
        {
            return false;
        }

        if (!stopManager.TryGetStopById(stopId, out StopNode stopNode) || stopNode == null)
        {
            return false;
        }

        if (!TryResolveNearestRoadCell(stopNode.GridCell, out Vector3Int stopRoadCell))
        {
            return false;
        }

        if (!TryGetCurrentRoadCellForRouting(out Vector3Int currentCell))
        {
            return false;
        }

        if (currentCell == stopRoadCell)
        {
            return true;
        }

        RoadDirectionMask forbiddenStartExit = GetForbiddenStartExitFromCurrentHeading();
        List<Vector3Int> candidatePath = new();
        return roadNetworkManager.FindShortestPath(currentCell, stopRoadCell, candidatePath, forbiddenStartExit)
            && candidatePath.Count >= 2;
    }

    public bool UsesStop(int stopId)
    {
        if (stopId <= 0)
        {
            return false;
        }

        for (int i = 0; i < assignedStopIds.Count; i++)
        {
            if (assignedStopIds[i] == stopId)
            {
                return true;
            }
        }

        for (int i = 0; i < pendingAssignedStopIds.Count; i++)
        {
            if (pendingAssignedStopIds[i] == stopId)
            {
                return true;
            }
        }

        return false;
    }

    public bool UsesRoadCell(Vector3Int roadCell)
    {
        if (hasCurrentRoadCell && currentRoadCell == roadCell)
        {
            return true;
        }

        if (targetCellIndex >= 0 && targetCellIndex < routeCells.Count && routeCells[targetCellIndex] == roadCell)
        {
            return true;
        }

        for (int i = 0; i < routeCells.Count; i++)
        {
            if (routeCells[i] == roadCell)
            {
                return true;
            }
        }

        for (int i = 0; i < assignedStopRoadCells.Count; i++)
        {
            if (assignedStopRoadCells[i] == roadCell)
            {
                return true;
            }
        }

        return false;
    }

    public void ClearAssignedStops()
    {
        ClearPendingStopAssignment();
        ReleaseIntersectionReservation();
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
        pendingTurnInDirection = RoadDirectionMask.None;
        pendingTurnOutDirection = RoadDirectionMask.None;
        activeTurnDistance = 0f;
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
        TryApplyPendingStopAssignment();

        float dt = Time.unscaledDeltaTime * Mathf.Max(0f, Time.timeScale);

        if (EconomyManager.HasInstance && EconomyManager.Instance.IsGameOver)
        {
            ReleaseIntersectionReservation();
            return;
        }

        if (!isMoving)
        {
            ReleaseIntersectionReservation();
            return;
        }

        if (!EnsureContext())
        {
            ReleaseIntersectionReservation();
            return;
        }

        if (targetCellIndex < 0 || targetCellIndex >= routeCells.Count)
        {
            ReleaseIntersectionReservation();
            return;
        }

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

        if (isTurningInCell)
        {
            UpdateTurnMovement(dt);
            return;
        }

        if (TryStartTurnAtTileEntry(planar))
        {
            return;
        }

        if (planar.sqrMagnitude <= reachDistance * reachDistance)
        {
            CompleteStep();
            return;
        }

        if (planar.sqrMagnitude > 0.0001f)
        {
            Vector3 desiredForward = planar.normalized;

            desiredForward.y = 0f;
            if (desiredForward.sqrMagnitude > 0.0001f)
            {
                Quaternion desired = Quaternion.LookRotation(desiredForward.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, Mathf.Max(1f, turnSpeed) * dt);
            }
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * dt);
    }

    private void TryApplyPendingStopAssignment()
    {
        if (!hasPendingStopAssignment || pendingStopManager == null || pendingAssignedStopIds.Count < 2)
        {
            return;
        }

        if (isMoving && !IsSafeToRebuildRouteNow())
        {
            return;
        }

        List<int> stopIds = new(pendingAssignedStopIds);
        StopManager stopManager = pendingStopManager;
        ClearPendingStopAssignment();
        AssignStops(stopManager, stopIds);
    }

    private void ClearPendingStopAssignment()
    {
        hasPendingStopAssignment = false;
        pendingStopManager = null;
        pendingAssignedStopIds.Clear();
    }

    private bool IsSafeToRebuildRouteNow()
    {
        if (!EnsureContext() || roadNetworkManager == null || grid == null || isTurningInCell)
        {
            return false;
        }

        Vector3Int roadCell;
        if (hasCurrentRoadCell)
        {
            roadCell = currentRoadCell;
        }
        else if (!TryResolveNearestRoadCell(grid.WorldToCell(transform.position), out roadCell))
        {
            return false;
        }
        else
        {
            currentRoadCell = roadCell;
            hasCurrentRoadCell = true;
        }

        if (!roadNetworkManager.TryGetRoad(roadCell, out RoadTileData tileData))
        {
            return false;
        }

        return IsStraightRoadConnections(tileData.connections);
    }

    private static bool IsStraightRoadConnections(RoadDirectionMask connections)
    {
        RoadDirectionMask northSouth = RoadDirectionMask.North | RoadDirectionMask.South;
        RoadDirectionMask eastWest = RoadDirectionMask.East | RoadDirectionMask.West;
        return connections == northSouth || connections == eastWest;
    }

    private bool TryStartTurnAtTileEntry(Vector3 planarToCurrentTarget)
    {
        if (routeCells.Count < 3 || targetCellIndex < 0 || targetCellIndex >= routeCells.Count || roadNetworkManager == null || grid == null)
        {
            return false;
        }

        Vector3Int turnCell = routeCells[targetCellIndex];
        if (turnCell == currentRoadCell)
        {
            return false;
        }

        Vector3 currentCenter = grid.GetCellCenterWorld(currentRoadCell);
        Vector3 turnCenter = grid.GetCellCenterWorld(turnCell);
        float segmentLength = Vector3.Distance(currentCenter, turnCenter);
        if (segmentLength <= 0.001f)
        {
            return false;
        }

        float remaining = planarToCurrentTarget.magnitude;
        float progressToTurnCell = Mathf.Clamp01(1f - (remaining / segmentLength));
        if (progressToTurnCell < turnEntryProgress)
        {
            return false;
        }

        int nextIndex = AdvanceIndex(targetCellIndex);
        for (int i = 0; i < routeCells.Count; i++)
        {
            if (nextIndex < 0 || nextIndex >= routeCells.Count)
            {
                return false;
            }

            if (routeCells[nextIndex] != turnCell)
            {
                break;
            }

            nextIndex = AdvanceIndex(nextIndex);
        }

        if (nextIndex < 0 || nextIndex >= routeCells.Count || routeCells[nextIndex] == turnCell)
        {
            return false;
        }

        Vector3Int nextRoadCell = routeCells[nextIndex];
        RoadDirectionMask incomingDirection = roadNetworkManager.GetDirectionBetweenCells(currentRoadCell, turnCell);
        RoadDirectionMask outgoingDirection = roadNetworkManager.GetDirectionBetweenCells(turnCell, nextRoadCell);
        if (incomingDirection == RoadDirectionMask.None
            || outgoingDirection == RoadDirectionMask.None
            || incomingDirection == outgoingDirection)
        {
            return false;
        }

        if (IsBlockedByRedLight(turnCell, nextRoadCell))
        {
            return false;
        }

        if (!TryReserveIntersectionForNextStep(nextRoadCell))
        {
            return false;
        }

        Vector3Int previousRoadCell = currentRoadCell;
        currentRoadCell = turnCell;
        hasCurrentRoadCell = true;
        if (hasReservedIntersection
            && previousRoadCell == reservedIntersectionCell
            && currentRoadCell != reservedIntersectionCell)
        {
            ReleaseIntersectionReservation();
        }

        Vector3 turnTarget = GetLaneTargetPosition(turnCell, nextRoadCell);
        Vector3 planarDelta = turnTarget - transform.position;
        planarDelta.y = 0f;
        if (planarDelta.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        isTurningInCell = true;
        pendingTurnInDirection = incomingDirection;
        pendingTurnOutDirection = outgoingDirection;
        pendingTargetCellIndex = nextIndex;
        targetPosition = turnTarget;
        targetPosition.y = movementY;
        turnStartPosition = transform.position;
        turnStartPosition.y = movementY;
        turnControlPosition = GetTurnControlPoint(turnStartPosition, targetPosition, incomingDirection, outgoingDirection);
        turnProgress = 0f;
        activeTurnDistance = EstimateQuadraticBezierLength(turnStartPosition, turnControlPosition, targetPosition, 8);
        return true;
    }

    private void UpdateTurnMovement(float dt)
    {
        float length = Mathf.Max(0.001f, activeTurnDistance);
        turnProgress = Mathf.Clamp01(turnProgress + ((moveSpeed * dt) / length));

        float start = Mathf.Min(turnRotationStartProgress, turnRotationEndProgress - 0.01f);
        float end = Mathf.Max(turnRotationEndProgress, start + 0.01f);
        float rotationT = Mathf.InverseLerp(start, end, turnProgress);

        Vector3 blendIn = DirectionToVector(pendingTurnInDirection);
        Vector3 blendOut = DirectionToVector(pendingTurnOutDirection);
        if (blendIn.sqrMagnitude > 0.0001f && blendOut.sqrMagnitude > 0.0001f)
        {
            Vector3 blended = Vector3.Slerp(blendIn.normalized, blendOut.normalized, rotationT);
            blended.y = 0f;
            if (blended.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredBlend = Quaternion.LookRotation(blended.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredBlend, Mathf.Max(1f, turnSpeed) * dt);
            }
        }

        Vector3 newPos = EvaluateQuadraticBezier(turnStartPosition, turnControlPosition, targetPosition, turnProgress);
        newPos.y = movementY;
        transform.position = newPos;

        Vector3 tangent = EvaluateQuadraticBezierTangent(turnStartPosition, turnControlPosition, targetPosition, turnProgress);
        tangent.y = 0f;
        if (tangent.sqrMagnitude > 0.0001f)
        {
            Quaternion desired = Quaternion.LookRotation(tangent.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, Mathf.Max(1f, turnSpeed) * dt);
        }

        if (turnProgress >= 0.999f)
        {
            transform.position = targetPosition;
            CompleteStep();
        }
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
            ReleaseIntersectionReservation();
            isMoving = false;
            pendingTurnInDirection = RoadDirectionMask.None;
            pendingTurnOutDirection = RoadDirectionMask.None;
            activeTurnDistance = 0f;
            return false;
        }

        isTurningInCell = false;
        pendingTurnInDirection = RoadDirectionMask.None;
        pendingTurnOutDirection = RoadDirectionMask.None;
        activeTurnDistance = 0f;
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
            pendingTurnInDirection = RoadDirectionMask.None;
            pendingTurnOutDirection = RoadDirectionMask.None;
            activeTurnDistance = 0f;
            turnProgress = 0f;
            if (pendingTargetCellIndex < 0 || pendingTargetCellIndex >= routeCells.Count)
            {
                isMoving = false;
                pendingTargetCellIndex = -1;
                return;
            }

            targetCellIndex = pendingTargetCellIndex;
            pendingTargetCellIndex = -1;
        }

        Vector3Int previousRoadCell = currentRoadCell;
        currentRoadCell = routeCells[targetCellIndex];
        hasCurrentRoadCell = true;
        if (hasReservedIntersection
            && previousRoadCell == reservedIntersectionCell
            && currentRoadCell != reservedIntersectionCell)
        {
            ReleaseIntersectionReservation();
        }

        int nextIndex = AdvanceIndex(targetCellIndex);
        for (int i = 0; i < routeCells.Count; i++)
        {
            if (nextIndex < 0 || nextIndex >= routeCells.Count)
            {
                ReleaseIntersectionReservation();
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
            ReleaseIntersectionReservation();
            isMoving = false;
            return;
        }

        Vector3Int nextRoadCell = routeCells[nextIndex];
        if (IsBlockedByRedLight(currentRoadCell, nextRoadCell))
        {
            stopWaitTimer = Mathf.Max(stopWaitTimer, redLightRetrySeconds);
            return;
        }

        if (!TryReserveIntersectionForNextStep(nextRoadCell))
        {
            stopWaitTimer = Mathf.Max(stopWaitTimer, redLightRetrySeconds);
            return;
        }

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
            Vector3 turnTarget = GetLaneTargetPosition(currentRoadCell, nextRoadCell);
            Vector3 planarDelta = turnTarget - transform.position;
            planarDelta.y = 0f;
            if (planarDelta.sqrMagnitude > 0.0001f)
            {
                isTurningInCell = true;
                pendingTurnInDirection = incomingDirection;
                pendingTurnOutDirection = outgoingDirection;
                pendingTargetCellIndex = nextIndex;
                targetPosition = turnTarget;
                targetPosition.y = movementY;
                turnStartPosition = transform.position;
                turnStartPosition.y = movementY;
                turnControlPosition = GetTurnControlPoint(turnStartPosition, targetPosition, incomingDirection, outgoingDirection);
                turnProgress = 0f;
                activeTurnDistance = EstimateQuadraticBezierLength(turnStartPosition, turnControlPosition, targetPosition, 8);
                return;
            }
        }

        targetCellIndex = nextIndex;
        HandleScheduledStopTransfer();
        SetTargetPosition(nextRoadCell);
    }

    private void SetTargetPosition(Vector3Int targetCell)
    {
        targetPosition = GetLaneTargetPosition(currentRoadCell, targetCell);
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

    private Vector3 GetLaneTargetPosition(Vector3Int fromCell, Vector3Int toCell)
    {
        Vector3 target = grid.GetCellCenterWorld(toCell);
        Vector3 from = grid.GetCellCenterWorld(fromCell);
        Vector3 segment = target - from;
        segment.y = 0f;
        if (segment.sqrMagnitude > 0.0001f && laneOffset > 0f)
        {
            Vector3 laneRight = Vector3.Cross(Vector3.up, segment.normalized);
            target += laneRight * laneOffset;
        }

        target.y = movementY;
        return target;
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

        return RoadUtility.Opposite(headingDirection);
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

    private static Vector3 EvaluateQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
    }

    private static Vector3 EvaluateQuadraticBezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        return (2f * (1f - t) * (p1 - p0)) + (2f * t * (p2 - p1));
    }

    private static float EstimateQuadraticBezierLength(Vector3 p0, Vector3 p1, Vector3 p2, int segments)
    {
        int steps = Mathf.Max(2, segments);
        Vector3 prev = p0;
        float length = 0f;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 cur = EvaluateQuadraticBezier(p0, p1, p2, t);
            length += Vector3.Distance(prev, cur);
            prev = cur;
        }

        return Mathf.Max(0.001f, length);
    }

    private Vector3 GetTurnControlPoint(
        Vector3 startPos,
        Vector3 endPos,
        RoadDirectionMask incomingDirection,
        RoadDirectionMask outgoingDirection)
    {
        Vector3 incomingForward = DirectionToVector(incomingDirection);
        Vector3 outgoingForward = DirectionToVector(outgoingDirection);
        incomingForward.y = 0f;
        outgoingForward.y = 0f;
        if (incomingForward.sqrMagnitude < 0.0001f || outgoingForward.sqrMagnitude < 0.0001f)
        {
            return (startPos + endPos) * 0.5f;
        }

        Vector3 p1 = startPos;
        Vector3 d1 = incomingForward.normalized;
        Vector3 p2 = endPos;
        Vector3 d2 = -outgoingForward.normalized;

        float det = (d1.x * d2.z) - (d1.z * d2.x);
        if (Mathf.Abs(det) < 0.0001f)
        {
            return (startPos + endPos) * 0.5f;
        }

        Vector3 delta = p2 - p1;
        float t = ((delta.x * d2.z) - (delta.z * d2.x)) / det;
        Vector3 intersection = p1 + (d1 * t);
        intersection.y = movementY;
        return intersection;
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

        if (trafficLightManager == null)
        {
            trafficLightManager = FindFirstObjectByType<TrafficLightManager>();
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

    private bool IsBlockedByRedLight(Vector3Int fromCell, Vector3Int toCell)
    {
        return trafficLightManager != null
            && trafficLightManager.IsApproachBlockedByRedLight(fromCell, toCell);
    }

    private bool TryReserveIntersectionForNextStep(Vector3Int toCell)
    {
        if (trafficLightManager == null)
        {
            return true;
        }

        if (hasReservedIntersection && reservedIntersectionCell == toCell)
        {
            return true;
        }

        if (!trafficLightManager.TryReserveIntersection(toCell, this))
        {
            return false;
        }

        hasReservedIntersection = true;
        reservedIntersectionCell = toCell;
        return true;
    }

    private void ReleaseIntersectionReservation()
    {
        if (!hasReservedIntersection)
        {
            return;
        }

        if (trafficLightManager != null)
        {
            trafficLightManager.ReleaseIntersection(reservedIntersectionCell, this);
        }

        hasReservedIntersection = false;
        reservedIntersectionCell = default;
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
            return;
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

}
