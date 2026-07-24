using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class StopsAndRoutesPlayTests
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
    public void StopNode_Initialize_SetsPublicProperties()
    {
        StopNode stopNode = Track(new GameObject("StopNode")).AddComponent<StopNode>();
        Vector3Int cell = new Vector3Int(3, 0, 4);

        stopNode.Initialize(7, cell, "Stop 7", StopRoadAxis.EastWest, true);

        Assert.AreEqual(7, stopNode.StopId);
        Assert.AreEqual("Stop 7", stopNode.StopName);
        Assert.AreEqual(cell, stopNode.GridCell);
        Assert.AreEqual(StopRoadAxis.EastWest, stopNode.RoadAxis);
        Assert.IsTrue(stopNode.IsLockedInPlace);
    }

    [Test]
    public void StopManager_BeginEndAndTogglePlacement_UpdatesPlacementState()
    {
        StopManager stopManager = CreateStopManagerWithRoadNetwork(out _, out _, out _);

        Assert.IsFalse(stopManager.IsStopPlacementActive);

        stopManager.BeginStopPlacement();
        Assert.IsTrue(stopManager.IsStopPlacementActive);

        stopManager.EndStopPlacement();
        Assert.IsFalse(stopManager.IsStopPlacementActive);

        stopManager.ToggleStopPlacement();
        Assert.IsTrue(stopManager.IsStopPlacementActive);

        stopManager.ToggleStopPlacement();
        Assert.IsFalse(stopManager.IsStopPlacementActive);
    }

    [Test]
    public void StopManager_TryPlaceStopAtCell_PlacesStopAndSupportsLookupMethods()
    {
        StopManager stopManager = CreateStopManagerWithRoadNetwork(out RoadNetworkManager roadNetworkManager, out _, out _);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));

        int placedEvents = 0;
        int changedEvents = 0;
        StopNode lastPlaced = null;
        stopManager.StopPlaced += stop =>
        {
            placedEvents++;
            lastPlaced = stop;
        };
        stopManager.StopsChanged += () => changedEvents++;

        bool placed = stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0));
        bool foundByCell = stopManager.TryGetStopAtCell(new Vector3Int(0, 0, 0), out StopNode stopByCell);
        bool foundById = stopManager.TryGetStopById(1, out StopNode stopById);

        Assert.IsTrue(placed);
        Assert.IsTrue(foundByCell);
        Assert.IsTrue(foundById);
        Assert.AreSame(stopByCell, stopById);
        Assert.AreEqual(StopRoadAxis.EastWest, stopByCell.RoadAxis);
        Assert.AreEqual(1, placedEvents);
        Assert.AreEqual(1, changedEvents);
        Assert.AreSame(stopByCell, lastPlaced);
    }

    [Test]
    public void StopManager_TryPlaceStopAtCell_RejectsInvalidRoadAndDuplicateStop()
    {
        StopManager stopManager = CreateStopManagerWithRoadNetwork(out RoadNetworkManager roadNetworkManager, out _, out _);
        Assert.IsTrue(roadNetworkManager.RegisterRoad(2, new Vector3Int(0, 0, 0), 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));

        bool placedOnIntersection = stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0));
        bool firstPlace = stopManager.TryPlaceStopAtCell(new Vector3Int(1, 0, 0));
        bool duplicatePlace = stopManager.TryPlaceStopAtCell(new Vector3Int(1, 0, 0));

        Assert.IsFalse(placedOnIntersection);
        Assert.IsTrue(firstPlace);
        Assert.IsFalse(duplicatePlace);
    }

    [Test]
    public void StopManager_TryGetStopFromObject_ResolvesSelfParentAndChildObjects()
    {
        StopManager stopManager = CreateStopManagerWithRoadNetwork(out RoadNetworkManager roadNetworkManager, out _, out _);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryGetStopAtCell(new Vector3Int(0, 0, 0), out StopNode stopNode));

        bool fromSelf = stopManager.TryGetStopFromObject(stopNode.gameObject, out StopNode selfResult);

        GameObject child = Track(new GameObject("StopChild"));
        child.transform.SetParent(stopNode.transform, false);
        bool fromChild = stopManager.TryGetStopFromObject(child, out StopNode childResult);

        GameObject parent = Track(new GameObject("StopParent"));
        stopNode.transform.SetParent(parent.transform, true);
        bool fromParent = stopManager.TryGetStopFromObject(parent, out StopNode parentResult);

        Assert.IsTrue(fromSelf);
        Assert.IsTrue(fromChild);
        Assert.IsTrue(fromParent);
        Assert.AreSame(stopNode, selfResult);
        Assert.AreSame(stopNode, childResult);
        Assert.AreSame(stopNode, parentResult);
    }

    [Test]
    public void StopManager_TryRemoveStopAtCell_ReturnsFalseForMissingAndRemovesExisting()
    {
        StopManager stopManager = CreateStopManagerWithRoadNetwork(out RoadNetworkManager roadNetworkManager, out _, out _);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));

        bool missingRemoval = stopManager.TryRemoveStopAtCell(new Vector3Int(9, 0, 9));

        bool previousIgnore = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        bool existingRemoval;
        try
        {
            existingRemoval = stopManager.TryRemoveStopAtCell(new Vector3Int(0, 0, 0));
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previousIgnore;
        }

        bool stillExists = stopManager.TryGetStopAtCell(new Vector3Int(0, 0, 0), out _);

        Assert.IsFalse(missingRemoval);
        Assert.IsTrue(existingRemoval);
        Assert.IsFalse(stillExists);
    }

    [Test]
    public void StopManager_GetSortedStopIds_ReturnsAscendingStopIds()
    {
        StopManager stopManager = CreateStopManagerWithRoadNetwork(out RoadNetworkManager roadNetworkManager, out _, out _);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));

        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));

        List<int> stopIds = new() { 99 };
        stopManager.GetSortedStopIds(stopIds);

        Assert.AreEqual(2, stopIds.Count);
        Assert.AreEqual(1, stopIds[0]);
        Assert.AreEqual(2, stopIds[1]);
    }

    [Test]
    public void StopBuildToolUI_Methods_OpenCloseToggleAndCancelPanelAndPlacement()
    {
        StopManager stopManager = CreateStopManagerWithRoadNetwork(out _, out _, out _);
        GameObject panel = Track(new GameObject("StopPanel"));
        panel.SetActive(false);
        StopBuildToolUI ui = CreateStopBuildToolUI(stopManager, panel);

        ui.ToggleStopPanel();
        Assert.IsTrue(panel.activeSelf);

        ui.CloseStopPanel();
        Assert.IsFalse(panel.activeSelf);

        ui.OpenStopPanel();
        Assert.IsTrue(panel.activeSelf);

        stopManager.BeginStopPlacement();
        Assert.IsTrue(stopManager.IsStopPlacementActive);
        ui.SelectStopPlacement();
        Assert.IsFalse(stopManager.IsStopPlacementActive);

        stopManager.BeginStopPlacement();
        ui.CancelStopPlacement();
        Assert.IsFalse(stopManager.IsStopPlacementActive);
        Assert.IsFalse(panel.activeSelf);
    }

    [Test]
    public void RouteManager_BeginCancelAndToggleDrafting_UpdatesDraftStateAndEvents()
    {
        RouteManager routeManager = CreateRouteManagerContext(out _, out _, out _, out _);

        int draftChangedCount = 0;
        routeManager.DraftChanged += () => draftChangedCount++;

        routeManager.ToggleRouteDrafting();
        Assert.IsTrue(routeManager.IsDraftingRoute);

        routeManager.ToggleRouteDrafting();
        Assert.IsFalse(routeManager.IsDraftingRoute);

        routeManager.BeginRouteDraft();
        Assert.IsTrue(routeManager.IsDraftingRoute);

        routeManager.CancelRouteDraft();
        Assert.IsFalse(routeManager.IsDraftingRoute);
        Assert.GreaterOrEqual(draftChangedCount, 4);
    }

    [Test]
    public void RouteManager_AddStopByIdToDraft_ValidatesDraftStateStopExistenceAndDuplicates()
    {
        RouteManager routeManager = CreateRouteManagerContext(out StopManager stopManager, out RoadNetworkManager roadNetworkManager, out _, out _);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        List<int> stopIds = GetSortedStopIds(stopManager);

        bool addWhileNotDrafting = routeManager.AddStopByIdToDraft(stopIds[0]);

        routeManager.BeginRouteDraft();
        bool addMissingStop = routeManager.AddStopByIdToDraft(999);
        bool firstAdd = routeManager.AddStopByIdToDraft(stopIds[0]);
        bool duplicateConsecutive = routeManager.AddStopByIdToDraft(stopIds[0]);
        bool secondAdd = routeManager.AddStopByIdToDraft(stopIds[1]);

        Assert.IsFalse(addWhileNotDrafting);
        Assert.IsFalse(addMissingStop);
        Assert.IsTrue(firstAdd);
        Assert.IsFalse(duplicateConsecutive);
        Assert.IsTrue(secondAdd);
        Assert.AreEqual(2, routeManager.DraftStopIds.Count);
    }

    [Test]
    public void RouteManager_AddSelectedStopMethods_UseCameraSelection()
    {
        RouteManager routeManager = CreateRouteManagerContext(out StopManager stopManager, out RoadNetworkManager roadNetworkManager, out CameraController cameraController, out _);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        Assert.IsTrue(stopManager.TryGetStopAtCell(new Vector3Int(0, 0, 0), out StopNode firstStop));
        Assert.IsTrue(stopManager.TryGetStopAtCell(new Vector3Int(2, 0, 0), out StopNode secondStop));

        routeManager.BeginRouteDraft();
        SetCameraSelection(cameraController, firstStop.gameObject);
        bool addedFirst = routeManager.AddSelectedStopToDraft();

        SetCameraSelection(cameraController, secondStop.gameObject);
        routeManager.AddSelectedStopToDraftFromUI();

        Assert.IsTrue(addedFirst);
        Assert.AreEqual(2, routeManager.DraftStopIds.Count);
        Assert.AreEqual(firstStop.StopId, routeManager.DraftStopIds[0]);
        Assert.AreEqual(secondStop.StopId, routeManager.DraftStopIds[1]);
    }

    [Test]
    public void RouteManager_RemoveLastStopMethods_RemoveDraftStopsAndReturnStatus()
    {
        RouteManager routeManager = CreateRouteManagerContext(out StopManager stopManager, out RoadNetworkManager roadNetworkManager, out _, out _);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        List<int> stopIds = GetSortedStopIds(stopManager);

        routeManager.BeginRouteDraft();
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[0]));
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[1]));

        bool removedFirst = routeManager.RemoveLastStopFromDraft();
        routeManager.RemoveLastStopFromDraftFromUI();
        bool removedWhenEmpty = routeManager.RemoveLastStopFromDraft();

        Assert.IsTrue(removedFirst);
        Assert.AreEqual(0, routeManager.DraftStopIds.Count);
        Assert.IsFalse(removedWhenEmpty);
    }

    [Test]
    public void RouteManager_FinalizeDraftRouteMethods_CreateRoutesAndExposeById()
    {
        RouteManager routeManager = CreateRouteManagerContext(out StopManager stopManager, out RoadNetworkManager roadNetworkManager, out _, out Grid grid);
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(0, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(1, 0, 0));
        RegisterStraightEastWestRoad(roadNetworkManager, new Vector3Int(2, 0, 0));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(0, 0, 0)));
        Assert.IsTrue(stopManager.TryPlaceStopAtCell(new Vector3Int(2, 0, 0)));
        List<int> stopIds = GetSortedStopIds(stopManager);

        int routeCreatedCount = 0;
        RouteData lastRoute = null;
        routeManager.RouteCreated += route =>
        {
            routeCreatedCount++;
            lastRoute = route;
        };

        routeManager.BeginRouteDraft();
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[0]));
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[1]));
        bool finalizedWithName = routeManager.FinalizeDraftRouteWithName("Main Line");

        Assert.IsTrue(finalizedWithName);
        Assert.IsFalse(routeManager.IsDraftingRoute);
        Assert.AreEqual(1, routeManager.Routes.Count);
        Assert.AreEqual(1, routeCreatedCount);
        Assert.IsNotNull(lastRoute);
        Assert.AreEqual("Main Line", lastRoute.routeName);
        Assert.AreEqual(2, lastRoute.stopIds.Count);
        Assert.AreEqual(3, lastRoute.pathCells.Count);
        Assert.AreEqual(lastRoute.pathCells.Count, lastRoute.waypoints.Count);
        Assert.AreEqual(lastRoute.waypoints.Count, lastRoute.waypointRotations.Count);
        Assert.AreEqual(grid.GetCellCenterWorld(new Vector3Int(0, 0, 0)).x, lastRoute.waypoints[0].x, 0.0001f);
        Assert.IsTrue(routeManager.TryGetRouteById(lastRoute.routeId, out RouteData fetchedRoute));
        Assert.AreEqual(lastRoute.routeId, fetchedRoute.routeId);
        Assert.IsFalse(routeManager.TryGetRouteById(9999, out _));

        routeManager.BeginRouteDraft();
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[1]));
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[0]));
        routeManager.FinalizeDraftRouteFromUI();

        Assert.AreEqual(2, routeManager.Routes.Count);
        Assert.AreEqual("Route 2", routeManager.Routes[1].routeName);

        routeManager.BeginRouteDraft();
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[0]));
        Assert.IsTrue(routeManager.AddStopByIdToDraft(stopIds[1]));
        bool finalizedDefaultName = routeManager.FinalizeDraftRoute();

        Assert.IsTrue(finalizedDefaultName);
        Assert.AreEqual(3, routeManager.Routes.Count);
        Assert.AreEqual("Route 3", routeManager.Routes[2].routeName);
    }

    [Test]
    public void BuildingInfoPanel_OpenAssignedStopsPanelForSelectedVehicle_OpensPanelForCurrentVehicle()
    {
        VehicleManager vehicleManager = Track(new GameObject("VehicleManager")).AddComponent<VehicleManager>();
        VehicleAgent vehicle = Track(new GameObject("Vehicle")).AddComponent<VehicleAgent>();
        vehicle.Initialize(1, CargoType.None);

        Dictionary<int, VehicleAgent> vehiclesById = GetPrivateField<Dictionary<int, VehicleAgent>>(vehicleManager, "vehiclesById");
        vehiclesById[vehicle.VehicleId] = vehicle;

        GameObject panelRoot = Track(new GameObject("VehicleStopAssignPanelRoot"));
        panelRoot.SetActive(false);
        VehicleStopAssignPanel assignPanel = Track(new GameObject("VehicleStopAssignPanel")).AddComponent<VehicleStopAssignPanel>();
        SetPrivateField(assignPanel, "vehicleManager", vehicleManager);
        SetPrivateField(assignPanel, "stopManager", null);
        SetPrivateField(assignPanel, "panelRoot", panelRoot);
        SetPrivateField(assignPanel, "hidePanelOnStart", false);
        InvokePrivateMethodIfExists(assignPanel, "Awake");

        InfoPanel infoPanel = Track(new GameObject("InfoPanel")).AddComponent<InfoPanel>();
        SetPrivateField(infoPanel, "vehicleStopAssignPanel", assignPanel);
        SetPrivateField(infoPanel, "currentVehicleAgent", vehicle);

        infoPanel.OpenAssignedStopsPanelForSelectedVehicle();
        Assert.IsTrue(panelRoot.activeSelf);

        panelRoot.SetActive(false);
        SetPrivateField(infoPanel, "currentVehicleAgent", null);
        infoPanel.OpenAssignedStopsPanelForSelectedVehicle();
        Assert.IsFalse(panelRoot.activeSelf);
    }

    [Test]
    public void BuildingInfoPanel_AwakeAndEnableWithoutCamera_HidesControlsAndShowsEmptySelectionText()
    {
        GameObject infoPanelRoot = Track(new GameObject("BuildingInfoPanelRoot"));
        infoPanelRoot.SetActive(false);
        InfoPanel infoPanel = infoPanelRoot.AddComponent<InfoPanel>();

        Component infoText = CreateTmpTextComponent("InfoText");
        GameObject trafficLightControlsRoot = Track(new GameObject("TrafficLightControlsRoot"));
        trafficLightControlsRoot.SetActive(true);
        UnityEngine.UI.Button assignStopsButton = Track(new GameObject("AssignStopsButton")).AddComponent<UnityEngine.UI.Button>();
        assignStopsButton.gameObject.SetActive(true);

        SetPrivateField(infoPanel, "infoText", infoText);
        SetPrivateField(infoPanel, "trafficLightControlsRoot", trafficLightControlsRoot);
        SetPrivateField(infoPanel, "assignStopsButton", assignStopsButton);
        SetPrivateField(infoPanel, "cameraController", null);
        SetPrivateField(infoPanel, "emptySelectionText", "Nothing Selected");

        InvokePrivateMethod(infoPanel, "Awake");
        Assert.IsFalse(trafficLightControlsRoot.activeSelf);
        Assert.IsFalse(assignStopsButton.gameObject.activeSelf);
        Assert.IsFalse(assignStopsButton.interactable);

        InvokePrivateMethod(infoPanel, "OnEnable");
        Assert.AreEqual("Nothing Selected", GetTmpText(infoText));

        InvokePrivateMethod(infoPanel, "OnDisable");
    }

    [Test]
    public void BuildingInfoPanel_SelectionChanges_UpdateVehicleBuildingAndFallbackInfo()
    {
        GameObject infoPanelRoot = Track(new GameObject("BuildingInfoPanelRoot"));
        infoPanelRoot.SetActive(false);
        InfoPanel infoPanel = infoPanelRoot.AddComponent<InfoPanel>();

        Component infoText = CreateTmpTextComponent("InfoText");
        GameObject trafficLightControlsRoot = Track(new GameObject("TrafficLightControlsRoot"));
        UnityEngine.UI.Button assignStopsButton = Track(new GameObject("AssignStopsButton")).AddComponent<UnityEngine.UI.Button>();

        SetPrivateField(infoPanel, "infoText", infoText);
        SetPrivateField(infoPanel, "trafficLightControlsRoot", trafficLightControlsRoot);
        SetPrivateField(infoPanel, "assignStopsButton", assignStopsButton);
        SetPrivateField(infoPanel, "emptySelectionText", "Nothing Selected");
        SetPrivateField(infoPanel, "noEconomyText", "NO_ECONOMY");
        SetPrivateField(infoPanel, "noVehicleText", "NO_VEHICLE");
        SetPrivateField(infoPanel, "noTrafficLightText", "NO_TRAFFIC_LIGHT");
        SetPrivateField(infoPanel, "cameraController", null);
        InvokePrivateMethod(infoPanel, "Awake");

        VehicleAgent vehicle = Track(new GameObject("Vehicle")).AddComponent<VehicleAgent>();
        vehicle.Initialize(55, CargoType.Wood);
        InvokePrivateMethod(infoPanel, "HandleSelectionChanged", vehicle.gameObject);

        Assert.IsTrue(assignStopsButton.gameObject.activeSelf);
        Assert.IsTrue(assignStopsButton.interactable);
        Assert.IsFalse(trafficLightControlsRoot.activeSelf);
        StringAssert.Contains("Cargo type:", GetTmpText(infoText));

        GameObject unknown = Track(new GameObject("UnknownObject"));
        InvokePrivateMethod(infoPanel, "HandleSelectionChanged", unknown);
        string fallbackText = GetTmpText(infoText);
        StringAssert.Contains("UnknownObject", fallbackText);
        StringAssert.Contains("NO_ECONOMY", fallbackText);
        StringAssert.Contains("NO_VEHICLE", fallbackText);
        StringAssert.Contains("NO_TRAFFIC_LIGHT", fallbackText);

        GameObject buildingParent = Track(new GameObject("BuildingParent"));
        BuildingEconomy building = buildingParent.AddComponent<BuildingEconomy>();
        GameObject selectedChild = Track(new GameObject("SelectedChild"));
        selectedChild.transform.SetParent(buildingParent.transform, false);

        InvokePrivateMethod(infoPanel, "HandleSelectionChanged", selectedChild);
        Assert.AreEqual(building.GetInfoText(), GetTmpText(infoText));

        InvokePrivateMethod(infoPanel, "HandleSelectionChanged", new object[] { null });
        Assert.AreEqual("Nothing Selected", GetTmpText(infoText));
    }

    [Test]
    public void BuildingInfoPanel_TrafficLightSelectionAndDurationInputs_UpdateNodeAndLabels()
    {
        GameObject infoPanelRoot = Track(new GameObject("BuildingInfoPanelRoot"));
        infoPanelRoot.SetActive(false);
        InfoPanel infoPanel = infoPanelRoot.AddComponent<InfoPanel>();

        Component infoText = CreateTmpTextComponent("InfoText");
        Component primaryLabelText = CreateTmpTextComponent("PrimaryLabel");
        Component secondaryLabelText = CreateTmpTextComponent("SecondaryLabel");
        Component primaryInput = CreateTmpInputFieldComponent("PrimaryInput");
        Component secondaryInput = CreateTmpInputFieldComponent("SecondaryInput");
        GameObject trafficLightControlsRoot = Track(new GameObject("TrafficLightControlsRoot"));
        UnityEngine.UI.Button assignStopsButton = Track(new GameObject("AssignStopsButton")).AddComponent<UnityEngine.UI.Button>();

        SetPrivateField(infoPanel, "infoText", infoText);
        SetPrivateField(infoPanel, "primaryPhaseLabelText", primaryLabelText);
        SetPrivateField(infoPanel, "secondaryPhaseLabelText", secondaryLabelText);
        SetPrivateField(infoPanel, "primaryPhaseDurationInput", primaryInput);
        SetPrivateField(infoPanel, "secondaryPhaseDurationInput", secondaryInput);
        SetPrivateField(infoPanel, "trafficLightControlsRoot", trafficLightControlsRoot);
        SetPrivateField(infoPanel, "assignStopsButton", assignStopsButton);
        SetPrivateField(infoPanel, "minTrafficLightPhaseDuration", 1f);
        SetPrivateField(infoPanel, "maxTrafficLightPhaseDuration", 10f);
        SetPrivateField(infoPanel, "cameraController", null);
        SetPrivateField(infoPanel, "liveUpdateInterval", 0.01f);
        SetPrivateField(infoPanel, "liveUpdateWhileSelected", true);

        InvokePrivateMethod(infoPanel, "Awake");
        InvokePrivateMethod(infoPanel, "OnEnable");

        TrafficLightNode trafficLightNode = Track(new GameObject("TrafficLightNode")).AddComponent<TrafficLightNode>();
        trafficLightNode.Initialize(10, Vector3Int.zero, "Main TL");
        trafficLightNode.ConfigureAllowedDirections(
            RoadDirectionMask.North | RoadDirectionMask.East | RoadDirectionMask.South | RoadDirectionMask.West);

        InvokePrivateMethod(infoPanel, "HandleSelectionChanged", trafficLightNode.gameObject);

        Assert.IsTrue(trafficLightControlsRoot.activeSelf);
        Assert.IsFalse(assignStopsButton.gameObject.activeSelf);
        Assert.AreEqual("N/S", GetTmpText(primaryLabelText));
        Assert.AreEqual("E/W", GetTmpText(secondaryLabelText));
        Assert.IsTrue(GetSelectableInteractable(primaryInput));
        Assert.IsTrue(GetSelectableInteractable(secondaryInput));

        InvokePrivateMethod(infoPanel, "HandlePrimaryPhaseDurationChanged", "0.5");
        Assert.AreEqual(1f, trafficLightNode.GetPrimaryGreenDurationSeconds(), 0.0001f);

        InvokePrivateMethod(infoPanel, "HandleSecondaryPhaseDurationChanged", "999");
        Assert.AreEqual(10f, trafficLightNode.GetSecondaryGreenDurationSeconds(), 0.0001f);

        InvokePrivateMethod(infoPanel, "HandleSecondaryPhaseDurationChanged", "not_a_number");
        Assert.AreEqual(10f, trafficLightNode.GetSecondaryGreenDurationSeconds(), 0.0001f);

        InvokePrivateMethod(infoPanel, "Update");
        StringAssert.Contains("Active phase:", GetTmpText(infoText));

        InvokePrivateMethod(infoPanel, "OnDisable");
    }

    private RouteManager CreateRouteManagerContext(
        out StopManager stopManager,
        out RoadNetworkManager roadNetworkManager,
        out CameraController cameraController,
        out Grid grid)
    {
        stopManager = CreateStopManagerWithRoadNetwork(out roadNetworkManager, out grid, out _);
        cameraController = Track(new GameObject("CameraController")).AddComponent<CameraController>();

        RouteManager routeManager = Track(new GameObject("RouteManager")).AddComponent<RouteManager>();
        SetPrivateField(routeManager, "roadNetworkManager", roadNetworkManager);
        SetPrivateField(routeManager, "stopManager", stopManager);
        SetPrivateField(routeManager, "cameraController", cameraController);
        SetPrivateField(routeManager, "grid", grid);
        SetPrivateField(routeManager, "addSelectedStopsAutomatically", true);
        SetPrivateField(routeManager, "stopStopPlacementWhenDrafting", true);
        return routeManager;
    }

    private StopManager CreateStopManagerWithRoadNetwork(
        out RoadNetworkManager roadNetworkManager,
        out Grid grid,
        out InputManager inputManager)
    {
        grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        GridMap gridMap = Track(new GameObject("GridMap")).AddComponent<GridMap>();

        roadNetworkManager = Track(new GameObject("RoadNetworkManager")).AddComponent<RoadNetworkManager>();
        SetPrivateField(roadNetworkManager, "grid", grid);
        SetPrivateField(roadNetworkManager, "gridMap", gridMap);
        SetPrivateField(roadNetworkManager, "useAutoRoadStep", false);
        SetPrivateField(roadNetworkManager, "manualRoadStep", 1);
        SetPrivateField(roadNetworkManager, "importPresetRoadsFromTag", false);
        InvokePrivateMethodIfExists(roadNetworkManager, "OnValidate");
        roadNetworkManager.ClearAllRoads();

        inputManager = Track(new GameObject("InputManager")).AddComponent<InputManager>();
        GameObject stopSignPrefab = Track(new GameObject("StopSignPrefab"));

        StopManager stopManager = Track(new GameObject("StopManager")).AddComponent<StopManager>();
        SetPrivateField(stopManager, "inputManager", inputManager);
        SetPrivateField(stopManager, "grid", grid);
        SetPrivateField(stopManager, "roadNetworkManager", roadNetworkManager);
        SetPrivateField(stopManager, "gridMap", gridMap);
        SetPrivateField(stopManager, "stopSignPrefab", stopSignPrefab);
        SetPrivateField(stopManager, "addSelectionColliderIfMissing", false);
        SetPrivateField(stopManager, "noStopZoneMask", (LayerMask)0);
        return stopManager;
    }

    private StopBuildToolUI CreateStopBuildToolUI(StopManager stopManager, GameObject stopPanel)
    {
        StopBuildToolUI ui = Track(new GameObject("StopBuildToolUI")).AddComponent<StopBuildToolUI>();
        SetPrivateField(ui, "stopManager", stopManager);
        SetPrivateField(ui, "stopPanel", stopPanel);
        SetPrivateField(ui, "closePanelAfterSelection", true);
        SetPrivateField(ui, "hidePanelOnStart", false);
        return ui;
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

    private void SetCameraSelection(CameraController cameraController, GameObject selectedObject)
    {
        FieldInfo selectedBackingField = typeof(CameraController).GetField("<SelectedObject>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(selectedBackingField);
        selectedBackingField.SetValue(cameraController, selectedObject);

        Assert.AreSame(selectedObject, cameraController.SelectedObject);
    }

    private static void InvokePrivateMethodIfExists(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }

    private static object InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        return method.Invoke(target, args);
    }

    private Component CreateTmpTextComponent(string name)
    {
        System.Type textType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        Assert.IsNotNull(textType, "TextMeshProUGUI type not found.");
        return Track(new GameObject(name)).AddComponent(textType);
    }

    private Component CreateTmpInputFieldComponent(string name)
    {
        System.Type inputType = System.Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
        Assert.IsNotNull(inputType, "TMP_InputField type not found.");

        GameObject inputGo = Track(new GameObject(name));
        Component input = inputGo.AddComponent(inputType);

        Component textComponent = CreateTmpTextComponent($"{name}_Text");
        textComponent.transform.SetParent(inputGo.transform, false);

        PropertyInfo textComponentProperty = inputType.GetProperty("textComponent", BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(textComponentProperty, "TMP_InputField.textComponent property not found.");
        textComponentProperty.SetValue(input, textComponent);
        return input;
    }

    private static string GetTmpText(Component textComponent)
    {
        PropertyInfo textProperty = textComponent.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(textProperty, "TMP text property not found.");
        return (string)textProperty.GetValue(textComponent);
    }

    private static bool GetSelectableInteractable(Component selectableComponent)
    {
        PropertyInfo interactableProperty = selectableComponent.GetType().GetProperty("interactable", BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(interactableProperty, "Selectable.interactable property not found.");
        return (bool)interactableProperty.GetValue(selectableComponent);
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
}
