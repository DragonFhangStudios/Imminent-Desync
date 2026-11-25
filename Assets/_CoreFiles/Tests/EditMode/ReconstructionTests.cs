using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Pure tests of Sync Charge generation and blueprint Reconstruction, without a scene.
	// The numbers are provisional test fixtures, not balance decisions.
	public class ReconstructionTests
	{
		private const float Range = 8f;

		private static SyncInfrastructureSettings Settings() => new()
		{
			enabled = true,
			coreGenerationPerTick = 4,
			stabilizerConstructionCost = 40,
			nullifierConstructionCost = 60,
			maxConstructionIntakePerTick = 2
		};

		private sealed class World
		{
			public readonly List<PlacedStructure> Structures = new();
			public readonly HashSet<Vector2Int> Contaminated = new();
			public readonly SyncInfrastructureSettings Settings = ReconstructionTests.Settings();
			public readonly SyncDiagnostics Diagnostics = new();
			private long order;

			public PlacedStructure Core(int x, int y) => Add(StructureType.Core, x, y, blueprint: false);
			public PlacedStructure Blueprint(StructureType type, int x, int y) => Add(type, x, y, blueprint: true);
			public PlacedStructure Built(StructureType type, int x, int y) => Add(type, x, y, blueprint: false);

			private PlacedStructure Add(StructureType type, int x, int y, bool blueprint)
			{
				var s = new PlacedStructure(type, new Vector2Int(x, y)) { PlacementOrder = ++order };
				if (blueprint)
					SyncInfrastructure.MakeBlueprint(s, Settings);
				Structures.Add(s);
				InfrastructureNetwork.Recalculate(Structures, Range);
				return s;
			}

			// Same order as GridManager: step, then refresh connectivity if anything completed.
			public void Tick(int count = 1)
			{
				for (int i = 0; i < count; i++)
				{
					if (SyncInfrastructure.Step(Structures, p => !Contaminated.Contains(p), Settings, Diagnostics))
						InfrastructureNetwork.Recalculate(Structures, Range);
				}
			}
		}

		[Test]
		public void SyncCore_GeneratesConfiguredChargesPerTick_UnusedWhenNoDemand()
		{
			var w = new World();
			w.Core(0, 0);

			w.Tick(3);

			Assert.AreEqual(4, w.Diagnostics.Generated);
			Assert.AreEqual(0, w.Diagnostics.Demand);
			Assert.AreEqual(4, w.Diagnostics.Unused);
			Assert.AreEqual(12, w.Diagnostics.TotalGenerated);
		}

		[Test]
		public void NoSyncCore_NoGeneration_NoConstruction()
		{
			var w = new World();
			PlacedStructure bp = w.Blueprint(StructureType.Stabilizer, 0, 0);

			w.Tick(10);

			Assert.AreEqual(0, w.Diagnostics.TotalGenerated);
			Assert.AreEqual(0, bp.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.WaitingForConnection, bp.Construction);
		}

		[Test]
		public void SyncCore_IsNeverABlueprint()
		{
			var w = new World();
			PlacedStructure core = w.Core(0, 0);
			SyncInfrastructure.MakeBlueprint(core, w.Settings);

			Assert.IsFalse(core.UnderConstruction);
			Assert.AreEqual(ConstructionStatus.Operational, core.Construction);
		}

		[Test]
		public void Blueprint_CompletesAfterExactlyItsCost_AndNotBefore()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure bp = w.Blueprint(StructureType.Stabilizer, 5, 0);

			w.Tick(19); // 40 charges at 2 per tick: 20 ticks
			Assert.IsTrue(bp.UnderConstruction);
			Assert.AreEqual(38, bp.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.Reconstructing, bp.Construction);

			w.Tick();
			Assert.IsFalse(bp.UnderConstruction);
			Assert.AreEqual(40, bp.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.Operational, bp.Construction);
			Assert.AreEqual(1, w.Diagnostics.CompletedThisTick);

			w.Tick(5);
			Assert.AreEqual(40, bp.ChargesDelivered, "A completed structure takes no more charges.");
			Assert.AreEqual(1, w.Diagnostics.TotalCompleted);
		}

		[Test]
		public void NullifierBlueprint_UsesItsOwnCost()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure bp = w.Blueprint(StructureType.Nullifier, 5, 0);

			w.Tick(29);
			Assert.IsTrue(bp.UnderConstruction);

			w.Tick();
			Assert.IsFalse(bp.UnderConstruction);
			Assert.AreEqual(60, bp.ChargesDelivered);
		}

		[Test]
		public void DisconnectedBlueprint_ReceivesNothing_AndWaitsForConnection()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure far = w.Blueprint(StructureType.Stabilizer, 50, 50);

			w.Tick(30);

			Assert.IsFalse(far.IsConnected);
			Assert.AreEqual(0, far.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.WaitingForConnection, far.Construction);
			Assert.AreEqual(1, w.Diagnostics.WaitingForConnection);
		}

		[Test]
		public void BlueprintOnContaminatedGround_Waits_ThenBuildsOnceClean()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure bp = w.Blueprint(StructureType.Stabilizer, 5, 0);
			w.Contaminated.Add(new Vector2Int(5, 0));

			w.Tick(10);
			Assert.IsTrue(bp.IsConnected);
			Assert.AreEqual(0, bp.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.WaitingForCleanGround, bp.Construction);
			Assert.AreEqual(0, w.Diagnostics.Demand);

			w.Contaminated.Clear();
			w.Tick();
			Assert.AreEqual(2, bp.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.Reconstructing, bp.Construction);
		}

		[Test]
		public void UnfinishedStructure_DoesNotRelay_UntilComplete()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure link = w.Blueprint(StructureType.Stabilizer, 8, 0);
			PlacedStructure beyond = w.Built(StructureType.Stabilizer, 16, 0); // only reachable through link

			Assert.IsTrue(link.IsConnected, "A blueprint connects to the network...");
			Assert.IsFalse(beyond.IsConnected, "...but does not relay it onward.");
			Assert.IsFalse(InfrastructureNetwork.WouldConnect(w.Structures, new Vector2Int(16, 0), StructureType.Stabilizer, Range));

			w.Tick(20);

			Assert.IsFalse(link.UnderConstruction);
			Assert.IsTrue(beyond.IsConnected, "Once complete, it relays.");
		}

		[Test]
		public void BlockedBlueprints_DoNotPreventEligibleConstructionElsewhere()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure contaminated = w.Blueprint(StructureType.Stabilizer, 5, 0);   // placed first
			PlacedStructure disconnected = w.Blueprint(StructureType.Stabilizer, 60, 60); // placed second
			PlacedStructure eligible = w.Blueprint(StructureType.Stabilizer, 0, 5);       // placed last
			w.Contaminated.Add(new Vector2Int(5, 0));

			w.Tick(5);

			Assert.AreEqual(0, contaminated.ChargesDelivered);
			Assert.AreEqual(0, disconnected.ChargesDelivered);
			Assert.AreEqual(10, eligible.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.WaitingForCleanGround, contaminated.Construction);
			Assert.AreEqual(ConstructionStatus.WaitingForConnection, disconnected.Construction);
			Assert.AreEqual(ConstructionStatus.Reconstructing, eligible.Construction);
		}

		[Test]
		public void ConstructionSpam_CannotExceedGeneration()
		{
			var w = new World();
			w.Core(0, 0);
			var blueprints = new List<PlacedStructure>();
			for (int i = 0; i < 30; i++)
				blueprints.Add(w.Blueprint(StructureType.Stabilizer, 1 + i % 5, 1 + i / 5));

			for (int t = 0; t < 40; t++)
			{
				w.Tick();
				Assert.LessOrEqual(w.Diagnostics.Distributed, w.Diagnostics.Generated);
				Assert.LessOrEqual(w.Diagnostics.Distributed, 4);
			}

			int delivered = 0;
			foreach (PlacedStructure bp in blueprints) delivered += bp.ChargesDelivered;

			Assert.AreEqual(160, w.Diagnostics.TotalDistributed);
			Assert.AreEqual(160, delivered, "Charges delivered equal charges distributed: nothing is created from nowhere.");
			Assert.AreEqual(4, w.Diagnostics.TotalCompleted, "40 ticks x 4 charges / 40 per Stabilizer = 4 structures at most.");
		}

		[Test]
		public void ScarceSupply_ServesPlacementOrder()
		{
			var w = new World();
			w.Settings.coreGenerationPerTick = 2;
			w.Core(0, 0);
			PlacedStructure first = w.Blueprint(StructureType.Stabilizer, 5, 0);
			PlacedStructure second = w.Blueprint(StructureType.Stabilizer, 0, 5);

			w.Tick(3);

			Assert.AreEqual(6, first.ChargesDelivered);
			Assert.AreEqual(0, second.ChargesDelivered);
			Assert.AreEqual(ConstructionStatus.WaitingForCharges, second.Construction);
			Assert.AreEqual(1, w.Diagnostics.WaitingForCharges);
		}

		[Test]
		public void SameScenario_SameResults()
		{
			string a = RunMixedScenario();
			string b = RunMixedScenario();

			Assert.AreEqual(a, b);
		}

		private static string RunMixedScenario()
		{
			var w = new World();
			w.Core(10, 10);
			for (int i = 0; i < 12; i++)
			{
				StructureType type = i % 3 == 0 ? StructureType.Nullifier : StructureType.Stabilizer;
				w.Blueprint(type, 11 + (i * 7) % 15, 11 + (i * 5) % 15);
			}

			var log = new System.Text.StringBuilder();
			for (int t = 0; t < 120; t++)
			{
				// Contamination toggles deterministically on some cells.
				w.Contaminated.Clear();
				if (t % 20 < 10) w.Contaminated.Add(new Vector2Int(18, 16));

				w.Tick();
				log.Append(w.Diagnostics.Distributed).Append(':').Append(w.Diagnostics.CompletedThisTick).Append(';');
			}

			foreach (PlacedStructure s in w.Structures)
				log.Append(s.ChargesDelivered).Append(s.UnderConstruction ? 'u' : 'c').Append(',');

			return log.ToString();
		}
	}
}
