using System.Collections.Generic;
using UnityEngine;

public static class RoadUtility
{
    public static bool HasDirection(RoadDirectionMask mask, RoadDirectionMask direction)
    {
        return (mask & direction) != 0;
    }

    public static int CountConnectedDirections(RoadDirectionMask mask)
    {
        int count = 0;
        if (HasDirection(mask, RoadDirectionMask.North))
        {
            count++;
        }

        if (HasDirection(mask, RoadDirectionMask.East))
        {
            count++;
        }

        if (HasDirection(mask, RoadDirectionMask.South))
        {
            count++;
        }

        if (HasDirection(mask, RoadDirectionMask.West))
        {
            count++;
        }

        return count;
    }

    public static RoadDirectionMask GetClosestCardinalDirection(Vector3 forward)
    {
        Vector3 planar = forward;
        planar.y = 0f;
        if (planar.sqrMagnitude <= 0.0001f)
        {
            return RoadDirectionMask.None;
        }

        if (Mathf.Abs(planar.x) > Mathf.Abs(planar.z))
        {
            return planar.x >= 0f ? RoadDirectionMask.East : RoadDirectionMask.West;
        }

        return planar.z >= 0f ? RoadDirectionMask.North : RoadDirectionMask.South;
    }

    public static RoadDirectionMask Opposite(RoadDirectionMask direction)
    {
        switch (direction)
        {
            case RoadDirectionMask.North:
                return RoadDirectionMask.South;
            case RoadDirectionMask.East:
                return RoadDirectionMask.West;
            case RoadDirectionMask.South:
                return RoadDirectionMask.North;
            case RoadDirectionMask.West:
                return RoadDirectionMask.East;
            default:
                return RoadDirectionMask.None;
        }
    }

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

    public static void AppendSegment(List<Vector3Int> fullPath, List<Vector3Int> segment)
    {
        if (fullPath == null || segment == null || segment.Count == 0)
        {
            return;
        }

        int startIndex = 0;
        if (fullPath.Count > 0 && fullPath[fullPath.Count - 1] == segment[0])
        {
            startIndex = 1;
        }

        for (int i = startIndex; i < segment.Count; i++)
        {
            fullPath.Add(segment[i]);
        }
    }
}
