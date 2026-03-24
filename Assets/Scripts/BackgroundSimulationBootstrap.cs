using UnityEngine;

public static class BackgroundSimulationBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnableRunInBackground()
    {
        Application.runInBackground = true;
    }
}
