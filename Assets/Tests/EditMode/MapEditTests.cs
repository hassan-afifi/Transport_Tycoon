using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class MapEditTests
{
    private readonly List<GameObject> createdObjects = new();
    private readonly List<ScriptableObject> createdScriptableObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();

        for (int i = createdScriptableObjects.Count - 1; i >= 0; i--)
        {
            if (createdScriptableObjects[i] != null)
            {
                Object.DestroyImmediate(createdScriptableObjects[i]);
            }
        }

        createdScriptableObjects.Clear();

        if (GridMap.HasInstance && GridMap.Instance != null && GridMap.Instance.gameObject != null)
        {
            Object.DestroyImmediate(GridMap.Instance.gameObject);
        }
    }

    [Test]
    public void EnsureInstance_ReturnsSingletonInstance()
    {
        GridMap first = GridMap.EnsureInstance();
        Track(first.gameObject);
        GridMap second = GridMap.EnsureInstance();
        GridMap third = GridMap.EnsureInstance();

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreSame(second, third);
        if (GridMap.HasInstance)
        {
            Assert.AreSame(first, GridMap.Instance);
        }
    }

    [Test]
    public void RegisterRoadCell_NormalizesVerticalAxisWhenStoring()
    {
        GridMap map = CreateGridMap();
        map.RegisterRoadCell(new Vector3Int(3, 9, 4), MakeRoadTile(7));

        Assert.IsTrue(map.HasRoadAt(new Vector3Int(3, 0, 4)));
    }

    [Test]
    public void UnregisterRoadCell_ReturnsTrueForExistingAndFalseAfterRemoval()
    {
        GridMap map = CreateGridMap();
        Vector3Int cell = new Vector3Int(5, 2, 6);
        map.RegisterRoadCell(cell, MakeRoadTile(1));

        bool removedFirst = map.UnregisterRoadCell(new Vector3Int(5, 0, 6));
        bool removedSecond = map.UnregisterRoadCell(cell);

        Assert.IsTrue(removedFirst);
        Assert.IsFalse(removedSecond);
    }

    [Test]
    public void ClearRoads_RemovesAllRegisteredRoads()
    {
        GridMap map = CreateGridMap();
        map.RegisterRoadCell(new Vector3Int(0, 1, 0), MakeRoadTile(1));
        map.RegisterRoadCell(new Vector3Int(2, 1, 2), MakeRoadTile(2));

        map.ClearRoads();

        Assert.IsFalse(map.HasRoadAt(new Vector3Int(0, 0, 0)));
        Assert.IsFalse(map.HasRoadAt(new Vector3Int(2, 0, 2)));
    }

    [Test]
    public void HasRoadAt_ReturnsExpectedPresence()
    {
        GridMap map = CreateGridMap();
        Vector3Int cell = new Vector3Int(9, 0, 9);

        Assert.IsFalse(map.HasRoadAt(cell));

        map.RegisterRoadCell(cell, MakeRoadTile(3));

        Assert.IsTrue(map.HasRoadAt(cell));
    }

    [Test]
    public void TryGetRoad_ReturnsStoredTileAndFalseForMissing()
    {
        GridMap map = CreateGridMap();
        RoadTileData tile = new RoadTileData
        {
            objectId = 9,
            rotationDegrees = 180,
            connections = RoadDirectionMask.East | RoadDirectionMask.West
        };

        map.RegisterRoadCell(new Vector3Int(1, 3, 1), tile);

        bool found = map.TryGetRoad(new Vector3Int(1, 0, 1), out RoadTileData stored);
        bool missing = map.TryGetRoad(new Vector3Int(99, 0, 99), out _);

        Assert.IsTrue(found);
        Assert.AreEqual(tile.objectId, stored.objectId);
        Assert.AreEqual(tile.rotationDegrees, stored.rotationDegrees);
        Assert.AreEqual(tile.connections, stored.connections);
        Assert.IsFalse(missing);
    }

    [Test]
    public void TryResolveNearestRoadCell_ReturnsNearestAndFalseWhenNoneExists()
    {
        GridMap map = CreateGridMap();
        map.RegisterRoadCell(new Vector3Int(2, 0, 0), MakeRoadTile(1));
        map.RegisterRoadCell(new Vector3Int(1, 0, 0), MakeRoadTile(2));

        bool found = map.TryResolveNearestRoadCell(new Vector3Int(0, 10, 0), out Vector3Int nearest);
        Assert.IsTrue(found);
        Assert.AreEqual(new Vector3Int(1, 0, 0), nearest);

        map.ClearRoads();
        bool notFound = map.TryResolveNearestRoadCell(new Vector3Int(0, 3, 0), out Vector3Int fallback);
        Assert.IsFalse(notFound);
        Assert.AreEqual(new Vector3Int(0, 0, 0), fallback);
    }

    [Test]
    public void RegisterStop_MakesStopAvailableByCellLookup()
    {
        GridMap map = CreateGridMap();
        StopNode stop = CreateStop(1, new Vector3Int(4, 8, 5));

        map.RegisterStop(stop);

        bool found = map.TryGetStopAtCell(new Vector3Int(4, 0, 5), out StopNode lookedUp);
        Assert.IsTrue(found);
        Assert.AreSame(stop, lookedUp);
    }

    [Test]
    public void UnregisterStop_RemovesStopFromLookup()
    {
        GridMap map = CreateGridMap();
        StopNode stop = CreateStop(2, new Vector3Int(6, 0, 7));
        map.RegisterStop(stop);

        map.UnregisterStop(stop);

        Assert.IsFalse(map.TryGetStopAtCell(new Vector3Int(6, 0, 7), out _));
    }

    [Test]
    public void TryGetStopAtCell_ReturnsFalseForUnknownCell()
    {
        GridMap map = CreateGridMap();

        bool found = map.TryGetStopAtCell(new Vector3Int(40, 0, 40), out _);

        Assert.IsFalse(found);
    }

    [Test]
    public void RegisterOrUpdateBuilding_TracksMovedBuildingAtNewCell()
    {
        GridMap map = CreateGridMap();
        BuildingEconomy building = CreateBuilding(new Vector3(10f, 0f, 10f), new List<Vector3Int> { Vector3Int.zero }, true);

        map.RegisterOrUpdateBuilding(building);

        List<BuildingEconomy> results = new();
        map.GetBuildingsAtOrAdjacentCardinal(new Vector3Int(10, 0, 10), results);
        Assert.Contains(building, results);

        building.transform.position = new Vector3(12f, 0f, 10f);
        map.RegisterOrUpdateBuilding(building);

        results.Clear();
        map.GetBuildingsAtOrAdjacentCardinal(new Vector3Int(10, 0, 10), results);
        Assert.IsFalse(results.Contains(building));

        results.Clear();
        map.GetBuildingsAtOrAdjacentCardinal(new Vector3Int(12, 0, 10), results);
        Assert.Contains(building, results);
    }

    [Test]
    public void RebuildAllBuildingsFromScene_RegistersOnlyActiveBuildings()
    {
        GridMap map = CreateGridMap();
        BuildingEconomy activeBuilding = CreateBuilding(new Vector3(0f, 0f, 0f), new List<Vector3Int> { Vector3Int.zero }, true);
        CreateBuilding(new Vector3(5f, 0f, 0f), new List<Vector3Int> { Vector3Int.zero }, false);

        map.RebuildAllBuildingsFromScene();

        List<BuildingEconomy> results = new();
        map.GetBuildingsAtOrAdjacentCardinal(new Vector3Int(0, 0, 0), results);
        Assert.Contains(activeBuilding, results);

        results.Clear();
        map.GetBuildingsAtOrAdjacentCardinal(new Vector3Int(5, 0, 0), results);
        Assert.IsFalse(results.Contains(activeBuilding));
        Assert.AreEqual(0, results.Count);
    }

    [Test]
    public void UnregisterBuilding_RemovesBuildingFromAdjacencyQueries()
    {
        GridMap map = CreateGridMap();
        BuildingEconomy building = CreateBuilding(new Vector3(3f, 0f, 3f), new List<Vector3Int> { Vector3Int.zero }, true);
        map.RegisterOrUpdateBuilding(building);

        map.UnregisterBuilding(building);

        List<BuildingEconomy> results = new();
        map.GetBuildingsAtOrAdjacentCardinal(new Vector3Int(3, 0, 3), results);
        Assert.IsFalse(results.Contains(building));
    }

    [Test]
    public void GetBuildingsAtOrAdjacentCardinal_ReturnsUniqueBuildingsFromCenterAndNeighbors()
    {
        GridMap map = CreateGridMap();
        BuildingEconomy centerAndEast = CreateBuilding(
            new Vector3(0f, 0f, 0f),
            new List<Vector3Int> { Vector3Int.zero, Vector3Int.right },
            true);
        BuildingEconomy north = CreateBuilding(
            new Vector3(0f, 0f, 1f),
            new List<Vector3Int> { Vector3Int.zero },
            true);

        map.RegisterOrUpdateBuilding(centerAndEast);
        map.RegisterOrUpdateBuilding(north);

        List<BuildingEconomy> results = new();
        map.GetBuildingsAtOrAdjacentCardinal(Vector3Int.zero, results);

        Assert.AreEqual(2, results.Count);
        Assert.Contains(centerAndEast, results);
        Assert.Contains(north, results);
    }

    [Test]
    public void TryGetOccupiedCells_ReturnsExpectedCellsAndFalseWhenDisabled()
    {
        GameObject go = CreateGameObject("MapTest_Occupancy");
        go.transform.position = new Vector3(2f, 0f, 3f);
        BuildingTileOccupancy occupancy = go.AddComponent<BuildingTileOccupancy>();
        SetPrivateField(occupancy, "localOccupiedTiles", new List<Vector3Int> { Vector3Int.zero, Vector3Int.right, Vector3Int.back });

        HashSet<Vector3Int> cells = new();
        bool succeeded = occupancy.TryGetOccupiedCells(null, cells);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(cells.Contains(new Vector3Int(2, 0, 3)));
        Assert.IsTrue(cells.Contains(new Vector3Int(3, 0, 3)));
        Assert.IsTrue(cells.Contains(new Vector3Int(2, 0, 2)));

        SetPrivateField(occupancy, "useManualTiles", false);
        HashSet<Vector3Int> disabledCells = new();
        bool disabled = occupancy.TryGetOccupiedCells(null, disabledCells);

        Assert.IsFalse(disabled);
        Assert.AreEqual(0, disabledCells.Count);
    }

    [Test]
    public void TryGetObjectDataById_ReturnsMatchAndFalseForUnknownId()
    {
        ObjectDatabaseSO database = CreateDatabase(
            CreateObjectData(10, "A", new Vector2Int(2, 3)),
            CreateObjectData(20, "B", new Vector2Int(1, 1)));

        bool found = database.TryGetObjectDataById(20, out ObjectData objectData);
        bool missing = database.TryGetObjectDataById(999, out ObjectData missingData);

        Assert.IsTrue(found);
        Assert.AreEqual(20, objectData.ID);
        Assert.IsFalse(missing);
        Assert.IsNull(missingData);
    }

    [Test]
    public void TryGetObjectDataByIndex_ReturnsValidAndInvalidResults()
    {
        ObjectDatabaseSO database = CreateDatabase(
            CreateObjectData(10, "A", new Vector2Int(2, 3)),
            CreateObjectData(20, "B", new Vector2Int(1, 1)));

        bool found = database.TryGetObjectDataByIndex(1, out ObjectData objectData);
        bool negative = database.TryGetObjectDataByIndex(-1, out _);
        bool overflow = database.TryGetObjectDataByIndex(7, out _);

        Assert.IsTrue(found);
        Assert.AreEqual(20, objectData.ID);
        Assert.IsFalse(negative);
        Assert.IsFalse(overflow);
    }

    [Test]
    public void GetSizeForRotation_SwapsAxesAtQuarterTurns()
    {
        ObjectData data = CreateObjectData(30, "C", new Vector2Int(2, 5));

        Assert.AreEqual(new Vector2Int(2, 5), data.GetSizeForRotation(0));
        Assert.AreEqual(new Vector2Int(5, 2), data.GetSizeForRotation(90));
        Assert.AreEqual(new Vector2Int(2, 5), data.GetSizeForRotation(180));
        Assert.AreEqual(new Vector2Int(5, 2), data.GetSizeForRotation(270));
    }

    [Test]
    public void ObjectData_PropertiesExposeAssignedValues()
    {
        ObjectData data = new ObjectData();
        GameObject prefab = CreateGameObject("ObjectDataPrefab");
        SetPrivateField(data, "<Name>k__BackingField", "Road Straight");
        SetPrivateField(data, "<ID>k__BackingField", 77);
        SetPrivateField(data, "<Size>k__BackingField", new Vector2Int(3, 4));
        SetPrivateField(data, "<Prefab>k__BackingField", prefab);
        SetPrivateField(data, "<Icon>k__BackingField", null);

        Assert.AreEqual("Road Straight", data.Name);
        Assert.AreEqual(77, data.ID);
        Assert.AreEqual(new Vector2Int(3, 4), data.Size);
        Assert.AreSame(prefab, data.Prefab);
        Assert.IsNull(data.Icon);
    }

    private GridMap CreateGridMap()
    {
        GameObject gridRoot = CreateGameObject("MapTest_Grid");
        gridRoot.AddComponent<Grid>();
        GameObject go = CreateGameObject("MapTest_GridMap");
        return go.AddComponent<GridMap>();
    }

    private StopNode CreateStop(int stopId, Vector3Int cell)
    {
        GameObject go = CreateGameObject($"MapTest_Stop_{stopId}");
        StopNode stop = go.AddComponent<StopNode>();
        stop.Initialize(stopId, cell, $"Stop {stopId}");
        return stop;
    }

    private BuildingEconomy CreateBuilding(Vector3 position, List<Vector3Int> localTiles, bool active)
    {
        GameObject go = CreateGameObject("MapTest_Building");
        go.SetActive(false);
        go.transform.position = position;

        BuildingEconomy building = go.AddComponent<BuildingEconomy>();
        BuildingTileOccupancy occupancy = go.AddComponent<BuildingTileOccupancy>();
        SetPrivateField(occupancy, "localOccupiedTiles", localTiles);

        if (active)
        {
            go.SetActive(true);
        }

        return building;
    }

    private ObjectDatabaseSO CreateDatabase(params ObjectData[] data)
    {
        ObjectDatabaseSO database = ScriptableObject.CreateInstance<ObjectDatabaseSO>();
        createdScriptableObjects.Add(database);
        SetPrivateField(database, "objectsData", new List<ObjectData>(data));
        return database;
    }

    private static ObjectData CreateObjectData(int id, string name, Vector2Int size)
    {
        ObjectData data = new ObjectData();
        SetPrivateField(data, "<ID>k__BackingField", id);
        SetPrivateField(data, "<Name>k__BackingField", name);
        SetPrivateField(data, "<Size>k__BackingField", size);
        return data;
    }

    private static RoadTileData MakeRoadTile(int objectId)
    {
        return new RoadTileData
        {
            objectId = objectId,
            rotationDegrees = 0,
            connections = RoadDirectionMask.North | RoadDirectionMask.South
        };
    }

    private GameObject CreateGameObject(string name)
    {
        GameObject go = new GameObject(name);
        Track(go);
        return go;
    }

    private void Track(GameObject go)
    {
        if (go != null && !createdObjects.Contains(go))
        {
            createdObjects.Add(go);
        }
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }
}
