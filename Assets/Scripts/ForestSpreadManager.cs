using System.Collections.Generic;
using UnityEngine;

public class ForestSpreadManager : MonoBehaviour
{
    [SerializeField] private Grid grid;
    [SerializeField] private GridMap gridMap;
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private Transform treesParent;
    [SerializeField] private Collider spreadBounds;
    [SerializeField] private GameObject cubeTreeSmallPrefab;
    [SerializeField] private GameObject cubeTreeBigPrefab;
    [SerializeField] private GameObject firTreeSmallPrefab;
    [SerializeField] private GameObject firTreeBigPrefab;
    [SerializeField, Min(0.1f)] private float spreadIntervalSeconds = 20f;
    [SerializeField, Min(0.1f)] private float growthToBigSeconds = 45f;
    [SerializeField] private float treeY = 0f;
    [SerializeField, Min(0)] private int clearRoadCostSmallTree = 250;
    [SerializeField, Min(0)] private int clearRoadCostBigTree = 500;
    [SerializeField] private bool randomizeTreeYaw = true;

    private readonly HashSet<Vector3Int> spreadSourceCells = new();
    private readonly HashSet<Vector3Int> protectedForestCells = new();
    private readonly Dictionary<Vector3Int, InfectedTreeState> infectedTrees = new();
    private readonly List<Vector3Int> sourceCellsBuffer = new();
    private readonly List<Vector3Int> neighborCellsBuffer = new();
    private readonly List<Vector3Int> occupiedCellsBuffer = new();
    private readonly List<Vector3Int> growthCellsBuffer = new();
    private readonly List<Vector3Int> cellsToRemoveBuffer = new();
    private readonly List<BuildingEconomy> buildingsAtCellBuffer = new();
    private readonly Vector3Int[] cardinalOffsets = new Vector3Int[4];

    private float spreadTimer;
    private int eastAxisIndex = 0;
    private int northAxisIndex = 2;
    private int verticalAxisIndex = 1;

    private enum TreeType
    {
        Cube,
        Fir
    }

    private sealed class InfectedTreeState
    {
        public TreeType Type;
        public bool IsBig;
        public float TimeSinceInfection;
        public GameObject Instance;
    }

    private void Awake()
    {
        if (grid == null)
        {
            grid = FindFirstObjectByType<Grid>();
        }

        if (gridMap == null)
        {
            gridMap = GridMap.EnsureInstance();
        }

        if (roadNetworkManager == null)
        {
            roadNetworkManager = FindFirstObjectByType<RoadNetworkManager>();
        }

        ConfigureAxes();
    }

    private void OnValidate()
    {
        ConfigureAxes();
    }

    private void Start()
    {
        RebuildForestSources();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        if (spreadSourceCells.Count == 0)
        {
            RebuildForestSources();
            if (spreadSourceCells.Count == 0)
            {
                return;
            }
        }

        UpdateTreeGrowth(dt);

        if (spreadIntervalSeconds <= 0f)
        {
            return;
        }

        spreadTimer += dt;
        while (spreadTimer >= spreadIntervalSeconds)
        {
            spreadTimer -= spreadIntervalSeconds;
            TrySpreadOneCell();
        }
    }

    [ContextMenu("Rebuild Forest Sources")]
    public void RebuildForestSources()
    {
        if (gridMap == null)
        {
            gridMap = GridMap.EnsureInstance();
        }

        if (gridMap == null)
        {
            return;
        }

        gridMap.RebuildAllBuildingsFromScene();
        protectedForestCells.Clear();
        spreadSourceCells.Clear();

        BuildingEconomy[] buildings = FindObjectsByType<BuildingEconomy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < buildings.Length; i++)
        {
            BuildingEconomy building = buildings[i];
            if (building == null || building.BuildingType != BuildingType.Forest)
            {
                continue;
            }

            occupiedCellsBuffer.Clear();
            if (!TryGetForestBaseCells(building, occupiedCellsBuffer))
            {
                continue;
            }

            for (int cellIndex = 0; cellIndex < occupiedCellsBuffer.Count; cellIndex++)
            {
                Vector3Int cell = NormalizeCell(occupiedCellsBuffer[cellIndex]);
                protectedForestCells.Add(cell);
                spreadSourceCells.Add(cell);
            }
        }

