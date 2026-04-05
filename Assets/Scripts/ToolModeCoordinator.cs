public enum ToolModeKind
{
    Road = 0,
    Stop = 1,
    TrafficLight = 2,
    Vehicle = 3
}

public static class ToolModeCoordinator
{
    public static void StopOtherModes(
        ToolModeKind requester,
        PlacementSystem roadPlacementSystem,
        StopManager stopManager,
        TrafficLightManager trafficLightManager,
        VehiclePlacementTool vehiclePlacementTool,
        RoadBuildToolUI roadBuildToolUI,
        StopBuildToolUI stopBuildToolUI,
        VehicleBuildToolUI vehicleBuildToolUI,
        VehicleStopAssignPanel vehicleStopAssignPanel)
    {
        if (requester != ToolModeKind.Road)
        {
            roadPlacementSystem?.StopPlacement();
        }

        if (requester != ToolModeKind.Stop)
        {
            stopManager?.EndStopPlacement();
        }

        if (requester != ToolModeKind.TrafficLight)
        {
            trafficLightManager?.EndPlacement();
        }

        if (requester != ToolModeKind.Vehicle)
        {
            vehiclePlacementTool?.EndPlacement();
        }

        if (requester != ToolModeKind.Road)
        {
            roadBuildToolUI?.CloseRoadPanel();
        }

        if (requester != ToolModeKind.Stop)
        {
            stopBuildToolUI?.CloseStopPanel();
        }

        if (requester != ToolModeKind.Vehicle)
        {
            vehicleBuildToolUI?.CloseVehiclePanel();
        }

        vehicleStopAssignPanel?.ClosePanel();
    }
}
