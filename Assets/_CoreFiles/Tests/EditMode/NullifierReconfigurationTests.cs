using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Pure tests of Nullifier reconfiguration N1–N3 with N1-A (increment 3), without a scene.
	// The numbers are the shipped PROVISIONAL defaults (decision 2 is open) unless a test says otherwise;
	// they are fixtures, not balance decisions.
	public class NullifierReconfigurationTests
	{
		private const float ConnectionRange = 8f;
		private const float NullifierRange = 6f; // Sandbox nullifierRange
		private const float MaxIntegrity = 500f;
		private const int PulseCost = 30;        // shipped default
		private const int StageTicks = 150;      // shipped default
		private const int FillTicks = 15;        // 30-charge store at 2 per tick

		private sealed class World
		{
			public readonly List<PlacedStructure> Structures = new();
			public readonly List<Emitter> Emitters = new();
			public readonly SyncDiagnostics Diagnostics = new();
			public readonly SyncInfrastructureSettings Settings = new()
			{
				enabled = true,
				nullifierReconfigurationEnabled = true,
				coreGenerationPerTick = 8
			};

			private long order;

			public PlacedStructure Core(int x, int y) => Add(StructureType.Core, x, y, blueprint: false);
			public PlacedStructure Nullifier(int x, int y) => Add(StructureType.Nullifier, x, y, blueprint: false);
			public PlacedStructure Blueprint(StructureType type, int x, int y) => Add(type, x, y, blueprint: true);

			public Emitter Emitter(int x, int y)
			{
				var e = new Emitter(new Vector2Int(x, y), MaxIntegrity);
				Emitters.Add(e);
				return e;
			}

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

			public void Refresh() => InfrastructureNetwork.Recalculate(Structures, ConnectionRange);

			// Same nearest-active-Emitter rule as GridManager.FindNullifierTarget.
			public Emitter Target(Vector2Int position)
			{
				Emitter best = null;
				float bestSq = NullifierRange * NullifierRange;
				foreach (var e in Emitters)
				{
					if (e.IsNullified) continue;
					float d = (e.Position - position).sqrMagnitude;
					if (d <= bestSq) { bestSq = d; best = e; }
				}
				return best;
			}

			// The order GridManager runs: Sync Charges (UpdateSyncInfrastructure), then UpdateNullifiers.
			public void Tick(int count = 1)
			{
				for (int i = 0; i < count; i++)
				{
					if (SyncInfrastructure.Step(Structures, p => true, Settings, Diagnostics, s => Target(s.Position) != null))
						Refresh();
					NullifierReconfiguration.Step(Structures, Emitters, Target, Settings, Diagnostics);
				}
			}

			public bool Initiate(PlacedStructure nullifier) => NullifierReconfiguration.TryInitiate(nullifier, Target, Settings);
		}

		// Core (0,0) -> Nullifier (0,5), 5 cells from the Emitter at (0,10).
		private static (World w, PlacedStructure n, Emitter e) Basic()
		{
			var w = new World();
			Emitter e = w.Emitter(0, 10);
			w.Core(0, 0);
			PlacedStructure n = w.Nullifier(0, 5);
			return (w, n, e);
		}

		// Ticks after initiation until the Emitter is reconfigured (or -1).
		private static int TicksToReconfigure(World w, Emitter e, int max = 5000)
		{
			for (int t = 1; t <= max; t++)
			{
				w.Tick();
				if (e.IsReconfigured) return t;
			}
			return -1;
		}

		[Test]
		public void Defaults_AreOptIn_AndProvisional()
		{
			var s = new SyncInfrastructureSettings();
			Assert.IsFalse(s.nullifierReconfigurationEnabled, "off by default");
			Assert.IsFalse(s.ReconfigurationActive);
			s.nullifierReconfigurationEnabled = true;
			Assert.IsFalse(s.ReconfigurationActive, "needs Sync Infrastructure enabled too");
			Assert.AreEqual(30, s.nullifierPulseCost);
			Assert.AreEqual(2, s.maxNullifierIntakePerTick);
			Assert.AreEqual(150, s.reconfigurationStageTicks);
		}

		[Test]
		public void ReconfigurationOff_NullifierRequestsNothing_AndStatusNotApplicable()
		{
			var (w, n, e) = Basic();
			w.Settings.nullifierReconfigurationEnabled = false;
			for (int i = 0; i < 50; i++)
				SyncInfrastructure.Step(w.Structures, p => true, w.Settings, w.Diagnostics, s => w.Target(s.Position) != null);
			Assert.AreEqual(0, n.StoredCharges);
			Assert.AreEqual(0, w.Diagnostics.NullifierDemand);
			Assert.AreEqual(NullifierStatus.NotApplicable, NullifierReconfiguration.EvaluateStatus(n, w.Target, w.Settings));
			Assert.IsFalse(w.Initiate(n));
			Assert.IsFalse(e.ReconfigurationStarted);
		}

		[Test]
		public void Preparation_FillsStoreWithSyncCharges_ThenReady()
		{
			var (w, n, _) = Basic();
			w.Tick(FillTicks - 1);
			Assert.AreEqual(NullifierStatus.Preparing, n.NullifierState);
			Assert.AreEqual(PulseCost - 2, n.StoredCharges);
			w.Tick();
			Assert.AreEqual(NullifierStatus.Ready, n.NullifierState);
			Assert.AreEqual(PulseCost, n.StoredCharges);
			w.Tick(10);
			Assert.AreEqual(PulseCost, n.StoredCharges, "a full store requests nothing more");
			Assert.AreEqual(0, w.Diagnostics.NullifierDemand);
		}

		[Test]
		public void DisconnectedNullifier_NeverPrepares()
		{
			var w = new World();
			w.Emitter(0, 14);
			w.Core(0, 0);
			PlacedStructure n = w.Nullifier(0, 9); // beyond the 8-cell connection range
			w.Tick(100);
			Assert.IsFalse(n.IsConnected);
			Assert.AreEqual(NullifierStatus.Disconnected, n.NullifierState);
			Assert.AreEqual(0, n.StoredCharges);
			Assert.IsFalse(w.Initiate(n));
		}

		[Test]
		public void NullifierWithoutEmitterInRange_NeverPrepares()
		{
			var w = new World();
			w.Emitter(0, 12); // 7 cells away
			w.Core(0, 0);
			PlacedStructure n = w.Nullifier(0, 5);
			w.Tick(100);
			Assert.IsTrue(n.IsConnected);
			Assert.AreEqual(NullifierStatus.NoTarget, n.NullifierState);
			Assert.AreEqual(0, n.StoredCharges);
		}

		[Test]
		public void BlueprintNullifier_IsBuiltWithCharges_ThenPrepares()
		{
			var w = new World();
			w.Emitter(0, 10);
			w.Core(0, 0);
			PlacedStructure n = w.Blueprint(StructureType.Nullifier, 0, 5);
			w.Tick(29);
			Assert.IsTrue(n.UnderConstruction);
			Assert.AreEqual(NullifierStatus.NotApplicable, n.NullifierState);
			w.Tick(); // 60 charges at 2 per tick
			Assert.IsFalse(n.UnderConstruction);
			w.Tick(FillTicks);
			Assert.AreEqual(NullifierStatus.Ready, n.NullifierState);
		}

		[Test]
		public void WithoutManualInitiation_ReadyNullifierNeverStartsTheSequence()
		{
			var (w, n, e) = Basic();
			w.Tick(1000);
			Assert.AreEqual(NullifierStatus.Ready, n.NullifierState);
			Assert.IsFalse(e.ReconfigurationStarted);
			Assert.AreEqual(0, e.CompletedStages);
			Assert.AreEqual(0, e.StageProgress);
			Assert.AreEqual(MaxIntegrity, e.Integrity);
		}

		[Test]
		public void Initiation_OnlyFromReady_AndOncePerSequence()
		{
			var (w, n, e) = Basic();
			w.Tick(5);
			Assert.AreEqual(NullifierStatus.Preparing, n.NullifierState);
			Assert.IsFalse(w.Initiate(n), "not Ready yet");
			Assert.IsFalse(e.ReconfigurationStarted);
			w.Tick(FillTicks);
			Assert.IsTrue(w.Initiate(n));
			Assert.IsTrue(e.ReconfigurationStarted);
			Assert.AreEqual(NullifierStatus.Engaged, n.NullifierState);
			Assert.IsFalse(w.Initiate(n), "already engaged");
		}

		[Test]
		public void Stages_CompleteOnSchedule_PulsesUseCharges_AndFinalStageNullifiesTheEmitter()
		{
			var (w, n, e) = Basic();
			w.Tick(FillTicks);
			Assert.IsTrue(w.Initiate(n));

			w.Tick(StageTicks - 1);
			Assert.AreEqual(0, e.CompletedStages);
			Assert.AreEqual(StageTicks - 1, e.StageProgress);
			w.Tick();
			Assert.AreEqual(1, e.CompletedStages, "Synchronize completes at exactly its duration");
			Assert.AreEqual(0, n.StoredCharges, "the pulse used the full store");
			Assert.AreEqual(MaxIntegrity * 2f / 3f, e.Integrity, 1e-3f);
			Assert.AreEqual("Disrupt", NullifierReconfiguration.CurrentStageName(e.CompletedStages));
			Assert.IsFalse(e.IsNullified);

			w.Tick(StageTicks);
			Assert.AreEqual(2, e.CompletedStages);
			Assert.AreEqual(MaxIntegrity / 3f, e.Integrity, 1e-3f);

			w.Tick(StageTicks);
			Assert.IsTrue(e.IsReconfigured);
			Assert.IsTrue(e.IsNullified, "the drain's existing end state");
			Assert.AreEqual(0f, e.Integrity);
			Assert.AreEqual(3, w.Diagnostics.TotalPulses);
			Assert.AreEqual(0, e.Interruptions);

			w.Tick(50);
			Assert.AreEqual(3, e.CompletedStages, "nothing runs after reconfiguration");
			Assert.AreEqual(NullifierStatus.NoTarget, n.NullifierState);
		}

		[Test]
		public void Interruption_ResetsOnlyTheCurrentStage_CompletedStagesAreKept()
		{
			var (w, n, e) = Basic();
			PlacedStructure core = w.Structures.Find(s => s.Type == StructureType.Core);
			w.Tick(FillTicks);
			Assert.IsTrue(w.Initiate(n));
			w.Tick(StageTicks + 100); // stage 1 done, 100 ticks into stage 2
			Assert.AreEqual(1, e.CompletedStages);
			Assert.AreEqual(100, e.StageProgress);

			w.Remove(core); // the network goes down
			w.Tick();
			Assert.AreEqual(1, e.CompletedStages, "completed stage kept");
			Assert.AreEqual(0, e.StageProgress, "current stage restarts");
			Assert.AreEqual(1, e.Interruptions);
			Assert.AreEqual(NullifierStatus.Disconnected, n.NullifierState);
			w.Tick(200);
			Assert.AreEqual(1, e.CompletedStages);

			w.Core(0, 0); // reconnect: no re-initiation needed
			w.Tick(StageTicks - 1);
			Assert.AreEqual(1, e.CompletedStages, "the interrupted stage needs its full duration again");
			w.Tick();
			Assert.AreEqual(2, e.CompletedStages);
			Assert.AreEqual(StageTicks, TicksToReconfigure(w, e));
		}

		[Test]
		public void AdditionalNullifiers_DoNotAccelerateReconfiguration()
		{
			int Run(int count)
			{
				var w = new World();
				Emitter e = w.Emitter(0, 10);
				w.Core(0, 0);
				var all = new List<PlacedStructure>();
				Vector2Int[] spots = { new(0, 5), new(1, 5), new(-1, 5), new(0, 6) };
				for (int i = 0; i < count; i++)
					all.Add(w.Nullifier(spots[i].x, spots[i].y));
				w.Tick(FillTicks);
				foreach (var n in all)
					Assert.AreEqual(NullifierStatus.Ready, n.NullifierState);
				Assert.IsTrue(w.Initiate(all[0]));
				foreach (var n in all)
					Assert.AreEqual(NullifierStatus.Engaged, NullifierReconfiguration.EvaluateStatus(n, w.Target, w.Settings));
				return TicksToReconfigure(w, e);
			}

			int one = Run(1);
			int four = Run(4);
			Assert.AreEqual(3 * StageTicks, one);
			Assert.AreEqual(one, four, "same completion tick with one or four Nullifiers");
		}

		[Test]
		public void RemovingTheInitiatingNullifier_KeepsCompletedStages_AnotherContinuesWithoutReinitiation()
		{
			var w = new World();
			Emitter e = w.Emitter(0, 10);
			w.Core(0, 0);
			PlacedStructure first = w.Nullifier(0, 5);
			PlacedStructure second = w.Nullifier(1, 5);
			w.Tick(FillTicks);
			Assert.IsTrue(w.Initiate(first));
			w.Tick(StageTicks + 20);
			Assert.AreEqual(1, e.CompletedStages);

			w.Remove(first);
			w.Tick();
			Assert.AreEqual(1, e.CompletedStages);
			Assert.AreEqual(21, e.StageProgress, "the other Nullifier keeps the stage going (no interruption)");
			Assert.AreEqual(0, e.Interruptions);
			Assert.AreEqual(NullifierStatus.Engaged, second.NullifierState);
			Assert.Greater(TicksToReconfigure(w, e), 0);
			Assert.IsTrue(e.IsNullified);
		}

		[Test]
		public void LastNullifierRemoved_ThenReplaced_ContinuesFromTheKeptStages()
		{
			var (w, n, e) = Basic();
			w.Tick(FillTicks);
			Assert.IsTrue(w.Initiate(n));
			w.Tick(2 * StageTicks + 40);
			Assert.AreEqual(2, e.CompletedStages);

			w.Remove(n);
			w.Tick(30);
			Assert.AreEqual(2, e.CompletedStages);
			Assert.AreEqual(0, e.StageProgress);

			PlacedStructure replacement = w.Nullifier(0, 5);
			w.Tick();
			Assert.AreEqual(NullifierStatus.Engaged, replacement.NullifierState, "the sequence is already initiated");
			Assert.AreEqual(1, e.StageProgress, "it qualifies while its store fills");
			Assert.AreEqual(StageTicks - 1, TicksToReconfigure(w, e));
		}

		[Test]
		public void StageWaitsForAFullPulseStore_WithoutLosingProgress()
		{
			var (w, n, e) = Basic();
			w.Settings.reconfigurationStageTicks = 5;
			w.Tick(FillTicks);
			Assert.IsTrue(w.Initiate(n));
			w.Tick(5);
			Assert.AreEqual(1, e.CompletedStages);
			Assert.AreEqual(0, n.StoredCharges);

			w.Tick(5); // stage 2's time is complete, but the store holds 10 of 30
			Assert.AreEqual(1, e.CompletedStages);
			Assert.IsTrue(e.AwaitingPulse);
			Assert.AreEqual(5, e.StageProgress);
			Assert.AreEqual(10, n.StoredCharges);

			w.Tick(9);
			Assert.AreEqual(1, e.CompletedStages);
			Assert.AreEqual(5, e.StageProgress, "held, not reset");
			w.Tick(); // store reaches 30
			Assert.AreEqual(2, e.CompletedStages);
			Assert.IsFalse(e.AwaitingPulse);
		}

		[Test]
		public void NullifierOperation_SharesChargesWithConstruction_OptionA()
		{
			var w = new World();
			w.Settings.coreGenerationPerTick = 3;
			w.Emitter(0, 10);
			w.Core(0, 0);
			PlacedStructure n = w.Nullifier(0, 5);
			PlacedStructure blueprint = w.Blueprint(StructureType.Stabilizer, 0, -5);
			w.Tick();
			Assert.AreEqual(3, w.Diagnostics.Distributed);
			Assert.Greater(blueprint.ChargesDelivered, 0, "construction is served");
			Assert.Greater(n.StoredCharges, 0, "the Nullifier is served too");
			Assert.AreEqual(n.StoredCharges, w.Diagnostics.NullifierDelivered);
		}

		[Test]
		public void Reconfiguration_IsDeterministic()
		{
			string Run()
			{
				var w = new World();
				Emitter e = w.Emitter(0, 10);
				w.Core(0, 0);
				PlacedStructure a = w.Nullifier(0, 5);
				w.Nullifier(1, 5);
				w.Tick(FillTicks);
				w.Initiate(a);
				w.Tick(200);
				w.Remove(a);
				int ticks = TicksToReconfigure(w, e);
				return $"{ticks}|{e.Interruptions}|{w.Diagnostics.TotalPulses}|{w.Diagnostics.TotalDistributed}";
			}

			Assert.AreEqual(Run(), Run());
		}
	}
}
