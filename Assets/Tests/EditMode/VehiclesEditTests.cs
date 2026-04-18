using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class VehiclesEditTests
{
    private readonly List<GameObject> createdObjects = new();

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

        if (GridMap.HasInstance && GridMap.Instance != null && GridMap.Instance.gameObject != null)
        {
            Object.DestroyImmediate(GridMap.Instance.gameObject);
        }

        if (EconomyManager.HasInstance && EconomyManager.Instance != null && EconomyManager.Instance.gameObject != null)
        {
            Object.DestroyImmediate(EconomyManager.Instance.gameObject);
        }
    }

    [Test]
    public void VehicleAgent_Initialize_SetsIdentityAndCargoType()
    {
        VehicleAgent agent = Track(new GameObject("VehicleAgent")).AddComponent<VehicleAgent>();

        agent.Initialize(12, CargoType.Wood);

        Assert.AreEqual(12, agent.VehicleId);
        Assert.AreEqual(CargoType.Wood, agent.CargoType);
    }

    [Test]
    public void VehicleAgent_ConfigureMovementContext_ResolvesCurrentRoadCellForLaneOccupancy()
    {
        CreateRoadNetworkContext(out Grid grid, out _, out RoadNetworkManager roadNetworkManager);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));

        VehicleAgent agent = Track(new GameObject("VehicleAgent")).AddComponent<VehicleAgent>();
        agent.transform.position = grid.GetCellCenterWorld(new Vector3Int(0, 0, 0));
        agent.Initialize(1, CargoType.Wood);
        agent.ConfigureMovementContext(roadNetworkManager, grid, new Vector3Int(0, 0, 0), 0f, 0.02f);

        bool hasOccupancy = agent.TryGetLaneOccupancy(
            out Vector3Int currentRoadCell,
            out _,
            out bool hasNextRoadCell,
            out Vector3 laneForward);

        Assert.IsTrue(hasOccupancy);
        Assert.AreEqual(new Vector3Int(0, 0, 0), currentRoadCell);
        Assert.IsFalse(hasNextRoadCell);
        Assert.Greater(laneForward.sqrMagnitude, 0f);
    }

    [Test]
    public void VehicleAgent_AssignRoute_RejectsInvalidPathAndAcceptsValidPath()
    {
        CreateRoadNetworkContext(out Grid grid, out _, out RoadNetworkManager roadNetworkManager);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));

        VehicleAgent agent = Track(new GameObject("VehicleAgent")).AddComponent<VehicleAgent>();
        agent.transform.position = grid.GetCellCenterWorld(new Vector3Int(0, 0, 0));
        agent.Initialize(2, CargoType.Paper);
        agent.ConfigureMovementContext(roadNetworkManager, grid, new Vector3Int(0, 0, 0), 0f, 0.02f);

        RouteData invalidRoute = new RouteData
        {
            routeId = 5,
            stopIds = new List<int> { 1, 2 },
            pathCells = new List<Vector3Int> { new Vector3Int(0, 0, 0) }
        };

        RouteData validRoute = new RouteData
        {
            routeId = 6,
            stopIds = new List<int> { 1, 2 },
            pathCells = new List<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0)
            }
        };

        bool invalidAssigned = agent.AssignRoute(invalidRoute);
        bool validAssigned = agent.AssignRoute(validRoute);

        Assert.IsFalse(invalidAssigned);
        Assert.IsTrue(validAssigned);
        Assert.AreEqual(6, agent.ActiveRouteId);
        Assert.AreEqual(2, agent.AssignedStopIds.Count);
        Assert.IsTrue(agent.UsesRoadCell(new Vector3Int(2, 0, 0)));
        Assert.IsTrue(agent.IsMoving);
    }

    [Test]
    public void VehicleAgent_AssignStops_TracksStopAndRoadUsageAndReachability()
    {
        CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        StopManager stopManager = CreateStopManager(roadNetworkManager, grid, gridMap, inputManager, null);

        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        List<int> stopIds = GetSortedStopIds(stopManager);

        VehicleAgent agent = Track(new GameObject("VehicleAgent")).AddComponent<VehicleAgent>();
        agent.transform.position = grid.GetCellCenterWorld(new Vector3Int(1, 0, 0));
        agent.Initialize(3, CargoType.Steel);
        agent.ConfigureMovementContext(roadNetworkManager, grid, new Vector3Int(1, 0, 0), 0f, 0.02f);

        bool assigned = agent.AssignStops(stopManager, stopIds);

        Assert.IsTrue(assigned);
        Assert.AreEqual(2, agent.AssignedStopIds.Count);
        Assert.IsTrue(agent.UsesStop(stopIds[0]));
        Assert.IsTrue(agent.UsesStop(stopIds[1]));
        Assert.IsTrue(agent.UsesRoadCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(agent.CanReachStop(stopManager, stopIds[0]));
        Assert.IsFalse(agent.CanReachStop(stopManager, 9999));
        Assert.AreEqual(-1, agent.ActiveRouteId);
    }

    [Test]
    public void VehicleAgent_RequestAssignStops_HandlesImmediateAndDeferredPaths()
    {
        CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        StopManager stopManager = CreateStopManager(roadNetworkManager, grid, gridMap, inputManager, null);

        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        List<int> stopIds = GetSortedStopIds(stopManager);

        VehicleAgent agent = Track(new GameObject("VehicleAgent")).AddComponent<VehicleAgent>();
        agent.transform.position = grid.GetCellCenterWorld(new Vector3Int(1, 0, 0));
        agent.Initialize(4, CargoType.Wood);
        agent.ConfigureMovementContext(roadNetworkManager, grid, new Vector3Int(1, 0, 0), 0f, 0.02f);

        bool immediateApplied = agent.RequestAssignStops(stopManager, stopIds);
        Assert.IsTrue(immediateApplied);
        Assert.AreEqual(2, agent.AssignedStopIds.Count);

        agent.ClearAssignedStops();
        SetPrivateField(agent, "isMoving", true);
        SetPrivateField(agent, "isTurningInCell", true);

        bool deferredApplied = agent.RequestAssignStops(stopManager, stopIds);

        Assert.IsTrue(deferredApplied);
        Assert.IsTrue(agent.UsesStop(stopIds[0]));
        Assert.IsTrue(agent.UsesStop(stopIds[1]));
        Assert.IsFalse(agent.RequestAssignStops(null, stopIds));
        Assert.IsFalse(agent.RequestAssignStops(stopManager, null));
    }

    [Test]
    public void VehicleAgent_RebuildRouteAndClearAssignedStops_UpdateState()
    {
        CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        StopManager stopManager = CreateStopManager(roadNetworkManager, grid, gridMap, inputManager, null);

        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        List<int> stopIds = GetSortedStopIds(stopManager);

        VehicleAgent agent = Track(new GameObject("VehicleAgent")).AddComponent<VehicleAgent>();
        agent.transform.position = grid.GetCellCenterWorld(new Vector3Int(1, 0, 0));
        agent.Initialize(5, CargoType.Iron);
        agent.ConfigureMovementContext(roadNetworkManager, grid, new Vector3Int(1, 0, 0), 0f, 0.02f);

        Assert.IsTrue(agent.AssignStops(stopManager, stopIds));
        Assert.IsTrue(agent.RebuildRouteFromAssignedStops(stopManager));

        agent.ClearAssignedStops();

        Assert.AreEqual(0, agent.AssignedStopIds.Count);
        Assert.AreEqual(-1, agent.ActiveRouteId);
        Assert.IsFalse(agent.IsMoving);
        Assert.IsFalse(agent.RebuildRouteFromAssignedStops(stopManager));
    }
    [Test]
    public void VehicleManager_SpawnMethods_CreateVehiclesAndLookupByIdAndCargo()
    {
        CreateRoadNetworkContext(out Grid grid, out _, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();

        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            null,
            null,
            null,
            CargoType.Wood,
            out _);

        vehicleManager.transform.position = new Vector3(5f, 0f, 7f);

        int spawnedEventCount = 0;
        VehicleAgent lastSpawned = null;
        vehicleManager.VehicleSpawned += vehicle =>
        {
            spawnedEventCount++;
            lastSpawned = vehicle;
        };

        int spawnedId = vehicleManager.SpawnVehicle(CargoType.Wood);
        int invalidNoneId = vehicleManager.SpawnVehicle(CargoType.None);
        int invalidMissingPrefabId = vehicleManager.SpawnVehicle(CargoType.Paper);

        bool foundVehicle = vehicleManager.TryGetVehicle(spawnedId, out VehicleAgent vehicle);
        bool hasWoodPrefab = vehicleManager.TryGetVehiclePrefab(CargoType.Wood, out GameObject prefab);
        bool hasPaperPrefab = vehicleManager.TryGetVehiclePrefab(CargoType.Paper, out _);

        Assert.Greater(spawnedId, 0);
        Assert.IsTrue(foundVehicle);
        Assert.IsNotNull(vehicle);
        Assert.AreEqual(1, spawnedEventCount);
        Assert.AreSame(vehicle, lastSpawned);
        Assert.AreEqual(5f, vehicle.transform.position.x, 0.0001f);
        Assert.AreEqual(7f, vehicle.transform.position.z, 0.0001f);
        Assert.AreEqual(0.02f, vehicle.transform.position.y, 0.0001f);
        Assert.IsTrue(hasWoodPrefab);
        Assert.IsNotNull(prefab);
        Assert.IsFalse(hasPaperPrefab);
        Assert.AreEqual(-1, invalidNoneId);
        Assert.AreEqual(-1, invalidMissingPrefabId);
    }

    [Test]
    public void VehicleManager_RemoveVehicleMethods_RemoveSingleAndAllVehicles()
    {
        CreateRoadNetworkContext(out Grid grid, out _, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();

        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            null,
            null,
            null,
            CargoType.Wood,
            out _);

        int firstId = vehicleManager.SpawnVehicleAt(CargoType.Wood, new Vector3(0f, 0.02f, 0f), Quaternion.identity);
        int secondId = vehicleManager.SpawnVehicleAt(CargoType.Wood, new Vector3(10f, 0.02f, 0f), Quaternion.identity);

        int removedEventCount = 0;
        vehicleManager.VehicleRemoved += _ => removedEventCount++;

        bool removedMissing = vehicleManager.RemoveVehicle(9999);
        bool removedFirst = false;
        ExecuteIgnoringEditModeDestroyErrors(() =>
        {
            removedFirst = vehicleManager.RemoveVehicle(firstId);
            vehicleManager.RemoveAllVehicles();

            Assert.IsTrue(removedFirst);
        });

        bool firstStillExists = vehicleManager.TryGetVehicle(firstId, out _);
        bool secondStillExists = vehicleManager.TryGetVehicle(secondId, out _);

        Assert.IsFalse(removedMissing);
        Assert.IsFalse(firstStillExists);
        Assert.IsFalse(secondStillExists);
        Assert.AreEqual(0, vehicleManager.VehiclesById.Count);
        Assert.GreaterOrEqual(removedEventCount, 1);
    }

    [Test]
    public void VehicleManager_PlacementMethods_BeginToggleAndEndPlacement()
    {
        CreateRoadNetworkContext(out Grid grid, out _, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();

        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            null,
            null,
            null,
            CargoType.Wood,
            out _);

        vehicleManager.BeginPlacement(CargoType.None);
        Assert.IsFalse(vehicleManager.IsPlacementActive);

        vehicleManager.BeginPlacement(CargoType.Wood);
        Assert.IsTrue(vehicleManager.IsPlacementActive);
        Assert.AreEqual(CargoType.Wood, vehicleManager.SelectedCargoType);

        vehicleManager.TogglePlacement(CargoType.Wood);
        Assert.IsFalse(vehicleManager.IsPlacementActive);

        vehicleManager.TogglePlacement(CargoType.Wood);
        Assert.IsTrue(vehicleManager.IsPlacementActive);

        vehicleManager.EndPlacement();
        Assert.IsFalse(vehicleManager.IsPlacementActive);
        Assert.AreEqual(CargoType.None, vehicleManager.SelectedCargoType);
    }

    [Test]
    public void VehicleManager_AssignLatestRouteToAllVehicles_AssignsMostRecentRoute()
    {
        CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        StopManager stopManager = CreateStopManager(roadNetworkManager, grid, gridMap, inputManager, null);
        RouteManager routeManager = CreateRouteManager(roadNetworkManager, stopManager, grid);

        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));

        RouteData route = new RouteData
        {
            routeId = 41,
            routeName = "Latest",
            stopIds = new List<int> { 1, 2 },
            pathCells = new List<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0)
            }
        };
        AddRoute(routeManager, route);

        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            routeManager,
            stopManager,
            null,
            CargoType.Wood,
            out _);

        int vehicleId = vehicleManager.SpawnVehicleAt(
            CargoType.Wood,
            grid.GetCellCenterWorld(new Vector3Int(0, 0, 0)) + Vector3.up * 0.02f,
            Quaternion.identity);
        Assert.IsTrue(vehicleManager.TryGetVehicle(vehicleId, out VehicleAgent vehicle));

        vehicleManager.AssignLatestRouteToAllVehicles();

        Assert.AreEqual(41, vehicle.ActiveRouteId);
        Assert.IsTrue(vehicle.IsMoving);
    }

    [Test]
    public void VehicleManager_AssignAllStopsToAllVehicles_AssignsWhenAtLeastTwoStopsExist()
    {
        CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        StopManager stopManager = CreateStopManager(roadNetworkManager, grid, gridMap, inputManager, null);

        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));

        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            null,
            stopManager,
            null,
            CargoType.Wood,
            out _);

        int vehicleId = vehicleManager.SpawnVehicleAt(
            CargoType.Wood,
            grid.GetCellCenterWorld(new Vector3Int(1, 0, 0)) + Vector3.up * 0.02f,
            Quaternion.identity);
        Assert.IsTrue(vehicleManager.TryGetVehicle(vehicleId, out VehicleAgent vehicle));

        vehicleManager.AssignAllStopsToAllVehicles();

        Assert.AreEqual(2, vehicle.AssignedStopIds.Count);
        Assert.IsTrue(vehicle.IsMoving);
    }

    [Test]
    public void VehicleBuildToolUI_Methods_OpenCloseSelectToggleAndCancelPlacement()
    {
        CreateRoadNetworkContext(out Grid grid, out _, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            null,
            null,
            null,
            CargoType.Wood,
            out _);

        GameObject panel = Track(new GameObject("VehiclePanel"));
        panel.SetActive(false);

        VehicleBuildToolUI ui = Track(new GameObject("VehicleBuildToolUI")).AddComponent<VehicleBuildToolUI>();
        SetPrivateField(ui, "vehicleManager", vehicleManager);
        SetPrivateField(ui, "vehicleTypePanel", panel);
        SetPrivateField(ui, "closePanelAfterSelection", true);
        SetPrivateField(ui, "hidePanelOnStart", false);

        ui.ToggleVehiclePanel();
        Assert.IsTrue(panel.activeSelf);

        ui.CloseVehiclePanel();
        Assert.IsFalse(panel.activeSelf);

        ui.OpenVehiclePanel();
        Assert.IsTrue(panel.activeSelf);

        ui.SelectCargo((int)CargoType.Wood);
        Assert.IsTrue(vehicleManager.IsPlacementActive);
        Assert.IsFalse(panel.activeSelf);

        ui.CancelVehiclePlacement();
        Assert.IsFalse(vehicleManager.IsPlacementActive);
        Assert.IsFalse(panel.activeSelf);

        ui.TogglePlacement(CargoType.Wood);
        Assert.IsTrue(vehicleManager.IsPlacementActive);
        ui.TogglePlacement(CargoType.Wood);
        Assert.IsFalse(vehicleManager.IsPlacementActive);

        ui.SelectCargo(999);
        Assert.IsFalse(vehicleManager.IsPlacementActive);

        ui.AssignLatestRouteToAllVehicles();
        ui.AssignAllStopsToAllVehicles();
    }

    [Test]
    public void VehicleStopToggleItemUI_SetupAndSilentToggleUpdateState()
    {
        GameObject go = Track(new GameObject("VehicleStopToggleItem", typeof(Toggle), typeof(VehicleStopToggleItemUI)));
        Toggle toggle = go.GetComponent<Toggle>();
        VehicleStopToggleItemUI item = go.GetComponent<VehicleStopToggleItemUI>();
        SetPrivateField(item, "toggle", toggle);

        item.Setup(null, 77, "Stop 77", true);
        Assert.AreEqual(77, item.StopId);
        Assert.IsTrue(toggle.isOn);

        item.SetIsOnWithoutNotify(false);
        Assert.IsFalse(toggle.isOn);
    }

    [Test]
    public void VehicleSelectedStopItemUI_SetupAndDraggingVisualUpdateProperties()
    {
        GameObject go = Track(new GameObject(
            "VehicleSelectedStopItem",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(LayoutElement),
            typeof(TextMeshProUGUI),
            typeof(VehicleSelectedStopItemUI)));

        VehicleSelectedStopItemUI item = go.GetComponent<VehicleSelectedStopItemUI>();
        CanvasGroup canvasGroup = go.GetComponent<CanvasGroup>();
        LayoutElement layout = go.GetComponent<LayoutElement>();
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();

        SetPrivateField(item, "canvasGroup", canvasGroup);
        SetPrivateField(item, "layoutElement", layout);
        SetPrivateField(item, "labelText", label);
        InvokePrivateMethodIfExists(item, "Awake");

        item.Setup(null, 9, "Stop 9");
        item.SetDraggingVisual(true);

        Assert.AreEqual(9, item.StopId);
        Assert.AreEqual("Stop 9", label.text);
        Assert.AreEqual(0.7f, canvasGroup.alpha, 0.0001f);
        Assert.IsFalse(canvasGroup.blocksRaycasts);

        item.SetDraggingVisual(false);
        Assert.AreEqual(1f, canvasGroup.alpha, 0.0001f);
        Assert.IsTrue(canvasGroup.blocksRaycasts);

        item.OnBeginDrag(null);
        item.OnDrag(null);
        item.OnEndDrag(null);
    }
    [Test]
    public void VehicleStopAssignPanel_OpenApplyClearAndClose_UpdateVehicleAssignments()
    {
        CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        StopManager stopManager = CreateStopManager(roadNetworkManager, grid, gridMap, inputManager, null);

        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        List<int> stopIds = GetSortedStopIds(stopManager);

        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            null,
            stopManager,
            null,
            CargoType.Wood,
            out _);
        SetPrivateField(stopManager, "vehicleManager", vehicleManager);

        int vehicleId = vehicleManager.SpawnVehicleAt(
            CargoType.Wood,
            grid.GetCellCenterWorld(new Vector3Int(1, 0, 0)) + Vector3.up * 0.02f,
            Quaternion.identity);
        Assert.IsTrue(vehicleManager.TryGetVehicle(vehicleId, out VehicleAgent vehicle));
        vehicle.ConfigureMovementContext(roadNetworkManager, grid, new Vector3Int(1, 0, 0), 0f, 0.02f);
        Assert.IsTrue(vehicle.AssignStops(stopManager, stopIds));

        VehicleStopAssignPanel panel = CreateVehicleStopAssignPanel(vehicleManager, stopManager, out GameObject panelRoot, out TMP_Text titleText);

        ExecuteIgnoringEditModeDestroyErrors(() =>
        {
            panel.OpenForVehicleId(vehicleId, false);
            panel.ApplyAssignments();
            panel.HandleStopToggleChanged(stopIds[1], false);
            panel.HandleStopToggleChanged(stopIds[1], true);
            panel.ClearSelectedStops();
            panel.OpenForVehicle(vehicle, false);
            panel.ApplyAndClose();
        });

        Assert.AreEqual("No vehicle selected", titleText.text);
        Assert.AreEqual(0, vehicle.AssignedStopIds.Count);
        Assert.IsFalse(panelRoot.activeSelf);
    }

    [Test]
    public void VehicleStopAssignPanel_ClosePanel_RemovesVehicleWhenMinimumStopsNotMetAndRequired()
    {
        CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager);
        InputManager inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        StopManager stopManager = CreateStopManager(roadNetworkManager, grid, gridMap, inputManager, null);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));

        VehicleManager vehicleManager = CreateVehicleManager(
            grid,
            inputManager,
            roadNetworkManager,
            null,
            stopManager,
            null,
            CargoType.Wood,
            out _);

        int vehicleId = vehicleManager.SpawnVehicleAt(
            CargoType.Wood,
            grid.GetCellCenterWorld(new Vector3Int(1, 0, 0)) + Vector3.up * 0.02f,
            Quaternion.identity);
        Assert.IsTrue(vehicleManager.TryGetVehicle(vehicleId, out VehicleAgent vehicle));

        VehicleStopAssignPanel panel = CreateVehicleStopAssignPanel(vehicleManager, stopManager, out _, out _);

        ExecuteIgnoringEditModeDestroyErrors(() =>
        {
            panel.OpenForVehicle(vehicle, true);
            panel.ClosePanel();
        });

        Assert.IsFalse(vehicleManager.TryGetVehicle(vehicleId, out _));
    }

    private VehicleStopAssignPanel CreateVehicleStopAssignPanel(
        VehicleManager vehicleManager,
        StopManager stopManager,
        out GameObject panelRoot,
        out TMP_Text titleText)
    {
        Canvas canvas = Track(new GameObject("Canvas", typeof(Canvas))).GetComponent<Canvas>();
        panelRoot = Track(new GameObject("VehicleStopPanel", typeof(RectTransform)));
        panelRoot.transform.SetParent(canvas.transform, false);
        panelRoot.SetActive(false);

        RectTransform selectedRoot = Track(new GameObject("SelectedStopsRoot", typeof(RectTransform))).GetComponent<RectTransform>();
        selectedRoot.SetParent(panelRoot.transform, false);

        RectTransform allRoot = Track(new GameObject("AllStopsRoot", typeof(RectTransform))).GetComponent<RectTransform>();
        allRoot.SetParent(panelRoot.transform, false);

        TextMeshProUGUI title = Track(new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI))).GetComponent<TextMeshProUGUI>();
        title.transform.SetParent(panelRoot.transform, false);
        titleText = title;

        TextMeshProUGUI assigned = Track(new GameObject("AssignedText", typeof(RectTransform), typeof(TextMeshProUGUI))).GetComponent<TextMeshProUGUI>();
        assigned.transform.SetParent(panelRoot.transform, false);

        VehicleSelectedStopItemUI selectedPrefab = BuildSelectedStopItemPrefab();
        VehicleStopToggleItemUI togglePrefab = BuildStopToggleItemPrefab();

        VehicleStopAssignPanel panel = Track(new GameObject("VehicleStopAssignPanel")).AddComponent<VehicleStopAssignPanel>();
        SetPrivateField(panel, "vehicleManager", vehicleManager);
        SetPrivateField(panel, "stopManager", stopManager);
        SetPrivateField(panel, "panelRoot", panelRoot);
        SetPrivateField(panel, "selectedStopsListRoot", selectedRoot);
        SetPrivateField(panel, "allStopsListRoot", allRoot);
        SetPrivateField(panel, "selectedStopItemPrefab", selectedPrefab);
        SetPrivateField(panel, "allStopsToggleItemPrefab", togglePrefab);
        SetPrivateField(panel, "dragCanvas", canvas);
        SetPrivateField(panel, "titleText", title);
        SetPrivateField(panel, "assignedStopsText", assigned);
        SetPrivateField(panel, "minimumStopsRequired", 2);
        SetPrivateField(panel, "hidePanelOnStart", false);
        SetPrivateField(panel, "emptyAssignedStopsText", "Assigned stops:");

        InvokePrivateMethodIfExists(panel, "Awake");
        return panel;
    }

    private VehicleSelectedStopItemUI BuildSelectedStopItemPrefab()
    {
        GameObject go = Track(new GameObject(
            "SelectedStopItemPrefab",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(LayoutElement),
            typeof(TextMeshProUGUI),
            typeof(VehicleSelectedStopItemUI)));

        VehicleSelectedStopItemUI item = go.GetComponent<VehicleSelectedStopItemUI>();
        SetPrivateField(item, "canvasGroup", go.GetComponent<CanvasGroup>());
        SetPrivateField(item, "layoutElement", go.GetComponent<LayoutElement>());
        SetPrivateField(item, "labelText", go.GetComponent<TextMeshProUGUI>());
        InvokePrivateMethodIfExists(item, "Awake");
        return item;
    }

    private VehicleStopToggleItemUI BuildStopToggleItemPrefab()
    {
        GameObject go = Track(new GameObject(
            "StopToggleItemPrefab",
            typeof(RectTransform),
            typeof(Toggle),
            typeof(TextMeshProUGUI),
            typeof(VehicleStopToggleItemUI)));

        VehicleStopToggleItemUI item = go.GetComponent<VehicleStopToggleItemUI>();
        SetPrivateField(item, "toggle", go.GetComponent<Toggle>());
        SetPrivateField(item, "labelText", go.GetComponent<TextMeshProUGUI>());
        InvokePrivateMethodIfExists(item, "Awake");
        return item;
    }

    private StopManager CreateStopManager(
        RoadNetworkManager roadNetworkManager,
        Grid grid,
        GridMap gridMap,
        InputManager inputManager,
        VehicleManager vehicleManager)
    {
        StopManager stopManager = Track(new GameObject("StopManager")).AddComponent<StopManager>();
        SetPrivateField(stopManager, "roadNetworkManager", roadNetworkManager);
        SetPrivateField(stopManager, "grid", grid);
        SetPrivateField(stopManager, "gridMap", gridMap);
        SetPrivateField(stopManager, "inputManager", inputManager);
        SetPrivateField(stopManager, "vehicleManager", vehicleManager);
        SetPrivateField(stopManager, "stopSignPrefab", Track(new GameObject("StopSignPrefab")));
        SetPrivateField(stopManager, "addSelectionColliderIfMissing", false);
        SetPrivateField(stopManager, "noStopZoneMask", (LayerMask)0);
        return stopManager;
    }

    private RouteManager CreateRouteManager(RoadNetworkManager roadNetworkManager, StopManager stopManager, Grid grid)
    {
        RouteManager routeManager = Track(new GameObject("RouteManager")).AddComponent<RouteManager>();
        SetPrivateField(routeManager, "roadNetworkManager", roadNetworkManager);
        SetPrivateField(routeManager, "stopManager", stopManager);
        SetPrivateField(routeManager, "grid", grid);
        SetPrivateField(routeManager, "addSelectedStopsAutomatically", false);
        SetPrivateField(routeManager, "stopStopPlacementWhenDrafting", false);
        return routeManager;
    }

    private VehicleManager CreateVehicleManager(
        Grid grid,
        InputManager inputManager,
        RoadNetworkManager roadNetworkManager,
        RouteManager routeManager,
        StopManager stopManager,
        VehicleStopAssignPanel vehicleStopAssignPanel,
        CargoType cargoType,
        out GameObject prefab)
    {
        prefab = Track(new GameObject($"VehiclePrefab_{cargoType}"));
        prefab.AddComponent<VehicleAgent>();

        VehicleManager vehicleManager = Track(new GameObject("VehicleManager")).AddComponent<VehicleManager>();
        SetPrivateField(vehicleManager, "grid", grid);
        SetPrivateField(vehicleManager, "inputManager", inputManager);
        SetPrivateField(vehicleManager, "roadNetworkManager", roadNetworkManager);
        SetPrivateField(vehicleManager, "routeManager", routeManager);
        SetPrivateField(vehicleManager, "stopManager", stopManager);
        SetPrivateField(vehicleManager, "vehicleStopAssignPanel", vehicleStopAssignPanel);
        SetPrivateField(vehicleManager, "laneOffset", 0f);
        SetPrivateField(vehicleManager, "previewHeightOffset", 0.08f);
        SetPrivateField(vehicleManager, "spawnY", 0.02f);
        SetPrivateField(vehicleManager, "useManagerPositionAsSpawn", true);
        SetPrivateField(vehicleManager, "openStopAssignmentPanelOnSpawn", false);
        SetPrivateField(vehicleManager, "allowTaggedRoadFallback", false);
        SetPrivateField(vehicleManager, "minimumStopsForAutoAssign", 2);
        SetPrivateField(vehicleManager, "previewAlpha", 0.5f);
        SetPrivateField(
            vehicleManager,
            "vehiclePrefabs",
            new List<VehiclePrefabEntry>
            {
                new VehiclePrefabEntry
                {
                    cargoType = cargoType,
                    prefab = prefab
                }
            });
        InvokePrivateMethodIfExists(vehicleManager, "OnValidate");
        return vehicleManager;
    }

    private void CreateRoadNetworkContext(out Grid grid, out GridMap gridMap, out RoadNetworkManager roadNetworkManager)
    {
        grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        gridMap = Track(new GameObject("GridMap")).AddComponent<GridMap>();
        SetPrivateField(gridMap, "grid", grid);
        InvokePrivateMethodIfExists(gridMap, "OnValidate");

        roadNetworkManager = Track(new GameObject("RoadNetworkManager")).AddComponent<RoadNetworkManager>();
        SetPrivateField(roadNetworkManager, "grid", grid);
        SetPrivateField(roadNetworkManager, "gridMap", gridMap);
        SetPrivateField(roadNetworkManager, "importPresetRoadsFromTag", false);
        SetPrivateField(roadNetworkManager, "useAutoRoadStep", false);
        SetPrivateField(roadNetworkManager, "manualRoadStep", 1);
        SetPrivateField(roadNetworkManager, "nearestRoadResolveRadius", 8);
        InvokePrivateMethodIfExists(roadNetworkManager, "OnValidate");
        roadNetworkManager.ClearAllRoads();
    }

    private static void RegisterStraightEastWestRoad(RoadNetworkManager roadNetworkManager, Vector3Int gridCell)
    {
        bool success = roadNetworkManager.RegisterRoad(0, gridCell, 90);
        Assert.IsTrue(success);
    }

    private static List<int> GetSortedStopIds(StopManager stopManager)
    {
        List<int> ids = new();
        stopManager.GetSortedStopIds(ids);
        return ids;
    }

    private static void AddRoute(RouteManager routeManager, RouteData route)
    {
        FieldInfo routesField = typeof(RouteManager).GetField("routes", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(routesField);
        List<RouteData> routes = routesField.GetValue(routeManager) as List<RouteData>;
        Assert.IsNotNull(routes);
        routes.Add(route);
    }

    private static void ExecuteIgnoringEditModeDestroyErrors(Action action)
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

    private static void InvokePrivateMethodIfExists(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }
}
