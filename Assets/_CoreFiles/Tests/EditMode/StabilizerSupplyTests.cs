using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Pure tests of U2 Stabilizer supply and the safeguards S1–S3, without a scene.
	// The numbers are provisional test fixtures, not balance decisions.
	public class StabilizerSupplyTests
	{
		private const float Range = 8f;

		private static SyncInfrastructureSettings Settings() => new()
		{
			enabled = true,
			stabilizerSupplyEnabled = true,
			coreGenerationPerTick = 8,
			stabilizerConstructionCost = 40,
			nullifierConstructionCost = 60,
			maxConstructionIntakePerTick = 2,
			stabilizerUpkeepPerTick = 2,
			stabilizerStoreCapacity = 20,
			maxSupplyIntakePerTick = 4,
			footholdStabilizerCount = 2,
			coreSafeZoneRadius = 3f
		};

		private sealed class World
		{
			public readonly List<PlacedStructure> Structures = new();
			public readonly HashSet<Vector2Int> Contaminated = new();
			public readonly SyncInfrastructureSettings Settings = StabilizerSupplyTests.Settings();
			public readonly SyncDiagnostics Diagnostics = new();
			private long order;

			public PlacedStructure Core(int x, int y) => Add(StructureType.Core, x, y, blueprint: false);
			public PlacedStructure Stabilizer(int x, int y) => Add(StructureType.Stabilizer, x, y, blueprint: false);
			public PlacedStructure Blueprint(StructureType type, int x, int y) => Add(type, x, y, blueprint: true);

			private PlacedStructure Add(StructureType type, int x, int y, bool blueprint)
			{
				var s = new PlacedStructure(type, new Vector2Int(x, y)) { PlacementOrder = ++order };
				if (blueprint)
					SyncInfrastructure.MakeBlueprint(s, Settings);
				Structures.Add(s);
				Refresh();
				return s;
			}

			public void Remove(PlacedStructure s)
			{
				Structures.Remove(s);
				Refresh();
			}

			public void Refresh() => InfrastructureNetwork.Recalculate(Structures, Range);

			public void Tick(int count = 1)
			{
				for (int i = 0; i < count; i++)
				{
					if (SyncInfrastructure.Step(Structures, p => !Contaminated.Contains(p), Settings, Diagnostics))
						Refresh();
				}
			}
		}

		[Test]
		public void SuppliedStabilizer_FullEffectiveness_AndStoreFillsToCapacity()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure s = w.Stabilizer(5, 0);

			w.Tick();
			Assert.AreEqual(1f, s.SupplyEffectiveness);
			Assert.AreEqual(SupplyStatus.Supplied, s.Supply);
			Assert.AreEqual(2, s.StoredCharges, "Received 4, consumed 2.");

			w.Tick(14);
			Assert.AreEqual(20, s.StoredCharges, "Store fills to capacity and stays there.");
			Assert.AreEqual(1f, s.SupplyEffectiveness);
		}

		[Test]
		public void S3_DisconnectedStabilizer_RunsOnStore_ThenStarvesToZero()
		{
			var w = new World();
			PlacedStructure core = w.Core(0, 0);
			PlacedStructure s = w.Stabilizer(5, 0);
			w.Tick(15); // store full (20)

			w.Remove(core);
			Assert.IsFalse(s.IsConnected);

			w.Tick(10); // 20 stored / 2 upkeep = 10 ticks
			Assert.AreEqual(1f, s.SupplyEffectiveness, "A brief disconnection does not drop it to zero at once.");
			Assert.AreEqual(SupplyStatus.OnStore, s.Supply);
			Assert.AreEqual(0, s.StoredCharges);

			w.Tick();
			Assert.AreEqual(0f, s.SupplyEffectiveness, "Complete starvation: zero once the store is empty.");
			Assert.AreEqual(SupplyStatus.Starved, s.Supply);
		}

		[Test]
		public void PartialSupply_ReducesEffectiveness()
		{
			var w = new World();
			w.Settings.footholdStabilizerCount = 0; // no S2 generation floor in this test
			w.Settings.coreGenerationPerTick = 1;
			w.Core(0, 0);
			PlacedStructure s = w.Stabilizer(5, 0);

			w.Tick(5);

			Assert.AreEqual(0.5f, s.SupplyEffectiveness, 1e-6f, "1 of 2 upkeep met.");
			Assert.AreEqual(SupplyStatus.Reduced, s.Supply);
			Assert.AreEqual(0, s.StoredCharges);
		}

		[Test]
		public void NoSupplyAtAll_IsZeroImmediately_WhenStoreIsEmpty()
		{
			var w = new World();
			w.Settings.footholdStabilizerCount = 0;
			w.Settings.coreGenerationPerTick = 0;
			w.Core(0, 0);
			PlacedStructure s = w.Stabilizer(5, 0);

			w.Tick();

			Assert.AreEqual(0f, s.SupplyEffectiveness);
			Assert.AreEqual(SupplyStatus.Starved, s.Supply);
		}

		[Test]
		public void OverExpansion_LaterStabilizersStarve_EarliestStaySupplied()
		{
			var w = new World(); // 8 per tick feeds four Stabilizers at upkeep 2
			w.Core(0, 0);
			var s = new List<PlacedStructure>();
			for (int i = 0; i < 6; i++)
				s.Add(w.Stabilizer(1 + i, 1));

			for (int t = 0; t < 100; t++)
			{
				w.Tick();
				Assert.LessOrEqual(w.Diagnostics.Distributed, w.Diagnostics.Generated);
			}

			for (int i = 0; i < 4; i++)
				Assert.AreEqual(1f, s[i].SupplyEffectiveness, $"Stabilizer {i + 1} (placed early) is supplied.");
			Assert.AreEqual(0f, s[4].SupplyEffectiveness, "Stabilizer 5 starves: the network outgrew its generation.");
			Assert.AreEqual(0f, s[5].SupplyEffectiveness, "Stabilizer 6 starves.");
			Assert.AreEqual(2, w.Diagnostics.StabilizersStarved);
		}

		[Test]
		public void S2_FootholdIsServedFirst_EvenAgainstConstructionSpam()
		{
			var w = new World();
			w.Settings.coreGenerationPerTick = 6;
			w.Core(0, 0);
			// Increment 2A (F4): the foothold is the Stabilizers nearest the Core, so the two foothold
			// Stabilizers stand nearer the Core than any blueprint that completes during the test.
			PlacedStructure f1 = w.Stabilizer(1, 0);
			PlacedStructure f2 = w.Stabilizer(0, 1);
			for (int i = 0; i < 12; i++)
				w.Blueprint(StructureType.Stabilizer, 2 + i % 4, 2 + i / 4);

			for (int t = 0; t < 80; t++)
			{
				w.Tick();
				Assert.AreEqual(1f, f1.SupplyEffectiveness, $"tick {t}: foothold 1 supplied");
				Assert.AreEqual(1f, f2.SupplyEffectiveness, $"tick {t}: foothold 2 supplied");
			}
		}

		[Test]
		public void S2_GenerationNeverBelowFootholdUpkeep()
		{
			var w = new World();
			w.Settings.coreGenerationPerTick = 1; // configured too low
			w.Core(0, 0);

			w.Tick();

			Assert.AreEqual(6, w.Diagnostics.Generated,
				"2 foothold Stabilizers x upkeep 2 = 4, plus the 25 % construction share on top (increment 2A): 6 - ceil(1.5) = 4.");

			var noShare = new World();
			noShare.Settings.coreGenerationPerTick = 1;
			noShare.Settings.constructionMinimumShare = 0f;
			noShare.Core(0, 0);
			noShare.Tick();
			Assert.AreEqual(4, noShare.Diagnostics.Generated, "Without a construction share the floor is the foothold upkeep alone.");
		}

		// ---------------------------------------------------------------
		// Increment 2A: F3 (construction share) and F4 (foothold by distance)
		// ---------------------------------------------------------------

		[Test]
		public void F3_ConstructionReserve_RoundsUp_AndIsZeroWhenOff()
		{
			var s = Settings();
			Assert.AreEqual(2, s.ConstructionReserve(8));
			Assert.AreEqual(2, s.ConstructionReserve(6));
			Assert.AreEqual(1, s.ConstructionReserve(4));
			Assert.AreEqual(1, s.ConstructionReserve(1), "Any positive share reserves at least one charge.");
			Assert.AreEqual(0, s.ConstructionReserve(0));
			s.constructionMinimumShare = 0f;
			Assert.AreEqual(0, s.ConstructionReserve(8));
		}

		[Test]
		public void F3_ShippedDefaults_AreGeneration8_Share25()
		{
			var d = new SyncInfrastructureSettings();
			Assert.AreEqual(8, d.coreGenerationPerTick);
			Assert.AreEqual(0.25f, d.constructionMinimumShare);
			Assert.IsFalse(d.enabled, "Still opt-in.");
			Assert.IsFalse(d.stabilizerSupplyEnabled, "Still opt-in.");
		}

		[Test]
		public void F3_ConstructionNeverFullyStarves_WhenTheFootholdTakesTheConfiguredGeneration()
		{
			// The increment 2 finding: generation 4, two foothold Stabilizers at upkeep 2 = 4 reserved.
			var w = new World();
			w.Settings.coreGenerationPerTick = 4;
			w.Core(0, 0);
			PlacedStructure f1 = w.Stabilizer(1, 0);
			PlacedStructure f2 = w.Stabilizer(0, 1);
			PlacedStructure blueprint = w.Blueprint(StructureType.Stabilizer, 4, 4);

			for (int t = 0; t < 10; t++)
			{
				w.Tick();
				Assert.Greater(w.Diagnostics.ConstructionDelivered, 0, $"tick {t}: construction receives charges");
				Assert.AreEqual(1f, f1.SupplyEffectiveness, $"tick {t}: foothold 1 still supplied");
				Assert.AreEqual(1f, f2.SupplyEffectiveness, $"tick {t}: foothold 2 still supplied");
			}

			Assert.AreEqual(6, w.Diagnostics.Generated, "Floor raised so foothold upkeep and the share both fit.");
			Assert.AreEqual(4, w.Diagnostics.FootholdReserved);
			Assert.AreEqual(2, w.Diagnostics.ConstructionReserved);

			int guard = 0;
			while (blueprint.UnderConstruction && guard++ < 100) w.Tick();
			Assert.IsFalse(blueprint.UnderConstruction, "The blueprint completes.");
		}

		[Test]
		public void F3_ConstructionShare_IsUsedByOtherConsumers_WhenNoBlueprintIsEligible()
		{
			var w = new World(); // 8 per tick
			w.Core(0, 0);
			for (int i = 0; i < 4; i++)
				w.Stabilizer(1 + i, 1);
			w.Blueprint(StructureType.Stabilizer, 20, 20); // out of range: not eligible, requests nothing

			w.Tick();

			Assert.AreEqual(0, w.Diagnostics.ConstructionReserved);
			Assert.AreEqual(8, w.Diagnostics.SupplyDelivered, "Nothing is held back for construction that cannot use it.");
			Assert.AreEqual(0, w.Diagnostics.Unused);
		}

		[Test]
		public void F3_ConstructionShare_ComesAfterTheFoothold_ThenRoundRobinForTheRest()
		{
			var w = new World(); // 8 per tick, upkeep 2, intake 4, construction intake 2
			w.Core(0, 0);
			w.Stabilizer(1, 0);
			w.Stabilizer(0, 1);
			w.Stabilizer(5, 5);
			w.Blueprint(StructureType.Stabilizer, 2, 2);
			w.Blueprint(StructureType.Stabilizer, 3, 2);

			w.Tick();

			Assert.AreEqual(4, w.Diagnostics.FootholdReserved, "Foothold upkeep first.");
			Assert.AreEqual(2, w.Diagnostics.ConstructionReserved, "Then 25 % of 8 for construction.");
			Assert.AreEqual(8, w.Diagnostics.Distributed);
			Assert.GreaterOrEqual(w.Diagnostics.ConstructionDelivered, 2);
		}

		[Test]
		public void F4_Foothold_IsTheConnectedStabilizersNearestTheCore_NotPlacementOrder()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure far = w.Stabilizer(7, 0);   // placed first
			PlacedStructure mid = w.Stabilizer(0, 5);
			PlacedStructure near = w.Stabilizer(2, 0);  // placed last

			List<PlacedStructure> foothold = SyncInfrastructure.FindFoothold(w.Structures, w.Settings);

			CollectionAssert.AreEqual(new[] { near, mid }, foothold, "Nearest first.");
			Assert.IsFalse(foothold.Contains(far));
		}

		[Test]
		public void F4_DisconnectedStabilizers_NeverHoldAFootholdSlot()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure stranded = w.Stabilizer(30, 0); // out of range, placed first
			PlacedStructure a = w.Stabilizer(6, 0);
			PlacedStructure b = w.Stabilizer(0, 6);
			Assert.IsFalse(stranded.IsConnected);

			List<PlacedStructure> foothold = SyncInfrastructure.FindFoothold(w.Structures, w.Settings);

			CollectionAssert.AreEquivalent(new[] { a, b }, foothold);
		}

		[Test]
		public void F4_EqualDistance_FallsBackToPlacementOrder()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure first = w.Stabilizer(3, 0);
			PlacedStructure second = w.Stabilizer(0, 3);
			PlacedStructure third = w.Stabilizer(-3, 0);

			CollectionAssert.AreEqual(new[] { first, second }, SyncInfrastructure.FindFoothold(w.Structures, w.Settings));
			Assert.IsFalse(SyncInfrastructure.FindFoothold(w.Structures, w.Settings).Contains(third));
		}

		[Test]
		public void F4_AfterReconnection_TheNewLinkNearTheCoreIsFedFirst_NotTheOldStrandedLine()
		{
			// The increment 2 finding F4: old, distant Stabilizers took the foothold when the line reconnected.
			var w = new World();
			w.Settings.coreGenerationPerTick = 4; // scarce: floor 6 = foothold 4 + share 2, nothing left over
			w.Core(0, 0);
			PlacedStructure gapLink = w.Stabilizer(7, 0);
			PlacedStructure s2 = w.Stabilizer(14, 0);
			PlacedStructure s3 = w.Stabilizer(21, 0);
			w.Remove(gapLink); // line severed: s2 and s3 stranded
			w.Tick(30);        // their stores run dry

			PlacedStructure rebuilt = w.Stabilizer(7, 0); // the player rebuilds the gap link
			Assert.IsTrue(s2.IsConnected && s3.IsConnected);

			w.Tick(5);

			List<PlacedStructure> foothold = SyncInfrastructure.FindFoothold(w.Structures, w.Settings);
			CollectionAssert.AreEqual(new[] { rebuilt, s2 }, foothold);
			Assert.AreEqual(1f, rebuilt.SupplyEffectiveness, "The link nearest the Core is fully supplied.");
		}

		[Test]
		public void S1_BlueprintNearSyncCore_BuildsOnContaminatedGround_ButNotOutsideTheSafeZone()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure inside = w.Blueprint(StructureType.Stabilizer, 2, 0);
			PlacedStructure outside = w.Blueprint(StructureType.Stabilizer, 0, 5);
			w.Contaminated.Add(new Vector2Int(2, 0));
			w.Contaminated.Add(new Vector2Int(0, 5));

			w.Tick(20);

			Assert.IsFalse(inside.UnderConstruction, "Within 3 cells of the Sync Core: always buildable.");
			Assert.AreEqual(ConstructionStatus.WaitingForCleanGround, outside.Construction);
			Assert.AreEqual(0, outside.ChargesDelivered);
		}

		[Test]
		public void SupplyOff_StabilizersNeedNothing()
		{
			var w = new World();
			w.Settings.stabilizerSupplyEnabled = false;
			w.Core(0, 0);
			PlacedStructure s = w.Stabilizer(5, 0);

			w.Tick(10);

			Assert.AreEqual(1f, s.SupplyEffectiveness);
			Assert.AreEqual(SupplyStatus.NotApplicable, s.Supply);
			Assert.AreEqual(0, s.StoredCharges);
			Assert.AreEqual(0, w.Diagnostics.SupplyDemand);
			Assert.AreEqual(8, w.Diagnostics.Generated, "Without U2 the configured generation is used as is.");
		}

		[Test]
		public void SeveredConnection_DownstreamStarves_AndRecoversWhenTheGapIsRebuilt()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure s1 = w.Stabilizer(7, 0);
			PlacedStructure s2 = w.Stabilizer(14, 0);
			PlacedStructure s3 = w.Stabilizer(21, 0);
			w.Tick(30);
			Assert.IsTrue(s3.IsConnected);
			Assert.AreEqual(1f, s3.SupplyEffectiveness);

			// The player severs the critical connection.
			w.Remove(s1);
			Assert.IsFalse(s2.IsConnected);
			Assert.IsFalse(s3.IsConnected);

			int before = s3.StoredCharges;
			w.Tick(3);
			Assert.Less(s3.StoredCharges, before, "Downstream stores deplete.");
			w.Tick(20);
			Assert.AreEqual(0f, s2.SupplyEffectiveness);
			Assert.AreEqual(0f, s3.SupplyEffectiveness);

			// A legal recovery action exists: a blueprint in the gap is connected and requests charges.
			PlacedStructure gap = w.Blueprint(StructureType.Stabilizer, 7, 0);
			w.Tick();
			Assert.AreEqual(ConstructionStatus.Reconstructing, gap.Construction);

			int guard = 0;
			while (gap.UnderConstruction && guard++ < 200) w.Tick();
			Assert.IsFalse(gap.UnderConstruction);
			Assert.IsTrue(s2.IsConnected && s3.IsConnected, "The rebuilt link relays again.");

			w.Tick(10);
			Assert.AreEqual(1f, s2.SupplyEffectiveness, "Supply resumes.");
			Assert.AreEqual(1f, s3.SupplyEffectiveness, "Supply resumes.");
		}

		[Test]
		public void SameScenario_SameResults()
		{
			Assert.AreEqual(RunMixed(), RunMixed());
		}

		private static string RunMixed()
		{
			var w = new World();
			PlacedStructure core = w.Core(10, 10);
			for (int i = 0; i < 9; i++)
				w.Stabilizer(11 + (i * 7) % 15, 11 + (i * 5) % 15);
			for (int i = 0; i < 4; i++)
				w.Blueprint(StructureType.Stabilizer, 9 - i, 12 + i);

			var log = new System.Text.StringBuilder();
			for (int t = 0; t < 150; t++)
			{
				if (t == 60) w.Remove(w.Structures[3]);
				w.Tick();
				log.Append(w.Diagnostics.Distributed).Append('/').Append(w.Diagnostics.StabilizersStarved).Append(';');
			}

			foreach (PlacedStructure s in w.Structures)
				log.Append(s.StoredCharges).Append(':').Append(s.SupplyEffectiveness).Append(',');

			return log.ToString();
		}
	}
}
