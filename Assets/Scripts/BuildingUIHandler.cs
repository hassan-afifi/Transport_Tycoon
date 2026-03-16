using UnityEngine;
using TMPro;

public class BuildingUIHandler : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private ObjectDatabaseSO database;
    [SerializeField] private placementSystem placementSystem;

    public void OnDropdownChanged(int index)
    {
 
        int databaseIndex = index; 

        if (databaseIndex < database.objectsData.Count)
        {
            int selectedID = database.objectsData[databaseIndex].ID;
            placementSystem.StartPlacement(selectedID);
            Debug.Log($"Building: {database.objectsData[databaseIndex].Name}");
        }
    }
}