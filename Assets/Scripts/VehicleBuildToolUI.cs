using UnityEngine;

public class VehicleBuildToolUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VehiclePlacementTool vehiclePlacementTool;
    [SerializeField] private placementSystem roadPlacementSystem;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private RoadBuildToolUI roadBuildToolUI;
    [SerializeField] private StopBuildToolUI stopBuildToolUI;
    [SerializeField] private GameObject vehicleTypePanel;

    [Header("Behavior")]
    [SerializeField] private bool closePanelAfterSelection = true;
    [SerializeField] private bool hidePanelOnStart = true;

    private void Awake()
    {
        if (vehiclePlacementTool == null)
        {
            vehiclePlacementTool = FindFirstObjectByType<VehiclePlacementTool>();
        }

        if (roadPlacementSystem == null)
        {
            roadPlacementSystem = FindFirstObjectByType<placementSystem>();
        }

        if (stopManager == null)
        {
            stopManager = FindFirstObjectByType<StopManager>();
        }

        if (roadBuildToolUI == null)
        {
            roadBuildToolUI = FindFirstObjectByType<RoadBuildToolUI>();
        }

        if (stopBuildToolUI == null)
        {
            stopBuildToolUI = FindFirstObjectByType<StopBuildToolUI>();
        }
    }

    private void Start()
    {
        if (hidePanelOnStart && vehicleTypePanel != null)
        {
            vehicleTypePanel.SetActive(false);
        }
    }

    public void ToggleVehiclePanel()
    {
        StopOtherPlacements();

        if (vehiclePlacementTool != null && vehiclePlacementTool.IsPlacementActive)
        {
            vehiclePlacementTool.EndPlacement();
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

    public void SelectBus()
    {
        SelectCargo(CargoType.Passengers);
    }

    public void SelectIronContainer()
    {
        SelectCargo(CargoType.Iron);
    }

    public void SelectSteelTruck()
    {
        SelectCargo(CargoType.Steel);
    }

    public void SelectWoodTruck()
    {
        SelectCargo(CargoType.Wood);
    }

    public void SelectPaperContainer()
    {
        SelectCargo(CargoType.Paper);
    }

    public void SelectFurniturePickupTruck()
    {
        SelectCargo(CargoType.Furniture);
    }

    public void CancelVehiclePlacement()
    {
        if (vehiclePlacementTool != null)
        {
            vehiclePlacementTool.EndPlacement();
        }

        CloseVehiclePanel();
    }

    private void SelectCargo(CargoType cargoType)
    {
        StopOtherPlacements();

        if (vehiclePlacementTool == null)
        {
            return;
        }

        vehiclePlacementTool.BeginPlacement(cargoType);

        if (closePanelAfterSelection && vehicleTypePanel != null)
        {
            vehicleTypePanel.SetActive(false);
        }
    }

    private void StopOtherPlacements()
    {
        if (roadPlacementSystem != null)
        {
            roadPlacementSystem.StopPlacement();
        }

        if (stopManager != null)
        {
            stopManager.EndStopPlacement();
        }

        if (roadBuildToolUI != null)
        {
            roadBuildToolUI.CloseRoadPanel();
        }

        if (stopBuildToolUI != null)
        {
            stopBuildToolUI.CloseStopPanel();
        }
    }
}