        cellsToRemoveBuffer.Clear();
        foreach (KeyValuePair<Vector3Int, InfectedTreeState> pair in infectedTrees)
        {
            Vector3Int cell = pair.Key;
            if (protectedForestCells.Contains(cell))
            {
                cellsToRemoveBuffer.Add(cell);
                continue;
            }

            spreadSourceCells.Add(cell);
        }

        for (int i = 0; i < cellsToRemoveBuffer.Count; i++)
        {
            ClearInfectedTreeAtCell(cellsToRemoveBuffer[i]);
        }
    }

    public bool IsProtectedForestCell(Vector3Int cell)
    {
        return protectedForestCells.Contains(NormalizeCell(cell));
    }

    public bool IsInfectedCell(Vector3Int cell)
    {
        return infectedTrees.ContainsKey(NormalizeCell(cell));
    }

    public int GetRoadClearCostAtCell(Vector3Int cell)
    {
        if (!infectedTrees.TryGetValue(NormalizeCell(cell), out InfectedTreeState state))
        {
            return 0;
        }

        return state.IsBig ? clearRoadCostBigTree : clearRoadCostSmallTree;
    }

    public int GetRoadClearCostForFootprint(Vector3Int rootCell, Vector2Int footprintSize)
    {
        int totalCost = 0;
        ForEachFootprintCell(rootCell, footprintSize, cell => totalCost += GetRoadClearCostAtCell(cell));
        return totalCost;
    }

    public void ClearInfectedTreesInFootprint(Vector3Int rootCell, Vector2Int footprintSize)
    {
        ForEachFootprintCell(rootCell, footprintSize, ClearInfectedTreeAtCell);
    }

    private void UpdateTreeGrowth(float dt)
    {
        if (infectedTrees.Count == 0)
        {
            return;
        }

        growthCellsBuffer.Clear();
        foreach (KeyValuePair<Vector3Int, InfectedTreeState> pair in infectedTrees)
        {
            InfectedTreeState state = pair.Value;
            if (state == null || state.IsBig)
            {
                continue;
            }

            state.TimeSinceInfection += dt;
            if (state.TimeSinceInfection >= growthToBigSeconds)
            {
                growthCellsBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < growthCellsBuffer.Count; i++)
        {
            GrowTree(growthCellsBuffer[i]);
        }
    }

    private void TrySpreadOneCell()
    {
        if (spreadSourceCells.Count == 0)
        {
            return;
        }

        sourceCellsBuffer.Clear();
        foreach (Vector3Int cell in spreadSourceCells)
        {
            sourceCellsBuffer.Add(cell);
        }

        while (sourceCellsBuffer.Count > 0)
        {
            int sourceIndex = Random.Range(0, sourceCellsBuffer.Count);
            Vector3Int sourceCell = sourceCellsBuffer[sourceIndex];
            int lastIndex = sourceCellsBuffer.Count - 1;
            sourceCellsBuffer[sourceIndex] = sourceCellsBuffer[lastIndex];
            sourceCellsBuffer.RemoveAt(lastIndex);

            CollectInfectableNeighbors(sourceCell, neighborCellsBuffer);
            if (neighborCellsBuffer.Count == 0)
            {
                continue;
            }

            Vector3Int targetCell = neighborCellsBuffer[Random.Range(0, neighborCellsBuffer.Count)];
            InfectCell(targetCell);
            return;
        }
    }

    private void CollectInfectableNeighbors(Vector3Int sourceCell, List<Vector3Int> neighborsOut)
    {
        neighborsOut.Clear();
        for (int i = 0; i < cardinalOffsets.Length; i++)
        {
            Vector3Int candidate = NormalizeCell(sourceCell + cardinalOffsets[i]);
            if (CanInfectCell(candidate))
            {
                neighborsOut.Add(candidate);
            }
        }
    }

    private bool CanInfectCell(Vector3Int cell)
    {
        if (protectedForestCells.Contains(cell))
        {
            return false;
        }

        if (infectedTrees.ContainsKey(cell))
        {
            return false;
        }

        if (!IsCellFreeOfOtherFacilities(cell))
        {
            return false;
        }

        bool hasRoad = roadNetworkManager != null ? roadNetworkManager.HasRoadAt(cell) : gridMap != null && gridMap.HasRoadAt(cell);
        if (hasRoad)
        {
            return false;
        }

        return IsInsideSpreadBounds(cell);
    }

    private bool IsCellFreeOfOtherFacilities(Vector3Int cell)
    {
        if (gridMap == null)
        {
            return true;
        }

        gridMap.GetBuildingsAtCell(cell, buildingsAtCellBuffer);
        for (int i = 0; i < buildingsAtCellBuffer.Count; i++)
        {
            BuildingEconomy building = buildingsAtCellBuffer[i];
            if (building == null)
            {
                continue;
            }

            if (building.BuildingType != BuildingType.Forest)
            {
                return false;
            }

            if (protectedForestCells.Contains(cell))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsInsideSpreadBounds(Vector3Int cell)
    {
        if (spreadBounds == null)
        {
            return true;
        }

        Vector3 worldPosition = GetCellCenterWorld(cell);
        Bounds bounds = spreadBounds.bounds;
        return worldPosition.x >= bounds.min.x
            && worldPosition.x <= bounds.max.x
            && worldPosition.z >= bounds.min.z
            && worldPosition.z <= bounds.max.z;
    }

    private void InfectCell(Vector3Int cell)
    {
        TreeType treeType = Random.value < 0.5f ? TreeType.Cube : TreeType.Fir;
        GameObject smallPrefab = GetSmallPrefab(treeType);
        if (smallPrefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(
            smallPrefab,
            GetSpawnWorldPosition(cell),
            GetSpawnRotation(),
            ResolveTreeParent());

        PreviewVisualUtility.DisableColliders(instance);

        infectedTrees[cell] = new InfectedTreeState
        {
            Type = treeType,
            IsBig = false,
            TimeSinceInfection = 0f,
            Instance = instance
        };

        spreadSourceCells.Add(cell);
    }

    private void GrowTree(Vector3Int cell)
    {
        if (!infectedTrees.TryGetValue(cell, out InfectedTreeState state) || state == null || state.IsBig)
        {
            return;
        }

        GameObject bigPrefab = GetBigPrefab(state.Type);
        if (bigPrefab == null)
        {
            state.IsBig = true;
            return;
        }

        Vector3 worldPosition = state.Instance != null ? state.Instance.transform.position : GetSpawnWorldPosition(cell);
        Quaternion worldRotation = state.Instance != null ? state.Instance.transform.rotation : GetSpawnRotation();
        DestroySafely(state.Instance);

        GameObject instance = Instantiate(bigPrefab, worldPosition, worldRotation, ResolveTreeParent());
        PreviewVisualUtility.DisableColliders(instance);
        state.Instance = instance;
        state.IsBig = true;
    }

    private void ClearInfectedTreeAtCell(Vector3Int cell)
    {
        cell = NormalizeCell(cell);
        if (!infectedTrees.TryGetValue(cell, out InfectedTreeState state))
        {
            return;
        }

        DestroySafely(state?.Instance);
        infectedTrees.Remove(cell);
        spreadSourceCells.Remove(cell);
    }

    private GameObject GetSmallPrefab(TreeType treeType)
    {
        switch (treeType)
        {
            case TreeType.Cube:
                return cubeTreeSmallPrefab != null ? cubeTreeSmallPrefab : firTreeSmallPrefab;
            case TreeType.Fir:
                return firTreeSmallPrefab != null ? firTreeSmallPrefab : cubeTreeSmallPrefab;
            default:
                return null;
        }
    }

    private GameObject GetBigPrefab(TreeType treeType)
    {
        switch (treeType)
        {
            case TreeType.Cube:
                return cubeTreeBigPrefab != null ? cubeTreeBigPrefab : firTreeBigPrefab;
            case TreeType.Fir:
                return firTreeBigPrefab != null ? firTreeBigPrefab : cubeTreeBigPrefab;
            default:
                return null;
        }
    }

    private Transform ResolveTreeParent()
    {
        if (treesParent != null && treesParent.gameObject.scene.IsValid() && treesParent.gameObject.scene.isLoaded)
        {
            return treesParent;
        }

        return transform;
    }

    private Vector3 GetSpawnWorldPosition(Vector3Int cell)
    {
        Vector3 worldPosition = GetCellCenterWorld(cell);
        worldPosition.y = treeY;
        return worldPosition;
    }

    private Vector3 GetCellCenterWorld(Vector3Int cell)
    {
        if (grid != null)
        {
            return grid.GetCellCenterWorld(cell);
        }

        return new Vector3(cell.x, 0f, cell.z);
    }

    private Quaternion GetSpawnRotation()
    {
        return randomizeTreeYaw
            ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
            : Quaternion.identity;
    }

    private void ConfigureAxes()
    {
        if (grid == null)
        {
            return;
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

        cardinalOffsets[0] = RoadUtility.UnitOnAxis(eastAxisIndex, 1);
        cardinalOffsets[1] = RoadUtility.UnitOnAxis(eastAxisIndex, -1);
        cardinalOffsets[2] = RoadUtility.UnitOnAxis(northAxisIndex, 1);
        cardinalOffsets[3] = RoadUtility.UnitOnAxis(northAxisIndex, -1);
    }

    private Vector3Int NormalizeCell(Vector3Int cell)
    {
        cell = SetAxis(cell, verticalAxisIndex, 0);
        return cell;
    }

    private bool TryGetForestBaseCells(BuildingEconomy building, List<Vector3Int> cellsOut)
    {
        if (building == null || cellsOut == null)
        {
            return false;
        }

        cellsOut.Clear();

        BuildingTileOccupancy manual = building.GetComponent<BuildingTileOccupancy>();
        if (manual != null)
        {
            HashSet<Vector3Int> manualCells = new();
            if (manual.TryGetOccupiedCells(grid, manualCells))
            {
                foreach (Vector3Int cell in manualCells)
                {
                    cellsOut.Add(NormalizeCell(cell));
                }

                return cellsOut.Count > 0;
            }
        }

        Collider rootCollider = building.GetComponent<Collider>();
        if (rootCollider != null)
        {
            AddBoundsFootprint(rootCollider.bounds, cellsOut);
        }
        else
        {
            Renderer rootRenderer = building.GetComponent<Renderer>();
            if (rootRenderer != null)
            {
                AddBoundsFootprint(rootRenderer.bounds, cellsOut);
            }
        }

        if (cellsOut.Count == 0)
        {
            Vector3Int fallbackCell = grid != null
                ? grid.WorldToCell(building.transform.position)
                : Vector3Int.RoundToInt(building.transform.position);
            cellsOut.Add(NormalizeCell(fallbackCell));
        }

        return cellsOut.Count > 0;
    }

    private void AddBoundsFootprint(Bounds bounds, List<Vector3Int> cellsOut)
    {
        Vector3 center = bounds.center;
        float eastCellSize = grid != null ? Mathf.Abs(grid.cellSize[eastAxisIndex]) : 1f;
        float northCellSize = grid != null ? Mathf.Abs(grid.cellSize[northAxisIndex]) : 1f;
        float epsilon = Mathf.Max(0.0001f, Mathf.Min(eastCellSize, northCellSize) * 0.01f);

        Vector3 min = new(bounds.min.x + epsilon, center.y, bounds.min.z + epsilon);
        Vector3 max = new(bounds.max.x - epsilon, center.y, bounds.max.z - epsilon);

        Vector3 p0 = new(min.x, center.y, min.z);
        Vector3 p1 = new(min.x, center.y, max.z);
        Vector3 p2 = new(max.x, center.y, min.z);
        Vector3 p3 = new(max.x, center.y, max.z);

        Vector3Int c0 = NormalizeCell(GetGridCell(p0));
        Vector3Int c1 = NormalizeCell(GetGridCell(p1));
        Vector3Int c2 = NormalizeCell(GetGridCell(p2));
        Vector3Int c3 = NormalizeCell(GetGridCell(p3));

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

    private Vector3Int GetGridCell(Vector3 worldPosition)
    {
        if (grid != null)
        {
            return grid.WorldToCell(worldPosition);
        }

        return Vector3Int.RoundToInt(worldPosition);
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

    private static void ForEachFootprintCell(Vector3Int rootCell, Vector2Int size, System.Action<Vector3Int> action)
    {
        if (action == null)
        {
            return;
        }

        int width = Mathf.Max(1, size.x);
        int height = Mathf.Max(1, size.y);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                action(rootCell + new Vector3Int(x, 0, z));
            }
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
}
