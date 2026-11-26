using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Increment 2A: the Sync Supply playtest scene (built from Sandbox.unity by PlaytestSceneBuilder).
	// The Director's manual checklist and these tests use the SAME scene, as saved, with the same Emitter
	// (placed at the grid centre at start) and the same coordinates, so both are tested the same way.
	// Nothing is overridden in memory here.
	public class PlaytestSceneTests
	{
		private const string ScenePath = "Assets/_CoreFiles/Scenes/Playtest_SyncSupply.unity";
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

		// Checklist coordinates (Docs/SYNC_INFRASTRUCTURE.md §8.6 and the playtest checklist).
		internal static readonly Vector2Int EmitterPos = new(50, 50);
		internal static readonly Vector2Int CorePos = new(50, 80);
		internal static readonly Vector2Int A = new(50, 74);
		internal static readonly Vector2Int B = new(44, 78);
		internal static readonly Vector2Int C = new(53, 80);   // nearer the Core than A and B, placed later
		internal static readonly Vector2Int[] Extra = { new(47, 83), new(54, 84) }; // over-expansion: 5 Stabilizers

		private GridManager gm;
		private List<PlacedStructure> structures;

		private void Open()
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm, "The playtest scene has a GridManager.");
			SandboxSmokeHarness.Invoke(gm, "Start");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
		}

		private SyncInfrastructureSettings Settings => (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");
		private void Tick() => SandboxSmokeHarness.Tick(gm);
		private PlacedStructure At(Vector2Int p) => structures.Find(s => s.Position == p);

		private void Place(Vector2Int p, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { p.x, p.y, tool });

		private List<Vector2Int> Foothold()
		{
			var cells = new List<Vector2Int>();
			foreach (PlacedStructure s in SyncInfrastructure.FindFoothold(structures, Settings))
				cells.Add(s.Position);
			return cells;
		}

		[Test]
		public void PlaytestScene_HasSyncSupplyAndDefeatOn_AtThe2AValues_AndStartsWithOneEmitterAtTheCentre()
		{
			Open();
			SyncInfrastructureSettings s = Settings;

			Assert.IsTrue(s.enabled, "Sync Charges and Reconstruction on.");
			Assert.IsTrue(s.stabilizerSupplyEnabled, "U2 supply on.");
			Assert.AreEqual(8, s.coreGenerationPerTick, "Generation 8 per tick (provisional, 2A).");
			Assert.AreEqual(0.25f, s.constructionMinimumShare, "Construction share 25 % (provisional, 2A).");
			Assert.IsTrue((bool)SandboxSmokeHarness.Field(gm, "realityIntegrityDefeat"), "S4 defeat on.");
			Assert.AreEqual(30f, (float)SandboxSmokeHarness.Field(gm, "defeatIntegrityThreshold"), "S4 at 30 % (provisional, 2A).");
			Assert.IsTrue((bool)SandboxSmokeHarness.Field(gm, "startWithCenterSource"));
			Assert.IsTrue((bool)SandboxSmokeHarness.Field(gm, "sandboxDebugControls"), "Sandbox controls kept.");

			var emitters = (System.Collections.IList)SandboxSmokeHarness.Field(gm, "emitters");
			Assert.AreEqual(1, emitters.Count, "One Emitter placed at start.");
			Assert.AreEqual(EmitterPos, ((Emitter)emitters[0]).Position);

			Assert.IsFalse(gm.SyncDebugOverlayVisible, "The overlay starts hidden.");
			StringAssert.Contains("generated 0", gm.SyncDebugOverlayText, "No Sync Core yet.");
		}

		// The manual checklist, step by step, with the same coordinates. Asserts the expected behavior and
		// prints the tick at which each thing happens (the checklist quotes these numbers).
		[Test, Timeout(600000)]
		public void Checklist_ScriptedRun_MatchesTheManualSteps()
		{
			Open();
			var log = new StringBuilder();

			// Step 2: Core and two Stabilizers, placed while paused at tick 0.
			Place(CorePos, BuildTool.Core);
			Place(A, BuildTool.Stabilizer);
			Place(B, BuildTool.Stabilizer);
			Assert.IsTrue(At(A).UnderConstruction && At(B).UnderConstruction, "Stabilizers start as blueprints.");

			int t = 0;
			while ((At(A).UnderConstruction || At(B).UnderConstruction) && t < 400) { Tick(); t++; }
			Assert.IsFalse(At(A).UnderConstruction || At(B).UnderConstruction, "Both foothold Stabilizers complete.");
			Assert.AreEqual(8, gm.SyncDiagnostics.Generated);
			log.AppendLine($"step 2: A and B complete at tick {t}");
			for (int i = 0; i < 5; i++) { Tick(); t++; }
			Assert.AreEqual(1f, At(A).SupplyEffectiveness);
			Assert.AreEqual(1f, At(B).SupplyEffectiveness);
			CollectionAssert.AreEquivalent(new[] { A, B }, Foothold());

			// Step 3: a third Stabilizer, nearer the Core. Construction keeps receiving charges (F3) and, once it
			// is complete, it joins the foothold in place of the farther one (F4).
			Place(C, BuildTool.Stabilizer);
			int placedAt = t;
			int minConstruction = int.MaxValue;
			while (At(C).UnderConstruction && t < placedAt + 400)
			{
				Tick(); t++;
				if (At(C).UnderConstruction) minConstruction = Mathf.Min(minConstruction, gm.SyncDiagnostics.ConstructionDelivered);
			}
			Assert.IsFalse(At(C).UnderConstruction, "C completes.");
			Assert.GreaterOrEqual(minConstruction, 2, "Construction received at least the share every tick.");
			log.AppendLine($"step 3: C placed at tick {placedAt}, complete at tick {t} ({t - placedAt} ticks); construction min {minConstruction}/tick");
			Tick(); t++;
			CollectionAssert.AreEqual(new[] { C, A }, Foothold(), "Foothold = the two nearest the Core.");

			// Step 4: over-expansion: five Stabilizers need 10 per tick, generation is 8.
			foreach (Vector2Int p in Extra) Place(p, BuildTool.Stabilizer);
			placedAt = t;
			while (Extra.Length > 0 && System.Array.Exists(Extra, p => At(p).UnderConstruction) && t < placedAt + 800) { Tick(); t++; }
			Assert.IsFalse(System.Array.Exists(Extra, p => At(p).UnderConstruction), "Extra Stabilizers complete.");
			int extrasDone = t;
			for (int i = 0; i < 200; i++) { Tick(); t++; } // stores run down to what generation sustains
			log.AppendLine($"step 4: extras placed at tick {placedAt}, complete by tick {extrasDone}; measured at tick {t}");
			float sum = 0f;
			foreach (PlacedStructure s in structures)
				if (s.Type == StructureType.Stabilizer && !s.UnderConstruction) sum += s.SupplyEffectiveness;
			Assert.AreEqual(1f, At(C).SupplyEffectiveness, "Foothold stays at full strength.");
			Assert.AreEqual(1f, At(A).SupplyEffectiveness, "Foothold stays at full strength.");
			Assert.LessOrEqual(sum, 4.0001f, "Generation 8 sustains at most four Stabilizers' worth of upkeep.");
			log.AppendLine($"step 4: total effectiveness {sum:F2} of 5 Stabilizers; overlay:\n{gm.SyncDebugOverlayText}");

			// Step 5: let it run; record when S4 (30 %) fails the mission.
			int defeatAt = -1;
			while (t < 20000)
			{
				Tick(); t++;
				if (gm.MissionDefeated) { defeatAt = t; break; }
			}
			log.AppendLine($"step 5: MISSION FAILED (Reality Integrity < 30 %) at tick {defeatAt} ({defeatAt / 10f:F0} s at 0.1 s/tick); integrity {gm.RealityIntegrity:F2}");
			TestContext.WriteLine(log.ToString());
		}

		// Step 5 without any player action: when does S4 end the mission?
		[Test, Timeout(600000)]
		public void NoPlayerAction_S4DefeatTick_IsRecorded()
		{
			Open();
			int t = 0;
			while (!gm.MissionDefeated && t < 20000) { Tick(); t++; }
			TestContext.WriteLine($"No player action: MISSION FAILED at tick {(gm.MissionDefeated ? t : -1)} ({t / 10f:F0} s); integrity {gm.RealityIntegrity:F2}");
			Assert.IsTrue(gm.MissionDefeated, "Unchecked, the Emitter drives Reality Integrity below 30 %.");
		}

		[Test]
		public void SameChecklistRun_SameResults()
		{
			Assert.AreEqual(Fingerprint(), Fingerprint());
		}

		private string Fingerprint()
		{
			Open();
			Place(CorePos, BuildTool.Core);
			Place(A, BuildTool.Stabilizer);
			Place(B, BuildTool.Stabilizer);
			for (int i = 0; i < 60; i++) Tick();
			Place(C, BuildTool.Stabilizer);
			for (int i = 0; i < 240; i++) Tick();

			var sb = new StringBuilder();
			sb.Append(gm.RealityIntegrity.ToString("R")).Append('|').Append(gm.SyncDiagnostics.TotalDistributed).Append('|');
			foreach (PlacedStructure s in structures)
				sb.Append(s.Position).Append(s.ChargesDelivered).Append(s.StoredCharges).Append(s.SupplyEffectiveness.ToString("R")).Append(';');
			return sb.ToString();
		}
	}
}
