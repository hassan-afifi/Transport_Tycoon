using UnityEngine;

public static class PlacementObjectUtility
{
    public static void RemoveComponentsInChildren<T>(GameObject root) where T : Component
    {
        if (root == null)
        {
            return;
        }

        T[] components = root.GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(components[i]);
            }
            else
            {
                Object.DestroyImmediate(components[i]);
            }
        }
    }

    public static void EnsureSelectionCollider(GameObject root, float radius)
    {
        if (root == null)
        {
            return;
        }

        if (root.GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        SphereCollider collider = root.AddComponent<SphereCollider>();
        collider.radius = radius;
        collider.center = Vector3.up * radius;
    }
}
