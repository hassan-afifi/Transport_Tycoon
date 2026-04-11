using System.Collections.Generic;
using UnityEngine;

public class BuildingTileOccupancy : MonoBehaviour
{
    [SerializeField] private bool useManualTiles = true;
    [SerializeField] private List<Vector3Int> localOccupiedTiles = new() { Vector3Int.zero };

    public bool TryGetOccupiedCells(Grid grid, HashSet<Vector3Int> cellsOut)
    {
        if (!useManualTiles || cellsOut == null || localOccupiedTiles == null || localOccupiedTiles.Count == 0)
        {
            return false;
        }

        Vector3Int rootCell = grid != null
            ? grid.WorldToCell(transform.position)
            : Vector3Int.RoundToInt(transform.position);

        for (int i = 0; i < localOccupiedTiles.Count; i++)
        {
            cellsOut.Add(rootCell + localOccupiedTiles[i]);
        }

        return true;
    }
}

