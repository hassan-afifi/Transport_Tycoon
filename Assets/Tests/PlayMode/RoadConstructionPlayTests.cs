using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class RoadConstructionPlayTests
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
    public void StartPlacement_SetsPlacingForValidObjectId()
    {
        PlacementSystem placement = CreatePlacementSystem(new[] { CreateObjectData(10, "Straight") });

        placement.StartPlacement(10);

        Assert.IsTrue(placement.IsPlacing);
    }

    [Test]
    public void StartPlacement_DoesNotPlaceForUnknownObjectId()
    {
        PlacementSystem placement = CreatePlacementSystem(new[] { CreateObjectData(10, "Straight") });

        placement.StartPlacement(999);

        Assert.IsFalse(placement.IsPlacing);
    }

    [Test]
    public void StopPlacement_ClearsPlacementState()
    {
        PlacementSystem placement = CreatePlacementSystem(new[] { CreateObjectData(10, "Straight") });
        placement.StartPlacement(10);
        Assert.IsTrue(placement.IsPlacing);

        placement.StopPlacement();

        Assert.IsFalse(placement.IsPlacing);
    }

    [Test]
    public void ToggleRoadPanel_TogglesWhenNotPlacingAndStopsWhenPlacing()
    {
        PlacementSystem placement = CreatePlacementSystem(new[] { CreateObjectData(0, "Straight") });
        GameObject panel = CreateGameObject("RoadPanel");
        panel.SetActive(false);
        RoadBuildToolUI ui = CreateRoadBuildToolUI(placement, panel);

        ui.ToggleRoadPanel();
        Assert.IsTrue(panel.activeSelf);

        placement.StartPlacement(0);
        Assert.IsTrue(placement.IsPlacing);

        ui.ToggleRoadPanel();

        Assert.IsFalse(placement.IsPlacing);
        Assert.IsTrue(panel.activeSelf);
    }

    [Test]
    public void SelectRoadMethods_StartPlacementAndClosePanel()
    {
        PlacementSystem placement = CreatePlacementSystem(new[] { CreateObjectData(0, "Straight"), CreateObjectData(1, "Turn"), CreateObjectData(2, "T"), CreateObjectData(3, "4Way") });
        GameObject panel = CreateGameObject("RoadPanel");
        panel.SetActive(true);
        RoadBuildToolUI ui = CreateRoadBuildToolUI(placement, panel);

        ui.SelectStraightRoad();
        Assert.IsTrue(placement.IsPlacing);
        Assert.IsFalse(panel.activeSelf);

        panel.SetActive(true);
        ui.SelectTurnRoad();
        Assert.IsTrue(placement.IsPlacing);
        Assert.IsFalse(panel.activeSelf);

        panel.SetActive(true);
        ui.SelectTIntersectionRoad();
        Assert.IsTrue(placement.IsPlacing);
        Assert.IsFalse(panel.activeSelf);

        panel.SetActive(true);
        ui.SelectFourWayRoad();
        Assert.IsTrue(placement.IsPlacing);
        Assert.IsFalse(panel.activeSelf);
    }

    [Test]
    public void CancelRoadPlacement_StopsPlacementAndClosesPanel()
    {
        PlacementSystem placement = CreatePlacementSystem(new[] { CreateObjectData(0, "Straight") });
        GameObject panel = CreateGameObject("RoadPanel");
        panel.SetActive(true);
        RoadBuildToolUI ui = CreateRoadBuildToolUI(placement, panel);

        placement.StartPlacement(0);
        Assert.IsTrue(placement.IsPlacing);

        ui.CancelRoadPlacement();

        Assert.IsFalse(placement.IsPlacing);
        Assert.IsFalse(panel.activeSelf);
    }

    [Test]
    public void RegisterRoad_StoresTileAndAppliesRotation()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);

        bool success = manager.RegisterRoad(0, Vector3Int.zero, 90);
        bool found = manager.TryGetRoad(Vector3Int.zero, out RoadTileData tile);

        Assert.IsTrue(success);
        Assert.IsTrue(found);
        Assert.AreEqual(90, tile.rotationDegrees);
        Assert.AreEqual(RoadDirectionMask.East | RoadDirectionMask.West, tile.connections);
        Assert.AreEqual(1, manager.RoadCount);
    }

    [Test]
    public void RegisterRoad_ReturnsFalseForUnknownDefinition()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);

        bool success = manager.RegisterRoad(99, Vector3Int.zero, 0);

        Assert.IsFalse(success);
        Assert.AreEqual(0, manager.RoadCount);
    }

    [Test]
    public void RegisterGenericRoadCell_BuildsConnectionsFromNeighbors()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);

        manager.RegisterGenericRoadCell(Vector3Int.zero);
        manager.RegisterGenericRoadCell(Vector3Int.right);

        bool firstFound = manager.TryGetRoad(Vector3Int.zero, out RoadTileData first);
        bool secondFound = manager.TryGetRoad(Vector3Int.right, out RoadTileData second);

        Assert.IsTrue(firstFound);
        Assert.IsTrue(secondFound);
        Assert.IsTrue((first.connections & RoadDirectionMask.East) != 0);
        Assert.IsTrue((second.connections & RoadDirectionMask.West) != 0);
    }

    [Test]
    public void UnregisterRoad_RemovesRoadAndReportsStatus()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);
        manager.RegisterRoad(0, Vector3Int.zero, 90);

        bool removedFirst = manager.UnregisterRoad(Vector3Int.zero);
        bool removedSecond = manager.UnregisterRoad(Vector3Int.zero);

        Assert.IsTrue(removedFirst);
        Assert.IsFalse(removedSecond);
        Assert.IsFalse(manager.HasRoadAt(Vector3Int.zero));
    }

    [Test]
    public void ClearAllRoads_RemovesAllRoadTiles()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);
        manager.RegisterRoad(0, Vector3Int.zero, 90);
        manager.RegisterGenericRoadCell(Vector3Int.right);

        manager.ClearAllRoads();

        Assert.AreEqual(0, manager.RoadCount);
        Assert.IsFalse(manager.HasRoadAt(Vector3Int.zero));
        Assert.IsFalse(manager.HasRoadAt(Vector3Int.right));
    }

    [Test]
    public void TryResolveNearestRoadCell_ReturnsNearestAndFalseWhenNoRoadExists()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);
        manager.RegisterRoad(0, new Vector3Int(2, 0, 0), 90);
        manager.RegisterRoad(0, new Vector3Int(1, 0, 0), 90);

        bool found = manager.TryResolveNearestRoadCell(Vector3Int.zero, out Vector3Int nearest);
        Assert.IsTrue(found);
        Assert.AreEqual(new Vector3Int(1, 0, 0), nearest);

        manager.ClearAllRoads();
        bool missing = manager.TryResolveNearestRoadCell(Vector3Int.zero, out Vector3Int fallback);
        Assert.IsFalse(missing);
        Assert.AreEqual(Vector3Int.zero, fallback);
    }

    [Test]
    public void GetConnectedNeighbors_ReturnsOnlyMutuallyConnectedRoads()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);

        manager.RegisterRoad(3, Vector3Int.zero, 0);
        manager.RegisterRoad(0, new Vector3Int(0, 0, 1), 0);
        manager.RegisterRoad(0, new Vector3Int(-1, 0, 0), 90);
        manager.RegisterRoad(0, new Vector3Int(1, 0, 0), 0);

        List<Vector3Int> neighbors = new();
        manager.GetConnectedNeighbors(Vector3Int.zero, neighbors);

        Assert.AreEqual(2, neighbors.Count);
        Assert.Contains(new Vector3Int(0, 0, 1), neighbors);
        Assert.Contains(new Vector3Int(-1, 0, 0), neighbors);
        Assert.IsFalse(neighbors.Contains(new Vector3Int(1, 0, 0)));
    }

    [Test]
    public void FindShortestPath_FindsPathAndHandlesMissingEndpoints()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);
        manager.RegisterRoad(0, new Vector3Int(0, 0, 0), 90);
        manager.RegisterRoad(0, new Vector3Int(1, 0, 0), 90);
        manager.RegisterRoad(0, new Vector3Int(2, 0, 0), 90);

        List<Vector3Int> path = new();
        bool found = manager.FindShortestPath(new Vector3Int(0, 0, 0), new Vector3Int(2, 0, 0), path);

        Assert.IsTrue(found);
        Assert.AreEqual(3, path.Count);
        Assert.AreEqual(new Vector3Int(0, 0, 0), path[0]);
        Assert.AreEqual(new Vector3Int(2, 0, 0), path[path.Count - 1]);

        List<Vector3Int> missingPath = new();
        bool missing = manager.FindShortestPath(new Vector3Int(0, 0, 0), new Vector3Int(9, 0, 0), missingPath);
        Assert.IsFalse(missing);
        Assert.AreEqual(0, missingPath.Count);
    }

    [Test]
    public void FindShortestPath_WithForbiddenStartExitBlocksForbiddenDirection()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);
        manager.RegisterRoad(3, Vector3Int.zero, 0);
        manager.RegisterRoad(0, new Vector3Int(1, 0, 0), 90);

        List<Vector3Int> path = new();
        bool blocked = manager.FindShortestPath(Vector3Int.zero, new Vector3Int(1, 0, 0), path, RoadDirectionMask.East);
        bool allowed = manager.FindShortestPath(Vector3Int.zero, new Vector3Int(1, 0, 0), path, RoadDirectionMask.None);

        Assert.IsFalse(blocked);
        Assert.IsTrue(allowed);
        Assert.AreEqual(2, path.Count);
    }

    [Test]
    public void GetDirectionBetweenCells_ReturnsExpectedDirection()
    {
        RoadNetworkManager manager = CreateRoadNetworkManager(out _);
        Vector3Int origin = Vector3Int.zero;

        Assert.AreEqual(RoadDirectionMask.East, manager.GetDirectionBetweenCells(origin, new Vector3Int(5, 0, 0)));
        Assert.AreEqual(RoadDirectionMask.West, manager.GetDirectionBetweenCells(origin, new Vector3Int(-2, 0, 0)));
        Assert.AreEqual(RoadDirectionMask.North, manager.GetDirectionBetweenCells(origin, new Vector3Int(0, 0, 3)));
        Assert.AreEqual(RoadDirectionMask.South, manager.GetDirectionBetweenCells(origin, new Vector3Int(0, 0, -3)));
        Assert.AreEqual(RoadDirectionMask.None, manager.GetDirectionBetweenCells(origin, origin));
    }

    [Test]
    public void PlacementObjectUtility_EnsureSelectionCollider_CreatesAndSkipsWhenChildColliderExists()
    {
        GameObject root = CreateGameObject("UtilityRoot");
        PlacementObjectUtility.EnsureSelectionCollider(root, 2.5f);
        SphereCollider created = root.GetComponent<SphereCollider>();
        Assert.IsNotNull(created);
        Assert.AreEqual(2.5f, created.radius, 0.0001f);
        Assert.AreEqual(Vector3.up * 2.5f, created.center);

        GameObject withChildCollider = CreateGameObject("UtilityWithChild");
        GameObject child = CreateGameObject("UtilityChild");
        child.transform.SetParent(withChildCollider.transform, false);
        child.AddComponent<BoxCollider>();
        PlacementObjectUtility.EnsureSelectionCollider(withChildCollider, 4f);
        Assert.IsNull(withChildCollider.GetComponent<SphereCollider>());
    }

    [Test]
    public void PlacementSystem_OccupancyAndRemovalHelpers_WorkForFootprint()
    {
        PlacementSystem placement = CreatePlacementSystem(new[] { CreateObjectData(123, "RoadLike") });
        GameObject gridVisualization = CreateGameObject("GridVisualization");
        SetPrivateField(placement, "gridVisualization", gridVisualization);

        ObjectData selected = CreateObjectData(123, "RoadLikeSelected");
        SetPrivateField(placement, "selectedObject", selected);

        object record = CreatePlacementRecord(
            placement,
            CreateGameObject("PlacedRoad"),
            123,
            new Vector3Int(2, 0, 3),
            new Vector2Int(2, 1),
            false);

        InvokePrivateMethod(placement, "MarkCellsOccupied", new Vector3Int(2, 0, 3), new Vector2Int(2, 1), record);
        bool occupiedBefore = (bool)InvokePrivateMethod(placement, "IsAnyFootprintCellOccupied", new Vector3Int(2, 0, 3), new Vector2Int(2, 1));
        Assert.IsTrue(occupiedBefore);

        bool canRemove = (bool)InvokePrivateMethod(placement, "CanRemoveAtCell", new Vector3Int(2, 0, 3));
        Assert.IsTrue(canRemove);

        bool removed = (bool)InvokePrivateMethod(placement, "TryRemovePlacedObjectAtCell", new Vector3Int(2, 0, 3));
        Assert.IsTrue(removed);

        bool occupiedAfter = (bool)InvokePrivateMethod(placement, "IsAnyFootprintCellOccupied", new Vector3Int(2, 0, 3), new Vector2Int(2, 1));
        Assert.IsFalse(occupiedAfter);

        InvokePrivateMethod(placement, "SetPlacementVisualsActive", true);
        Assert.IsTrue(gridVisualization.activeSelf);
        InvokePrivateMethod(placement, "SetPlacementVisualsActive", false);
        Assert.IsFalse(gridVisualization.activeSelf);
    }

    private PlacementSystem CreatePlacementSystem(ObjectData[] objects)
    {
        GameObject gridGo = CreateGameObject("Grid");
        Grid grid = gridGo.AddComponent<Grid>();

        GameObject inputGo = CreateGameObject("InputManager");
        InputManager inputManager = inputGo.AddComponent<InputManager>();

        ObjectDatabaseSO database = ScriptableObject.CreateInstance<ObjectDatabaseSO>();
        createdScriptableObjects.Add(database);
        SetPrivateField(database, "objectsData", new List<ObjectData>(objects));

        GameObject placementGo = CreateGameObject("PlacementSystem");
        PlacementSystem placement = placementGo.AddComponent<PlacementSystem>();
        SetPrivateField(placement, "inputManager", inputManager);
        SetPrivateField(placement, "grid", grid);
        SetPrivateField(placement, "database", database);

        return placement;
    }

    private RoadBuildToolUI CreateRoadBuildToolUI(PlacementSystem placementSystem, GameObject roadPanel)
    {
        GameObject go = CreateGameObject("RoadBuildToolUI");
        RoadBuildToolUI ui = go.AddComponent<RoadBuildToolUI>();
        SetPrivateField(ui, "placementSystem", placementSystem);
        SetPrivateField(ui, "roadTypePanel", roadPanel);
        SetPrivateField(ui, "closePanelAfterSelection", true);
        SetPrivateField(ui, "hidePanelOnStart", false);
        return ui;
    }

    private RoadNetworkManager CreateRoadNetworkManager(out Grid grid)
    {
        GameObject gridGo = CreateGameObject("Grid");
        grid = gridGo.AddComponent<Grid>();

        GameObject mapGo = CreateGameObject("GridMap");
        mapGo.AddComponent<GridMap>();

        GameObject managerGo = CreateGameObject("RoadNetworkManager");
        RoadNetworkManager manager = managerGo.AddComponent<RoadNetworkManager>();
        SetPrivateField(manager, "useAutoRoadStep", false);
        SetPrivateField(manager, "manualRoadStep", 1);
        SetPrivateField(manager, "importPresetRoadsFromTag", false);
        manager.ClearAllRoads();
        return manager;
    }

    private ObjectData CreateObjectData(int id, string name)
    {
        ObjectData data = new ObjectData();
        SetPrivateField(data, "<ID>k__BackingField", id);
        SetPrivateField(data, "<Name>k__BackingField", name);
        SetPrivateField(data, "<Size>k__BackingField", Vector2Int.one);
        SetPrivateField(data, "<Prefab>k__BackingField", CreateGameObject($"{name}_Prefab"));
        return data;
    }

    private static object CreatePlacementRecord(
        PlacementSystem placement,
        GameObject instance,
        int objectId,
        Vector3Int rootCell,
        Vector2Int size,
        bool registeredAsRoad)
    {
        System.Type recordType = typeof(PlacementSystem).GetNestedType("PlacementRecord", BindingFlags.NonPublic);
        Assert.IsNotNull(recordType);

        object record = System.Activator.CreateInstance(recordType);
        SetField(recordType, record, "Instance", instance);
        SetField(recordType, record, "ObjectId", objectId);
        SetField(recordType, record, "RootCell", rootCell);
        SetField(recordType, record, "Size", size);
        SetField(recordType, record, "RegisteredAsRoad", registeredAsRoad);
        return record;
    }

    private GameObject CreateGameObject(string name)
    {
        GameObject go = new GameObject(name);
        createdObjects.Add(go);
        return go;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void SetField(System.Type type, object target, string fieldName, object value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {type.Name}");
        field.SetValue(target, value);
    }

    private static object InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        return method.Invoke(target, args);
    }
}
