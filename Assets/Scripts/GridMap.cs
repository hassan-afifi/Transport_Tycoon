using System.Collections.Generic;
using UnityEngine;

public class GridMap : MonoBehaviour
{
    public static GridMap Instance { get; private set; }
    public static bool HasInstance => Instance != null;
    [SerializeField] private Grid grid;
    [SerializeField, Min(1)] private int nearestRoadResolveRadius = 6;

    private readonly Dictionary<Vector3Int, RoadTileData> roadsByCell = new();
    private readonly Dictionary<int, StopNode> stopsById = new();
    private readonly Dictionary<Vector3Int, StopNode> stopsByCell = new();
    private readonly Dictionary<BuildingEconomy, HashSet<Vector3Int>> cellsByBuilding = new();
    private readonly Dictionary<Vector3Int, HashSet<BuildingEconomy>> buildingsByCell = new();
    private readonly Vector3Int[] cardinalOffsets = new Vector3Int[4];

    private int eastAxisIndex = 0;
    private int northAxisIndex = 2;
    private int verticalAxisIndex = 1;

    public static GridMap EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GridMap existing = FindFirstObjectByType<GridMap>();
        if (existing != null)
        {
            return existing;
        }

        GameObject root = new("GridMap");
        return root.AddComponent<GridMap>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (grid == null)
        {
            grid = FindFirstObjectByType<Grid>();
        }

