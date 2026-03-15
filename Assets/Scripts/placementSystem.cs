using UnityEngine;
using TMPro;
public class placementSystem : MonoBehaviour
{
    [SerializeField]
    private GameObject cellIndicator;
    [SerializeField]
    private InputManager inputManager;

    [SerializeField]
    private Grid grid;
    [SerializeField]
    private ObjectDatabaseSO database;
    private int selectedObjectIndex = -1;

    [SerializeField]
    private GameObject gridVisualization;

    private void Start()
    {

        StopPlacement();
    }
    public void StartPlacement(int ID)
    {
        StopPlacement();    
        selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        if(selectedObjectIndex < 0)
        {
            Debug.LogError($"NO ID found {ID}");
            return;
        }
        gridVisualization.SetActive(true);
        cellIndicator.SetActive(true);
        inputManager.onClicked += PlaceStructure;
        inputManager.onExit += StopPlacement;
    }

    private void PlaceStructure()
    {
        if(inputManager.IsPointerOverUI())
        {
            return;
        }
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);
        GameObject newObject = Instantiate(database.objectsData[selectedObjectIndex].Prefab);
        Vector3 finalPosition = grid.CellToWorld(gridPosition);
        finalPosition.y = 0.01f;

        newObject.transform.position = finalPosition;
        newObject.transform.localScale = Vector3.one *0.5f;
        
    }
    private void StopPlacement()
    {
        selectedObjectIndex = -1;
        gridVisualization.SetActive(false);
        cellIndicator.SetActive(false);
        inputManager.onClicked -= PlaceStructure;
        inputManager.onExit -= StopPlacement;
    } 
    private void Update()
    {
        if(selectedObjectIndex < 0){return;}
        if (inputManager != null)
        {
            Vector3 mousePosition = inputManager.GetSelectedMapPosition();
            // mouseIndicator.transform.position = mousePosition;
            Vector3Int gridPosition = grid.WorldToCell(mousePosition);
            cellIndicator.transform.position = grid.CellToWorld(gridPosition);
        }
    }
}