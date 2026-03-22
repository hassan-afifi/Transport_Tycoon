using UnityEngine;

public class RoadBuildToolUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private placementSystem placementSystem;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private VehiclePlacementTool vehiclePlacementTool;
    [SerializeField] private VehicleBuildToolUI vehicleBuildToolUI;
    [SerializeField] private StopBuildToolUI stopBuildToolUI;
    [SerializeField] private VehicleStopAssignPanel vehicleStopAssignPanel;
    [SerializeField] private GameObject roadTypePanel;

    [Header("Road IDs (from ObjectDatabaseSO)")]
    [SerializeField] private int straightRoadId = 0;
    [SerializeField] private int turnRoadId = 1;
    [SerializeField] private int tIntersectionRoadId = 2;
    [SerializeField] private int fourWayRoadId = 3;

    [Header("Behavior")]
    [SerializeField] private bool closePanelAfterSelection = true;
    [SerializeField] private bool hidePanelOnStart = true;

    private void Awake()
    {
        if (stopManager == null)
        {
            stopManager = FindFirstObjectByType<StopManager>();
        }

        if (vehiclePlacementTool == null)
        {
            vehiclePlacementTool = FindFirstObjectByType<VehiclePlacementTool>();
        }

        if (vehicleBuildToolUI == null)
        {
            vehicleBuildToolUI = FindFirstObjectByType<VehicleBuildToolUI>();
        }

        if (stopBuildToolUI == null)
        {
            stopBuildToolUI = FindFirstObjectByType<StopBuildToolUI>();
        }

        if (vehicleStopAssignPanel == null)
        {
            vehicleStopAssignPanel = FindFirstObjectByType<VehicleStopAssignPanel>();
        }
    }

    private void Start()
    {
        if (hidePanelOnStart && roadTypePanel != null)
        {
            roadTypePanel.SetActive(false);
        }
    }

    public void ToggleRoadPanel()
    {
        StopOtherToolsAndClosePanels();

        if (placementSystem != null && placementSystem.IsPlacing)
        {
            placementSystem.StopPlacement();
            if (roadTypePanel != null)
            {
                roadTypePanel.SetActive(true);
            }
            return;
        }

        if (roadTypePanel == null)
        {
            return;
        }

        roadTypePanel.SetActive(!roadTypePanel.activeSelf);
    }

    public void OpenRoadPanel()
    {
        StopOtherToolsAndClosePanels();

        if (roadTypePanel != null)
        {
            roadTypePanel.SetActive(true);
        }
    }

    public void CloseRoadPanel()
    {
        if (roadTypePanel != null)
        {
            roadTypePanel.SetActive(false);
        }
    }

    public void SelectStraightRoad()
    {
        SelectRoad(straightRoadId);
    }

    public void SelectTurnRoad()
    {
        SelectRoad(turnRoadId);
    }

    public void SelectTIntersectionRoad()
    {
        SelectRoad(tIntersectionRoadId);
    }

    public void SelectFourWayRoad()
    {
        SelectRoad(fourWayRoadId);
    }

    public void CancelRoadPlacement()
    {
        StopOtherToolsAndClosePanels();

        if (placementSystem != null)
        {
            placementSystem.StopPlacement();
        }

        CloseRoadPanel();
    }

    private void SelectRoad(int objectId)
    {
        StopOtherToolsAndClosePanels();

        if (placementSystem == null)
        {
            return;
        }

        placementSystem.StartPlacement(objectId);

        if (closePanelAfterSelection && roadTypePanel != null)
        {
            roadTypePanel.SetActive(false);
        }
    }

    private void StopOtherToolsAndClosePanels()
    {
        if (stopManager != null)
        {
            stopManager.EndStopPlacement();
        }

        if (vehiclePlacementTool != null)
        {
            vehiclePlacementTool.EndPlacement();
        }

        if (vehicleBuildToolUI != null)
        {
            vehicleBuildToolUI.CloseVehiclePanel();
        }

        if (stopBuildToolUI != null)
        {
            stopBuildToolUI.CloseStopPanel();
        }

        if (vehicleStopAssignPanel != null)
        {
            vehicleStopAssignPanel.ClosePanel();
        }
    }
}