        ConfigureAxes();
    }

    private void OnValidate()
    {
        ConfigureAxes();
    }

    private void Start()
    {
        RebuildAllBuildingsFromScene();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void RegisterRoadCell(Vector3Int cell, RoadTileData tileData)
    {
        roadsByCell[NormalizeCell(cell)] = tileData;
    }

    public bool UnregisterRoadCell(Vector3Int cell)
    {
        return roadsByCell.Remove(NormalizeCell(cell));
    }

    public void ClearRoads()
    {
        roadsByCell.Clear();
    }

    public bool HasRoadAt(Vector3Int cell)
    {
        return roadsByCell.ContainsKey(NormalizeCell(cell));
    }

    public bool TryGetRoad(Vector3Int cell, out RoadTileData tileData)
    {
        return roadsByCell.TryGetValue(NormalizeCell(cell), out tileData);
    }

    public bool TryResolveNearestRoadCell(Vector3Int sourceCell, out Vector3Int roadCell)
    {
        sourceCell = NormalizeCell(sourceCell);
        roadCell = sourceCell;

        if (roadsByCell.ContainsKey(sourceCell))
        {
            return true;
        }

        int maxRadius = Mathf.Max(1, nearestRoadResolveRadius);
        int bestDistance = int.MaxValue;
        bool found = false;

        int sourceEast = GetAxis(sourceCell, eastAxisIndex);
        int sourceNorth = GetAxis(sourceCell, northAxisIndex);

        for (int east = -maxRadius; east <= maxRadius; east++)
        {
            for (int north = -maxRadius; north <= maxRadius; north++)
            {
                int manhattan = Mathf.Abs(east) + Mathf.Abs(north);
                if (manhattan == 0 || manhattan > maxRadius || manhattan >= bestDistance)
                {
                    continue;
                }

                Vector3Int candidate = MakeCell(sourceEast + east, sourceNorth + north);
                if (!roadsByCell.ContainsKey(candidate))
                {
                    continue;
                }

                roadCell = candidate;
                bestDistance = manhattan;
                found = true;
            }
        }

        return found;
    }

    public void RegisterStop(StopNode stopNode)
    {
        if (stopNode == null || stopNode.StopId <= 0)
        {
            return;
        }

        stopsById[stopNode.StopId] = stopNode;
        stopsByCell[NormalizeCell(stopNode.GridCell)] = stopNode;
    }

    public void UnregisterStop(StopNode stopNode)
    {
        if (stopNode == null)
        {
            return;
        }

        if (stopNode.StopId > 0)
        {
            stopsById.Remove(stopNode.StopId);
        }

        stopsByCell.Remove(NormalizeCell(stopNode.GridCell));
    }

    public bool TryGetStopAtCell(Vector3Int cell, out StopNode stopNode)
    {
        return stopsByCell.TryGetValue(NormalizeCell(cell), out stopNode);
    }

    public void RegisterOrUpdateBuilding(BuildingEconomy building)
    {
        if (building == null)
        {
            return;
        }

        UnregisterBuilding(building);

        HashSet<Vector3Int> occupiedCells = new();
        ComputeBuildingCells(building, occupiedCells);
        if (occupiedCells.Count == 0)
        {
            occupiedCells.Add(GetGridCell(building.transform.position));
        }

        cellsByBuilding[building] = occupiedCells;
        foreach (Vector3Int cell in occupiedCells)
        {
            Vector3Int key = NormalizeCell(cell);
            if (!buildingsByCell.TryGetValue(key, out HashSet<BuildingEconomy> buildings))
            {
                buildings = new HashSet<BuildingEconomy>();
                buildingsByCell[key] = buildings;
            }

            buildings.Add(building);
        }
    }

    public void RebuildAllBuildingsFromScene()
    {
        cellsByBuilding.Clear();
        buildingsByCell.Clear();

        BuildingEconomy[] buildings = FindObjectsByType<BuildingEconomy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < buildings.Length; i++)
        {
            BuildingEconomy building = buildings[i];
            if (building == null || !building.isActiveAndEnabled)
            {
                continue;
            }

            RegisterOrUpdateBuilding(building);
        }
    }

    public void UnregisterBuilding(BuildingEconomy building)
    {
        if (building == null || !cellsByBuilding.TryGetValue(building, out HashSet<Vector3Int> occupiedCells))
        {
            return;
        }

        foreach (Vector3Int cell in occupiedCells)
        {
            Vector3Int key = NormalizeCell(cell);
            if (!buildingsByCell.TryGetValue(key, out HashSet<BuildingEconomy> buildings))
            {
                continue;
            }

            buildings.Remove(building);
            if (buildings.Count == 0)
            {
                buildingsByCell.Remove(key);
            }
        }

        cellsByBuilding.Remove(building);
    }

    public void GetBuildingsAtOrAdjacentCardinal(Vector3Int centerCell, List<BuildingEconomy> results)
    {
        results.Clear();
        HashSet<BuildingEconomy> unique = new();
        Vector3Int center = NormalizeCell(centerCell);

        AddBuildingsAtCell(center, unique, results);
        for (int i = 0; i < cardinalOffsets.Length; i++)
        {
            AddBuildingsAtCell(center + cardinalOffsets[i], unique, results);
        }
    }

    private void ComputeBuildingCells(BuildingEconomy building, HashSet<Vector3Int> cellsOut)
    {
        if (building == null || cellsOut == null)
        {
            return;
        }

        BuildingTileOccupancy manualOccupancy = building.GetComponent<BuildingTileOccupancy>();
        if (manualOccupancy != null && manualOccupancy.TryGetOccupiedCells(grid, cellsOut))
        {
            return;
        }

        Collider[] colliders = building.GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                AddBoundsFootprint(collider.bounds, cellsOut);
            }

            return;
        }

        Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            AddBoundsFootprint(renderer.bounds, cellsOut);
        }
    }

    private void AddBoundsFootprint(Bounds bounds, HashSet<Vector3Int> cellsOut)
    {
        Vector3 center = bounds.center;
        Vector3 p0 = new(bounds.min.x, center.y, bounds.min.z);
        Vector3 p1 = new(bounds.min.x, center.y, bounds.max.z);
        Vector3 p2 = new(bounds.max.x, center.y, bounds.min.z);
        Vector3 p3 = new(bounds.max.x, center.y, bounds.max.z);

        Vector3Int c0 = GetGridCell(p0);
        Vector3Int c1 = GetGridCell(p1);
        Vector3Int c2 = GetGridCell(p2);
        Vector3Int c3 = GetGridCell(p3);

        int minEast = Mathf.Min(GetAxis(c0, eastAxisIndex), GetAxis(c1, eastAxisIndex), GetAxis(c2, eastAxisIndex), GetAxis(c3, eastAxisIndex));
        int maxEast = Mathf.Max(GetAxis(c0, eastAxisIndex), GetAxis(c1, eastAxisIndex), GetAxis(c2, eastAxisIndex), GetAxis(c3, eastAxisIndex));
        int minNorth = Mathf.Min(GetAxis(c0, northAxisIndex), GetAxis(c1, northAxisIndex), GetAxis(c2, northAxisIndex), GetAxis(c3, northAxisIndex));
        int maxNorth = Mathf.Max(GetAxis(c0, northAxisIndex), GetAxis(c1, northAxisIndex), GetAxis(c2, northAxisIndex), GetAxis(c3, northAxisIndex));

        for (int east = minEast; east <= maxEast; east++)
        {
            for (int north = minNorth; north <= maxNorth; north++)
            {
                cellsOut.Add(MakeCell(east, north));
            }
        }
    }

    private void AddBuildingsAtCell(Vector3Int cell, HashSet<BuildingEconomy> unique, List<BuildingEconomy> results)
    {
        if (!buildingsByCell.TryGetValue(NormalizeCell(cell), out HashSet<BuildingEconomy> buildings))
        {
            return;
        }

        foreach (BuildingEconomy building in buildings)
        {
            if (building != null && unique.Add(building))
            {
                results.Add(building);
            }
        }
    }

    private Vector3Int GetGridCell(Vector3 worldPosition)
    {
        if (grid != null)
        {
            return NormalizeCell(grid.WorldToCell(worldPosition));
        }

        return NormalizeCell(Vector3Int.RoundToInt(worldPosition));
    }

    private void ConfigureAxes()
    {
        if (grid == null)
        {
            Grid foundGrid = FindFirstObjectByType<Grid>();
            if (foundGrid == null)
            {
                return;
            }

            grid = foundGrid;
        }

        switch (grid.cellSwizzle)
        {
            case GridLayout.CellSwizzle.XYZ:
                eastAxisIndex = 0;
                northAxisIndex = 2;
                verticalAxisIndex = 1;
                break;
            case GridLayout.CellSwizzle.XZY:
                eastAxisIndex = 0;
                northAxisIndex = 1;
                verticalAxisIndex = 2;
                break;
            case GridLayout.CellSwizzle.YXZ:
                eastAxisIndex = 1;
                northAxisIndex = 2;
                verticalAxisIndex = 0;
                break;
            case GridLayout.CellSwizzle.YZX:
                eastAxisIndex = 1;
                northAxisIndex = 0;
                verticalAxisIndex = 2;
                break;
            case GridLayout.CellSwizzle.ZXY:
                eastAxisIndex = 2;
                northAxisIndex = 1;
                verticalAxisIndex = 0;
                break;
            case GridLayout.CellSwizzle.ZYX:
                eastAxisIndex = 2;
                northAxisIndex = 0;
                verticalAxisIndex = 1;
                break;
            default:
                eastAxisIndex = 0;
                northAxisIndex = 2;
                verticalAxisIndex = 1;
                break;
        }

        cardinalOffsets[0] = GridAxisUtility.UnitOnAxis(eastAxisIndex, 1);
        cardinalOffsets[1] = GridAxisUtility.UnitOnAxis(eastAxisIndex, -1);
        cardinalOffsets[2] = GridAxisUtility.UnitOnAxis(northAxisIndex, 1);
        cardinalOffsets[3] = GridAxisUtility.UnitOnAxis(northAxisIndex, -1);
    }

    private Vector3Int NormalizeCell(Vector3Int cell)
    {
        cell = SetAxis(cell, verticalAxisIndex, 0);
        return cell;
    }

    private Vector3Int MakeCell(int east, int north)
    {
        Vector3Int cell = Vector3Int.zero;
        cell = SetAxis(cell, eastAxisIndex, east);
        cell = SetAxis(cell, northAxisIndex, north);
        cell = SetAxis(cell, verticalAxisIndex, 0);
        return cell;
    }

    private static int GetAxis(Vector3Int cell, int axisIndex)
    {
        switch (axisIndex)
        {
            case 0:
                return cell.x;
            case 1:
                return cell.y;
            default:
                return cell.z;
        }
    }

    private static Vector3Int SetAxis(Vector3Int cell, int axisIndex, int value)
    {
        switch (axisIndex)
        {
            case 0:
                cell.x = value;
                break;
            case 1:
                cell.y = value;
                break;
            default:
                cell.z = value;
                break;
        }

        return cell;
    }

}
