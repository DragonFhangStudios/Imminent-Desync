// Sandbox smoke regression (63 checks) and determinism baseline.
//
// A repository copy of the 2026-11-23 headless smoke harness, which passed 63/63 on Unity 6000.5.4f1.
// It opens Sandbox.unity, takes the scene's real GridManager (with the scene's own tuned values),
// and drives its private methods by reflection in the same order GridManager.Update() uses for one
// simulation tick. Input is skipped because batch mode has no keyboard or mouse. Nothing is saved:
// the scene is only opened, never written.
//
// On top of the original 63 checks, the final total Desync and the contaminated-cell history are
// compared with the values recorded on 2026-11-23, so any behavior change in the Sandbox shows up.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	public class SandboxSmokeRegressionTests
	{
		// Recorded by the 2026-11-23 headless smoke test (Unity 6000.5.4f1), before any code change.
		private const string BaselineFinalTotalDesync = "21332.911624925426";
		private const string BaselineHistory = "1,21,42,83,169,788";
		private const int BaselineTicksToNullify = 190;
		private const int ExpectedCheckCount = 63;

		[Test]
		public void SandboxSmoke_AllChecksPass_AndMatchRecordedBaseline()
		{
			SandboxSmokeHarness.Report report = SandboxSmokeHarness.Run();

			TestContext.WriteLine(report.Text);

			Assert.AreEqual(0, report.Failures, "Smoke checks failed:\n" + report.Text);
			Assert.AreEqual(ExpectedCheckCount, report.Checks, "Unexpected number of smoke checks.");
			Assert.AreEqual(BaselineFinalTotalDesync, report.RunA.FinalTotalDesync.ToString("R"),
				"Sandbox final total Desync differs from the 2026-11-23 baseline.");
			Assert.AreEqual(BaselineHistory, report.RunA.History,
				"Sandbox contaminated-cell history differs from the 2026-11-23 baseline.");
			Assert.AreEqual(BaselineTicksToNullify, report.RunA.TicksToNullify,
				"Ticks to nullify differ from the 2026-11-23 baseline.");
		}

		[Test]
		public void Sandbox_SyncInfrastructure_IsDisabled()
		{
			EditorSceneManager.OpenScene("Assets/_CoreFiles/Scenes/Sandbox.unity", OpenSceneMode.Single);
			GridManager gm = UnityEngine.Object.FindAnyObjectByType<GridManager>();

			Assert.IsNotNull(gm);
			Assert.IsFalse(gm.SyncInfrastructureEnabled, "The Sandbox must keep instant, free building.");
		}
	}

	internal static class SandboxSmokeHarness
	{
		private const string ScenePath = "Assets/_CoreFiles/Scenes/Sandbox.unity";
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

		internal struct ScenarioResult
		{
			public double FinalTotalDesync;
			public string History;
			public int TicksToNullify;
		}

		internal sealed class Report
		{
			public int Checks;
			public int Failures;
			public ScenarioResult RunA;
			public ScenarioResult RunB;
			public string Text;
		}

		private static StringBuilder output;
		private static int failures;
		private static int checks;

		internal static Report Run()
		{
			output = new StringBuilder();
			failures = 0;
			checks = 0;

			Line("Sandbox smoke test, Unity " + Application.unityVersion);

			ScenarioResult a = RunScenario("Run A", verbose: true);
			ScenarioResult b = RunScenario("Run B (determinism repeat)", verbose: false);

			Section("Determinism");
			Check("Final total Desync identical across two fresh runs", a.FinalTotalDesync == b.FinalTotalDesync,
				$"A={a.FinalTotalDesync:R} B={b.FinalTotalDesync:R}");
			Check("Contaminated-cell history identical across runs", a.History == b.History, a.History == b.History ? "match" : "differ");
			Check("Ticks to nullify identical across runs", a.TicksToNullify == b.TicksToNullify, $"A={a.TicksToNullify} B={b.TicksToNullify}");

			Section("Input path (Update) in batch mode");
			TryUpdate();

			Section("Summary");
			Line($"{checks} checks, {failures} failed");

			return new Report { Checks = checks, Failures = failures, RunA = a, RunB = b, Text = output.ToString() };
		}

		private static ScenarioResult RunScenario(string name, bool verbose)
		{
			Section(name);
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

			GridManager gm = UnityEngine.Object.FindAnyObjectByType<GridManager>();
			Check("Sandbox scene contains a GridManager", gm != null, "");
			Line("Sync Infrastructure enabled in the Sandbox: " + gm.SyncInfrastructureEnabled + " (must be False)");

			if (verbose)
			{
				var so = new SerializedObject(gm);
				string[] fields = { "width", "height", "decayRate", "growthRate", "tickRate", "terrainInfluence",
					"startWithCenterSource", "nodeRadius", "nodeStrength", "connectionRange", "emitterMaxIntegrity",
					"nullifierRange", "nullifierDrainPerTick", "sandboxDebugControls", "mapSeed", "landformSize", "surfaceDetailLayers" };
				var sb = new StringBuilder();
				foreach (string f in fields)
				{
					SerializedProperty p = so.FindProperty(f);
					sb.Append(f).Append('=').Append(p == null ? "?" : PropValue(p)).Append("  ");
				}
				Line("Scene values: " + sb);
			}

			// Startup, exactly as Unity would call it.
			Invoke(gm, "Start");

			var grid = (Cell[,])Field(gm, "grid");
			var emitters = (List<Emitter>)Field(gm, "emitters");
			var structures = (List<PlacedStructure>)Field(gm, "structures");

			Check("Start() builds a 100x100 grid", grid.GetLength(0) == 100 && grid.GetLength(1) == 100, $"{grid.GetLength(0)}x{grid.GetLength(1)}");
			Check("Scene starts with no Emitter (startWithCenterSource off)", emitters.Count == 0, $"emitters={emitters.Count}");

			float minE = float.MaxValue, maxE = float.MinValue;
			foreach (Cell c in grid) { minE = Mathf.Min(minE, c.Elevation); maxE = Mathf.Max(maxE, c.Elevation); }
			Check("Terrain generated and normalized to 0..1", Mathf.Approximately(minE, 0f) && Mathf.Approximately(maxE, 1f), $"min={minE:F3} max={maxE:F3}");

			// --- Emitter and spread ---
			var center = new Vector2Int(50, 50);
			bool added = (bool)Invoke(gm, "TryAddEmitter", center);
			Check("Emitter placed at (50,50)", added && emitters.Count == 1, "");

			var history = new StringBuilder();
			int[] marks = { 1, 10, 25, 50, 100 };
			int tick = 0;
			var spread = new List<int>();
			foreach (int m in marks)
			{
				while (tick < m) { Tick(gm); tick++; }
				spread.Add(gm.ContaminatedCells);
				history.Append(gm.ContaminatedCells).Append(',');
			}
			if (verbose)
				Line($"Contaminated cells after ticks 1/10/25/50/100: {string.Join(" / ", spread)}");

			bool grows = true;
			for (int i = 1; i < spread.Count; i++) grows &= spread[i] > spread[i - 1];
			Check("Desync spreads: contaminated cells rise at every checkpoint", grows, string.Join(" < ", spread));
			Check("Active Emitter cell pinned at Source Strength 250", Mathf.Approximately(grid[50, 50].Desync, 250f), $"{grid[50, 50].Desync}");
			Check("Reality Integrity below 100% once Desync spreads", gm.RealityIntegrity < 100f, $"{gm.RealityIntegrity:F2}%");
			Check("Border cells stay at 0 (absorbing boundary)", grid[0, 50].Desync == 0f && grid[99, 50].Desync == 0f, "");

			// --- Structures and connectivity ---
			float desyncAtChainEnd = grid[50, 57].Desync;
			Place(gm, 50, 85, BuildTool.Core);
			Place(gm, 50, 78, BuildTool.Stabilizer);
			Place(gm, 50, 71, BuildTool.Stabilizer);
			Place(gm, 50, 64, BuildTool.Stabilizer);
			Place(gm, 50, 57, BuildTool.Stabilizer);
			Place(gm, 58, 64, BuildTool.Nullifier);  // exactly 8 from (50,64): inclusive range
			Place(gm, 66, 64, BuildTool.Stabilizer); // reachable only through that Nullifier
			Place(gm, 10, 10, BuildTool.Stabilizer); // far from everything
			Place(gm, 50, 40, BuildTool.Core);       // second Core must be refused

			Check("Exactly one Core accepted (second Core refused)", Count(structures, StructureType.Core) == 1, $"cores={Count(structures, StructureType.Core)}");
			Check("Core connected", Find(structures, 50, 85)?.IsConnected == true, "");
			Check("Stabilizer chain at 7-cell spacing all connected",
				Find(structures, 50, 78)?.IsConnected == true && Find(structures, 50, 71)?.IsConnected == true &&
				Find(structures, 50, 64)?.IsConnected == true && Find(structures, 50, 57)?.IsConnected == true, "");
			Check("Nullifier at exactly 8 cells connects (inclusive range)", Find(structures, 58, 64)?.IsConnected == true, "");
			Check("Nullifier does not relay: Stabilizer beyond it is disconnected", Find(structures, 66, 64)?.IsConnected == false, "");
			Check("Isolated Stabilizer is disconnected", Find(structures, 10, 10)?.IsConnected == false, "");
			Check("Disconnected Stabilizer's node is inactive", Find(structures, 10, 10)?.Node?.IsActive == false, "");
			Line($"Info: desync at (50,57) when the Stabilizer was placed = {desyncAtChainEnd:F1} (threshold 5)");

			Tick(gm); tick++;
			Check("Connected Stabilizer applies stabilization at its cell (= Node Strength 10)", Mathf.Approximately(grid[50, 57].Stabilization, 10f), $"{grid[50, 57].Stabilization:F3}");
			Check("Disconnected Stabilizer applies no stabilization", grid[10, 10].Stabilization == 0f, $"{grid[10, 10].Stabilization:F3}");
			if (verbose)
			{
				var conds = new StringBuilder();
				foreach (PlacedStructure s in structures)
					if (s.Node != null) conds.Append($"({s.Position.x},{s.Position.y})={s.Node.Condition} ");
				Line("Stabilizer conditions: " + conds);
			}

			// --- Nullifier damage ---
			Emitter emitter = emitters[0];
			float before = emitter.Integrity;
			Check("Emitter starts at 500 integrity", Mathf.Approximately(before, 500f), $"{before}");

			float desyncAtNullifierCell = grid[50, 55].Desync;
			Place(gm, 50, 55, BuildTool.Nullifier); // 5 cells from the Emitter
			Check("Placement allowed on a contaminated cell", desyncAtNullifierCell > 5f && Find(structures, 50, 55) != null, $"desync at (50,55) when placed={desyncAtNullifierCell:F1}");
			Check("Nullifier near Emitter is connected", Find(structures, 50, 55)?.IsConnected == true, "");

			Tick(gm); tick++;
			Check("One Nullifier drains 1 per tick", Mathf.Approximately(emitter.Integrity, 499f), $"{emitter.Integrity}");

			for (int i = 0; i < 99; i++) { Tick(gm); tick++; }
			Check("After 100 ticks with one Nullifier, integrity = 400", Mathf.Approximately(emitter.Integrity, 400f), $"{emitter.Integrity}");

			Place(gm, 47, 55, BuildTool.Nullifier); // second Nullifier, also within 6 of the Emitter
			Check("Second Nullifier connected", Find(structures, 47, 55)?.IsConnected == true, "");
			for (int i = 0; i < 10; i++) { Tick(gm); tick++; }
			Check("Two Nullifiers stack: 2 per tick (380 after 10 more ticks)", Mathf.Approximately(emitter.Integrity, 380f), $"{emitter.Integrity}");

			Check("Objective not complete while Emitter has integrity", !gm.PrimaryObjectiveComplete, "");

			int nullifyStart = tick;
			int guard = 0;
			while (!emitter.IsNullified && guard++ < 2000) { Tick(gm); tick++; }
			int ticksToNullify = tick - nullifyStart;
			Check("Emitter reaches 0 integrity (190 more ticks expected)", emitter.IsNullified && ticksToNullify == 190, $"ticks={ticksToNullify}, integrity={emitter.Integrity}");
			Check("PrimaryObjectiveComplete becomes true", gm.PrimaryObjectiveComplete, "");

			Tick(gm); tick++;
			Check("Nullified Emitter no longer sustains Desync (Source Output 0)", gm.SourceOutput == 0f, $"{gm.SourceOutput}");

			for (int i = 0; i < 50; i++) { Tick(gm); tick++; }
			if (verbose)
				Line($"50 ticks after nullify: contaminated cells={gm.ContaminatedCells}, Reality Integrity={gm.RealityIntegrity:F2}%, total ticks={tick}");

			// Telemetry path the HUD reads every frame.
			object stats = Invoke(gm, "GetDebugStats");
			Check("GetDebugStats() runs (HUD telemetry source)", stats != null, "");

			double total = 0d;
			foreach (Cell c in grid) total += c.Desync;
			history.Append(gm.ContaminatedCells);

			return new ScenarioResult { FinalTotalDesync = total, History = history.ToString(), TicksToNullify = ticksToNullify };
		}

		// The tick GridManager.Update() runs. Since increment 1 this is RunSimulationTick(), which first calls
		// UpdateSyncInfrastructure() (a no-op while Sync Infrastructure is disabled, as in the Sandbox) and
		// then the original sequence: Simulate, UpdateNullifiers, CalculateIntegrity, CalculateActivity,
		// UpdateContainmentStatus, RenderGrid.
		internal static void Tick(GridManager gm)
		{
			Invoke(gm, "RunSimulationTick");
		}

		private static void TryUpdate()
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			GridManager gm = UnityEngine.Object.FindAnyObjectByType<GridManager>();
			Invoke(gm, "Start");

			Line("Keyboard.current is " + (UnityEngine.InputSystem.Keyboard.current == null ? "null" : "present") +
				", Mouse.current is " + (UnityEngine.InputSystem.Mouse.current == null ? "null" : "present"));
			try
			{
				typeof(GridManager).GetMethod("Update", Any).Invoke(gm, null);
				Line("GridManager.Update() ran once without throwing.");
			}
			catch (TargetInvocationException e)
			{
				// Recorded, not counted as a failure: batch mode has no input devices.
				Line("GridManager.Update() threw " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message +
					" (expected if no keyboard exists; Update reads Keyboard.current without a null check)");
			}
		}

		private static void Place(GridManager gm, int x, int y, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { x, y, tool });

		internal static object Invoke(object target, string method, params object[] args)
		{
			MethodInfo mi = null;
			foreach (MethodInfo m in target.GetType().GetMethods(Any))
				if (m.Name == method && m.GetParameters().Length == args.Length) { mi = m; break; }
			if (mi == null) throw new MissingMethodException(target.GetType().Name, method);
			try { return mi.Invoke(target, args); }
			catch (TargetInvocationException e) { throw new Exception($"{method}() threw: {e.InnerException}", e.InnerException); }
		}

		internal static object Field(object target, string name) =>
			target.GetType().GetField(name, Any)?.GetValue(target) ?? throw new MissingFieldException(target.GetType().Name, name);

		private static PlacedStructure Find(List<PlacedStructure> list, int x, int y) =>
			list.Find(s => s.Position.x == x && s.Position.y == y);

		private static int Count(List<PlacedStructure> list, StructureType t) => list.FindAll(s => s.Type == t).Count;

		private static string PropValue(SerializedProperty p) => p.propertyType switch
		{
			SerializedPropertyType.Integer => p.intValue.ToString(),
			SerializedPropertyType.Boolean => p.boolValue.ToString(),
			SerializedPropertyType.Float => p.floatValue.ToString("R"),
			_ => p.propertyType.ToString()
		};

		private static void Check(string name, bool ok, string detail)
		{
			checks++;
			if (!ok) failures++;
			Line((ok ? "PASS  " : "FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
		}

		private static void Section(string title) => Line("\n== " + title + " ==");

		private static void Line(string s) => output.AppendLine(s);
	}
}
