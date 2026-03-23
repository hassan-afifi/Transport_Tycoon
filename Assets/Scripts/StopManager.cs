using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class StopManager : MonoBehaviour
{
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Grid grid;
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private GridMap gridMap;
    [SerializeField] private placementSystem placementSystemToDisable;
    [SerializeField] private VehiclePlacementTool vehiclePlacementToolToDisable;
    [SerializeField] private GameObject stopSignPrefab;
    [SerializeField] private Transform stopParent;
    [SerializeField] private float stopY = 0.02f;
    [SerializeField] private string stopNamePrefix = "Stop";
    [SerializeField] private bool addSelectionColliderIfMissing = true;
    [SerializeField, Min(0.1f)] private float fallbackColliderRadius = 2f;
    [SerializeField] private LayerMask noStopZoneMask;
    [SerializeField, Min(0.1f)] private float noStopZoneCheckRadius = 1f;
    [SerializeField, Min(0.1f)] private float signSideOffset = 4f;
    [SerializeField] private float signLocalY = 0f;
    [SerializeField] private float previewY = 0.02f;
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.5f;
    [SerializeField] private Color previewValidColor = new Color(0f, 0.5f, 0f, 1f);
    [SerializeField] private Color previewInvalidColor = new Color(0.5f, 0f, 0f, 1f);

    private readonly Dictionary<int, StopNode> stopsById = new();
    private readonly Dictionary<Vector3Int, StopNode> stopsByCell = new();
    private readonly List<Material> previewMaterials = new();
    private int nextStopId = 1;
    private GameObject previewObject;
    private Transform previewSignA;
    private Transform previewSignB;
    private bool dragStopHasCell;
    private Vector3Int dragStopCell;
    private StopDragMode dragStopMode;
    private int lastDragActionFrame = -1;
    private Vector3Int lastDragActionCell;

    private enum StopDragMode
    {
        None,
        Place,
        Remove
    }

    public bool IsStopPlacementActive { get; private set; }
    public IReadOnlyDictionary<int, StopNode> StopsById => stopsById;

    public event Action<StopNode> StopPlaced;
    public event Action StopsChanged;

    private void Awake()
    {
        if (inputManager == null)
        {
            inputManager = FindFirstObjectByType<InputManager>();
        }

        if (grid == null)
        {
            grid = FindFirstObjectByType<Grid>();
        }

        if (roadNetworkManager == null)
        {
            roadNetworkManager = FindFirstObjectByType<RoadNetworkManager>();
        }

        if (gridMap == null)
        {
            gridMap = GridMap.EnsureInstance();
        }

        if (vehiclePlacementToolToDisable == null)
        {
            vehiclePlacementToolToDisable = FindFirstObjectByType<VehiclePlacementTool>();
        }

        if (noStopZoneMask.value == 0)
        {
            noStopZoneMask = LayerMask.GetMask("Selectable");
        }
    }

    private void OnDisable()
    {
        EndStopPlacement();
    }

    private void Start()
    {
        RegisterExistingSceneStops();
        StopsChanged?.Invoke();
    }

    private void Update()
    {
        if (!IsStopPlacementActive || inputManager == null || grid == null)
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mapPos))
        {
            UpdatePreviewColor(false);
            return;
        }

        Vector3Int gridCell = grid.WorldToCell(mapPos);
        Vector3 snappedPos = grid.GetCellCenterWorld(gridCell);

        if (previewObject != null)
        {
            previewObject.transform.position = new Vector3(snappedPos.x, previewY, snappedPos.z);
        }

        bool hasStraightRoad = TryGetStraightRoadAxisAtCell(gridCell, out StopRoadAxis roadAxis);
        UpdateSignPairLayout(previewSignA, previewSignB, hasStraightRoad ? roadAxis : StopRoadAxis.NorthSouth);

        bool canRemove = CanRemoveStopAtCell(gridCell) && !inputManager.IsPointerOverUI();
        bool canPlace = hasStraightRoad
            && !stopsByCell.ContainsKey(gridCell)
            && !IsBlockedByNoStopZone(gridCell)
            && !inputManager.IsPointerOverUI();
        UpdatePreviewColor(canPlace || canRemove);
        HandleDragStopPlacement(gridCell);
    }

    public void ToggleStopPlacement()
    {
        if (IsStopPlacementActive)
        {
            EndStopPlacement();
            return;
        }

        BeginStopPlacement();
    }

    public void BeginStopPlacement()
    {
        if (IsStopPlacementActive)
        {
            return;
        }

        if (inputManager == null || grid == null || roadNetworkManager == null || stopSignPrefab == null)
        {
            return;
        }

        if (placementSystemToDisable != null)
        {
            placementSystemToDisable.StopPlacement();
        }

        if (vehiclePlacementToolToDisable != null)
        {
            vehiclePlacementToolToDisable.EndPlacement();
        }

        CreatePreviewObject();
        IsStopPlacementActive = true;
        dragStopHasCell = false;
        dragStopMode = StopDragMode.None;
        lastDragActionFrame = -1;
        inputManager.onClicked += HandleMapClickForStopPlacement;
        inputManager.onExit += EndStopPlacement;
    }

    public void EndStopPlacement()
    {
        if (!IsStopPlacementActive)
        {
            return;
        }

        IsStopPlacementActive = false;
        if (inputManager != null)
        {
            inputManager.onClicked -= HandleMapClickForStopPlacement;
            inputManager.onExit -= EndStopPlacement;
        }

        dragStopHasCell = false;
        dragStopMode = StopDragMode.None;
        lastDragActionFrame = -1;

        DestroyPreviewObject();
    }

    public bool TryPlaceStopAtCell(Vector3Int gridCell)
    {
        if (roadNetworkManager == null || grid == null || stopSignPrefab == null)
        {
            return false;
        }

        if (!TryGetStraightRoadAxisAtCell(gridCell, out StopRoadAxis roadAxis)
            || stopsByCell.ContainsKey(gridCell)
            || IsBlockedByNoStopZone(gridCell))
        {
            return false;
        }

        if (EconomyManager.HasInstance && !EconomyManager.Instance.TrySpendForStopPlacement())
        {
            return false;
        }

        Vector3 worldPos = grid.GetCellCenterWorld(gridCell);
        worldPos.y = stopY;
        Transform parent = ResolveRuntimeParent();
        int stopId = nextStopId++;
        string stopName = $"{stopNamePrefix} {stopId}";

        GameObject stopObject = new(stopName);
        if (parent != null)
        {
            stopObject.transform.SetParent(parent, false);
        }
        stopObject.transform.position = worldPos;

        CreateSignPair(stopObject.transform, roadAxis, false);

        StopNode stopNode = stopObject.AddComponent<StopNode>();
        stopNode.Initialize(stopId, gridCell, stopName, roadAxis, false);
        stopObject.name = stopName;

        if (addSelectionColliderIfMissing)
        {
            EnsureSelectionCollider(stopObject);
        }

        stopsById[stopId] = stopNode;
        stopsByCell[gridCell] = stopNode;
        if (gridMap != null)
        {
            gridMap.RegisterStop(stopNode);
        }
        StopPlaced?.Invoke(stopNode);
        StopsChanged?.Invoke();

        return true;
    }

    public bool TryGetStopById(int stopId, out StopNode stopNode)
    {
        return stopsById.TryGetValue(stopId, out stopNode);
    }

    public bool TryGetStopAtCell(Vector3Int gridCell, out StopNode stopNode)
    {
        return stopsByCell.TryGetValue(gridCell, out stopNode);
    }

    public bool TryGetStopFromObject(GameObject selectedObject, out StopNode stopNode)
    {
        stopNode = null;
        if (selectedObject == null)
        {
            return false;
        }

        stopNode = selectedObject.GetComponent<StopNode>();
        if (stopNode != null)
        {
            return true;
        }

        stopNode = selectedObject.GetComponentInParent<StopNode>();
        if (stopNode != null)
        {
            return true;
        }

        stopNode = selectedObject.GetComponentInChildren<StopNode>(true);
        return stopNode != null;
    }

    private void HandleMapClickForStopPlacement()
    {
        if (!IsStopPlacementActive || inputManager == null || grid == null)
        {
            return;
        }

        if (inputManager.IsPointerOverUI())
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mapPos))
        {
            return;
        }

        Vector3Int gridCell = grid.WorldToCell(mapPos);
        if (lastDragActionFrame == Time.frameCount && lastDragActionCell == gridCell)
        {
            return;
        }

        if (TryRemoveStopAtCell(gridCell))
        {
            lastDragActionFrame = Time.frameCount;
            lastDragActionCell = gridCell;
            return;
        }

        if (TryPlaceStopAtCell(gridCell))
        {
            lastDragActionFrame = Time.frameCount;
            lastDragActionCell = gridCell;
        }
    }

    private void HandleDragStopPlacement(Vector3Int gridCell)
    {
        if (Mouse.current == null || !Mouse.current.leftButton.isPressed || inputManager.IsPointerOverUI())
        {
            dragStopHasCell = false;
            dragStopMode = StopDragMode.None;
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragStopMode = CanRemoveStopAtCell(gridCell) ? StopDragMode.Remove : StopDragMode.Place;
            dragStopHasCell = false;
        }

        if (lastDragActionFrame == Time.frameCount && lastDragActionCell == gridCell)
        {
            dragStopHasCell = true;
            dragStopCell = gridCell;
            return;
        }

        if (dragStopHasCell && dragStopCell == gridCell)
        {
            return;
        }

        dragStopHasCell = true;
        dragStopCell = gridCell;

        bool changed = dragStopMode switch
        {
            StopDragMode.Remove => TryRemoveStopAtCell(gridCell),
            _ => TryPlaceStopAtCell(gridCell)
        };

        if (changed)
        {
            lastDragActionFrame = Time.frameCount;
            lastDragActionCell = gridCell;
        }
    }

    public bool TryRemoveStopAtCell(Vector3Int gridCell)
    {
        if (!stopsByCell.TryGetValue(gridCell, out StopNode stopNode) || stopNode == null)
        {
            return false;
        }

        if (stopNode.IsLockedInPlace)
        {
            return false;
        }

        stopsByCell.Remove(gridCell);
        stopsById.Remove(stopNode.StopId);
        if (gridMap != null)
        {
            gridMap.UnregisterStop(stopNode);
        }

        if (EconomyManager.HasInstance)
        {
            EconomyManager.Instance.RefundForStopRemoval();
        }

        Destroy(stopNode.gameObject);
        StopsChanged?.Invoke();
        return true;
    }

    public void GetSortedStopIds(List<int> stopIdsOut)
    {
        stopIdsOut.Clear();
        foreach (KeyValuePair<int, StopNode> pair in stopsById)
        {
            if (pair.Key > 0 && pair.Value != null)
            {
                stopIdsOut.Add(pair.Key);
            }
        }

        stopIdsOut.Sort();
    }

    private void CreatePreviewObject()
    {
        if (stopSignPrefab == null)
        {
            return;
        }

        previewObject = new GameObject($"{stopSignPrefab.name}_Preview");
        previewObject.transform.SetParent(transform, false);
        CreateSignPair(previewObject.transform, StopRoadAxis.NorthSouth, true);

        foreach (Collider collider in previewObject.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        SetLayerRecursively(previewObject, LayerMask.NameToLayer("Ignore Raycast"));
        CacheAndPreparePreviewMaterials();
        UpdatePreviewColor(false);
    }

    private void DestroyPreviewObject()
    {
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
        }

        previewSignA = null;
        previewSignB = null;
        previewMaterials.Clear();
    }

    private void CacheAndPreparePreviewMaterials()
    {
        previewMaterials.Clear();
        if (previewObject == null)
        {
            return;
        }

        Renderer[] renderers = previewObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material[] materials = renderer.materials;
            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null)
                {
                    continue;
                }

                MakeMaterialTransparent(material);
                previewMaterials.Add(material);
            }
        }
    }

    private void UpdatePreviewColor(bool isValid)
    {
        if (previewMaterials.Count == 0)
        {
            return;
        }

        Color color = isValid ? previewValidColor : previewInvalidColor;
        color.a = Mathf.Clamp01(previewAlpha);

        for (int i = 0; i < previewMaterials.Count; i++)
        {
            Material material = previewMaterials[i];
            if (material == null)
            {
                continue;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }
    }

    private static void MakeMaterialTransparent(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private bool TryGetStraightRoadAxisAtCell(Vector3Int gridCell, out StopRoadAxis roadAxis)
    {
        roadAxis = StopRoadAxis.None;

        if (roadNetworkManager == null || !roadNetworkManager.TryGetRoad(gridCell, out RoadTileData roadTile))
        {
            return false;
        }

        if (roadTile.connections == (RoadDirectionMask.North | RoadDirectionMask.South))
        {
            roadAxis = StopRoadAxis.NorthSouth;
            return true;
        }

        if (roadTile.connections == (RoadDirectionMask.East | RoadDirectionMask.West))
        {
            roadAxis = StopRoadAxis.EastWest;
            return true;
        }

        return false;
    }

    private bool IsBlockedByNoStopZone(Vector3Int gridCell)
    {
        if (grid == null || noStopZoneMask.value == 0)
        {
            return false;
        }

        Vector3 center = grid.GetCellCenterWorld(gridCell);
        center.y = stopY + 0.5f;
        return Physics.CheckSphere(center, noStopZoneCheckRadius, noStopZoneMask, QueryTriggerInteraction.Collide);
    }

    private void RegisterExistingSceneStops()
    {
        if (grid == null)
        {
            return;
        }

        StopNode[] existingStops = FindObjectsByType<StopNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < existingStops.Length; i++)
        {
            RegisterExistingStop(existingStops[i]);
        }
    }

    private void RegisterExistingStop(StopNode stopNode)
    {
        if (stopNode == null)
        {
            return;
        }

        Vector3Int cell = grid.WorldToCell(stopNode.transform.position);
        if (stopsByCell.ContainsKey(cell))
        {
            return;
        }

        int stopId = stopNode.StopId;
        if (stopId <= 0 || stopsById.ContainsKey(stopId))
        {
            stopId = nextStopId;
        }

        string displayName = string.IsNullOrWhiteSpace(stopNode.StopName) ? $"{stopNamePrefix} {stopId}" : stopNode.StopName;
        StopRoadAxis axis = stopNode.RoadAxis;
        if (axis == StopRoadAxis.None && TryGetStraightRoadAxisAtCell(cell, out StopRoadAxis detectedAxis))
        {
            axis = detectedAxis;
        }

        stopNode.Initialize(stopId, cell, displayName, axis, true);
        if (addSelectionColliderIfMissing)
        {
            EnsureSelectionCollider(stopNode.gameObject);
        }

        stopsById[stopId] = stopNode;
        stopsByCell[cell] = stopNode;
        if (gridMap != null)
        {
            gridMap.RegisterStop(stopNode);
        }
        nextStopId = Mathf.Max(nextStopId, stopId + 1);
    }

    private void CreateSignPair(Transform parent, StopRoadAxis axis, bool isPreview)
    {
        if (parent == null || stopSignPrefab == null)
        {
            return;
        }

        GameObject signAObject = Instantiate(stopSignPrefab, parent);
        GameObject signBObject = Instantiate(stopSignPrefab, parent);
        RemoveStopNodeComponents(signAObject);
        RemoveStopNodeComponents(signBObject);

        Transform signATransform = signAObject.transform;
        Transform signBTransform = signBObject.transform;
        UpdateSignPairLayout(signATransform, signBTransform, axis);

        if (isPreview)
        {
            previewSignA = signATransform;
            previewSignB = signBTransform;
        }
    }

    private void UpdateSignPairLayout(Transform signA, Transform signB, StopRoadAxis axis)
    {
        if (signA == null || signB == null)
        {
            return;
        }

        Vector3 offsetA;
        Vector3 offsetB;
        Vector3 lookA;
        Vector3 lookB;

        if (axis == StopRoadAxis.EastWest)
        {
            offsetA = Vector3.forward * signSideOffset;
            offsetB = -offsetA;
            lookA = Vector3.right;
            lookB = Vector3.left;
        }
        else
        {
            offsetA = Vector3.right * signSideOffset;
            offsetB = -offsetA;
            lookA = Vector3.back;
            lookB = Vector3.forward;
        }

        SetSignTransform(signA, offsetA, lookA);
        SetSignTransform(signB, offsetB, lookB);
    }

    private void SetSignTransform(Transform signTransform, Vector3 localOffset, Vector3 lookDirection)
    {
        if (signTransform == null)
        {
            return;
        }

        signTransform.localPosition = new Vector3(localOffset.x, signLocalY, localOffset.z);

        Quaternion lookRotation = Quaternion.identity;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            lookRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        signTransform.localRotation = lookRotation;
    }

    private static void RemoveStopNodeComponents(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        StopNode[] nodes = root.GetComponentsInChildren<StopNode>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (Application.isPlaying)
            {
                Destroy(nodes[i]);
            }
            else
            {
                DestroyImmediate(nodes[i]);
            }
        }
    }

    private void EnsureSelectionCollider(GameObject stopObject)
    {
        if (stopObject == null)
        {
            return;
        }

        if (stopObject.GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        SphereCollider collider = stopObject.AddComponent<SphereCollider>();
        collider.radius = fallbackColliderRadius;
        collider.center = Vector3.up * fallbackColliderRadius;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
        {
            return;
        }

        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private bool CanRemoveStopAtCell(Vector3Int gridCell)
    {
        return stopsByCell.TryGetValue(gridCell, out StopNode stopNode)
            && stopNode != null
            && !stopNode.IsLockedInPlace;
    }

    private Transform ResolveRuntimeParent()
    {
        Transform candidate = stopParent != null ? stopParent : transform;
        if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded)
        {
            return candidate;
        }

        if (stopParent != null)
        {
        }

        return transform;
    }
}
