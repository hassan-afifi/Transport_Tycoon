// using UnityEngine;
// using System;
// using System.Collections.Generic;
// using TMPro;
// public class placementSystem : MonoBehaviour
// {
//     [SerializeField]
//     private GameObject cellIndicator;
//     [SerializeField]
//     private InputManager inputManager;

//     [SerializeField]
//     private Grid grid;
//     [SerializeField]
//     private ObjectDatabaseSO database;
//     private int selectedObjectIndex = -1;

//     [SerializeField]
//     private GameObject gridVisualization;

//     private GridData roadData; 
//     private GameObject previewObject;

//     private Renderer previewRenderer;
//     private List<GameObject> placedGameObjects = new();
//     private int currentRotation = 0;

//     private void Start()
//     {
//         StopPlacement();
//         roadData = new();
//         previewRenderer = cellIndicator.GetComponent<MeshRenderer>();  
//     }
//     private void RotateObject()
//     {
//         currentRotation += 90;
//         if (currentRotation >= 360) currentRotation = 0;
        
//         cellIndicator.transform.rotation = Quaternion.Euler(0, currentRotation, 0);
//     }
//     public void StartPlacement(int ID)
//     {
//         StopPlacement();    
//         selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
//         if(selectedObjectIndex < 0)
//         {
//             Debug.LogError($"NO ID found {ID}");
//             return;
//         }
//         gridVisualization.SetActive(true);
//         cellIndicator.SetActive(true);

//         previewObject = Instantiate(database.objectsData[selectedObjectIndex].Prefab);
//         PreparePreview(previewObject);


//         inputManager.onClicked += PlaceStructure;
//         inputManager.onExit += StopPlacement;
//         inputManager.onRotate += RotateObject; 
//     }
//     private void PreparePreview(GameObject obj)
//     {
//         obj.transform.localScale = Vector3.one * 0.5f;

//         Collider[] colliders = obj.GetComponentsInChildren<Collider>();
//         foreach (Collider c in colliders)
//         {
//             c.enabled = false;
//         }
//         obj.layer = LayerMask.NameToLayer("Ignore Raycast");
//         foreach (Transform child in obj.transform)
//         {
//             child.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
//         }
//     }


//     private void PlaceStructure()
//     {
//         if(inputManager.IsPointerOverUI())
//         {
//             return;
//         }
//         Vector3 mousePosition = inputManager.GetSelectedMapPosition();
//         Vector3Int gridPosition = grid.WorldToCell(mousePosition);
//         if (!CheckPlacementValidity(gridPosition, selectedObjectIndex))
//         {
//             Debug.Log("Area Blocked!");
//             return;
//         }
//         Vector3 finalPosition = grid.CellToWorld(gridPosition);
//         finalPosition.y = 0.01f;
//         GameObject newObject = Instantiate(database.objectsData[selectedObjectIndex].Prefab,new Vector3(grid.CellToWorld(gridPosition).x, 0.01f, grid.CellToWorld(gridPosition).z), 
//         Quaternion.Euler(0, currentRotation, 0));

//         bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);
//         if(placementValidity == false)
//             return;
//         newObject.transform.position = finalPosition;
//         newObject.transform.localScale = Vector3.one *0.5f;
//         placedGameObjects.Add(newObject);
//         roadData.AddObjectAt(
//             gridPosition,
//             database.objectsData[selectedObjectIndex].Size,
//             database.objectsData[selectedObjectIndex].ID,
//             placedGameObjects.Count -1
//         );
        
//     }
//     private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
//     {
//         Vector2Int size = database.objectsData[selectedObjectIndex].Size;
    
//         // If rotated 90 or 270, swap X and Y sizes
//         if (currentRotation == 90 || currentRotation == 270)
//         {
//             size = new Vector2Int(size.y, size.x);
//         }
//         // 1. Check if WE already placed a road here
//         bool isGridFree = roadData.CanPlaceObjectAt(gridPosition, database.objectsData[selectedObjectIndex].Size);
//         if (!isGridFree) return false;

//         // 2. Check if a FOREST/CITY/MINE is physically here
//         Vector3 cellCenter = grid.GetCellCenterWorld(gridPosition);
//         float checkSize = (grid.cellSize.x / 2) * 0.8f; // 0.8 makes the box slightly smaller than the tile
//         Vector3 halfExtents = new Vector3(checkSize, 2f, checkSize); // Tall box to catch high trees

//         int obstacleLayerMask = LayerMask.GetMask("Obstacle");
//         return !Physics.CheckBox(cellCenter, halfExtents, Quaternion.identity, obstacleLayerMask);
//     }

//     private void StopPlacement()
//     {
//         selectedObjectIndex = -1;
//         gridVisualization.SetActive(false);
//         cellIndicator.SetActive(false);

//         if (previewObject != null)
//         {
//             Destroy(previewObject);
//         }

