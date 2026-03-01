using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildScript
{
    public static void BuildWindows()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new Exception("No enabled scenes in Build Settings.");

        var report = BuildPipeline.BuildPlayer(
            scenes,
            "Builds/Windows/MyGame.exe",
            BuildTarget.StandaloneWindows64,
            BuildOptions.None
        );

        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Build failed: " + report.summary.result);
    }
}
