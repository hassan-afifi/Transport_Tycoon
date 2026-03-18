using UnityEngine;

public class StopBuildToolUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StopManager stopManager;
    [SerializeField] private placementSystem roadPlacementSystem;
    [SerializeField] private VehiclePlacementTool vehiclePlacementTool;
    [SerializeField] private RoadBuildToolUI roadBuildToolUI;
    [SerializeField] private VehicleBuildToolUI vehicleBuildToolUI;
    [SerializeField] private GameObject stopPanel;

    [Header("Behavior")]
    [SerializeField] private bool closePanelAfterSelection = true;
    [SerializeField] private bool hidePanelOnStart = true;

    private void Awake()
    {
        if (stopManager == null)
        {
            stopManager = FindFirstObjectByType<StopManager>();
        }

        if (roadPlacementSystem == null)
        {
            roadPlacementSystem = FindFirstObjectByType<placementSystem>();
        }

        if (vehiclePlacementTool == null)
        {
            vehiclePlacementTool = FindFirstObjectByType<VehiclePlacementTool>();
        }

        if (roadBuildToolUI == null)
        {
            roadBuildToolUI = FindFirstObjectByType<RoadBuildToolUI>();
        }

        if (vehicleBuildToolUI == null)
        {
            vehicleBuildToolUI = FindFirstObjectByType<VehicleBuildToolUI>();
        }
    }

    private void Start()
    {
        if (hidePanelOnStart && stopPanel != null)
        {
            stopPanel.SetActive(false);
        }
    }

    public void ToggleStopPanel()
    {
        StopOtherToolsAndClosePanels();

        if (stopManager == null)
        {
            return;
        }

        if (stopPanel == null)
        {
            if (stopManager.IsStopPlacementActive)
            {
                stopManager.EndStopPlacement();
            }
            else
            {
                stopManager.BeginStopPlacement();
            }

            return;
        }

        if (stopManager != null && stopManager.IsStopPlacementActive)
        {
            stopManager.EndStopPlacement();
            if (stopPanel != null)
            {
                stopPanel.SetActive(true);
            }
            return;
        }

        stopPanel.SetActive(!stopPanel.activeSelf);
    }

    public void OpenStopPanel()
    {
        StopOtherToolsAndClosePanels();

        if (stopManager == null)
        {
            return;
        }

        if (stopPanel == null)
        {
            if (stopManager.IsStopPlacementActive)
            {
                stopManager.EndStopPlacement();
            }
            else
            {
                stopManager.BeginStopPlacement();
            }

            return;
        }

        if (stopPanel != null)
        {
            stopPanel.SetActive(true);
        }
    }

    public void CloseStopPanel()
    {
        if (stopPanel != null)
        {
            stopPanel.SetActive(false);
        }
    }

    public void SelectStopPlacement()
    {
        StopOtherToolsAndClosePanels();

        if (stopManager == null)
        {
            return;
        }

        if (stopManager.IsStopPlacementActive)
        {
            stopManager.EndStopPlacement();
            return;
        }

        stopManager.BeginStopPlacement();

        if (closePanelAfterSelection && stopPanel != null)
        {
            stopPanel.SetActive(false);
        }
    }

    public void CancelStopPlacement()
    {
        if (stopManager != null)
        {
            stopManager.EndStopPlacement();
        }

        CloseStopPanel();
    }

    private void StopOtherToolsAndClosePanels()
    {
        if (roadPlacementSystem != null)
        {
            roadPlacementSystem.StopPlacement();
        }

        if (vehiclePlacementTool != null)
        {
            vehiclePlacementTool.EndPlacement();
        }

        if (roadBuildToolUI != null)
        {
            roadBuildToolUI.CloseRoadPanel();
        }

        if (vehicleBuildToolUI != null)
        {
            vehicleBuildToolUI.CloseVehiclePanel();
        }
    }
}
