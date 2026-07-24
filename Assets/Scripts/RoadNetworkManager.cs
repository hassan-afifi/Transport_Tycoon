using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum RoadDirectionMask
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3
}

[Serializable]
public struct RoadDefinition
{
    public string name;
    public int objectId;
    public RoadDirectionMask baseConnections;
}

public struct RoadTileData
{
    public int objectId;
    public int rotationDegrees;
    public RoadDirectionMask connections;
}

public class RoadNetworkManager : MonoBehaviour
{
    [SerializeField] private Grid grid;
    [SerializeField] private GridMap gridMap;
    [SerializeField] private Transform roadsParent;
    [SerializeField]
    private List<RoadDefinition> roadDefinitions = new()
    {
        new RoadDefinition { name = "Straight", objectId = 0, baseConnections = RoadDirectionMask.North | RoadDirectionMask.South },
        new RoadDefinition { name = "Turn", objectId = 1, baseConnections = RoadDirectionMask.North | RoadDirectionMask.East },
        new RoadDefinition { name = "T-Intersection", objectId = 2, baseConnections = RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.West },
        new RoadDefinition { name = "4-Way", objectId = 3, baseConnections = RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West }
    };
    [SerializeField] private bool importPresetRoadsFromTag = true;
    [SerializeField] private string presetRoadTag = "Road";
    [SerializeField] private bool useAutoRoadStep = true;
    [SerializeField, Min(1)] private int manualRoadStep = 1;
    [SerializeField, Min(0.1f)] private float expectedRoadTileWorldSize = 20f;
    [SerializeField, Min(1)] private int nearestRoadResolveRadius = 6;

    private readonly Dictionary<int, RoadDirectionMask> definitionLookup = new();
    private readonly Dictionary<Vector3Int, RoadTileData> roadTiles = new();

    private Vector3Int northOffset = new(0, 0, 1);
    private Vector3Int eastOffset = new(1, 0, 0);
    private Vector3Int southOffset = new(0, 0, -1);
    private Vector3Int westOffset = new(-1, 0, 0);
    private int eastAxisIndex = 0;
    private int northAxisIndex = 2;

    private static readonly RoadDirectionMask[] CardinalDirections =
    {
        RoadDirectionMask.North,
        RoadDirectionMask.East,
        RoadDirectionMask.South,
        RoadDirectionMask.West
    };

