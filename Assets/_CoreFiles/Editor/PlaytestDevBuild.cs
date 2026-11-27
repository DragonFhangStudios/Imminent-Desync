using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Standalone development build of the combined playtest (Director's overnight assignment, 2026-11-26:
// "Standalone Development Build 001"). Builds Playtest_Combined.unity ALONE as a Windows x64 development
// player that launches straight into the combined playtest.
//
// Nothing in the project is changed: the scene list is passed to BuildPipeline directly, so
// EditorBuildSettings (which lists only Sandbox.unity) and the Player settings stay as they are.
// The output folder comes from the command line and must be OUTSIDE the project folder, so generated
// build files never land in tracked repository content.
//
// Headless: Unity -batchmode -executeMethod PlaytestDevBuild.BuildCombinedWindows64 -dfsBuildOut <folder> -quit
// Exit code: 0 when the build succeeded, 1 otherwise (the build report summary is in the log).
public static class PlaytestDevBuild
{
	public const string OutputArgument = "-dfsBuildOut";
	public const string ExecutableName = "ImminentDesync.exe";

	public static void BuildCombinedWindows64()
	{
		string output = ReadArgument(OutputArgument);

		if (string.IsNullOrEmpty(output))
			Fail("Missing " + OutputArgument + " <folder>.");

		string outputFull = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string projectFull = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (outputFull.Equals(projectFull, StringComparison.OrdinalIgnoreCase) ||
			outputFull.StartsWith(projectFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			Fail("The build folder must be outside the project folder.");

		if (!File.Exists(Path.Combine(projectFull, PlaytestSceneBuilder.CombinedScenePath)))
			Fail("Scene not found: " + PlaytestSceneBuilder.CombinedScenePath);

		var options = new BuildPlayerOptions
		{
			scenes = new[] { PlaytestSceneBuilder.CombinedScenePath },
			locationPathName = Path.Combine(outputFull, ExecutableName),
			target = BuildTarget.StandaloneWindows64,
			targetGroup = BuildTargetGroup.Standalone,
			options = BuildOptions.Development | BuildOptions.StrictMode
		};

		BuildReport report = BuildPipeline.BuildPlayer(options);
		BuildSummary summary = report.summary;

		Debug.Log($"PlaytestDevBuild: result {summary.result}, platform {summary.platform}, " +
			$"errors {summary.totalErrors}, warnings {summary.totalWarnings}, size {summary.totalSize} bytes, " +
			$"time {summary.totalTime}, output {summary.outputPath}, scene {PlaytestSceneBuilder.CombinedScenePath}, " +
			$"backend {PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone)}");

		if (summary.result != BuildResult.Succeeded)
			EditorApplication.Exit(1);
	}

	private static string ReadArgument(string name)
	{
		string[] args = Environment.GetCommandLineArgs();

		for (int i = 0; i < args.Length - 1; i++)
		{
			if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
				return args[i + 1];
		}

		return null;
	}

	private static void Fail(string message)
	{
		Debug.LogError("PlaytestDevBuild: " + message);
		EditorApplication.Exit(1);
		throw new InvalidOperationException(message);
	}
}
