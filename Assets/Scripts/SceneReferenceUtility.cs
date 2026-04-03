using UnityEngine;

public static class SceneReferenceUtility
{
    public static void ResolveIfNull<T>(ref T field) where T : Object
    {
        if (field == null)
        {
            field = Object.FindFirstObjectByType<T>();
        }
    }
}