//         inputManager.onClicked -= PlaceStructure;
//         inputManager.onExit -= StopPlacement;
//         currentRotation = 0;
//         inputManager.onRotate -= RotateObject;
//     } 
//     private void Update()
//     {
//         if(selectedObjectIndex < 0){return;}
//         if (inputManager != null)
//         {
//             Vector3 mousePosition = inputManager.GetSelectedMapPosition();
//         Vector3Int gridPosition = grid.WorldToCell(mousePosition);
//         Vector3 snappedPos = grid.CellToWorld(gridPosition);
//         cellIndicator.transform.position = new Vector3(snappedPos.x, 0.02f, snappedPos.z);
//         cellIndicator.transform.rotation = Quaternion.Euler(0, currentRotation, 0);
//         if (previewObject != null)
//         {
//             previewObject.transform.position = new Vector3(snappedPos.x, 0f, snappedPos.z);
//             previewObject.transform.rotation = Quaternion.Euler(0, currentRotation, 0);
//         }

//             bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);
//             previewRenderer.material.color = placementValidity ? Color.white : Color.red;
           
//         }
//     }
// }

using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class placementSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject cellIndicator;
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Grid grid;
    [SerializeField] private ObjectDatabaseSO database;
    [SerializeField] private GameObject gridVisualization;

    private int selectedObjectIndex = -1;
    private int currentRotation = 0;
    private GridData roadData;
    private Renderer indicatorRenderer;
    
    private GameObject previewObject; 

    private List<GameObject> placedGameObjects = new List<GameObject>();

    private void Start()
    {
        roadData = new GridData();
        
        if (cellIndicator != null)
            indicatorRenderer = cellIndicator.GetComponent<Renderer>();
        
        StopPlacement();
    }

    public void StartPlacement(int ID)
    {
        StopPlacement();
        
        selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        
        if (selectedObjectIndex < 0)
        {
            Debug.LogError($"ID {ID} not found in Database!");
            return;
        }

        gridVisualization.SetActive(true);
        cellIndicator.SetActive(true);

        previewObject = Instantiate(database.objectsData[selectedObjectIndex].Prefab);
        PreparePreview(previewObject);

        inputManager.onClicked += PlaceStructure;
        inputManager.onExit += StopPlacement;
        inputManager.onRotate += RotateObject;

        Debug.Log($"Placement Started for: {database.objectsData[selectedObjectIndex].Name}");
    }

    private void PreparePreview(GameObject obj)
    {
        obj.transform.localScale = Vector3.one * 0.5f;

        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders) c.enabled = false;

        obj.layer = LayerMask.NameToLayer("Ignore Raycast");
        foreach (Transform child in obj.transform)
        {
            child.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }
    }

    public void StopPlacement()
    {
        selectedObjectIndex = -1;
        currentRotation = 0;

        gridVisualization.SetActive(false);
        cellIndicator.SetActive(false);

        if (previewObject != null) Destroy(previewObject);


        inputManager.onClicked -= PlaceStructure;
        inputManager.onExit -= StopPlacement;
        inputManager.onRotate -= RotateObject;
    }

    private void Update()
    {
        if (selectedObjectIndex < 0) return;

        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);
        
        Vector3 snappedPos = grid.GetCellCenterWorld(gridPosition);

        cellIndicator.transform.position = new Vector3(snappedPos.x, 0.02f, snappedPos.z);
        cellIndicator.transform.rotation = Quaternion.Euler(0, currentRotation, 0);

        if (previewObject != null)
        {
            previewObject.transform.position = new Vector3(snappedPos.x, 0.01f, snappedPos.z);
            previewObject.transform.rotation = Quaternion.Euler(0, currentRotation, 0);
        }
        gridVisualization.transform.position = new Vector3(snappedPos.x, 0.005f, snappedPos.z);

        bool isValid = CheckPlacementValidity(gridPosition, selectedObjectIndex);
        if (indicatorRenderer != null)
        {
            indicatorRenderer.material.color = isValid ? Color.white : Color.red;
        }
    }

    private void RotateObject()
    {
        currentRotation += 90;
        if (currentRotation >= 360) currentRotation = 0;
    }

    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI()) return;

        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        if (!CheckPlacementValidity(gridPosition, selectedObjectIndex)) return;

        Vector3 finalPos = grid.GetCellCenterWorld(gridPosition);
        finalPos.y = 0.01f;

        GameObject newObject = Instantiate(
            database.objectsData[selectedObjectIndex].Prefab, 
            finalPos, 
            Quaternion.Euler(0, currentRotation, 0)
        );

        newObject.transform.localScale = Vector3.one * 0.5f;

        placedGameObjects.Add(newObject);
        roadData.AddObjectAt(gridPosition, database.objectsData[selectedObjectIndex].Size, 
                             database.objectsData[selectedObjectIndex].ID, placedGameObjects.Count - 1);
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        bool isGridFree = roadData.CanPlaceObjectAt(gridPosition, database.objectsData[selectedObjectIndex].Size);
        if (!isGridFree) return false;

        Vector3 cellCenter = grid.GetCellCenterWorld(gridPosition);
        float checkSize = (grid.cellSize.x / 2) * 0.8f;
        int obstacleLayerMask = LayerMask.GetMask("Obstacle");
        
        return !Physics.CheckBox(cellCenter, new Vector3(checkSize, 2f, checkSize), Quaternion.identity, obstacleLayerMask);
    }
}