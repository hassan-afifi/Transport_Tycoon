using UnityEngine;

public class TrafficLightBuildToolUI : MonoBehaviour
{
    [SerializeField] private TrafficLightManager trafficLightManager;
    [SerializeField] private PlacementSystem roadPlacementSystem;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private RoadBuildToolUI roadBuildToolUI;
    [SerializeField] private StopBuildToolUI stopBuildToolUI;
    [SerializeField] private VehicleBuildToolUI vehicleBuildToolUI;
    [SerializeField] private VehicleStopAssignPanel vehicleStopAssignPanel;

    private void Awake()
    {
        CoreUtility.ResolveIfNull(ref trafficLightManager);
        CoreUtility.ResolveIfNull(ref roadPlacementSystem);
        CoreUtility.ResolveIfNull(ref stopManager);
        CoreUtility.ResolveIfNull(ref roadBuildToolUI);
        CoreUtility.ResolveIfNull(ref stopBuildToolUI);
        CoreUtility.ResolveIfNull(ref vehicleBuildToolUI);
        CoreUtility.ResolveIfNull(ref vehicleStopAssignPanel);
    }

    public void ToggleTrafficLightPlacement()
    {
        if (trafficLightManager != null && trafficLightManager.IsPlacementActive)
        {
            trafficLightManager.EndPlacement();
            return;
        }

        BeginTrafficLightPlacement();
    }

    public void BeginTrafficLightPlacement()
    {
        StopOtherPlacementsAndClosePanels();
        trafficLightManager?.BeginPlacement();
    }

    public void CancelTrafficLightPlacement()
    {
        if (trafficLightManager != null)
        {
            trafficLightManager.EndPlacement();
        }
    }

    private void StopOtherPlacementsAndClosePanels()
    {
        ToolModeCoordinator.StopOtherModes(
            ToolModeKind.TrafficLight,
            roadPlacementSystem,
            stopManager,
            trafficLightManager,
            roadBuildToolUI,
            stopBuildToolUI,
            vehicleBuildToolUI,
            vehicleStopAssignPanel);
    }
}
