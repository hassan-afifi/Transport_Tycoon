using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class VehicleSelectedStopItemUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private LayoutElement layoutElement;

    private VehicleStopAssignPanel owner;

    public int StopId { get; private set; }
    public RectTransform RectTransform { get; private set; }

    private void Awake()
    {
        RectTransform = transform as RectTransform;
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (layoutElement == null)
        {
            layoutElement = GetComponent<LayoutElement>();
        }
    }

    public void Setup(VehicleStopAssignPanel panel, int stopId, string label)
    {
        owner = panel;
        StopId = stopId;
        if (labelText != null)
        {
            labelText.text = label;
        }
    }

    public void SetDraggingVisual(bool isDragging)
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = isDragging ? 0.7f : 1f;
        canvasGroup.blocksRaycasts = !isDragging;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (owner == null)
        {
            return;
        }

        owner.BeginDragSelectedItem(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (owner == null)
        {
            return;
        }

        owner.DragSelectedItem(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (owner == null)
        {
            return;
        }

        owner.EndDragSelectedItem(this, eventData);
    }
}
