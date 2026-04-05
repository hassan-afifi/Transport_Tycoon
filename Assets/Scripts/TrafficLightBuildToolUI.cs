using UnityEngine;

public class TrafficLightBuildToolUI : MonoBehaviour
{
    [SerializeField] private TrafficLightManager trafficLightManager;
    [SerializeField] private PlacementSystem roadPlacementSystem;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private VehiclePlacementTool vehiclePlacementTool;
    [SerializeField] private RoadBuildToolUI roadBuildToolUI;
    [SerializeField] private StopBuildToolUI stopBuildToolUI;
    [SerializeField] private VehicleBuildToolUI vehicleBuildToolUI;
    [SerializeField] private VehicleStopAssignPanel vehicleStopAssignPanel;

    private void Awake()
    {
        SceneReferenceUtility.ResolveIfNull(ref trafficLightManager);
        SceneReferenceUtility.ResolveIfNull(ref roadPlacementSystem);
        SceneReferenceUtility.ResolveIfNull(ref stopManager);
        SceneReferenceUtility.ResolveIfNull(ref vehiclePlacementTool);
        SceneReferenceUtility.ResolveIfNull(ref roadBuildToolUI);
        SceneReferenceUtility.ResolveIfNull(ref stopBuildToolUI);
        SceneReferenceUtility.ResolveIfNull(ref vehicleBuildToolUI);
        SceneReferenceUtility.ResolveIfNull(ref vehicleStopAssignPanel);
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
            vehiclePlacementTool,
            roadBuildToolUI,
            stopBuildToolUI,
            vehicleBuildToolUI,
            vehicleStopAssignPanel);
    }
}
