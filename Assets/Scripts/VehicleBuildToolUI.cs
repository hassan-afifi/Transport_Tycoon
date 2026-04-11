using UnityEngine;

public class VehicleBuildToolUI : MonoBehaviour
{
    [SerializeField] private VehicleManager vehicleManager;
    [SerializeField] private PlacementSystem roadPlacementSystem;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private TrafficLightManager trafficLightManager;
    [SerializeField] private RoadBuildToolUI roadBuildToolUI;
    [SerializeField] private StopBuildToolUI stopBuildToolUI;
    [SerializeField] private VehicleStopAssignPanel vehicleStopAssignPanel;
    [SerializeField] private GameObject vehicleTypePanel;
    [SerializeField] private bool closePanelAfterSelection = true;
    [SerializeField] private bool hidePanelOnStart = true;

    public bool IsPlacementActive => vehicleManager != null && vehicleManager.IsPlacementActive;
    public CargoType SelectedCargoType => vehicleManager != null ? vehicleManager.SelectedCargoType : CargoType.None;

    private void Awake()
    {
        CoreUtility.ResolveIfNull(ref vehicleManager);
        CoreUtility.ResolveIfNull(ref roadPlacementSystem);
        CoreUtility.ResolveIfNull(ref stopManager);
        CoreUtility.ResolveIfNull(ref trafficLightManager);
        CoreUtility.ResolveIfNull(ref roadBuildToolUI);
        CoreUtility.ResolveIfNull(ref stopBuildToolUI);
        CoreUtility.ResolveIfNull(ref vehicleStopAssignPanel);
    }

    private void Start()
    {
        if (hidePanelOnStart && vehicleTypePanel != null)
        {
            vehicleTypePanel.SetActive(false);
        }
    }

    private void OnDisable()
    {
        EndPlacement();
    }

    public void ToggleVehiclePanel()
    {
        StopOtherPlacements();

        if (IsPlacementActive)
        {
            EndPlacement();
            if (vehicleTypePanel != null)
            {
                vehicleTypePanel.SetActive(true);
            }

            return;
        }

        if (vehicleTypePanel == null)
        {
            return;
        }

        vehicleTypePanel.SetActive(!vehicleTypePanel.activeSelf);
    }

    public void OpenVehiclePanel()
    {
        StopOtherPlacements();

        if (vehicleTypePanel != null)
        {
            vehicleTypePanel.SetActive(true);
        }
    }

    public void CloseVehiclePanel()
    {
        if (vehicleTypePanel != null)
        {
            vehicleTypePanel.SetActive(false);
        }
    }

    public void SelectCargo(int cargoTypeValue)
    {
        CargoType cargoType = (CargoType)cargoTypeValue;
        if (cargoType == CargoType.None || !System.Enum.IsDefined(typeof(CargoType), cargoType))
        {
            return;
        }

        SelectCargoInternal(cargoType);
    }

    public void CancelVehiclePlacement()
    {
        EndPlacement();
        CloseVehiclePanel();
    }

    public void TogglePlacement(CargoType cargoType)
    {
        if (vehicleManager == null)
        {
            return;
        }

        if (IsPlacementActive && SelectedCargoType == cargoType)
        {
            EndPlacement();
            return;
        }

        BeginPlacement(cargoType);
    }

    public void BeginPlacement(CargoType cargoType)
    {
        StopOtherPlacements();
        vehicleManager?.BeginPlacement(cargoType);
    }

    public void EndPlacement()
    {
        vehicleManager?.EndPlacement();
    }

    public void AssignLatestRouteToAllVehicles()
    {
        vehicleManager?.AssignLatestRouteToAllVehicles();
    }

    public void AssignAllStopsToAllVehicles()
    {
        vehicleManager?.AssignAllStopsToAllVehicles();
    }

    private void SelectCargoInternal(CargoType cargoType)
    {
        StopOtherPlacements();
        vehicleManager?.BeginPlacement(cargoType);

        if (closePanelAfterSelection && vehicleTypePanel != null)
        {
            vehicleTypePanel.SetActive(false);
        }
    }

    private void StopOtherPlacements()
    {
        ToolModeCoordinator.StopOtherModes(
            ToolModeKind.Vehicle,
            roadPlacementSystem,
            stopManager,
            trafficLightManager,
            roadBuildToolUI,
            stopBuildToolUI,
            this,
            vehicleStopAssignPanel);
    }
}
