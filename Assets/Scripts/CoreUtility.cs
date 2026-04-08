using UnityEngine;
using UnityEngine.EventSystems;

public static class CoreUtility
{
    public static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    public static void ResolveIfNull<T>(ref T field) where T : Object
    {
        if (field == null)
        {
            field = Object.FindFirstObjectByType<T>();
        }
    }

    public static Transform ResolveRuntimeParent(Transform preferredParent, Transform fallbackParent)
    {
        Transform candidate = preferredParent != null ? preferredParent : fallbackParent;
        if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded)
        {
            return candidate;
        }

        return fallbackParent;
    }

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnableRunInBackground()
    {
        Application.runInBackground = true;
    }
}
