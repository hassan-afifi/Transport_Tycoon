using UnityEngine;
using TMPro;

public class BuildingUIHandler : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private ObjectDatabaseSO database;
    [SerializeField] private placementSystem placementSystem;

    // We don't need Start() or SetupDropdown() anymore!
    // Just link this function to the Dropdown in the Inspector.

    public void OnDropdownChanged(int index)
    {
        // 1. Get the text of the option the user clicked
        string selectedName = dropdown.options[index].text;

        // 2. Search the database for a road with that exact name
        var foundData = database.objectsData.Find(data => data.Name == selectedName);

        // 3. If we found it, start placement using its ID
        if (foundData != null)
        {
            placementSystem.StartPlacement(foundData.ID);
            Debug.Log($"Manual Link worked! Started: {selectedName}");
        }
        else
        {
            Debug.LogWarning($"Could not find a road named '{selectedName}' in the Database!");
        }
    }
}