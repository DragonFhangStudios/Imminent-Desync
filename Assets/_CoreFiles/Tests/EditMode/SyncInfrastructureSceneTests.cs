using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Sync Infrastructure inside the real GridManager. Each test opens Sandbox.unity, then turns Sync
	// Infrastructure on IN MEMORY with provisional test values. The scene file is never saved, so the
	// Sandbox itself stays unchanged. The numbers are test fixtures, not balance decisions.
	public class SyncInfrastructureSceneTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

		private GridManager gm;
		private Cell[,] grid;
		private List<PlacedStructure> structures;
		private List<Emitter> emitters;

		private void OpenSandbox(bool enableSync)
		{
			EditorSceneManager.OpenScene("Assets/_CoreFiles/Scenes/Sandbox.unity", OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm);

			var settings = (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");
			settings.enabled = enableSync;
			settings.coreGenerationPerTick = 4;
			settings.stabilizerConstructionCost = 40;
			settings.nullifierConstructionCost = 60;
			settings.maxConstructionIntakePerTick = 2;

			SandboxSmokeHarness.Invoke(gm, "Start");
			grid = (Cell[,])SandboxSmokeHarness.Field(gm, "grid");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
			emitters = (List<Emitter>)SandboxSmokeHarness.Field(gm, "emitters");
		}

		private void Place(int x, int y, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { x, y, tool });

		private void Tick(int count = 1)
		{
			for (int i = 0; i < count; i++)
				SandboxSmokeHarness.Tick(gm);
		}

		private PlacedStructure At(int x, int y) => structures.Find(s => s.Position.x == x && s.Position.y == y);

		[Test]
		public void Disabled_PlacementIsInstantAndOperational()
		{
			OpenSandbox(enableSync: false);

			Place(50, 85, BuildTool.Core);
			Place(50, 78, BuildTool.Stabilizer);

			Assert.IsFalse(At(50, 78).UnderConstruction);
			Assert.IsTrue(At(50, 78).Node.IsActive);

			Tick();
			Assert.AreEqual(10f, grid[50, 78].Stabilization, 1e-4f);
			Assert.AreEqual(0, gm.SyncDiagnostics.Ticks, "Disabled: the Sync step never runs.");
		}

		[Test]
		public void Enabled_StabilizerBlueprint_GivesNoStabilizationUntilComplete()
		{
			OpenSandbox(enableSync: true);

			Place(50, 85, BuildTool.Core);
			Place(50, 78, BuildTool.Stabilizer);
			PlacedStructure bp = At(50, 78);

			Assert.IsNotNull(bp, "Blueprint placement is immediate.");
			Assert.IsTrue(bp.UnderConstruction);
			Assert.IsTrue(bp.IsConnected);
			Assert.IsFalse(bp.Node.IsActive, "An unfinished Stabilizer's node is inactive.");

			Tick(19);
			Assert.IsTrue(bp.UnderConstruction);
			Assert.AreEqual(38, bp.ChargesDelivered);
			Assert.AreEqual(0f, grid[50, 78].Stabilization, "No stabilization while unfinished.");

			Tick();
			Assert.IsFalse(bp.UnderConstruction);
			Assert.IsTrue(bp.Node.IsActive);
			Assert.AreEqual(10f, grid[50, 78].Stabilization, 1e-4f, "Stabilizes from the tick it completes.");
		}

		[Test]
		public void Enabled_UnfinishedStabilizer_DoesNotRelay_ChainBuildsInOrder()
		{
			OpenSandbox(enableSync: true);

			Place(50, 85, BuildTool.Core);
			Place(50, 78, BuildTool.Stabilizer);
			Place(50, 71, BuildTool.Stabilizer); // only reachable through (50,78)

			Assert.IsFalse(At(50, 71).IsConnected);

			Tick(5);
			Assert.AreEqual(ConstructionStatus.WaitingForConnection, At(50, 71).Construction);
			Assert.AreEqual(0, At(50, 71).ChargesDelivered);

			Tick(15); // (50,78) completes on tick 20 and relays
			Assert.IsFalse(At(50, 78).UnderConstruction);
			Assert.IsTrue(At(50, 71).IsConnected);

			Tick(20);
			Assert.IsFalse(At(50, 71).UnderConstruction);
		}

		[Test]
		public void Enabled_OccupancyRulesStillApply()
		{
			OpenSandbox(enableSync: true);

			Assert.IsTrue((bool)SandboxSmokeHarness.Invoke(gm, "TryAddEmitter", new Vector2Int(30, 30)));
			Place(50, 85, BuildTool.Core);
			Place(50, 78, BuildTool.Stabilizer);
			int count = structures.Count;

			Place(50, 78, BuildTool.Nullifier);  // on a blueprint
			Place(50, 85, BuildTool.Stabilizer); // on the Core
			Place(30, 30, BuildTool.Stabilizer); // on the Emitter
			Place(60, 85, BuildTool.Core);       // second Core
			Place(-1, 5, BuildTool.Stabilizer);  // off the grid

			Assert.AreEqual(count, structures.Count);
			Assert.AreEqual(StructureType.Stabilizer, grid[50, 78].Structure);
		}

		[Test]
		public void Enabled_BlueprintOnContaminatedGround_IsPlacedButWaits()
		{
			OpenSandbox(enableSync: true);

			SandboxSmokeHarness.Invoke(gm, "TryAddEmitter", new Vector2Int(50, 50));
			Tick(30);
			Assert.Greater(grid[50, 52].Desync, 5f, "Test setup: (50,52) is contaminated.");

			Place(50, 58, BuildTool.Core);
			Place(50, 52, BuildTool.Stabilizer);
			PlacedStructure bp = At(50, 52);

			Assert.IsNotNull(bp, "Placement on contaminated ground is allowed.");
			Tick(10);
			Assert.AreEqual(ConstructionStatus.WaitingForCleanGround, bp.Construction);
			Assert.AreEqual(0, bp.ChargesDelivered);
			Assert.AreEqual(0f, grid[50, 52].Stabilization);
		}

		[Test]
		public void Enabled_NullifierBlueprint_DoesNotDrainEmitterUntilComplete()
		{
			OpenSandbox(enableSync: true);

			SandboxSmokeHarness.Invoke(gm, "TryAddEmitter", new Vector2Int(50, 50));
			Place(50, 63, BuildTool.Core);
			Place(50, 56, BuildTool.Nullifier); // 6 from the Emitter, 7 from the Core
			PlacedStructure bp = At(50, 56);

			Tick(5);
			Assert.IsTrue(bp.UnderConstruction);
			Assert.AreEqual(500f, emitters[0].Integrity, "An unfinished Nullifier does nothing.");
		}

		[Test]
		public void Enabled_SameScenario_SameResults()
		{
			string a = RunScenario();
			string b = RunScenario();

			Assert.AreEqual(a, b);
		}

		private string RunScenario()
		{
			OpenSandbox(enableSync: true);
			SandboxSmokeHarness.Invoke(gm, "TryAddEmitter", new Vector2Int(50, 50));
			Place(50, 85, BuildTool.Core);

			var log = new System.Text.StringBuilder();
			for (int t = 0; t < 150; t++)
			{
				if (t == 0) { Place(50, 78, BuildTool.Stabilizer); Place(57, 85, BuildTool.Stabilizer); }
				if (t == 10) { Place(50, 71, BuildTool.Stabilizer); Place(50, 64, BuildTool.Stabilizer); }
				if (t == 40) { Place(50, 57, BuildTool.Stabilizer); Place(52, 55, BuildTool.Nullifier); }

				Tick();
				SyncDiagnostics d = gm.SyncDiagnostics;
				log.Append(d.Distributed).Append('/').Append(d.Reconstructing).Append('/').Append(d.WaitingForCleanGround)
					.Append('/').Append(gm.ContaminatedCells).Append(';');
			}

			foreach (PlacedStructure s in structures)
				log.Append(s.ChargesDelivered).Append(s.UnderConstruction ? 'u' : 'c').Append(',');

			return log.ToString();
		}
	}
}
