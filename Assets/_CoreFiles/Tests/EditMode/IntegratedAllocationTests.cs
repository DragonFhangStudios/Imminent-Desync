using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Integration of increment 2A into increment 3 (Director, 2026-11-26): compatibility of the three charge
	// consumers in one tick: construction (with the 2A construction share), U2 Stabilizer upkeep (with the 2A
	// foothold by distance) and N1-A Nullifier preparation. Pure, at the shipped provisional defaults
	// (generation 8, share 25 %, upkeep 2, store 20, intake 4, Nullifier pulse 30 at 2 per tick), all opt-ins on.
	public class IntegratedAllocationTests
	{
		private const float ConnectionRange = 8f;
		private const float NullifierRange = 6f;

		private sealed class World
		{
			public readonly List<PlacedStructure> Structures = new();
			public readonly List<Emitter> Emitters = new();
			public readonly SyncDiagnostics Diagnostics = new();
			public readonly SyncInfrastructureSettings Settings = new()
			{
				enabled = true,
				stabilizerSupplyEnabled = true,
				nullifierReconfigurationEnabled = true
			};

			private long order;

			public World() => Emitters.Add(new Emitter(new Vector2Int(0, 10), 500f));

			public PlacedStructure Core(int x, int y) => Add(StructureType.Core, x, y, false);
			public PlacedStructure Stabilizer(int x, int y) => Add(StructureType.Stabilizer, x, y, false);
			public PlacedStructure Nullifier(int x, int y) => Add(StructureType.Nullifier, x, y, false);
			public PlacedStructure Blueprint(StructureType type, int x, int y) => Add(type, x, y, true);

			private PlacedStructure Add(StructureType type, int x, int y, bool blueprint)
			{
				var s = new PlacedStructure(type, new Vector2Int(x, y)) { PlacementOrder = ++order };
				if (blueprint) SyncInfrastructure.MakeBlueprint(s, Settings);
				Structures.Add(s);
				Refresh();
				return s;
			}

			public void Refresh() => InfrastructureNetwork.Recalculate(Structures, ConnectionRange);

			public Emitter Target(Vector2Int p)
			{
				foreach (Emitter e in Emitters)
					if (!e.IsNullified && (e.Position - p).sqrMagnitude <= NullifierRange * NullifierRange)
						return e;
				return null;
			}

			// The order GridManager runs: Sync Charges, then the Nullifier step.
			public void Tick()
			{
				if (SyncInfrastructure.Step(Structures, _ => true, Settings, Diagnostics, s => Target(s.Position) != null))
					Refresh();
				NullifierReconfiguration.Step(Structures, Emitters, Target, Settings, Diagnostics);
			}
		}

		private static void AssertConservation(SyncDiagnostics d, int tick)
		{
			Assert.AreEqual(d.ConstructionDelivered + d.SupplyDelivered + d.NullifierDelivered, d.Distributed, $"tick {tick}: every charge is accounted for");
			Assert.LessOrEqual(d.Distributed, d.Generated, $"tick {tick}: never more than generated");
			Assert.GreaterOrEqual(d.Unused, 0, $"tick {tick}");
			Assert.LessOrEqual(d.ConstructionDelivered, d.ConstructionDemand, $"tick {tick}");
			Assert.LessOrEqual(d.SupplyDelivered, d.SupplyDemand, $"tick {tick}");
			Assert.LessOrEqual(d.NullifierDelivered, d.NullifierDemand, $"tick {tick}");
		}

		[Test]
		public void AllThreeConsumers_FootholdFirst_ThenConstructionShare_ThenRoundRobin()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure f1 = w.Stabilizer(1, 0);
			PlacedStructure f2 = w.Stabilizer(0, 1);
			w.Nullifier(0, 5);
			w.Blueprint(StructureType.Stabilizer, 4, 0);

			w.Tick();
			SyncDiagnostics d = w.Diagnostics;

			Assert.AreEqual(8, d.Generated);
			Assert.AreEqual(4, d.FootholdReserved, "1. foothold upkeep (2 x 2)");
			Assert.AreEqual(2, d.ConstructionReserved, "2. construction share (25 % of 8)");
			Assert.AreEqual(2, d.ConstructionDelivered, "the blueprint's intake is already met by the share");
			Assert.AreEqual(5, d.SupplyDelivered, "3. round-robin of the last 2: one to supply refill ...");
			Assert.AreEqual(1, d.NullifierDelivered, "... and one to Nullifier preparation");
			Assert.AreEqual(8, d.Distributed);
			Assert.AreEqual(1f, f1.SupplyEffectiveness);
			Assert.AreEqual(1f, f2.SupplyEffectiveness);
			AssertConservation(d, 1);
		}

		[Test]
		public void NullifierPreparation_NeverTakesTheFootholdUpkeep_OrTheConstructionShare()
		{
			var w = new World();
			w.Core(0, 0);
			PlacedStructure f1 = w.Stabilizer(1, 0);
			PlacedStructure f2 = w.Stabilizer(0, 1);
			foreach (var p in new[] { new Vector2Int(0, 5), new Vector2Int(1, 5), new Vector2Int(-1, 5), new Vector2Int(0, 6) })
				w.Nullifier(p.x, p.y);
			var blueprints = new List<PlacedStructure>
			{
				w.Blueprint(StructureType.Stabilizer, 3, 0),
				w.Blueprint(StructureType.Stabilizer, 4, 0),
				w.Blueprint(StructureType.Nullifier, 2, 5)
			};

			for (int t = 1; t <= 200; t++)
			{
				w.Tick();
				SyncDiagnostics d = w.Diagnostics;
				AssertConservation(d, t);
				Assert.AreEqual(1f, f1.SupplyEffectiveness, $"tick {t}: foothold 1 at full strength");
				Assert.AreEqual(1f, f2.SupplyEffectiveness, $"tick {t}: foothold 2 at full strength");
				if (d.ConstructionDemand > 0)
					Assert.GreaterOrEqual(d.ConstructionDelivered, Mathf.Min(2, d.ConstructionDemand), $"tick {t}: construction share honored");
			}

			Assert.IsTrue(blueprints.TrueForAll(b => !b.UnderConstruction), "All blueprints complete despite four preparing Nullifiers.");
		}

		[Test]
		public void NullifierBecomesReady_In15Ticks_OnceOnlyTheFootholdCompetes()
		{
			var w = new World();
			w.Core(0, 0);
			w.Stabilizer(1, 0);
			w.Stabilizer(0, 1);
			PlacedStructure n = w.Nullifier(0, 5);

			int ready = -1;
			for (int t = 1; t <= 100 && ready < 0; t++)
			{
				w.Tick();
				AssertConservation(w.Diagnostics, t);
				if (n.NullifierState == NullifierStatus.Ready) ready = t;
			}

			Assert.AreEqual(15, ready, "8 per tick: foothold 4, the unused share returns, then supply and the Nullifier alternate: 2 per tick into a 30 store.");
		}

		// Finding F7 (recorded, not changed; no feature work authorized): NullifierOperation is last in the
		// allocator's category order. When only two charges remain after the reservations and both construction
		// and Stabilizer refill still want charges, the Nullifier gets none that tick. This shows it is a delay, not
		// a permanent starvation, once construction finishes: the Nullifier still becomes Ready.
		[Test]
		public void F7_ConstructionAndSupplyContention_DelaysNullifierPreparation_ButItStillBecomesReady()
		{
			var w = new World();
			w.Core(0, 0);
			w.Stabilizer(1, 0);
			w.Stabilizer(0, 1);
			w.Stabilizer(2, 2);  // a third Stabilizer: supply demand beyond the foothold
			w.Blueprint(StructureType.Stabilizer, 3, 0);
			w.Blueprint(StructureType.Stabilizer, 4, 0);
			PlacedStructure n = w.Nullifier(0, 5);

			int firstCharge = -1, ready = -1, starvedTicks = 0;
			for (int t = 1; t <= 600 && ready < 0; t++)
			{
				w.Tick();
				AssertConservation(w.Diagnostics, t);
				if (w.Diagnostics.NullifierDemand > 0 && w.Diagnostics.NullifierDelivered == 0) starvedTicks++;
				if (firstCharge < 0 && w.Diagnostics.NullifierDelivered > 0) firstCharge = t;
				if (n.NullifierState == NullifierStatus.Ready) ready = t;
			}

			TestContext.WriteLine($"F7: Nullifier first charge at tick {firstCharge}, Ready at tick {ready}, {starvedTicks} ticks with demand but no charge (uncontended: Ready at 15).");
			Assert.Greater(ready, 0, "Not starved forever: Ready once construction finishes.");
		}

		[Test]
		public void MixedRun_ConservesCharges_AndIsDeterministic()
		{
			Assert.AreEqual(Mixed(), Mixed());
		}

		private static string Mixed()
		{
			var w = new World();
			w.Core(0, 0);
			for (int i = 0; i < 5; i++) w.Stabilizer(1 + i, 1 + (i % 2));
			w.Nullifier(0, 5);
			w.Nullifier(1, 6);
			var log = new StringBuilder();

			for (int t = 1; t <= 300; t++)
			{
				if (t % 40 == 0) w.Blueprint(StructureType.Stabilizer, -1 - t / 40, 0);
				if (t == 60) NullifierReconfiguration.TryInitiate(w.Structures.Find(s => s.Type == StructureType.Nullifier), w.Target, w.Settings);
				w.Tick();
				AssertConservation(w.Diagnostics, t);
				SyncDiagnostics d = w.Diagnostics;
				log.Append(d.ConstructionDelivered).Append('/').Append(d.SupplyDelivered).Append('/').Append(d.NullifierDelivered).Append(';');
			}

			log.Append('|').Append(w.Emitters[0].CompletedStages).Append('|').Append(w.Emitters[0].StageProgress);
			return log.ToString();
		}
	}
}
