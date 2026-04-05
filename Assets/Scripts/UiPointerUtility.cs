using UnityEngine.EventSystems;

public static class UiPointerUtility
{
    public static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
