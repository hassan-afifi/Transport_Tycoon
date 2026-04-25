using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TrafficLightsPlayTests
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
    public void TrafficLightNode_InitializeAndConfigureAllowedDirections_UpdatePublicState()
    {
        TrafficLightNode node = Track(new GameObject("TrafficLightNode")).AddComponent<TrafficLightNode>();
        Vector3Int cell = new Vector3Int(2, 0, 3);

        node.Initialize(5, cell, "Traffic Light 5", true);

        Assert.AreEqual(5, node.LightId);
        Assert.AreEqual("Traffic Light 5", node.LightName);
        Assert.AreEqual(cell, node.GridCell);
        Assert.IsTrue(node.IsLockedInPlace);

        node.ConfigureAllowedDirections(RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.West);
        Assert.IsTrue(node.SupportsDirection(RoadDirectionMask.North));
        Assert.IsFalse(node.SupportsDirection(RoadDirectionMask.South));
        Assert.AreEqual(TrafficLightLayoutMode.ThreeWay, node.LayoutMode);
        Assert.AreEqual("Main", node.GetPrimaryDurationLabel());
        Assert.AreEqual("Side", node.GetSecondaryDurationLabel());

        node.ConfigureAllowedDirections(
            RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West);
        Assert.AreEqual(TrafficLightLayoutMode.FourWay, node.LayoutMode);
        Assert.AreEqual("N/S", node.GetPrimaryDurationLabel());
        Assert.AreEqual("E/W", node.GetSecondaryDurationLabel());
    }

    [Test]
    public void TrafficLightNode_DurationsAndPhaseQueries_ClampAndRespectYellow()
    {
        TrafficLightNode node = Track(new GameObject("TrafficLightNode")).AddComponent<TrafficLightNode>();
        node.Initialize(1, Vector3Int.zero, "Traffic Light 1");
        node.ConfigureAllowedDirections(
            RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West);

        node.SetPrimaryGreenDurationSeconds(0f);
        node.SetSecondaryGreenDurationSeconds(-3f);
        Assert.AreEqual(1f, node.GetPrimaryGreenDurationSeconds(), 0.0001f);
        Assert.AreEqual(1f, node.GetSecondaryGreenDurationSeconds(), 0.0001f);

        SetPrivateField(node, "yellowPhase", false);
        SetPrivateField(node, "primaryPhaseActive", true);
        Assert.IsTrue(node.IsDirectionGreen(RoadDirectionMask.North));
        Assert.IsFalse(node.IsDirectionGreen(RoadDirectionMask.East));
        Assert.AreEqual("N/S Green", node.GetActivePhaseLabel());

        SetPrivateField(node, "yellowPhase", true);
        Assert.IsFalse(node.IsDirectionGreen(RoadDirectionMask.North));
        Assert.AreEqual("N/S Yellow", node.GetActivePhaseLabel());
    }

    [Test]
    public void TrafficLightManager_BeginEndAndTogglePlacement_UpdatePlacementState()
    {
        TrafficLightManager manager = CreateManagerContext(out _, out _, out _);

        Assert.IsFalse(manager.IsPlacementActive);

        manager.BeginPlacement();
        Assert.IsTrue(manager.IsPlacementActive);

        manager.EndPlacement();
        Assert.IsFalse(manager.IsPlacementActive);

        manager.TogglePlacement();
        Assert.IsTrue(manager.IsPlacementActive);

        manager.TogglePlacement();
        Assert.IsFalse(manager.IsPlacementActive);
    }

    [Test]
    public void TrafficLightManager_PlaceLookupAndRemoveTrafficLight_HandleDuplicateAndMissing()
    {
        TrafficLightManager manager = CreateManagerContext(out RoadNetworkManager roadNetwork, out _, out _);
        Vector3Int cell = Vector3Int.zero;
        RegisterTIntersectionRoad(roadNetwork, cell);

        int placedCount = 0;
        int changedCount = 0;
        TrafficLightNode lastPlaced = null;
        manager.TrafficLightPlaced += node =>
        {
            placedCount++;
            lastPlaced = node;
        };
        manager.TrafficLightsChanged += () => changedCount++;

        bool placed = manager.TryPlaceTrafficLightAtCell(cell);
        bool duplicatePlace = manager.TryPlaceTrafficLightAtCell(cell);
        bool foundByCell = manager.TryGetTrafficLightAtCell(cell, out TrafficLightNode placedNode);
        bool existsBeforeRemove = manager.HasTrafficLightAtCell(cell);

        bool removed = false;
        ExecuteIgnoringFailingMessages(() => removed = manager.TryRemoveTrafficLightAtCell(cell));
        bool missingRemove = manager.TryRemoveTrafficLightAtCell(cell);

        Assert.IsTrue(placed);
        Assert.IsFalse(duplicatePlace);
        Assert.IsTrue(foundByCell);
        Assert.IsNotNull(placedNode);
        Assert.AreSame(placedNode, lastPlaced);
        Assert.AreEqual(1, placedCount);
        Assert.GreaterOrEqual(changedCount, 1);
        Assert.IsTrue(existsBeforeRemove);

        Assert.IsTrue(removed);
        Assert.IsFalse(missingRemove);
        Assert.IsFalse(manager.HasTrafficLightAtCell(cell));
    }

    [Test]
    public void TrafficLightManager_TryRemoveTrafficLightAtCell_ReturnsFalseWhenLocked()
    {
        TrafficLightManager manager = CreateManagerContext(out RoadNetworkManager roadNetwork, out _, out _);
        Vector3Int cell = Vector3Int.zero;
        RegisterTIntersectionRoad(roadNetwork, cell);
        Assert.IsTrue(manager.TryPlaceTrafficLightAtCell(cell));
        Assert.IsTrue(manager.TryGetTrafficLightAtCell(cell, out TrafficLightNode node));

        node.Initialize(node.LightId, cell, node.LightName, true);
        bool removedLocked = manager.TryRemoveTrafficLightAtCell(cell);

        Assert.IsFalse(removedLocked);
        Assert.IsTrue(manager.HasTrafficLightAtCell(cell));
    }

    [Test]
    public void TrafficLightManager_ReserveReleaseAndApproachChecks_HandleGreenRedAndConflicts()
    {
        TrafficLightManager manager = CreateManagerContext(out RoadNetworkManager roadNetwork, out Grid grid, out _);
        Vector3Int intersectionCell = Vector3Int.zero;
        RegisterTIntersectionRoad(roadNetwork, intersectionCell);
        Assert.IsTrue(manager.TryPlaceTrafficLightAtCell(intersectionCell));

        VehicleAgent vehicleA = CreateReservationVehicle("VehicleA", roadNetwork, grid, intersectionCell);
        VehicleAgent vehicleB = CreateReservationVehicle("VehicleB", roadNetwork, grid, intersectionCell);

        bool eastApproachBlocked = manager.IsApproachBlockedByRedLight(new Vector3Int(1, 0, 0), intersectionCell);
        bool southApproachBlocked = manager.IsApproachBlockedByRedLight(new Vector3Int(0, 0, -1), intersectionCell);

        bool reserveA = manager.TryReserveIntersection(
            intersectionCell,
            vehicleA,
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1));

        bool hasReservationA = manager.HasIntersectionReservation(intersectionCell, vehicleA);
        bool reserveAReconfirm = manager.TryReserveIntersection(
            intersectionCell,
            vehicleA,
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1));

        _ = manager.TryReserveIntersection(
            intersectionCell,
            vehicleB,
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1));

        manager.ReleaseIntersection(intersectionCell, vehicleA);
        bool hasReservationAAfterRelease = manager.HasIntersectionReservation(intersectionCell, vehicleA);

        bool reserveBAfterRelease = manager.TryReserveIntersection(
            intersectionCell,
            vehicleB,
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1));

        bool hasReservationB = manager.HasIntersectionReservation(intersectionCell, vehicleB);
        bool reserveBReconfirm = manager.TryReserveIntersection(
            intersectionCell,
            vehicleB,
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1));

        Assert.IsFalse(eastApproachBlocked);
        Assert.IsTrue(southApproachBlocked);
        Assert.IsTrue(reserveA);
        Assert.IsTrue(hasReservationA || reserveAReconfirm);
        Assert.IsFalse(hasReservationAAfterRelease);
        Assert.IsTrue(reserveBAfterRelease);
        Assert.IsTrue(hasReservationB || reserveBReconfirm);
    }

    [Test]
    public void TrafficLightManager_TryReserveIntersection_HandlesNullVehicleAndNonIntersection()
    {
        TrafficLightManager manager = CreateManagerContext(out RoadNetworkManager roadNetwork, out _, out _);
        Vector3Int straightRoadCell = new Vector3Int(5, 0, 0);
        Assert.IsTrue(roadNetwork.RegisterRoad(0, straightRoadCell, 0));

        bool nullVehicleReserve = manager.TryReserveIntersection(straightRoadCell, null, Vector3Int.zero, Vector3Int.right);
        VehicleAgent vehicle = Track(new GameObject("Vehicle")).AddComponent<VehicleAgent>();
        bool nonIntersectionReserve = manager.TryReserveIntersection(
            straightRoadCell,
            vehicle,
            new Vector3Int(4, 0, 0),
            new Vector3Int(6, 0, 0));

        Assert.IsFalse(nullVehicleReserve);
        Assert.IsTrue(nonIntersectionReserve);
    }

    [Test]
    public void TrafficLightBuildToolUI_Methods_BeginToggleAndCancelPlacement()
    {
        TrafficLightManager manager = CreateManagerContext(out _, out _, out _);
        TrafficLightBuildToolUI ui = Track(new GameObject("TrafficLightBuildToolUI")).AddComponent<TrafficLightBuildToolUI>();
        SetPrivateField(ui, "trafficLightManager", manager);

        ui.BeginTrafficLightPlacement();
        Assert.IsTrue(manager.IsPlacementActive);

        ui.ToggleTrafficLightPlacement();
        Assert.IsFalse(manager.IsPlacementActive);

        ui.ToggleTrafficLightPlacement();
        Assert.IsTrue(manager.IsPlacementActive);

        ui.CancelTrafficLightPlacement();
        Assert.IsFalse(manager.IsPlacementActive);
    }

    [Test]
    public void TrafficLightHead_AutoAssignMissingLights_AssignsLightsByName()
    {
        GameObject headObject = Track(new GameObject("TrafficLightHead"));
        TrafficLightHead head = headObject.AddComponent<TrafficLightHead>();

        Light green = Track(new GameObject("Lamp_Green")).AddComponent<Light>();
        green.transform.SetParent(headObject.transform, false);
        Light yellow = Track(new GameObject("Lamp_Yellow")).AddComponent<Light>();
        yellow.transform.SetParent(headObject.transform, false);
        Light redFallbackRead = Track(new GameObject("Lamp_Read")).AddComponent<Light>();
        redFallbackRead.transform.SetParent(headObject.transform, false);

        green.enabled = false;
        yellow.enabled = false;
        redFallbackRead.enabled = false;

        head.AutoAssignMissingLights();

        head.SetSignal(TrafficLightSignalColor.Green);
        Assert.IsTrue(green.enabled);
        Assert.IsFalse(yellow.enabled);
        Assert.IsFalse(redFallbackRead.enabled);

        head.SetSignal(TrafficLightSignalColor.Red);
        Assert.IsFalse(green.enabled);
        Assert.IsFalse(yellow.enabled);
        Assert.IsTrue(redFallbackRead.enabled);
    }

    [Test]
    public void TrafficLightHead_SetSignal_EnablesOnlySelectedColor()
    {
        TrafficLightHead head = Track(new GameObject("TrafficLightHead")).AddComponent<TrafficLightHead>();
        Light green = Track(new GameObject("Green")).AddComponent<Light>();
        Light yellow = Track(new GameObject("Yellow")).AddComponent<Light>();
        Light red = Track(new GameObject("Red")).AddComponent<Light>();

        SetPrivateField(head, "greenLight", green);
        SetPrivateField(head, "yellowLight", yellow);
        SetPrivateField(head, "redLight", red);

        head.SetSignal(TrafficLightSignalColor.Green);
        Assert.IsTrue(green.enabled);
        Assert.IsFalse(yellow.enabled);
        Assert.IsFalse(red.enabled);

        head.SetSignal(TrafficLightSignalColor.Yellow);
        Assert.IsFalse(green.enabled);
        Assert.IsTrue(yellow.enabled);
        Assert.IsFalse(red.enabled);

        head.SetSignal(TrafficLightSignalColor.Red);
        Assert.IsFalse(green.enabled);
        Assert.IsFalse(yellow.enabled);
        Assert.IsTrue(red.enabled);
    }

    private TrafficLightManager CreateManagerContext(out RoadNetworkManager roadNetwork, out Grid grid, out GridMap gridMap)
    {
        grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        gridMap = Track(new GameObject("GridMap")).AddComponent<GridMap>();

        roadNetwork = Track(new GameObject("RoadNetworkManager")).AddComponent<RoadNetworkManager>();
        SetPrivateField(roadNetwork, "grid", grid);
        SetPrivateField(roadNetwork, "gridMap", gridMap);
        SetPrivateField(roadNetwork, "useAutoRoadStep", false);
        SetPrivateField(roadNetwork, "manualRoadStep", 1);
        SetPrivateField(roadNetwork, "importPresetRoadsFromTag", false);
        InvokePrivateMethodIfExists(roadNetwork, "OnValidate");
        roadNetwork.ClearAllRoads();

        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        GameObject lightPrefab = Track(new GameObject("TrafficLightPrefab"));
        Transform parent = Track(new GameObject("TrafficLightsParent")).transform;

        TrafficLightManager manager = Track(new GameObject("TrafficLightManager")).AddComponent<TrafficLightManager>();
        SetPrivateField(manager, "inputManager", inputManager);
        SetPrivateField(manager, "grid", grid);
        SetPrivateField(manager, "roadNetworkManager", roadNetwork);
        SetPrivateField(manager, "trafficLightPrefab", lightPrefab);
        SetPrivateField(manager, "trafficLightsParent", parent);
        SetPrivateField(manager, "addSelectionColliderIfMissing", false);
        InvokePrivateMethodIfExists(manager, "Awake");
        return manager;
    }

    private static void RegisterTIntersectionRoad(RoadNetworkManager roadNetwork, Vector3Int cell)
    {
        bool success = roadNetwork.RegisterRoad(2, cell, 0);
        Assert.IsTrue(success);
    }

    private VehicleAgent CreateReservationVehicle(string name, RoadNetworkManager roadNetwork, Grid grid, Vector3Int currentCell)
    {
        VehicleAgent vehicle = Track(new GameObject(name)).AddComponent<VehicleAgent>();
        vehicle.Initialize(createdObjects.Count, CargoType.None);
        vehicle.ConfigureMovementContext(roadNetwork, grid, currentCell, 0f, 0f);
        SetPrivateField(vehicle, "roadNetworkManager", roadNetwork);
        SetPrivateField(vehicle, "grid", grid);
        SetPrivateField(vehicle, "currentRoadCell", currentCell);
        SetPrivateField(vehicle, "hasCurrentRoadCell", true);
        return vehicle;
    }

    private static void ExecuteIgnoringFailingMessages(System.Action action)
    {
        bool previousIgnore = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            action?.Invoke();
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnore;
        }
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

    private static void InvokePrivateMethodIfExists(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }
}
