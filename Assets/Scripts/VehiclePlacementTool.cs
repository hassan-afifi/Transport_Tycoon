using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class VehiclePlacementTool : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Grid grid;
    [SerializeField] private RoadNetworkManager roadNetworkManager;
    [SerializeField] private VehicleManager vehicleManager;
    [SerializeField] private placementSystem roadPlacementToDisable;
    [SerializeField] private StopManager stopManagerToDisable;

    [Header("Placement")]
    [SerializeField, Min(0.1f)] private float laneOffset = 3f;
    [SerializeField] private float spawnY = 0.02f;
    [SerializeField] private float previewHeightOffset = 0.08f;

    [Header("Tagged Road Fallback")]
    [SerializeField] private bool allowTaggedRoadFallback = true;
    [SerializeField] private string roadTag = "Road";
    [SerializeField] private LayerMask taggedRoadLayerMask = ~0;
    [SerializeField, Range(0.1f, 1f)] private float taggedRoadCheckScale = 0.45f;
    [SerializeField, Min(0.1f)] private float taggedRoadCheckHeight = 6f;

    [Header("Preview")]
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.5f;
    [SerializeField] private Color previewValidColor = new Color(0f, 0.5f, 0f, 1f);
    [SerializeField] private Color previewInvalidColor = new Color(0.5f, 0f, 0f, 1f);

    private readonly Dictionary<LaneSlot, int> vehicleIdsBySlot = new();
    private readonly Dictionary<int, LaneSlot> slotByVehicleId = new();
    private readonly List<Material> previewMaterials = new();
    private readonly Collider[] taggedRoadOverlapBuffer = new Collider[64];

    private CargoType selectedCargoType = CargoType.None;
    private GameObject previewObject;
    private Vector3Int currentCell;
    private Quaternion currentRotation = Quaternion.identity;
    private Vector3 currentSpawnPosition;
    private int currentLaneIndex;
    private bool hasCurrentCell;
    private bool canPlaceCurrentCell;

    public bool IsPlacementActive => selectedCargoType != CargoType.None;
    public CargoType SelectedCargoType => selectedCargoType;

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

        if (vehicleManager == null)
        {
            vehicleManager = FindFirstObjectByType<VehicleManager>();
        }

        if (roadPlacementToDisable == null)
        {
            roadPlacementToDisable = FindFirstObjectByType<placementSystem>();
        }

        if (stopManagerToDisable == null)
        {
            stopManagerToDisable = FindFirstObjectByType<StopManager>();
        }
    }

    private void OnEnable()
    {
        if (vehicleManager != null)
        {
            vehicleManager.VehicleRemoved += HandleVehicleRemoved;
        }
    }

    private void OnDisable()
    {
        EndPlacement();

        if (vehicleManager != null)
        {
            vehicleManager.VehicleRemoved -= HandleVehicleRemoved;
        }
    }

    private void Update()
    {
        if (!IsPlacementActive || inputManager == null || grid == null)
        {
            return;
        }

        if (!inputManager.TryGetSelectedMapPosition(out Vector3 mapPosition))
        {
            hasCurrentCell = false;
            canPlaceCurrentCell = false;
            UpdatePreviewColor(false);
            return;
        }

        Vector3Int cell = grid.WorldToCell(mapPosition);
        hasCurrentCell = true;
        currentCell = cell;

        bool hasValidRoad = TryBuildPlacementPose(cell, currentLaneIndex, out Vector3 spawnPosition, out Quaternion spawnRotation);
        bool occupied = IsSlotOccupied(cell, currentLaneIndex);
        bool pointerOverUi = inputManager.IsPointerOverUI();
        canPlaceCurrentCell = hasValidRoad && !occupied && !pointerOverUi;

        currentSpawnPosition = spawnPosition;
        currentRotation = spawnRotation;
        UpdatePreviewTransform(spawnPosition, spawnRotation, hasValidRoad);
        UpdatePreviewColor(canPlaceCurrentCell);
    }

    public void TogglePlacement(CargoType cargoType)
    {
        if (IsPlacementActive && selectedCargoType == cargoType)
        {
            EndPlacement();
            return;
        }

        BeginPlacement(cargoType);
    }

    public void BeginPlacement(CargoType cargoType)
    {
        EndPlacement();

        if (cargoType == CargoType.None
            || inputManager == null
            || grid == null
            || vehicleManager == null
            || !vehicleManager.TryGetVehiclePrefab(cargoType, out _))
        {
            return;
        }

        if (roadPlacementToDisable != null)
        {
            roadPlacementToDisable.StopPlacement();
        }

        if (stopManagerToDisable != null)
        {
            stopManagerToDisable.EndStopPlacement();
        }

        selectedCargoType = cargoType;
        currentLaneIndex = 0;
        hasCurrentCell = false;
        canPlaceCurrentCell = false;

        CreatePreviewObject();
        inputManager.onClicked += HandleMapClick;
        inputManager.onExit += EndPlacement;
        inputManager.onRotate += SwitchLane;
    }

    public void EndPlacement()
    {
        if (!IsPlacementActive)
        {
            return;
        }

        selectedCargoType = CargoType.None;
        currentLaneIndex = 0;
        hasCurrentCell = false;
        canPlaceCurrentCell = false;

        if (inputManager != null)
        {
            inputManager.onClicked -= HandleMapClick;
            inputManager.onExit -= EndPlacement;
            inputManager.onRotate -= SwitchLane;
        }

        DestroyPreviewObject();
    }

    private void HandleMapClick()
    {
        if (!IsPlacementActive || !hasCurrentCell || !canPlaceCurrentCell || vehicleManager == null)
        {
            return;
        }

        if (inputManager != null && inputManager.IsPointerOverUI())
        {
            return;
        }

        LaneSlot slot = new LaneSlot(currentCell, currentLaneIndex);
        if (vehicleIdsBySlot.ContainsKey(slot))
        {
            return;
        }

        int vehicleId = vehicleManager.SpawnVehicleAt(selectedCargoType, currentSpawnPosition, currentRotation);
        if (vehicleId <= 0)
        {
            return;
        }

        vehicleIdsBySlot[slot] = vehicleId;
        slotByVehicleId[vehicleId] = slot;
    }

    private void SwitchLane()
    {
        if (!IsPlacementActive)
        {
            return;
        }

        currentLaneIndex = 1 - currentLaneIndex;
    }

    private bool TryBuildPlacementPose(Vector3Int cell, int laneIndex, out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        spawnPosition = grid != null ? grid.GetCellCenterWorld(cell) : Vector3.zero;
        spawnPosition.y = spawnY;
        spawnRotation = Quaternion.identity;

        Vector3 forward = Vector3.zero;

        if (roadNetworkManager != null && roadNetworkManager.TryGetRoad(cell, out RoadTileData roadTile))
        {
            forward = GetForwardVector(roadTile.connections);
        }
        else if (TryGetTaggedRoadForwardAtCell(cell, out Vector3 taggedForward))
        {
            forward = taggedForward;
        }

        if (forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        if (laneIndex == 1)
        {
            forward = -forward;
        }

        Vector3 laneRight = Vector3.Cross(Vector3.up, forward).normalized;
        spawnPosition += laneRight * laneOffset;
        spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
        return true;
    }

    private bool TryGetTaggedRoadForwardAtCell(Vector3Int cell, out Vector3 forward)
    {
        forward = Vector3.zero;

        if (!allowTaggedRoadFallback
            || grid == null
            || string.IsNullOrWhiteSpace(roadTag)
            || taggedRoadLayerMask.value == 0)
        {
            return false;
        }

        Vector3 center = grid.GetCellCenterWorld(cell);
        float halfY = Mathf.Max(0.05f, taggedRoadCheckHeight * 0.5f);
        Vector3 halfExtents = new Vector3(
            Mathf.Max(0.05f, grid.cellSize.x * taggedRoadCheckScale * 0.5f),
            halfY,
            Mathf.Max(0.05f, grid.cellSize.z * taggedRoadCheckScale * 0.5f));
        Vector3 overlapCenter = center + Vector3.up * halfY;

        int hitCount = Physics.OverlapBoxNonAlloc(
            overlapCenter,
            halfExtents,
            taggedRoadOverlapBuffer,
            Quaternion.identity,
            taggedRoadLayerMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = taggedRoadOverlapBuffer[i];
            if (hit == null)
            {
                continue;
            }

            Transform taggedRoadTransform = FindTaggedRoadTransform(hit.transform);
            if (taggedRoadTransform == null)
            {
                continue;
            }

            Vector3 taggedForward = GetPlanarForward(taggedRoadTransform.forward);
            if (taggedForward.sqrMagnitude < 0.0001f)
            {
                taggedForward = GetPlanarForward(taggedRoadTransform.right);
            }

            if (taggedForward.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            forward = taggedForward.normalized;
            return true;
        }

        return false;
    }

    private Transform FindTaggedRoadTransform(Transform source)
    {
        Transform current = source;
        while (current != null)
        {
            if (current.CompareTag(roadTag))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private static Vector3 GetPlanarForward(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }

    private static Vector3 GetForwardVector(RoadDirectionMask connections)
    {
        if ((connections & RoadDirectionMask.North) != 0)
        {
            return Vector3.forward;
        }

        if ((connections & RoadDirectionMask.East) != 0)
        {
            return Vector3.right;
        }

        if ((connections & RoadDirectionMask.South) != 0)
        {
            return Vector3.back;
        }

        if ((connections & RoadDirectionMask.West) != 0)
        {
            return Vector3.left;
        }

        return Vector3.zero;
    }

    private bool IsSlotOccupied(Vector3Int cell, int laneIndex)
    {
        return vehicleIdsBySlot.ContainsKey(new LaneSlot(cell, laneIndex));
    }

    private void HandleVehicleRemoved(VehicleAgent vehicle)
    {
        if (vehicle == null)
        {
            return;
        }

        int vehicleId = vehicle.VehicleId;
        if (!slotByVehicleId.TryGetValue(vehicleId, out LaneSlot slot))
        {
            return;
        }

        slotByVehicleId.Remove(vehicleId);
        vehicleIdsBySlot.Remove(slot);
    }

    private void CreatePreviewObject()
    {
        if (vehicleManager == null || !vehicleManager.TryGetVehiclePrefab(selectedCargoType, out GameObject prefab))
        {
            return;
        }

        previewObject = Instantiate(prefab);

        foreach (Collider collider in previewObject.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        SetLayerRecursively(previewObject, LayerMask.NameToLayer("Ignore Raycast"));
        CachePreviewMaterials();
        UpdatePreviewColor(false);
    }

    private void DestroyPreviewObject()
    {
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
        }

        previewMaterials.Clear();
    }

    private void UpdatePreviewTransform(Vector3 spawnPosition, Quaternion spawnRotation, bool hasRoad)
    {
        if (previewObject == null)
        {
            return;
        }

        Vector3 position = hasRoad ? spawnPosition : (grid != null ? grid.GetCellCenterWorld(currentCell) : spawnPosition);
        position.y = spawnY + previewHeightOffset;

        previewObject.transform.position = position;
        previewObject.transform.rotation = spawnRotation;
    }

    private void CachePreviewMaterials()
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

    private readonly struct LaneSlot : IEquatable<LaneSlot>
    {
        public readonly Vector3Int cell;
        public readonly int laneIndex;

        public LaneSlot(Vector3Int cell, int laneIndex)
        {
            this.cell = cell;
            this.laneIndex = laneIndex;
        }

        public bool Equals(LaneSlot other)
        {
            return cell == other.cell && laneIndex == other.laneIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is LaneSlot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (cell.GetHashCode() * 397) ^ laneIndex;
            }
        }
    }
}
