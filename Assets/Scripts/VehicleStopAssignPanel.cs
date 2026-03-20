using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class VehicleStopAssignPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VehicleManager vehicleManager;
    [SerializeField] private StopManager stopManager;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private RectTransform selectedStopsListRoot;
    [SerializeField] private RectTransform allStopsListRoot;
    [SerializeField] private VehicleSelectedStopItemUI selectedStopItemPrefab;
    [SerializeField] private VehicleStopToggleItemUI allStopsToggleItemPrefab;
    [SerializeField] private Canvas dragCanvas;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text assignedStopsText;

    [Header("Rules")]
    [SerializeField, Min(2)] private int minimumStopsRequired = 2;
    [SerializeField] private bool hidePanelOnStart = true;

    [Header("Text")]
    [SerializeField] private string noVehicleText = "No vehicle selected";
    [SerializeField] private string emptyAssignedStopsText = "Assigned stops:";

    private readonly List<int> availableStopIds = new();
    private readonly List<int> workingAssignedStopIds = new();
    private readonly List<VehicleStopToggleItemUI> allStopItems = new();

    private int currentVehicleId = -1;
    private bool removeVehicleIfInvalidOnClose;
    private bool hasBeenOpened;

    private VehicleSelectedStopItemUI draggingItem;
    private RectTransform draggingPlaceholder;
    private Transform dragRoot;

    private void Awake()
    {
        if (vehicleManager == null)
        {
            vehicleManager = FindFirstObjectByType<VehicleManager>();
        }

        if (stopManager == null)
        {
            stopManager = FindFirstObjectByType<StopManager>();
        }

        if (dragCanvas == null)
        {
            dragCanvas = GetComponentInParent<Canvas>();
        }

        dragRoot = dragCanvas != null ? dragCanvas.transform : transform;
    }

    private void OnEnable()
    {
        if (stopManager != null)
        {
            stopManager.StopsChanged += HandleStopsChanged;
        }

        if (vehicleManager != null)
        {
            vehicleManager.VehicleRemoved += HandleVehicleRemoved;
        }
    }

    private void OnDisable()
    {
        if (stopManager != null)
        {
            stopManager.StopsChanged -= HandleStopsChanged;
        }

        if (vehicleManager != null)
        {
            vehicleManager.VehicleRemoved -= HandleVehicleRemoved;
        }

        CancelDrag();
    }

    private void Start()
    {
        if (hidePanelOnStart && !hasBeenOpened)
        {
            SetPanelVisible(false);
        }

        RefreshListsAndText();
    }

    public void OpenForVehicle(VehicleAgent vehicle, bool requireMinimumOnClose = false)
    {
        if (vehicle == null)
        {
            return;
        }

        OpenForVehicleId(vehicle.VehicleId, requireMinimumOnClose);
    }

    public void OpenForVehicleId(int vehicleId, bool requireMinimumOnClose = false)
    {
        if (vehicleId <= 0 || vehicleManager == null)
        {
            return;
        }

        if (!vehicleManager.TryGetVehicle(vehicleId, out VehicleAgent vehicle) || vehicle == null)
        {
            return;
        }

        if (currentVehicleId > 0 && currentVehicleId != vehicleId)
        {
            FinalizeCurrentVehicle(false);
        }

        currentVehicleId = vehicleId;
        removeVehicleIfInvalidOnClose = requireMinimumOnClose;
        hasBeenOpened = true;

        workingAssignedStopIds.Clear();
        for (int i = 0; i < vehicle.AssignedStopIds.Count; i++)
        {
            int stopId = vehicle.AssignedStopIds[i];
            if (!workingAssignedStopIds.Contains(stopId))
            {
                workingAssignedStopIds.Add(stopId);
            }
        }

        SetPanelVisible(true);
        RefreshListsAndText();
    }

    public void ApplyAssignments()
    {
        ApplyWorkingStopsToCurrentVehicle();
        RefreshAssignedStopsText();
    }

    public void ApplyAndClose()
    {
        FinalizeCurrentVehicle(true);
    }

    public void ClosePanel()
    {
        FinalizeCurrentVehicle(true);
    }

    public void ClearSelectedStops()
    {
        if (workingAssignedStopIds.Count == 0)
        {
            return;
        }

        workingAssignedStopIds.Clear();
        SyncAllTogglesFromSelection();
        RefreshSelectedStopsList();
        ApplyWorkingStopsToCurrentVehicle();
        RefreshAssignedStopsText();
    }

    public void HandleStopToggleChanged(int stopId, bool isOn)
    {
        if (stopId <= 0)
        {
            return;
        }

        if (isOn)
        {
            if (!workingAssignedStopIds.Contains(stopId))
            {
                workingAssignedStopIds.Add(stopId);
            }
        }
        else
        {
            workingAssignedStopIds.Remove(stopId);
        }

        RefreshSelectedStopsList();
        ApplyWorkingStopsToCurrentVehicle();
        RefreshAssignedStopsText();
    }

    public void BeginDragSelectedItem(VehicleSelectedStopItemUI item, PointerEventData eventData)
    {
        if (item == null || selectedStopsListRoot == null || dragRoot == null)
        {
            return;
        }

        CancelDrag();

        draggingItem = item;
        draggingItem.SetDraggingVisual(true);
        CreatePlaceholder(item);
        item.transform.SetParent(dragRoot, true);
        item.RectTransform.position = eventData.position;
    }

    public void DragSelectedItem(VehicleSelectedStopItemUI item, PointerEventData eventData)
    {
        if (draggingItem == null || draggingItem != item || selectedStopsListRoot == null)
        {
            return;
        }

        draggingItem.RectTransform.position = eventData.position;

        int newIndex = selectedStopsListRoot.childCount;
        for (int i = 0; i < selectedStopsListRoot.childCount; i++)
        {
            Transform child = selectedStopsListRoot.GetChild(i);
            if (child == draggingPlaceholder)
            {
                continue;
            }

            RectTransform childRect = child as RectTransform;
            if (childRect == null)
            {
                continue;
            }

            if (eventData.position.x < childRect.position.x)
            {
                newIndex = i;
                break;
            }
        }

        if (draggingPlaceholder != null)
        {
            draggingPlaceholder.SetSiblingIndex(newIndex);
        }
    }

    public void EndDragSelectedItem(VehicleSelectedStopItemUI item, PointerEventData eventData)
    {
        if (draggingItem == null || draggingItem != item || selectedStopsListRoot == null)
        {
            return;
        }

        int finalIndex = draggingPlaceholder != null ? draggingPlaceholder.GetSiblingIndex() : selectedStopsListRoot.childCount;
        item.transform.SetParent(selectedStopsListRoot, false);
        item.transform.SetSiblingIndex(finalIndex);
        item.RectTransform.anchoredPosition = Vector2.zero;
        item.SetDraggingVisual(false);

        if (draggingPlaceholder != null)
        {
            Destroy(draggingPlaceholder.gameObject);
        }

        draggingPlaceholder = null;
        draggingItem = null;

        SyncSelectionOrderFromUi();
        ApplyWorkingStopsToCurrentVehicle();
        RefreshAssignedStopsText();
    }

    private void HandleStopsChanged()
    {
        bool removedAny = false;
        for (int i = workingAssignedStopIds.Count - 1; i >= 0; i--)
        {
            int stopId = workingAssignedStopIds[i];
            if (stopManager != null && stopManager.TryGetStopById(stopId, out _))
            {
                continue;
            }

            workingAssignedStopIds.RemoveAt(i);
            removedAny = true;
        }

        if (removedAny)
        {
            ApplyWorkingStopsToCurrentVehicle();
        }

        RefreshListsAndText();
    }

    private void HandleVehicleRemoved(VehicleAgent vehicle)
    {
        if (vehicle == null || vehicle.VehicleId != currentVehicleId)
        {
            return;
        }

        currentVehicleId = -1;
        removeVehicleIfInvalidOnClose = false;
        workingAssignedStopIds.Clear();
        CancelDrag();
        SetPanelVisible(false);
        RefreshListsAndText();
    }

    private void FinalizeCurrentVehicle(bool hidePanel)
    {
        bool hasVehicle = currentVehicleId > 0;
        bool hasEnoughStops = workingAssignedStopIds.Count >= minimumStopsRequired;
        if (hasVehicle)
        {
            ApplyWorkingStopsToCurrentVehicle();
        }

        if (hasVehicle && removeVehicleIfInvalidOnClose && !hasEnoughStops && vehicleManager != null)
        {
            vehicleManager.RemoveVehicle(currentVehicleId);
        }

        currentVehicleId = -1;
        removeVehicleIfInvalidOnClose = false;
        workingAssignedStopIds.Clear();
        CancelDrag();

        if (hidePanel)
        {
            SetPanelVisible(false);
        }

        RefreshListsAndText();
    }

    private bool ApplyWorkingStopsToCurrentVehicle()
    {
        if (currentVehicleId <= 0 || vehicleManager == null || stopManager == null)
        {
            return false;
        }

        if (!vehicleManager.TryGetVehicle(currentVehicleId, out VehicleAgent vehicle) || vehicle == null)
        {
            return false;
        }

        if (workingAssignedStopIds.Count < minimumStopsRequired)
        {
            vehicle.ClearAssignedStops();
            return false;
        }

        return vehicle.AssignStops(stopManager, workingAssignedStopIds);
    }

    private void RefreshListsAndText()
    {
        RefreshAvailableStops();
        RefreshAllStopsList();
        RefreshSelectedStopsList();
        RefreshAssignedStopsText();
    }

    private void RefreshAvailableStops()
    {
        availableStopIds.Clear();
        if (stopManager == null)
        {
            return;
        }

        stopManager.GetSortedStopIds(availableStopIds);
    }

    private void RefreshAllStopsList()
    {
        if (allStopsListRoot == null || allStopsToggleItemPrefab == null)
        {
            return;
        }

        for (int i = allStopsListRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(allStopsListRoot.GetChild(i).gameObject);
        }

        allStopItems.Clear();
        for (int i = 0; i < availableStopIds.Count; i++)
        {
            int stopId = availableStopIds[i];
            VehicleStopToggleItemUI item = Instantiate(allStopsToggleItemPrefab, allStopsListRoot);
            item.Setup(this, stopId, BuildStopLabel(stopId), workingAssignedStopIds.Contains(stopId));
            allStopItems.Add(item);
        }
    }

    private void RefreshSelectedStopsList()
    {
        CancelDrag();

        if (selectedStopsListRoot == null || selectedStopItemPrefab == null)
        {
            return;
        }

        for (int i = selectedStopsListRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(selectedStopsListRoot.GetChild(i).gameObject);
        }

        for (int i = 0; i < workingAssignedStopIds.Count; i++)
        {
            int stopId = workingAssignedStopIds[i];
            if (stopManager == null || !stopManager.TryGetStopById(stopId, out _))
            {
                continue;
            }

            VehicleSelectedStopItemUI item = Instantiate(selectedStopItemPrefab, selectedStopsListRoot);
            item.Setup(this, stopId, BuildStopLabel(stopId));
        }
    }

    private void RefreshAssignedStopsText()
    {
        if (titleText != null)
        {
            titleText.text = currentVehicleId > 0 ? $"Vehicle {currentVehicleId}" : noVehicleText;
        }

        if (assignedStopsText == null)
        {
            return;
        }

        if (workingAssignedStopIds.Count == 0)
        {
            assignedStopsText.text = emptyAssignedStopsText;
            return;
        }

        string text = emptyAssignedStopsText;
        for (int i = 0; i < workingAssignedStopIds.Count; i++)
        {
            text += (i == 0 ? "\n" : " -> ") + BuildStopLabel(workingAssignedStopIds[i]);
        }

        assignedStopsText.text = text;
    }

    private string BuildStopLabel(int stopId)
    {
        return $"Stop {stopId}";
    }

    private void SyncAllTogglesFromSelection()
    {
        for (int i = 0; i < allStopItems.Count; i++)
        {
            VehicleStopToggleItemUI item = allStopItems[i];
            if (item == null)
            {
                continue;
            }

            item.SetIsOnWithoutNotify(workingAssignedStopIds.Contains(item.StopId));
        }
    }

    private void SyncSelectionOrderFromUi()
    {
        if (selectedStopsListRoot == null)
        {
            return;
        }

        List<int> orderedStopIds = new();
        for (int i = 0; i < selectedStopsListRoot.childCount; i++)
        {
            VehicleSelectedStopItemUI item = selectedStopsListRoot.GetChild(i).GetComponent<VehicleSelectedStopItemUI>();
            if (item == null || item.StopId <= 0)
            {
                continue;
            }

            orderedStopIds.Add(item.StopId);
        }

        workingAssignedStopIds.Clear();
        workingAssignedStopIds.AddRange(orderedStopIds);
        SyncAllTogglesFromSelection();
    }

    private void CreatePlaceholder(VehicleSelectedStopItemUI item)
    {
        if (item == null || selectedStopsListRoot == null)
        {
            return;
        }

        GameObject placeholderObject = new("DragPlaceholder", typeof(RectTransform), typeof(LayoutElement));
        placeholderObject.transform.SetParent(selectedStopsListRoot, false);
        draggingPlaceholder = placeholderObject.transform as RectTransform;

        LayoutElement source = item.GetComponent<LayoutElement>();
        LayoutElement target = placeholderObject.GetComponent<LayoutElement>();
        if (source != null && target != null)
        {
            target.preferredWidth = source.preferredWidth > 0f ? source.preferredWidth : item.RectTransform.rect.width;
            target.preferredHeight = source.preferredHeight > 0f ? source.preferredHeight : item.RectTransform.rect.height;
            target.flexibleWidth = 0f;
            target.flexibleHeight = 0f;
        }
        else if (target != null)
        {
            target.preferredWidth = item.RectTransform.rect.width;
            target.preferredHeight = item.RectTransform.rect.height;
            target.flexibleWidth = 0f;
            target.flexibleHeight = 0f;
        }

        draggingPlaceholder.SetSiblingIndex(item.transform.GetSiblingIndex());
    }

    private void CancelDrag()
    {
        if (draggingItem != null)
        {
            draggingItem.SetDraggingVisual(false);
        }

        if (draggingPlaceholder != null)
        {
            Destroy(draggingPlaceholder.gameObject);
        }

        draggingItem = null;
        draggingPlaceholder = null;
    }

    private void SetPanelVisible(bool isVisible)
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(isVisible);
        }
        else
        {
            gameObject.SetActive(isVisible);
        }
    }
}