    private readonly struct RoadPathState : IEquatable<RoadPathState>
    {
        public readonly Vector3Int cell;
        public readonly RoadDirectionMask cameFrom;

        public RoadPathState(Vector3Int cell, RoadDirectionMask cameFrom)
        {
            this.cell = cell;
            this.cameFrom = cameFrom;
        }

        public bool Equals(RoadPathState other)
        {
            return cell == other.cell && cameFrom == other.cameFrom;
        }

        public override bool Equals(object obj)
        {
            return obj is RoadPathState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (cell.GetHashCode() * 397) ^ (int)cameFrom;
            }
        }
    }

    private readonly struct RoadTransition
    {
        public readonly Vector3Int neighborCell;
        public readonly RoadDirectionMask exitDirection;

        public RoadTransition(Vector3Int neighborCell, RoadDirectionMask exitDirection)
        {
            this.neighborCell = neighborCell;
            this.exitDirection = exitDirection;
        }
    }

    public int RoadCount => roadTiles.Count;

    public Transform GetRoadsParent()
    {
        return CoreUtility.ResolveRuntimeParent(roadsParent, transform);
    }

    public bool IsRoadObjectId(int objectId)
    {
        return definitionLookup.ContainsKey(objectId);
    }

    private void Awake()
    {
        RebuildDefinitionLookup();
        if (grid == null)
        {
            grid = FindFirstObjectByType<Grid>();
        }

        if (gridMap == null)
        {
            gridMap = GridMap.EnsureInstance();
        }

        ConfigureCardinalOffsets();

        if (importPresetRoadsFromTag)
        {
            ImportPresetRoadsFromScene();
        }
    }

    private void OnValidate()
    {
        RebuildDefinitionLookup();
        ConfigureCardinalOffsets();
    }

    public void ClearAllRoads()
    {
        roadTiles.Clear();
        if (gridMap != null)
        {
            gridMap.ClearRoads();
        }
    }

    public bool RegisterRoad(int objectId, Vector3Int gridCell, int rotationDegrees)
    {
        if (!definitionLookup.TryGetValue(objectId, out RoadDirectionMask baseConnections))
        {
            return false;
        }

        int quarterTurns = NormalizeQuarterTurns(rotationDegrees);
        RoadTileData tile = new RoadTileData
        {
            objectId = objectId,
            rotationDegrees = NormalizeRotation(rotationDegrees),
            connections = RotateMaskClockwise(baseConnections, quarterTurns)
        };

        roadTiles[gridCell] = tile;
        RefreshGenericRoadConnections();
        SyncAllRoadsToGridMap();
        return true;
    }

    public void RegisterGenericRoadCell(Vector3Int gridCell)
    {
        RoadTileData tile = new RoadTileData
        {
            objectId = -1,
            rotationDegrees = 0,
            connections = RoadDirectionMask.None
        };

        roadTiles[gridCell] = tile;
        RefreshGenericRoadConnections();
        SyncAllRoadsToGridMap();
    }

    public bool UnregisterRoad(Vector3Int gridCell)
    {
        bool removed = roadTiles.Remove(gridCell);
        if (removed)
        {
            RefreshGenericRoadConnections();
            if (gridMap != null)
            {
                gridMap.UnregisterRoadCell(gridCell);
            }

            SyncAllRoadsToGridMap();
        }

        return removed;
    }

    public void ImportPresetRoadsFromScene()
    {
        if (grid == null || string.IsNullOrWhiteSpace(presetRoadTag))
        {
            return;
        }

        List<Vector3Int> existingCells = new(roadTiles.Keys);
        for (int i = 0; i < existingCells.Count; i++)
        {
            Vector3Int cell = existingCells[i];
            if (roadTiles.TryGetValue(cell, out RoadTileData tile) && tile.objectId < 0)
            {
                roadTiles.Remove(cell);
            }
        }

        GameObject[] taggedRoadObjects;
        try
        {
            taggedRoadObjects = GameObject.FindGameObjectsWithTag(presetRoadTag);
        }
        catch (UnityException)
        {
            return;
        }

        Dictionary<Vector3Int, int> bestPriorityByCell = new();
        Dictionary<Vector3Int, RoadTileData> bestTileByCell = new();

        for (int i = 0; i < taggedRoadObjects.Length; i++)
        {
            GameObject roadObject = taggedRoadObjects[i];
            if (roadObject == null || !roadObject.activeInHierarchy)
            {
                continue;
            }

            Vector3Int cell = grid.WorldToCell(roadObject.transform.position);
            if (!TryBuildPresetRoadTileData(roadObject, out RoadTileData presetTile, out int priority))
            {
                continue;
            }

            if (bestPriorityByCell.TryGetValue(cell, out int existingPriority) && existingPriority >= priority)
            {
                continue;
            }

            bestPriorityByCell[cell] = priority;
            bestTileByCell[cell] = presetTile;
        }

        foreach (KeyValuePair<Vector3Int, RoadTileData> pair in bestTileByCell)
        {
            roadTiles[pair.Key] = pair.Value;
        }

        RefreshGenericRoadConnections();
        SyncAllRoadsToGridMap();
    }

    private bool TryBuildPresetRoadTileData(GameObject roadObject, out RoadTileData tileData, out int priority)
    {
        tileData = default;
        priority = 0;
        if (roadObject == null)
        {
            return false;
        }

        if (!TryResolvePresetRoadObjectId(roadObject.name, out _, out priority))
        {
            return false;
        }

        tileData = new RoadTileData
        {
            objectId = -1,
            rotationDegrees = 0,
            connections = RoadDirectionMask.None
        };

        return true;
    }

    private static bool TryResolvePresetRoadObjectId(string roadObjectName, out int objectId, out int priority)
    {
        objectId = -1;
        priority = 0;
        if (string.IsNullOrWhiteSpace(roadObjectName))
        {
            return false;
        }

        string normalized = roadObjectName.Trim().ToLowerInvariant();

        if (normalized.Contains("t_intersection") || normalized.Contains("t intersection"))
        {
            objectId = 2;
            priority = 30;
            return true;
        }

        if (normalized.Contains("intersection"))
        {
            objectId = 3;
            priority = 40;
            return true;
        }

        if (normalized.Contains("corner"))
        {
            objectId = 1;
            priority = 20;
            return true;
        }

        if (normalized.Contains("lane"))
        {
            objectId = 0;
            priority = 10;
            return true;
        }

        return false;
    }

    public bool HasRoadAt(Vector3Int gridCell)
    {
        return roadTiles.ContainsKey(gridCell);
    }

    public bool TryGetRoad(Vector3Int gridCell, out RoadTileData tileData)
    {
        return roadTiles.TryGetValue(gridCell, out tileData);
    }

    public bool TryResolveNearestRoadCell(Vector3Int sourceCell, out Vector3Int roadCell)
    {
        roadCell = sourceCell;
        if (roadTiles.ContainsKey(sourceCell))
        {
            return true;
        }

        int maxRadius = Mathf.Max(1, nearestRoadResolveRadius);
        int bestDistance = int.MaxValue;
        bool found = false;
        Vector3Int bestCell = sourceCell;

        for (int east = -maxRadius; east <= maxRadius; east++)
        {
            for (int north = -maxRadius; north <= maxRadius; north++)
            {
                int manhattan = Mathf.Abs(east) + Mathf.Abs(north);
                if (manhattan == 0 || manhattan > maxRadius || manhattan >= bestDistance)
                {
                    continue;
                }

                Vector3Int candidate = sourceCell + (eastOffset * east) + (northOffset * north);
                if (!roadTiles.ContainsKey(candidate))
                {
                    continue;
                }

                found = true;
                bestDistance = manhattan;
                bestCell = candidate;
            }
        }

        if (found)
        {
            roadCell = bestCell;
        }

        return found;
    }

    public void GetConnectedNeighbors(Vector3Int gridCell, List<Vector3Int> neighborsOut)
    {
        neighborsOut.Clear();
        if (!roadTiles.TryGetValue(gridCell, out RoadTileData sourceTile))
        {
            return;
        }

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            RoadDirectionMask direction = CardinalDirections[i];
            if (!HasDirection(sourceTile.connections, direction))
            {
                continue;
            }

            if (!TryGetRoadNeighbor(gridCell, direction, out Vector3Int neighborCell, out RoadTileData neighborTile))
            {
                continue;
            }

            if (!HasDirection(neighborTile.connections, RoadUtility.Opposite(direction)))
            {
                continue;
            }

            neighborsOut.Add(neighborCell);
        }
    }

    public bool FindShortestPath(Vector3Int startCell, Vector3Int endCell, List<Vector3Int> pathOut)
    {
        return FindShortestPath(startCell, endCell, pathOut, RoadDirectionMask.None);
    }

    public bool FindShortestPath(
        Vector3Int startCell,
        Vector3Int endCell,
        List<Vector3Int> pathOut,
        RoadDirectionMask forbiddenStartExit)
    {
        pathOut.Clear();

        if (!roadTiles.ContainsKey(startCell) || !roadTiles.ContainsKey(endCell))
        {
            return false;
        }

        if (startCell == endCell)
        {
            pathOut.Add(startCell);
            return true;
        }

        RoadPathState startState = new(startCell, RoadDirectionMask.None);
        List<RoadPathState> openSet = new() { startState };
        HashSet<RoadPathState> closedSet = new();
        Dictionary<RoadPathState, RoadPathState> cameFrom = new();
        Dictionary<RoadPathState, int> gScore = new() { [startState] = 0 };
        Dictionary<RoadPathState, int> fScore = new() { [startState] = Heuristic(startCell, endCell) };
        List<RoadTransition> transitions = new(4);

        while (openSet.Count > 0)
        {
            RoadPathState current = GetBestOpenState(openSet, fScore);
            if (current.cell == endCell)
            {
                ReconstructStatePath(cameFrom, current, pathOut);
                return true;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            bool isStartState = current.cell == startCell && current.cameFrom == RoadDirectionMask.None;
            GetConnectedTransitions(current.cell, current.cameFrom, isStartState, forbiddenStartExit, transitions);

            for (int i = 0; i < transitions.Count; i++)
            {
                RoadTransition transition = transitions[i];
                RoadPathState neighborState = new(transition.neighborCell, RoadUtility.Opposite(transition.exitDirection));
                if (closedSet.Contains(neighborState))
                {
                    continue;
                }

                int tentativeGScore = GetScoreOrDefault(gScore, current) + 1;
                int knownGScore = GetScoreOrDefault(gScore, neighborState);
                if (tentativeGScore >= knownGScore)
                {
                    continue;
                }

                cameFrom[neighborState] = current;
                gScore[neighborState] = tentativeGScore;
                fScore[neighborState] = tentativeGScore + Heuristic(transition.neighborCell, endCell);

                if (!openSet.Contains(neighborState))
                {
                    openSet.Add(neighborState);
                }
            }
        }

        return false;
    }

    private void RebuildDefinitionLookup()
    {
        definitionLookup.Clear();
        for (int i = 0; i < roadDefinitions.Count; i++)
        {
            RoadDefinition definition = roadDefinitions[i];
            definitionLookup[definition.objectId] = definition.baseConnections;
        }
    }

    private void RefreshGenericRoadConnections()
    {
        List<Vector3Int> cells = new(roadTiles.Keys);
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3Int cell = cells[i];
            if (!roadTiles.TryGetValue(cell, out RoadTileData tile) || tile.objectId >= 0)
            {
                continue;
            }

            tile.connections = GetNeighborPresenceMask(cell);
            roadTiles[cell] = tile;
        }
    }

    private void SyncAllRoadsToGridMap()
    {
        if (gridMap == null)
        {
            return;
        }

        gridMap.ClearRoads();
        foreach (KeyValuePair<Vector3Int, RoadTileData> pair in roadTiles)
        {
            gridMap.RegisterRoadCell(pair.Key, pair.Value);
        }
    }

    private RoadDirectionMask GetNeighborPresenceMask(Vector3Int cell)
    {
        RoadDirectionMask mask = RoadDirectionMask.None;
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            RoadDirectionMask direction = CardinalDirections[i];
            if (TryGetRoadNeighbor(cell, direction, out _, out _))
            {
                mask |= direction;
            }
        }

        return mask;
    }

    private bool TryGetRoadNeighbor(
        Vector3Int sourceCell,
        RoadDirectionMask direction,
        out Vector3Int neighborCell,
        out RoadTileData neighborTile)
    {
        neighborCell = sourceCell;
        neighborTile = default;
        int maxStep = Mathf.Max(1, GetRoadStep());
        Vector3Int offset = DirectionToOffset(direction);
        Vector3Int cursor = sourceCell;

        for (int step = 1; step <= maxStep; step++)
        {
            cursor += offset;
            if (!roadTiles.TryGetValue(cursor, out RoadTileData candidate))
            {
                continue;
            }

            neighborCell = cursor;
            neighborTile = candidate;
            return true;
        }

        return false;
    }

    private int GetRoadStep()
    {
        int fallback = Mathf.Max(1, manualRoadStep);
        if (!useAutoRoadStep || grid == null)
        {
            return fallback;
        }

        float eastCellSize = Mathf.Abs(grid.cellSize[eastAxisIndex]);
        float northCellSize = Mathf.Abs(grid.cellSize[northAxisIndex]);
        float cellSize = Mathf.Max(0.0001f, Mathf.Max(eastCellSize, northCellSize));
        int inferred = Mathf.RoundToInt(expectedRoadTileWorldSize / cellSize);
        if (inferred <= 0)
        {
            return fallback;
        }

        return Mathf.Max(1, inferred);
    }

    private void GetConnectedTransitions(
        Vector3Int gridCell,
        RoadDirectionMask cameFrom,
        bool applyStartForbidden,
        RoadDirectionMask forbiddenStartExit,
        List<RoadTransition> transitionsOut)
    {
        transitionsOut.Clear();
        if (!roadTiles.TryGetValue(gridCell, out RoadTileData sourceTile))
        {
            return;
        }

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            RoadDirectionMask direction = CardinalDirections[i];
            if (!HasDirection(sourceTile.connections, direction))
            {
                continue;
            }

            if (!IsExitAllowedByRoadRules(sourceTile.connections, cameFrom, direction))
            {
                continue;
            }

            if (applyStartForbidden && forbiddenStartExit != RoadDirectionMask.None && direction == forbiddenStartExit)
            {
                continue;
            }

            if (!TryGetRoadNeighbor(gridCell, direction, out Vector3Int neighborCell, out RoadTileData neighborTile))
            {
                continue;
            }

            if (!HasDirection(neighborTile.connections, RoadUtility.Opposite(direction)))
            {
                continue;
            }

            transitionsOut.Add(new RoadTransition(neighborCell, direction));
        }
    }

    private static bool IsExitAllowedByRoadRules(
        RoadDirectionMask tileConnections,
        RoadDirectionMask cameFrom,
        RoadDirectionMask exitDirection)
    {
        if (!HasDirection(tileConnections, exitDirection))
        {
            return false;
        }

        if (cameFrom == RoadDirectionMask.None)
        {
            return true;
        }

        if (!HasDirection(tileConnections, cameFrom) || exitDirection == cameFrom)
        {
            return false;
        }

        int connectionCount = CountConnectedDirections(tileConnections);
        if (connectionCount <= 1)
        {
            return false;
        }

        if (connectionCount == 2)
        {
            RoadDirectionMask forcedExit = GetOtherConnectedDirection(tileConnections, cameFrom);
            return forcedExit != RoadDirectionMask.None && exitDirection == forcedExit;
        }

        return true;
    }

    private static int CountConnectedDirections(RoadDirectionMask mask)
    {
        return RoadUtility.CountConnectedDirections(mask);
    }

    private static RoadDirectionMask GetOtherConnectedDirection(RoadDirectionMask mask, RoadDirectionMask knownDirection)
    {
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            RoadDirectionMask candidate = CardinalDirections[i];
            if (candidate == knownDirection || !HasDirection(mask, candidate))
            {
                continue;
            }

            return candidate;
        }

        return RoadDirectionMask.None;
    }

    private static RoadPathState GetBestOpenState(List<RoadPathState> openSet, Dictionary<RoadPathState, int> fScore)
    {
        RoadPathState bestState = openSet[0];
        int bestScore = GetScoreOrDefault(fScore, bestState);

        for (int i = 1; i < openSet.Count; i++)
        {
            RoadPathState candidate = openSet[i];
            int candidateScore = GetScoreOrDefault(fScore, candidate);
            if (candidateScore < bestScore)
            {
                bestState = candidate;
                bestScore = candidateScore;
            }
        }

        return bestState;
    }

    private static int GetScoreOrDefault(Dictionary<RoadPathState, int> scores, RoadPathState state)
    {
        return scores.TryGetValue(state, out int score) ? score : int.MaxValue;
    }

    private int Heuristic(Vector3Int a, Vector3Int b)
    {
        int aEast = a[eastAxisIndex];
        int bEast = b[eastAxisIndex];
        int aNorth = a[northAxisIndex];
        int bNorth = b[northAxisIndex];
        return Mathf.Abs(aEast - bEast) + Mathf.Abs(aNorth - bNorth);
    }

    private static void ReconstructStatePath(Dictionary<RoadPathState, RoadPathState> cameFrom, RoadPathState current, List<Vector3Int> pathOut)
    {
        pathOut.Clear();
        pathOut.Add(current.cell);

        while (cameFrom.TryGetValue(current, out RoadPathState previous))
        {
            current = previous;
            pathOut.Add(current.cell);
        }

        pathOut.Reverse();
    }

    private static bool HasDirection(RoadDirectionMask mask, RoadDirectionMask direction)
    {
        return RoadUtility.HasDirection(mask, direction);
    }

    public RoadDirectionMask GetDirectionBetweenCells(Vector3Int fromCell, Vector3Int toCell)
    {
        int eastDelta = toCell[eastAxisIndex] - fromCell[eastAxisIndex];
        int northDelta = toCell[northAxisIndex] - fromCell[northAxisIndex];
        if (Mathf.Abs(eastDelta) > Mathf.Abs(northDelta))
        {
            if (eastDelta > 0)
            {
                return RoadDirectionMask.East;
            }

            if (eastDelta < 0)
            {
                return RoadDirectionMask.West;
            }
        }
        else
        {
            if (northDelta > 0)
            {
                return RoadDirectionMask.North;
            }

            if (northDelta < 0)
            {
                return RoadDirectionMask.South;
            }
        }

        return RoadDirectionMask.None;
    }

    private Vector3Int DirectionToOffset(RoadDirectionMask direction)
    {
        switch (direction)
        {
            case RoadDirectionMask.North:
                return northOffset;
            case RoadDirectionMask.East:
                return eastOffset;
            case RoadDirectionMask.South:
                return southOffset;
            case RoadDirectionMask.West:
                return westOffset;
            default:
                return Vector3Int.zero;
        }
    }

    private static int NormalizeRotation(int degrees)
    {
        int normalized = degrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized;
    }

    private static int NormalizeQuarterTurns(int degrees)
    {
        int normalized = NormalizeRotation(degrees);
        return Mathf.RoundToInt(normalized / 90f) % 4;
    }

    private static RoadDirectionMask RotateMaskClockwise(RoadDirectionMask mask, int quarterTurns)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        RoadDirectionMask rotated = mask;
        for (int i = 0; i < turns; i++)
        {
            rotated = RotateOnceClockwise(rotated);
        }

        return rotated;
    }

    private static RoadDirectionMask RotateOnceClockwise(RoadDirectionMask mask)
    {
        RoadDirectionMask rotated = RoadDirectionMask.None;
        if (HasDirection(mask, RoadDirectionMask.North))
        {
            rotated |= RoadDirectionMask.East;
        }

        if (HasDirection(mask, RoadDirectionMask.East))
        {
            rotated |= RoadDirectionMask.South;
        }

        if (HasDirection(mask, RoadDirectionMask.South))
        {
            rotated |= RoadDirectionMask.West;
        }

        if (HasDirection(mask, RoadDirectionMask.West))
        {
            rotated |= RoadDirectionMask.North;
        }

        return rotated;
    }

    private void ConfigureCardinalOffsets()
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
                break;
            case GridLayout.CellSwizzle.XZY:
                eastAxisIndex = 0;
                northAxisIndex = 1;
                break;
            case GridLayout.CellSwizzle.YXZ:
                eastAxisIndex = 1;
                northAxisIndex = 2;
                break;
            case GridLayout.CellSwizzle.YZX:
                eastAxisIndex = 1;
                northAxisIndex = 0;
                break;
            case GridLayout.CellSwizzle.ZXY:
                eastAxisIndex = 2;
                northAxisIndex = 1;
                break;
            case GridLayout.CellSwizzle.ZYX:
                eastAxisIndex = 2;
                northAxisIndex = 0;
                break;
            default:
                eastAxisIndex = 0;
                northAxisIndex = 2;
                break;
        }

        eastOffset = RoadUtility.UnitOnAxis(eastAxisIndex, 1);
        westOffset = RoadUtility.UnitOnAxis(eastAxisIndex, -1);
        northOffset = RoadUtility.UnitOnAxis(northAxisIndex, 1);
        southOffset = RoadUtility.UnitOnAxis(northAxisIndex, -1);
    }
}
