using UnityEngine;

public class StopBuildToolUI : MonoBehaviour
{
    [SerializeField] private StopManager stopManager;
    [SerializeField] private TrafficLightManager trafficLightManager;
    [SerializeField] private PlacementSystem roadPlacementSystem;
    [SerializeField] private VehiclePlacementTool vehiclePlacementTool;
    [SerializeField] private RoadBuildToolUI roadBuildToolUI;
    [SerializeField] private VehicleBuildToolUI vehicleBuildToolUI;
    [SerializeField] private VehicleStopAssignPanel vehicleStopAssignPanel;
    [SerializeField] private GameObject stopPanel;
    [SerializeField] private bool closePanelAfterSelection = true;
    [SerializeField] private bool hidePanelOnStart = true;

    private void Awake()
    {
        SceneReferenceUtility.ResolveIfNull(ref stopManager);
        SceneReferenceUtility.ResolveIfNull(ref roadPlacementSystem);
        SceneReferenceUtility.ResolveIfNull(ref trafficLightManager);
        SceneReferenceUtility.ResolveIfNull(ref vehiclePlacementTool);
        SceneReferenceUtility.ResolveIfNull(ref roadBuildToolUI);
        SceneReferenceUtility.ResolveIfNull(ref vehicleBuildToolUI);
        SceneReferenceUtility.ResolveIfNull(ref vehicleStopAssignPanel);
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
        ToolModeCoordinator.StopOtherModes(
            ToolModeKind.Stop,
            roadPlacementSystem,
            stopManager,
            trafficLightManager,
            vehiclePlacementTool,
            roadBuildToolUI,
            this,
            vehicleBuildToolUI,
            vehicleStopAssignPanel);
    }
}
