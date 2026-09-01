using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Produces the Android builds, from the menu or from a command line.
///
/// The point of having this in the repo rather than clicking through the Build Settings
/// window is that a build stops being something a person remembers how to configure. The
/// architecture, the scripting backend and the scene list are asserted here every time, so
/// a setting nudged while debugging cannot quietly ship.
///
/// It deliberately does not touch the version code, the keystore, or the machine's SDK
/// paths. Those are either the developer's secrets or their machine's setup; a build script
/// that edits them is a build script that surprises someone.
/// </summary>
public static class BuildRunner
{
    private const string OutputDirectory = "Builds";

    // ---------- menu ----------

    [MenuItem("Build/Android APK (Development)", priority = 0)]
    public static void MenuDevelopmentApk() => Run(BuildKind.DevelopmentApk);

    [MenuItem("Build/Android APK (Release)", priority = 1)]
    public static void MenuReleaseApk() => Run(BuildKind.ReleaseApk);

    [MenuItem("Build/Android App Bundle (Play Store)", priority = 2)]
    public static void MenuAppBundle() => Run(BuildKind.AppBundle);

    [MenuItem("Build/Report Build Configuration", priority = 20)]
    public static void MenuReport() => Debug.Log(DescribeConfiguration());

    // ---------- command line ----------
    //
    // Unity.exe -quit -batchmode -projectPath <path> -executeMethod BuildRunner.CliReleaseApk

    public static void CliDevelopmentApk() => RunFromCli(BuildKind.DevelopmentApk);
    public static void CliReleaseApk() => RunFromCli(BuildKind.ReleaseApk);
    public static void CliAppBundle() => RunFromCli(BuildKind.AppBundle);

    private static void RunFromCli(BuildKind kind)
    {
        // In batch mode nobody reads a console warning, so a failed build has to be a
        // non-zero exit code or CI will treat it as a success.
        BuildReport report = Run(kind);
        if (report == null || report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public enum BuildKind
    {
        DevelopmentApk,
        ReleaseApk,
        AppBundle,
    }

    // ---------- build ----------

    public static BuildReport Run(BuildKind kind)
    {
        string[] scenes = EnabledScenes();
        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildRunner] No enabled scenes in Build Settings; nothing to build.");
            return null;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Debug.LogError("[BuildRunner] Could not switch the active build target to Android. " +
                           "Is the Android module installed for this Editor version?");
            return null;
        }

        ApplyAndroidSettings(kind);

        Directory.CreateDirectory(OutputDirectory);
        string path = Path.Combine(OutputDirectory, OutputName(kind));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = kind == BuildKind.DevelopmentApk
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None,
        };

        Debug.Log($"[BuildRunner] Building {kind} -> {path}\n{DescribeConfiguration()}");

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            // The artifact on disk, not summary.totalSize: that counts the whole output
            // folder including the Burst debug symbols that are explicitly not shipped,
            // and reported 766 MB for a 37 MB APK.
            var artifact = new FileInfo(path);
            string size = artifact.Exists
                ? $"{artifact.Length / (1024f * 1024f):F1} MB"
                : $"{summary.totalSize / (1024f * 1024f):F1} MB (whole output folder)";

            Debug.Log($"[BuildRunner] {kind} succeeded in {summary.totalTime.TotalMinutes:F1} min, " +
                      $"{size}\n{Path.GetFullPath(path)}" +
                      (kind == BuildKind.AppBundle ? "\n" + StoreUploadNotes() : string.Empty));
        }
        else
        {
            Debug.LogError($"[BuildRunner] {kind} {summary.result} with {summary.totalErrors} error(s).");
        }

        return report;
    }

    /// <summary>
    /// Re-asserts the settings a shipped build depends on, so none of them can be left
    /// somewhere else by a debugging session.
    /// </summary>
    private static void ApplyAndroidSettings(BuildKind kind)
    {
        // ARM64 only, IL2CPP: Google Play has required 64-bit since 2019, and adding
        // ARMv7 roughly doubles the native payload for devices the store no longer takes.
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;

        EditorUserBuildSettings.buildAppBundle = kind == BuildKind.AppBundle;
        EditorUserBuildSettings.androidBuildType = kind == BuildKind.DevelopmentApk
            ? AndroidBuildType.Development
            : AndroidBuildType.Release;

        EditorUserBuildSettings.development = kind == BuildKind.DevelopmentApk;
    }

    private static string OutputName(BuildKind kind)
    {
        string safeProduct = string.Concat(PlayerSettings.productName
            .Where(c => !char.IsWhiteSpace(c) && Path.GetInvalidFileNameChars().All(bad => bad != c)));
        string suffix = kind == BuildKind.DevelopmentApk ? "-dev" : string.Empty;
        string extension = kind == BuildKind.AppBundle ? ".aab" : ".apk";

        return $"{safeProduct}-{PlayerSettings.bundleVersion}" +
               $"-{PlayerSettings.Android.bundleVersionCode}{suffix}{extension}";
    }

    private static string[] EnabledScenes() =>
        EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    // ---------- reporting ----------

    /// <summary>
    /// What this build would be. Printed before every build and available on its own from
    /// the menu, because most build problems are a setting nobody looked at.
    /// </summary>
    public static string DescribeConfiguration()
    {
        var lines = new List<string>
        {
            "  product      " + PlayerSettings.productName,
            "  bundle id    " + PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android),
            "  version      " + PlayerSettings.bundleVersion + " (code " + PlayerSettings.Android.bundleVersionCode + ")",
            "  scripting    " + PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android)
                              + " / " + PlayerSettings.Android.targetArchitectures,
            "  stripping    " + PlayerSettings.GetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Android),
            "  min sdk      " + PlayerSettings.Android.minSdkVersion,
            "  colour space " + PlayerSettings.colorSpace,
            "  scenes       " + string.Join(", ", EnabledScenes().Select(Path.GetFileNameWithoutExtension)),
            "  signing      " + (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName)
                ? "debug keystore (fine for a device, NOT uploadable to Play)"
                : Path.GetFileName(PlayerSettings.Android.keystoreName)),
        };

        return "[BuildRunner] configuration:\n" + string.Join("\n", lines);
    }

    private static string StoreUploadNotes() =>
        "Before uploading: set a release keystore in Player Settings > Publishing Settings, " +
        "and raise PlayerSettings.Android.bundleVersionCode -- Play rejects a bundle whose " +
        "code is not higher than the last one uploaded. Neither is changed automatically, " +
        "because one is a secret and the other is a decision.";
}
