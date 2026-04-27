using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ForestsPlayTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();

        if (GridMap.HasInstance && GridMap.Instance != null && GridMap.Instance.gameObject != null)
        {
            UnityEngine.Object.DestroyImmediate(GridMap.Instance.gameObject);
        }

        if (EconomyManager.HasInstance && EconomyManager.Instance != null && EconomyManager.Instance.gameObject != null)
        {
            UnityEngine.Object.DestroyImmediate(EconomyManager.Instance.gameObject);
        }
    }

    [Test]
    public void RebuildForestSources_RegistersForestCellsAndSpreadSources()
    {
        ForestSpreadManager manager = CreateForestManagerContext(out _, out _);

        CreateBuilding(
            "ForestA",
            BuildingType.Forest,
            new Vector3(10f, 3f, 20f),
            Vector3Int.zero,
            new Vector3Int(1, 0, 0));

        CreateBuilding(
            "CityA",
            BuildingType.City,
            new Vector3(30f, 0f, 30f),
            Vector3Int.zero);

        manager.RebuildForestSources();

        Vector3Int forestCellA = new Vector3Int(10, 0, 20);
        Vector3Int forestCellB = new Vector3Int(11, 0, 20);
        Vector3Int cityCell = new Vector3Int(30, 0, 30);

        Assert.IsTrue(manager.IsProtectedForestCell(forestCellA));
        Assert.IsTrue(manager.IsProtectedForestCell(forestCellB));
        Assert.IsFalse(manager.IsProtectedForestCell(cityCell));
        Assert.IsTrue(manager.IsProtectedForestCell(new Vector3Int(10, 99, 20)));

        HashSet<Vector3Int> spreadSources = GetPrivateField<HashSet<Vector3Int>>(manager, "spreadSourceCells");
        Assert.IsTrue(spreadSources.Contains(forestCellA));
        Assert.IsTrue(spreadSources.Contains(forestCellB));
        Assert.IsFalse(spreadSources.Contains(cityCell));
    }

    [Test]
    public void InfectionAndClearCostMethods_ReportAndClearAsExpected()
    {
        ForestSpreadManager manager = CreateForestManagerContext(out _, out _);

        Vector3Int cellA = new Vector3Int(0, 0, 0);
        Vector3Int cellB = new Vector3Int(1, 0, 0);
        Vector3Int sourceA = new Vector3Int(0, 0, 1);
        Vector3Int sourceB = new Vector3Int(1, 0, 1);

        InvokePrivateMethod(manager, "InfectCell", cellA, sourceA);
        InvokePrivateMethod(manager, "InfectCell", cellB, sourceB);

        Assert.IsTrue(manager.IsInfectedCell(cellA));
        Assert.IsTrue(manager.IsInfectedCell(cellB));
        Assert.IsFalse(manager.IsInfectedCell(new Vector3Int(99, 0, 99)));

        Assert.AreEqual(250, manager.GetRoadClearCostAtCell(cellA));
        Assert.AreEqual(250, manager.GetRoadClearCostAtCell(cellB));
        Assert.AreEqual(0, manager.GetRoadClearCostAtCell(new Vector3Int(9, 0, 9)));

        int footprintCost = manager.GetRoadClearCostForFootprint(cellA, new Vector2Int(2, 1));
        Assert.AreEqual(500, footprintCost);

        manager.ClearInfectedTreesInFootprint(cellA, new Vector2Int(2, 1));

        Assert.IsFalse(manager.IsInfectedCell(cellA));
        Assert.IsFalse(manager.IsInfectedCell(cellB));
        Assert.AreEqual(0, manager.GetRoadClearCostForFootprint(cellA, new Vector2Int(2, 1)));
    }

    [Test]
    public void RebuildForestSources_ClearsInfectedTreesOnProtectedForestCells()
    {
        ForestSpreadManager manager = CreateForestManagerContext(out _, out _);

        Vector3Int protectedCell = new Vector3Int(40, 0, 50);
        CreateBuilding(
            "ForestProtected",
            BuildingType.Forest,
            new Vector3(40f, 0f, 50f),
            Vector3Int.zero);

        manager.RebuildForestSources();
        Assert.IsTrue(manager.IsProtectedForestCell(protectedCell));

        InvokePrivateMethod(manager, "InfectCell", protectedCell, new Vector3Int(40, 0, 51));
        Assert.IsTrue(manager.IsInfectedCell(protectedCell));
        Assert.AreEqual(250, manager.GetRoadClearCostAtCell(protectedCell));

        manager.RebuildForestSources();

        Assert.IsTrue(manager.IsProtectedForestCell(protectedCell));
        Assert.IsFalse(manager.IsInfectedCell(protectedCell));
        Assert.AreEqual(0, manager.GetRoadClearCostAtCell(protectedCell));
    }

    [Test]
    public void RoadClearCostForFootprint_UsesAtLeastOneTilePerAxis()
    {
        ForestSpreadManager manager = CreateForestManagerContext(out _, out _);
        Vector3Int cell = new Vector3Int(5, 0, 5);
        InvokePrivateMethod(manager, "InfectCell", cell, new Vector3Int(5, 0, 6));

        int zeroSizeCost = manager.GetRoadClearCostForFootprint(cell, Vector2Int.zero);
        Assert.AreEqual(250, zeroSizeCost);
    }

    private ForestSpreadManager CreateForestManagerContext(out Grid grid, out GridMap gridMap)
    {
        grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        gridMap = Track(new GameObject("GridMap")).AddComponent<GridMap>();
        SetPrivateField(gridMap, "grid", grid);
        InvokePrivateMethodIfExists(gridMap, "OnValidate");

        RoadNetworkManager roadNetwork = Track(new GameObject("RoadNetworkManager")).AddComponent<RoadNetworkManager>();
        SetPrivateField(roadNetwork, "grid", grid);
        SetPrivateField(roadNetwork, "gridMap", gridMap);
        SetPrivateField(roadNetwork, "useAutoRoadStep", false);
        SetPrivateField(roadNetwork, "manualRoadStep", 1);
        SetPrivateField(roadNetwork, "importPresetRoadsFromTag", false);
        InvokePrivateMethodIfExists(roadNetwork, "OnValidate");

        GameObject cubeSmall = Track(new GameObject("CubeTreeSmallPrefab"));
        GameObject cubeBig = Track(new GameObject("CubeTreeBigPrefab"));
        GameObject firSmall = Track(new GameObject("FirTreeSmallPrefab"));
        GameObject firBig = Track(new GameObject("FirTreeBigPrefab"));

        ForestSpreadManager manager = Track(new GameObject("ForestSpreadManager")).AddComponent<ForestSpreadManager>();
        SetPrivateField(manager, "grid", grid);
        SetPrivateField(manager, "gridMap", gridMap);
        SetPrivateField(manager, "roadNetworkManager", roadNetwork);
        SetPrivateField(manager, "treesParent", manager.transform);
        SetPrivateField(manager, "spreadBounds", null);
        SetPrivateField(manager, "cubeTreeSmallPrefab", cubeSmall);
        SetPrivateField(manager, "cubeTreeBigPrefab", cubeBig);
        SetPrivateField(manager, "firTreeSmallPrefab", firSmall);
        SetPrivateField(manager, "firTreeBigPrefab", firBig);
        SetPrivateField(manager, "randomDelayMinSeconds", 28);
        SetPrivateField(manager, "randomDelayMaxSeconds", 46);
        SetPrivateField(manager, "clearRoadCostSmallTree", 250);
        SetPrivateField(manager, "clearRoadCostBigTree", 500);
        SetPrivateField(manager, "randomizeTreeYaw", false);
        SetPrivateField(manager, "treeY", 0f);

        InvokePrivateMethodIfExists(manager, "Awake");
        return manager;
    }

    private BuildingEconomy CreateBuilding(string name, BuildingType type, Vector3 worldPosition, params Vector3Int[] localTiles)
    {
        GameObject go = Track(new GameObject(name));
        go.transform.position = worldPosition;

        BuildingEconomy building = go.AddComponent<BuildingEconomy>();
        SetPrivateField(building, "buildingType", type);
        SetPrivateField(building, "useBuiltInRecipe", false);
        SetPrivateField(building, "buildingName", name);
        SetPrivateField(building, "production", new List<GoodsEntry>());
        SetPrivateField(building, "consumption", new List<GoodsEntry>());
        SetPrivateField(building, "demand", new List<GoodsEntry>());
        SetPrivateField(building, "stock", new List<GoodsEntry>());

        BuildingTileOccupancy occupancy = go.AddComponent<BuildingTileOccupancy>();
        SetPrivateField(occupancy, "useManualTiles", true);
        SetPrivateField(occupancy, "localOccupiedTiles", new List<Vector3Int>(localTiles));
        return building;
    }

    private GameObject Track(GameObject gameObject)
    {
        createdObjects.Add(gameObject);
        return gameObject;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        return (T)field.GetValue(target);
    }

    private static void InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        method.Invoke(target, args);
    }

    private static void InvokePrivateMethodIfExists(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }
}
