using UnityEngine;

public static class GridAxisUtility
{
    public static Vector3Int UnitOnAxis(int axisIndex, int sign)
    {
        int value = sign >= 0 ? 1 : -1;
        switch (axisIndex)
        {
            case 0:
                return new Vector3Int(value, 0, 0);
            case 1:
                return new Vector3Int(0, value, 0);
            default:
                return new Vector3Int(0, 0, value);
        }
    }
}
