using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SilentNight.Editor
{
    public static class BuildScript
    {
        private const string BuildDir = "Builds/Windows";
        private const string ExeName = "SilentNight.exe";

        private static readonly string[] RequiredScenes = new[]
        {
            "Assets/_Project/Scenes/Boot.unity",
            "Assets/_Project/Scenes/Hub.unity",
            "Assets/_Project/Scenes/Rooftop.unity",
            "Assets/_Project/Scenes/House.unity"
        };

        [MenuItem("Silent Night/📦 Собрать игру (Windows EXE)", priority = 100)]
        public static void BuildWindowsPlayerMenu()
        {
            BuildStandaloneWindows(reveal: true, isCommandLine: false);
        }

        public static void PerformBuildCommandLine()
        {
            BuildStandaloneWindows(reveal: false, isCommandLine: true);
        }

        public static bool BuildStandaloneWindows(bool reveal, bool isCommandLine)
        {
            Debug.Log("[SilentNight.Build] Starting StandaloneWindows64 build...");

            // Ensure destination folder exists
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullBuildDir = Path.Combine(projectRoot, BuildDir);
            if (!Directory.Exists(fullBuildDir))
            {
                Directory.CreateDirectory(fullBuildDir);
            }

            string fullExePath = Path.Combine(fullBuildDir, ExeName);

            // Verify scenes exist on disk
            foreach (var scene in RequiredScenes)
            {
                if (!File.Exists(Path.Combine(projectRoot, scene)))
                {
                    string err = $"[SilentNight.Build] Scene file not found: {scene}";
                    Debug.LogError(err);
                    if (isCommandLine) EditorApplication.Exit(1);
                    return false;
                }
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = RequiredScenes,
                locationPathName = fullExePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                string msg = $"[SilentNight.Build] ✅ Build succeeded! Total time: {summary.totalTime:g}, Size: {summary.totalSize / (1024 * 1024):F1} MB\nPath: {fullExePath}";
                Debug.Log(msg);

                if (!isCommandLine)
                {
                    bool open = EditorUtility.DisplayDialog(
                        "Сборка завершена успешно!",
                        $"SilentNight.exe готов к запуску!\n\nРазмер: {summary.totalSize / (1024 * 1024):F1} MB\nПуть: {fullExePath}\n\nОткрыть папку с файлами для отправки другу?",
                        "Открыть папку",
                        "Закрыть"
                    );

                    if (open)
                    {
                        EditorUtility.RevealInFinder(fullExePath);
                    }
                }
                return true;
            }
            else
            {
                string errMsg = $"[SilentNight.Build] ❌ Build failed with result: {summary.result}. Total errors: {summary.totalErrors}";
                Debug.LogError(errMsg);

                if (!isCommandLine)
                {
                    EditorUtility.DisplayDialog(
                        "Ошибка сборки",
                        $"Не удалось собрать игру. Подробности смотрите в Console.\nОшибок: {summary.totalErrors}",
                        "Понятно"
                    );
                }

                if (isCommandLine) EditorApplication.Exit(1);
                return false;
            }
        }
    }
}
