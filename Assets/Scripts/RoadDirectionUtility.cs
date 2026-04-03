public static class RoadDirectionUtility
{
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
}
