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
    [Header("References")]
    [SerializeField] private Grid grid;

    [Header("Road Type Mapping")]
    [SerializeField] private List<RoadDefinition> roadDefinitions = new()
    {
        new RoadDefinition { name = "Straight", objectId = 0, baseConnections = RoadDirectionMask.North | RoadDirectionMask.South },
        new RoadDefinition { name = "Turn", objectId = 1, baseConnections = RoadDirectionMask.North | RoadDirectionMask.East },
        new RoadDefinition { name = "T-Intersection", objectId = 2, baseConnections = RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.West },
        new RoadDefinition { name = "4-Way", objectId = 3, baseConnections = RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West }
    };

    [Header("Preset Scene Roads")]
    [SerializeField] private bool importPresetRoadsFromTag = true;
    [SerializeField] private string presetRoadTag = "Road";

    private readonly Dictionary<int, RoadDirectionMask> definitionLookup = new();
    private readonly Dictionary<Vector3Int, RoadTileData> roadTiles = new();

    private static readonly RoadDirectionMask[] CardinalDirections =
    {
        RoadDirectionMask.North,
        RoadDirectionMask.East,
        RoadDirectionMask.South,
        RoadDirectionMask.West
    };

    public int RoadCount => roadTiles.Count;

    private void Awake()
    {
        RebuildDefinitionLookup();
        if (grid == null)
        {
            grid = FindFirstObjectByType<Grid>();
        }

        if (importPresetRoadsFromTag)
        {
            ImportPresetRoadsFromScene();
        }
    }

    private void OnValidate()
    {
        RebuildDefinitionLookup();
    }

    public void ClearAllRoads()
    {
        roadTiles.Clear();
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
        return true;
    }

    public bool UnregisterRoad(Vector3Int gridCell)
    {
        return roadTiles.Remove(gridCell);
    }

    public void ImportPresetRoadsFromScene()
    {
        if (grid == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(presetRoadTag))
        {
            return;
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

        HashSet<Vector3Int> roadCells = new();
        for (int i = 0; i < taggedRoadObjects.Length; i++)
        {
            GameObject roadObject = taggedRoadObjects[i];
            if (roadObject == null || !roadObject.activeInHierarchy)
            {
                continue;
            }

            roadCells.Add(grid.WorldToCell(roadObject.transform.position));
        }

        foreach (Vector3Int cell in roadCells)
        {
            RoadDirectionMask connections = GetNeighborConnectionsFromSet(cell, roadCells);
            if (connections == RoadDirectionMask.None)
            {
                continue;
            }

            if (roadTiles.ContainsKey(cell))
            {
                continue;
            }

            roadTiles[cell] = new RoadTileData
            {
                objectId = -1,
                rotationDegrees = 0,
                connections = connections
            };
        }

    }

    public bool HasRoadAt(Vector3Int gridCell)
    {
        return roadTiles.ContainsKey(gridCell);
    }

    public bool TryGetRoad(Vector3Int gridCell, out RoadTileData tileData)
    {
        return roadTiles.TryGetValue(gridCell, out tileData);
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

            Vector3Int neighborCell = gridCell + DirectionToOffset(direction);
            if (!roadTiles.TryGetValue(neighborCell, out RoadTileData neighborTile))
            {
                continue;
            }

            RoadDirectionMask oppositeDirection = Opposite(direction);
            if (!HasDirection(neighborTile.connections, oppositeDirection))
            {
                continue;
            }

            neighborsOut.Add(neighborCell);
        }
    }

    public bool FindShortestPath(Vector3Int startCell, Vector3Int endCell, List<Vector3Int> pathOut)
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

        List<Vector3Int> openSet = new() { startCell };
        HashSet<Vector3Int> closedSet = new();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new();
        Dictionary<Vector3Int, int> gScore = new() { [startCell] = 0 };
        Dictionary<Vector3Int, int> fScore = new() { [startCell] = Heuristic(startCell, endCell) };
        List<Vector3Int> neighbors = new(4);

        while (openSet.Count > 0)
        {
            Vector3Int current = GetBestOpenNode(openSet, fScore);
            if (current == endCell)
            {
                ReconstructPath(cameFrom, current, pathOut);
                return true;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            GetConnectedNeighbors(current, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector3Int neighbor = neighbors[i];
                if (closedSet.Contains(neighbor))
                {
                    continue;
                }

                int tentativeGScore = GetScoreOrDefault(gScore, current) + 1;
                int knownGScore = GetScoreOrDefault(gScore, neighbor);
                if (tentativeGScore >= knownGScore)
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeGScore;
                fScore[neighbor] = tentativeGScore + Heuristic(neighbor, endCell);

                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
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

    private static Vector3Int GetBestOpenNode(List<Vector3Int> openSet, Dictionary<Vector3Int, int> fScore)
    {
        Vector3Int bestNode = openSet[0];
        int bestScore = GetScoreOrDefault(fScore, bestNode);

        for (int i = 1; i < openSet.Count; i++)
        {
            Vector3Int candidate = openSet[i];
            int candidateScore = GetScoreOrDefault(fScore, candidate);
            if (candidateScore < bestScore)
            {
                bestNode = candidate;
                bestScore = candidateScore;
            }
        }

        return bestNode;
    }

    private static int GetScoreOrDefault(Dictionary<Vector3Int, int> scores, Vector3Int cell)
    {
        return scores.TryGetValue(cell, out int score) ? score : int.MaxValue;
    }

    private static int Heuristic(Vector3Int a, Vector3Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }

    private static void ReconstructPath(Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int current, List<Vector3Int> pathOut)
    {
        pathOut.Clear();
        pathOut.Add(current);

        while (cameFrom.TryGetValue(current, out Vector3Int previous))
        {
            current = previous;
            pathOut.Add(current);
        }

        pathOut.Reverse();
    }

    private static bool HasDirection(RoadDirectionMask mask, RoadDirectionMask direction)
    {
        return (mask & direction) != 0;
    }

    private static RoadDirectionMask Opposite(RoadDirectionMask direction)
    {
        switch (direction)
        {
            case RoadDirectionMask.North:
                return RoadDirectionMask.South;
            case RoadDirectionMask.East:
                return RoadDirectionMask.West;
            case RoadDirectionMask.South:
                return RoadDirectionMask.North;
            case RoadDirectionMask.West:
                return RoadDirectionMask.East;
            default:
                return RoadDirectionMask.None;
        }
    }

    private static Vector3Int DirectionToOffset(RoadDirectionMask direction)
    {
        switch (direction)
        {
            case RoadDirectionMask.North:
                return new Vector3Int(0, 0, 1);
            case RoadDirectionMask.East:
                return new Vector3Int(1, 0, 0);
            case RoadDirectionMask.South:
                return new Vector3Int(0, 0, -1);
            case RoadDirectionMask.West:
                return new Vector3Int(-1, 0, 0);
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

    private static RoadDirectionMask GetNeighborConnectionsFromSet(Vector3Int cell, HashSet<Vector3Int> roadCells)
    {
        RoadDirectionMask mask = RoadDirectionMask.None;
        if (roadCells.Contains(cell + new Vector3Int(0, 0, 1)))
        {
            mask |= RoadDirectionMask.North;
        }

        if (roadCells.Contains(cell + new Vector3Int(1, 0, 0)))
        {
            mask |= RoadDirectionMask.East;
        }

        if (roadCells.Contains(cell + new Vector3Int(0, 0, -1)))
        {
            mask |= RoadDirectionMask.South;
        }

        if (roadCells.Contains(cell + new Vector3Int(-1, 0, 0)))
        {
            mask |= RoadDirectionMask.West;
        }

        return mask;
    }
}
