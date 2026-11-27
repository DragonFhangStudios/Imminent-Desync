using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the increment 2A playtest scene from Sandbox.unity (Director, 2026-11-26): the same map and tuned
// values, with Sync Charges, Reconstruction, U2 supply and S4 defeat switched on, and an Emitter placed at
// the grid centre at start so the Director's manual playtest and the automated tests start identically.
//
// Sandbox.unity is opened but NEVER saved: the result is written to a separate scene file.
// The new settings take their current script defaults (all provisional), written into the playtest scene.
//
// Headless: Unity -batchmode -executeMethod PlaytestSceneBuilder.Build -quit
// In the Editor: menu Imminent Desync > Rebuild Sync Supply Playtest Scene.
public static class PlaytestSceneBuilder
{
	public const string SourceScenePath = "Assets/_CoreFiles/Scenes/Sandbox.unity";
	public const string PlaytestScenePath = "Assets/_CoreFiles/Scenes/Playtest_SyncSupply.unity";

	// Combined playtest (2A merged into increment 3, Director 2026-11-26): the same scene plus Nullifier
	// reconfiguration N1–N3 (increment 3 opt-in) instead of the Integrity drain.
	public const string CombinedScenePath = "Assets/_CoreFiles/Scenes/Playtest_Combined.unity";

	[MenuItem("Imminent Desync/Rebuild Sync Supply Playtest Scene")]
	public static void Build() => BuildScene(PlaytestScenePath, reconfiguration: false);

	[MenuItem("Imminent Desync/Rebuild Combined Playtest Scene (2A + Nullifier reconfiguration)")]
	public static void BuildCombined() => BuildScene(CombinedScenePath, reconfiguration: true);

	private static void BuildScene(string path, bool reconfiguration)
	{
		var scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
		var gm = Object.FindAnyObjectByType<GridManager>();

		if (gm == null)
			throw new System.InvalidOperationException("No GridManager in " + SourceScenePath);

		var so = new SerializedObject(gm);
		Set(so, "startWithCenterSource", true);
		Set(so, "syncInfrastructure.enabled", true);
		Set(so, "syncInfrastructure.stabilizerSupplyEnabled", true);
		Set(so, "realityIntegrityDefeat", true);
		if (reconfiguration)
			Set(so, "syncInfrastructure.nullifierReconfigurationEnabled", true);
		so.ApplyModifiedPropertiesWithoutUndo();

		if (!EditorSceneManager.SaveScene(scene, path, true))
			throw new System.InvalidOperationException("Could not save " + path);

		AssetDatabase.Refresh();
		Debug.Log("PlaytestSceneBuilder: wrote " + path + " from " + SourceScenePath + " (Sandbox not saved).");
	}

	private static void Set(SerializedObject so, string path, bool value)
	{
		SerializedProperty p = so.FindProperty(path);

		if (p == null)
			throw new System.InvalidOperationException("GridManager has no serialized property " + path);

		p.boolValue = value;
	}
}
