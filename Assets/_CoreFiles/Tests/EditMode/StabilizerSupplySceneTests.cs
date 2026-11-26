using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// U2 Stabilizer supply, safeguards S1–S4, inside the real GridManager and the real Desync simulation.
	// Each test opens Sandbox.unity and turns the opt-in systems on IN MEMORY (never saved). The numbers are
	// provisional test fixtures. Measurements follow the research metric standard: the game's own condition
	// values and contamination threshold, with centre cell, footprint, effectiveness and connectivity reported
	// separately.
	public class StabilizerSupplySceneTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		private const float Threshold = 5f; // Sandbox contaminationThreshold

		private GridManager gm;
		private Cell[,] grid;
		private List<PlacedStructure> structures;
		private List<Emitter> emitters;
		private SyncInfrastructureSettings settings;

		private void OpenSandbox(bool supply = true, bool defeat = false, float defeatThreshold = 50f)
		{
			EditorSceneManager.OpenScene("Assets/_CoreFiles/Scenes/Sandbox.unity", OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm);

			settings = (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");
			settings.enabled = true;
			settings.stabilizerSupplyEnabled = supply;
			settings.coreGenerationPerTick = 8;
			settings.stabilizerConstructionCost = 40;
			settings.nullifierConstructionCost = 60;
			settings.maxConstructionIntakePerTick = 2;
			settings.stabilizerUpkeepPerTick = 2;
			settings.stabilizerStoreCapacity = 20;
			settings.maxSupplyIntakePerTick = 4;
			settings.footholdStabilizerCount = 2;
			settings.coreSafeZoneRadius = 3f;

			typeof(GridManager).GetField("realityIntegrityDefeat", Any).SetValue(gm, defeat);
			typeof(GridManager).GetField("defeatIntegrityThreshold", Any).SetValue(gm, defeatThreshold);

			SandboxSmokeHarness.Invoke(gm, "Start");
			grid = (Cell[,])SandboxSmokeHarness.Field(gm, "grid");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
			emitters = (List<Emitter>)SandboxSmokeHarness.Field(gm, "emitters");
		}

		private void Place(int x, int y, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { x, y, tool });

		// The player's own removal (Shift + Middle Click). The marker is detached first because
		// Destroy() is not allowed in edit mode; the removal logic itself is the game's.
		private void Remove(int x, int y)
		{
			At(x, y).Marker = null;
			SandboxSmokeHarness.Invoke(gm, "RemoveNearestStructure", x, y);
			Assert.IsNull(At(x, y));
		}

		private void AddEmitter(int x, int y) =>
			Assert.IsTrue((bool)SandboxSmokeHarness.Invoke(gm, "TryAddEmitter", new Vector2Int(x, y)));

		private void Tick(int count = 1)
		{
			for (int i = 0; i < count; i++)
				SandboxSmokeHarness.Tick(gm);
		}

		private PlacedStructure At(int x, int y) => structures.Find(s => s.Position.x == x && s.Position.y == y);

		// Footprint measurements with the game's own definitions (Stabilizer radius 5 in the Sandbox).
		private struct Footprint
		{
			public float DesyncSum;
			public int Contaminated;
			public bool CentreContaminated;
		}

		private Footprint Measure(Vector2Int centre, int radius = 5)
		{
			var f = new Footprint();
			for (int dx = -radius; dx <= radius; dx++)
			for (int dy = -radius; dy <= radius; dy++)
			{
				if (Mathf.Sqrt(dx * dx + dy * dy) > radius) continue;
				float d = grid[centre.x + dx, centre.y + dy].Desync;
				f.DesyncSum += d;
				if (d > Threshold) f.Contaminated++;
			}
			f.CentreContaminated = grid[centre.x, centre.y].Desync > Threshold;
			return f;
		}

		private string Report(string label, PlacedStructure s, Vector2Int pos)
		{
			Footprint f = Measure(pos);
			string node = s?.Node != null ? $"{s.Node.Condition}, footprint {s.Node.ContaminatedFootprintCells}/{s.Node.FootprintCells}" : "removed";
			string supply = s != null ? $"effectiveness {s.SupplyEffectiveness:P0} ({s.Supply}), store {s.StoredCharges}, connected {s.IsConnected}" : "-";
			return $"{label} ({pos.x},{pos.y}): centre contaminated {f.CentreContaminated}; footprint contaminated {f.Contaminated} cells, Desync sum {f.DesyncSum:F1}; condition {node}; {supply}";
		}

		// "Can the player still recover?": the Sync Core is connected and generating at least one Stabilizer's
		// upkeep, and there is free ground in its S1 safe zone where a blueprint is always buildable.
		private bool RecoveryActionExists()
		{
			PlacedStructure core = structures.Find(s => s.Type == StructureType.Core);
			if (core == null || !core.IsConnected || settings.CoreGeneration() < settings.stabilizerUpkeepPerTick)
				return false;

			int r = Mathf.FloorToInt(settings.coreSafeZoneRadius);
			for (int dx = -r; dx <= r; dx++)
			for (int dy = -r; dy <= r; dy++)
			{
				var p = core.Position + new Vector2Int(dx, dy);
				if (p.x < 0 || p.y < 0 || p.x >= 100 || p.y >= 100) continue;
				if (grid[p.x, p.y].Structure == StructureType.None && SyncInfrastructure.InCoreSafeZone(structures, p, settings))
					return true;
			}
			return false;
		}

		// ---------------------------------------------------------------
		// Required acceptance scenario: severing a critical connection.
		// ---------------------------------------------------------------

		private static readonly Vector2Int CorePos = new(50, 85);
		private static readonly Vector2Int S1 = new(50, 78);
		private static readonly Vector2Int S2 = new(50, 71);
		private static readonly Vector2Int S3 = new(50, 64);
		private const int HoldTicks = 200;
		private const int SeveredTicks = 150;

		private void BuildSuppliedLine()
		{
			AddEmitter(50, 52);
			Place(CorePos.x, CorePos.y, BuildTool.Core);
			Place(S1.x, S1.y, BuildTool.Stabilizer);
			Place(S2.x, S2.y, BuildTool.Stabilizer);
			Place(S3.x, S3.y, BuildTool.Stabilizer);
			Tick(HoldTicks);
		}

		[Test]
		public void SeveredConnection_DownstreamStarves_DesyncAdvances_AndRebuildingRecovers()
		{
			// Control: the same line, never severed.
			OpenSandbox();
			BuildSuppliedLine();
			Tick(SeveredTicks);
			Footprint controlS3 = Measure(S3);
			Footprint controlS2 = Measure(S2);
			TestContext.WriteLine(Report("CONTROL S3", At(S3.x, S3.y), S3));
			TestContext.WriteLine(Report("CONTROL S2", At(S2.x, S2.y), S2));

			// 1. Build a supplied line: Sync Core -> S1 -> S2 -> S3, holding a frontier.
			OpenSandbox();
			BuildSuppliedLine();
			foreach (Vector2Int p in new[] { S1, S2, S3 })
			{
				PlacedStructure s = At(p.x, p.y);
				Assert.IsFalse(s.UnderConstruction, $"({p.x},{p.y}) built");
				Assert.IsTrue(s.IsConnected, $"({p.x},{p.y}) connected");
				Assert.AreEqual(1f, s.SupplyEffectiveness, $"({p.x},{p.y}) fully supplied");
			}
			TestContext.WriteLine(Report("HELD S3", At(S3.x, S3.y), S3));
			Assert.IsTrue(RecoveryActionExists(), "Recovery action exists before the cut.");

			// 2. The player removes S1, severing the critical connection.
			Remove(S1.x, S1.y);
			PlacedStructure s2 = At(S2.x, S2.y);
			PlacedStructure s3 = At(S3.x, S3.y);

			// 3. S2 and S3 are disconnected, receive nothing, run down their stores, then stabilize nothing.
			Assert.IsFalse(s2.IsConnected);
			Assert.IsFalse(s3.IsConnected);
			int storeBefore = s3.StoredCharges;
			Tick(3);
			Assert.Less(s3.StoredCharges, storeBefore, "Stores deplete.");
			Assert.AreEqual(1f, s3.SupplyEffectiveness, "S3: still working on its store right after the cut.");
			Tick(12);
			Assert.AreEqual(0f, s2.SupplyEffectiveness);
			Assert.AreEqual(0f, s3.SupplyEffectiveness);
			Assert.AreEqual(SupplyStatus.Starved, s3.Supply);
			Assert.AreEqual(0f, grid[S3.x, S3.y].Stabilization, "Starved: zero stabilization.");
			Assert.IsTrue(RecoveryActionExists(), "Recovery action exists while starved.");

			Tick(SeveredTicks - 15);
			Footprint severedS3 = Measure(S3);
			Footprint severedS2 = Measure(S2);
			TestContext.WriteLine(Report("SEVERED S3", s3, S3));
			TestContext.WriteLine(Report("SEVERED S2", s2, S2));
			Assert.Greater(severedS3.DesyncSum, controlS3.DesyncSum, "Desync advanced into S3's former footprint (vs. the unsevered control).");
			Assert.GreaterOrEqual(severedS3.Contaminated, controlS3.Contaminated);
			Assert.GreaterOrEqual(severedS2.DesyncSum, controlS2.DesyncSum);
			Assert.IsTrue(RecoveryActionExists(), "Recovery action exists after Desync advanced.");

			// 4. The player rebuilds the gap with a new blueprint.
			Assert.LessOrEqual(grid[S1.x, S1.y].Desync, Threshold, "Test setup: the gap is still clean ground.");
			Place(S1.x, S1.y, BuildTool.Stabilizer);
			PlacedStructure gap = At(S1.x, S1.y);
			int guard = 0;
			while (gap.UnderConstruction && guard++ < 200) Tick();
			Assert.IsFalse(gap.UnderConstruction, "The gap blueprint completes.");
			Assert.IsTrue(s2.IsConnected && s3.IsConnected, "S2 and S3 reconnected.");

			Tick(30);
			Assert.AreEqual(1f, s2.SupplyEffectiveness, "Supply resumes at S2.");
			Assert.AreEqual(1f, s3.SupplyEffectiveness, "Supply resumes at S3.");
			Footprint reconnected = Measure(S3);

			Tick(200);
			Footprint pushedBack = Measure(S3);
			TestContext.WriteLine(Report("RECOVERED S3", s3, S3) + $" (footprint Desync sum at reconnection {reconnected.DesyncSum:F1})");
			Assert.Less(pushedBack.DesyncSum, reconnected.DesyncSum, "Desync is pushed back once supply returns.");
			Assert.IsTrue(RecoveryActionExists());
		}

		// ---------------------------------------------------------------
		// "Can the player still recover?" from a collapse around the Sync Core (S1).
		// ---------------------------------------------------------------

		[Test]
		public void S1_PlayerCanRebuildFromTheSyncCore_AfterCollapse()
		{
			OpenSandbox();
			AddEmitter(50, 50);
			Tick(250); // Desync spreads with no infrastructure at all

			Place(50, 55, BuildTool.Core);
			var nearCore = new Vector2Int(50, 57); // 2 cells from the Core: in the S1 safe zone
			var farther = new Vector2Int(50, 59);  // 4 cells: outside it
			Assert.Greater(grid[nearCore.x, nearCore.y].Desync, Threshold, "Test setup: ground next to the Core is contaminated.");
			Assert.Greater(grid[farther.x, farther.y].Desync, Threshold, "Test setup: ground farther out is contaminated.");
			Assert.IsTrue(RecoveryActionExists());

			Place(nearCore.x, nearCore.y, BuildTool.Stabilizer);
			Place(farther.x, farther.y, BuildTool.Stabilizer);
			Tick(25);

			PlacedStructure a = At(nearCore.x, nearCore.y);
			PlacedStructure b = At(farther.x, farther.y);
			Assert.IsFalse(a.UnderConstruction, "S1: a blueprint next to the Sync Core builds on contaminated ground.");
			Assert.AreEqual(ConstructionStatus.WaitingForCleanGround, b.Construction, "Outside the safe zone the clean-ground rule still applies.");

			float desyncAtA = grid[nearCore.x, nearCore.y].Desync;
			Tick(300);
			TestContext.WriteLine(Report("REBUILT A", a, nearCore));
			TestContext.WriteLine($"Blueprint B at ({farther.x},{farther.y}): {b.Construction}, {b.ChargesDelivered}/{b.ChargesRequired}");
			Assert.AreEqual(1f, a.SupplyEffectiveness, "The rebuilt Stabilizer is supplied (S2 foothold).");
			Assert.Less(grid[nearCore.x, nearCore.y].Desync, desyncAtA, "It pushes Desync back at its own cell.");
		}

		// ---------------------------------------------------------------
		// U2 inside the simulation: effectiveness scales stabilization.
		// ---------------------------------------------------------------

		[Test]
		public void U2_PartialSupply_HalvesStabilization_AndStarvationGivesZero()
		{
			OpenSandbox();
			settings.footholdStabilizerCount = 0;
			settings.coreGenerationPerTick = 1;
			Place(CorePos.x, CorePos.y, BuildTool.Core);
			Place(S1.x, S1.y, BuildTool.Stabilizer);
			Tick(45); // 40 charges at 1 per tick, then supply

			PlacedStructure s = At(S1.x, S1.y);
			Assert.IsFalse(s.UnderConstruction);
			Assert.AreEqual(0.5f, s.SupplyEffectiveness, 1e-6f);
			Assert.AreEqual(5f, grid[S1.x, S1.y].Stabilization, 1e-4f, "Node Strength 10 x effectiveness 0.5.");

			Remove(CorePos.x, CorePos.y);
			Tick();
			Assert.AreEqual(0f, s.SupplyEffectiveness);
			Assert.AreEqual(0f, grid[S1.x, S1.y].Stabilization);
		}

		[Test]
		public void U2_Off_SyncOn_StabilizersAreNotSupplied_AsInIncrement1()
		{
			OpenSandbox(supply: false);
			Place(CorePos.x, CorePos.y, BuildTool.Core);
			Place(S1.x, S1.y, BuildTool.Stabilizer);
			Tick(40);

			PlacedStructure s = At(S1.x, S1.y);
			Assert.IsFalse(s.UnderConstruction);
			Assert.AreEqual(SupplyStatus.NotApplicable, s.Supply);
			Assert.AreEqual(10f, grid[S1.x, S1.y].Stabilization, 1e-4f);
		}

		// ---------------------------------------------------------------
		// S4: Reality Integrity defeat (opt-in).
		// ---------------------------------------------------------------

		[Test]
		public void S4_Defeat_LatchesAndPauses_BelowThreshold()
		{
			OpenSandbox(defeat: true, defeatThreshold: 99.9f);
			AddEmitter(50, 50);

			int guard = 0;
			while (!gm.MissionDefeated && guard++ < 200) Tick();

			Assert.IsTrue(gm.MissionDefeated);
			Assert.Less(gm.RealityIntegrity, 99.9f);
			Assert.IsTrue((bool)SandboxSmokeHarness.Field(gm, "isPaused"), "Defeat ends the mission: the simulation pauses.");
		}

		[Test]
		public void S4_Defeat_Off_NeverTriggers()
		{
			OpenSandbox(defeat: false, defeatThreshold: 99.9f);
			AddEmitter(50, 50);
			Tick(100);

			Assert.Less(gm.RealityIntegrity, 99.9f);
			Assert.IsFalse(gm.MissionDefeated);
		}

		[Test]
		public void S4_Defeat_DoesNotTriggerAfterTheObjectiveIsComplete()
		{
			OpenSandbox(defeat: false, defeatThreshold: 99.9f);
			AddEmitter(50, 50);
			Tick(50);
			emitters[0].Integrity = 0f; // objective complete (all Emitters nullified)

			typeof(GridManager).GetField("realityIntegrityDefeat", Any).SetValue(gm, true);
			Tick(20);

			Assert.IsTrue(gm.PrimaryObjectiveComplete);
			Assert.Less(gm.RealityIntegrity, 99.9f);
			Assert.IsFalse(gm.MissionDefeated, "Victory first: no defeat after the objective is complete.");
		}

		[Test]
		public void Sandbox_U2AndDefeat_AreDisabled()
		{
			EditorSceneManager.OpenScene("Assets/_CoreFiles/Scenes/Sandbox.unity", OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			var s = (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");

			Assert.IsFalse(s.enabled);
			Assert.IsFalse(s.stabilizerSupplyEnabled);
			Assert.IsFalse((bool)SandboxSmokeHarness.Field(gm, "realityIntegrityDefeat"));
		}
	}
}
