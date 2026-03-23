using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RouteData
{
    public int routeId;
    public string routeName;
    public List<int> stopIds = new();
    public List<Vector3Int> pathCells = new();
    public List<Vector3> waypoints = new();
    public List<Quaternion> waypointRotations = new();
}

public class RouteManager : MonoBehaviour
{
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private placementSystem placementSystemToDisable;
    [SerializeField] private Grid grid;
    [SerializeField] private bool addSelectedStopsAutomatically = true;
    [SerializeField] private bool stopStopPlacementWhenDrafting = true;
    [SerializeField] private float waypointY = 0.02f;
    [SerializeField] private bool drawDebugPaths = true;
    [SerializeField] private Color draftPathColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color savedRouteColor = new Color(0.2f, 1f, 1f, 1f);

    private readonly List<RouteData> routes = new();
    private readonly List<int> draftStopIds = new();
    private readonly List<Vector3Int> draftPathCells = new();
    private int nextRouteId = 1;

    public bool IsDraftingRoute { get; private set; }
    public IReadOnlyList<RouteData> Routes => routes;
    public IReadOnlyList<int> DraftStopIds => draftStopIds;
    public IReadOnlyList<Vector3Int> DraftPathCells => draftPathCells;

    public event Action DraftChanged;
    public event Action<RouteData> RouteCreated;

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

        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<CameraController>();
        }

        if (grid == null)
        {
            grid = FindFirstObjectByType<Grid>();
        }
    }

    private void OnEnable()
    {
        if (cameraController != null)
        {
            cameraController.SelectionChanged += HandleSelectionChanged;
        }

        if (stopManager != null)
        {
            stopManager.StopsChanged += HandleStopsChanged;
        }
    }

    private void OnDisable()
    {
        if (cameraController != null)
        {
            cameraController.SelectionChanged -= HandleSelectionChanged;
        }

        if (stopManager != null)
        {
            stopManager.StopsChanged -= HandleStopsChanged;
        }
    }

    public void ToggleRouteDrafting()
    {
        if (IsDraftingRoute)
        {
            CancelRouteDraft();
            return;
        }

        BeginRouteDraft();
    }

    public void BeginRouteDraft()
    {
        IsDraftingRoute = true;
        draftStopIds.Clear();
        draftPathCells.Clear();

        if (placementSystemToDisable != null)
        {
            placementSystemToDisable.StopPlacement();
        }

        if (stopStopPlacementWhenDrafting && stopManager != null)
        {
            stopManager.EndStopPlacement();
        }

        NotifyDraftChanged();
    }

    public void CancelRouteDraft()
    {
        IsDraftingRoute = false;
        draftStopIds.Clear();
        draftPathCells.Clear();
        NotifyDraftChanged();
    }

    public bool AddSelectedStopToDraft()
    {
        if (cameraController == null)
        {
            return false;
        }

        return AddStopFromObjectToDraft(cameraController.SelectedObject);
    }

    public void AddSelectedStopToDraftFromUI()
    {
        AddSelectedStopToDraft();
    }

    public bool AddStopByIdToDraft(int stopId)
    {
        if (!IsDraftingRoute || stopManager == null)
        {
            return false;
        }

        if (!stopManager.TryGetStopById(stopId, out _))
        {
            return false;
        }

        if (draftStopIds.Count > 0 && draftStopIds[draftStopIds.Count - 1] == stopId)
        {
            return false;
        }

        draftStopIds.Add(stopId);
        RebuildDraftPath();
        NotifyDraftChanged();
        return true;
    }

    public bool RemoveLastStopFromDraft()
    {
        if (draftStopIds.Count == 0)
        {
            return false;
        }

        draftStopIds.RemoveAt(draftStopIds.Count - 1);
        RebuildDraftPath();
        NotifyDraftChanged();
        return true;
    }

    public void RemoveLastStopFromDraftFromUI()
    {
        RemoveLastStopFromDraft();
    }

    public bool FinalizeDraftRoute()
    {
        return FinalizeDraftRouteInternal(string.Empty);
    }

    public void FinalizeDraftRouteFromUI()
    {
        FinalizeDraftRoute();
    }

    public bool FinalizeDraftRouteWithName(string routeName)
    {
        return FinalizeDraftRouteInternal(routeName);
    }

    public bool TryGetRouteById(int routeId, out RouteData route)
    {
        for (int i = 0; i < routes.Count; i++)
        {
            if (routes[i].routeId == routeId)
            {
                route = routes[i];
                return true;
            }
        }

        route = null;
        return false;
    }

    private bool FinalizeDraftRouteInternal(string routeName)
    {
        if (!IsDraftingRoute || draftStopIds.Count < 2 || roadNetworkManager == null || stopManager == null || grid == null)
        {
            return false;
        }

        List<Vector3Int> fullPath = new();
        List<Vector3Int> segment = new();

        for (int i = 0; i < draftStopIds.Count - 1; i++)
        {
            if (!stopManager.TryGetStopById(draftStopIds[i], out StopNode fromStop)
                || !stopManager.TryGetStopById(draftStopIds[i + 1], out StopNode toStop))
            {
                return false;
            }

            if (!TryResolveStopRoadCell(fromStop, out Vector3Int fromRoadCell)
                || !TryResolveStopRoadCell(toStop, out Vector3Int toRoadCell))
            {
                return false;
            }

            RoadDirectionMask forbiddenStartExit = RoadDirectionMask.None;
            if (fullPath.Count >= 2 && fullPath[fullPath.Count - 1] == fromRoadCell)
            {
                Vector3Int previousCell = fullPath[fullPath.Count - 2];
                forbiddenStartExit = roadNetworkManager.GetDirectionBetweenCells(fromRoadCell, previousCell);
            }

            if (!roadNetworkManager.FindShortestPath(fromRoadCell, toRoadCell, segment, forbiddenStartExit))
            {
                return false;
            }

            AppendSegment(fullPath, segment);
        }

        RouteData route = BuildRouteData(routeName, fullPath);
        routes.Add(route);
        RouteCreated?.Invoke(route);
        CancelRouteDraft();
        return true;
    }

    private RouteData BuildRouteData(string routeName, List<Vector3Int> fullPath)
    {
        int routeId = nextRouteId++;
        string finalRouteName = string.IsNullOrWhiteSpace(routeName) ? $"Route {routeId}" : routeName.Trim();

        RouteData route = new RouteData
        {
            routeId = routeId,
            routeName = finalRouteName,
            stopIds = new List<int>(draftStopIds),
            pathCells = new List<Vector3Int>(fullPath)
        };

        route.waypoints = BuildWaypoints(route.pathCells);
        route.waypointRotations = BuildWaypointRotations(route.waypoints);
        return route;
    }

    private List<Vector3> BuildWaypoints(List<Vector3Int> pathCells)
    {
        List<Vector3> waypoints = new(pathCells.Count);
        for (int i = 0; i < pathCells.Count; i++)
        {
            Vector3 point = grid.GetCellCenterWorld(pathCells[i]);
            point.y = waypointY;
            waypoints.Add(point);
        }

        return waypoints;
    }

    private static List<Quaternion> BuildWaypointRotations(List<Vector3> waypoints)
    {
        List<Quaternion> rotations = new(waypoints.Count);
        for (int i = 0; i < waypoints.Count; i++)
        {
            Vector3 forward;
            if (i < waypoints.Count - 1)
            {
                forward = waypoints[i + 1] - waypoints[i];
            }
            else if (i > 0)
            {
                forward = waypoints[i] - waypoints[i - 1];
            }
            else
            {
                forward = Vector3.forward;
            }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            rotations.Add(Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        return rotations;
    }

    private void HandleSelectionChanged(GameObject selectedObject)
    {
        if (!IsDraftingRoute || !addSelectedStopsAutomatically)
        {
            return;
        }

        AddStopFromObjectToDraft(selectedObject);
    }

    private bool AddStopFromObjectToDraft(GameObject selectedObject)
    {
        if (stopManager == null || selectedObject == null)
        {
            return false;
        }

        if (!stopManager.TryGetStopFromObject(selectedObject, out StopNode stopNode))
        {
            return false;
        }

        return AddStopByIdToDraft(stopNode.StopId);
    }

    private void RebuildDraftPath()
    {
        draftPathCells.Clear();
        if (draftStopIds.Count < 2 || roadNetworkManager == null || stopManager == null)
        {
            return;
        }

        List<Vector3Int> segment = new();

        for (int i = 0; i < draftStopIds.Count - 1; i++)
        {
            if (!stopManager.TryGetStopById(draftStopIds[i], out StopNode fromStop)
                || !stopManager.TryGetStopById(draftStopIds[i + 1], out StopNode toStop))
            {
                draftPathCells.Clear();
                return;
            }

            if (!TryResolveStopRoadCell(fromStop, out Vector3Int fromRoadCell)
                || !TryResolveStopRoadCell(toStop, out Vector3Int toRoadCell))
            {
                draftPathCells.Clear();
                return;
            }

            RoadDirectionMask forbiddenStartExit = RoadDirectionMask.None;
            if (draftPathCells.Count >= 2 && draftPathCells[draftPathCells.Count - 1] == fromRoadCell)
            {
                Vector3Int previousCell = draftPathCells[draftPathCells.Count - 2];
                forbiddenStartExit = roadNetworkManager.GetDirectionBetweenCells(fromRoadCell, previousCell);
            }

            if (!roadNetworkManager.FindShortestPath(fromRoadCell, toRoadCell, segment, forbiddenStartExit))
            {
                draftPathCells.Clear();
                return;
            }

            AppendSegment(draftPathCells, segment);
        }
    }

    private static void AppendSegment(List<Vector3Int> fullPath, List<Vector3Int> segment)
    {
        if (segment.Count == 0)
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

    private void NotifyDraftChanged()
    {
        DraftChanged?.Invoke();
    }

    private void HandleStopsChanged()
    {
        if (!IsDraftingRoute)
        {
            return;
        }

        RebuildDraftPath();
        NotifyDraftChanged();
    }

    private bool TryResolveStopRoadCell(StopNode stopNode, out Vector3Int roadCell)
    {
        roadCell = default;
        if (stopNode == null || roadNetworkManager == null)
        {
            return false;
        }

        return roadNetworkManager.TryResolveNearestRoadCell(stopNode.GridCell, out roadCell);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugPaths || grid == null)
        {
            return;
        }

        DrawPath(draftPathCells, draftPathColor);

        for (int i = 0; i < routes.Count; i++)
        {
            DrawPath(routes[i].pathCells, savedRouteColor);
        }
    }

    private void DrawPath(List<Vector3Int> cells, Color color)
    {
        if (cells == null || cells.Count < 2)
        {
            return;
        }

        Gizmos.color = color;
        for (int i = 0; i < cells.Count - 1; i++)
        {
            Vector3 a = grid.GetCellCenterWorld(cells[i]);
            Vector3 b = grid.GetCellCenterWorld(cells[i + 1]);
            a.y = waypointY;
            b.y = waypointY;
            Gizmos.DrawLine(a, b);
        }
    }
}
