using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Increment 2A: the text-only debug overlay. Pure: it reads state and formats text.
	public class SyncDebugOverlayTests
	{
		private static SyncInfrastructureSettings Settings() => new()
		{
			enabled = true,
			stabilizerSupplyEnabled = true
		};

		private static List<PlacedStructure> World(SyncInfrastructureSettings settings, SyncDiagnostics diagnostics, int ticks)
		{
			var structures = new List<PlacedStructure>();
			long order = 0;

			PlacedStructure Add(StructureType type, int x, int y, bool blueprint)
			{
				var s = new PlacedStructure(type, new Vector2Int(x, y)) { PlacementOrder = ++order };
				if (blueprint) SyncInfrastructure.MakeBlueprint(s, settings);
				structures.Add(s);
				return s;
			}

			Add(StructureType.Core, 0, 0, false);
			Add(StructureType.Stabilizer, 2, 0, false);
			Add(StructureType.Stabilizer, 0, 3, false);
			Add(StructureType.Stabilizer, 30, 0, false); // out of range: disconnected
			Add(StructureType.Stabilizer, 4, 4, true);

			InfrastructureNetwork.Recalculate(structures, 8f);
			for (int i = 0; i < ticks; i++)
				SyncInfrastructure.Step(structures, _ => true, settings, diagnostics);

			return structures;
		}

		[Test]
		public void SyncOff_SaysSo_AndShowsRealityIntegrity()
		{
			string text = SyncDebugOverlay.Build(new SyncInfrastructureSettings(), new SyncDiagnostics(), new List<PlacedStructure>(),
				72.46f, false, 30f, false, 12);

			StringAssert.Contains("tick 12", text);
			StringAssert.Contains("Reality Integrity 72.5 %", text);
			StringAssert.Contains("S4 defeat: off", text);
			StringAssert.Contains("Sync Infrastructure: off", text);
		}

		[Test]
		public void ShowsCharges_Upkeep_BlueprintProgress_AndEachStabilizer()
		{
			SyncInfrastructureSettings settings = Settings();
			var d = new SyncDiagnostics();
			List<PlacedStructure> world = World(settings, d, 3);

			string text = SyncDebugOverlay.Build(settings, d, world, 64f, true, 30f, false, 3);
			TestContext.WriteLine(text);

			StringAssert.Contains($"generated {d.Generated} | delivered {d.Distributed} | unused {d.Unused}", text);
			StringAssert.Contains("share reserved 2", text);
			StringAssert.Contains("foothold reserved 4", text);
			StringAssert.Contains("Upkeep: 2 per Stabilizer per tick", text);
			StringAssert.Contains("construction share 25 %", text);
			StringAssert.Contains("S4 defeat below 30 %: ON", text);
			StringAssert.Contains("Blueprints (1):", text);
			StringAssert.Contains("Stabilizer (4,4)  6/40 (15 %)  Reconstructing", text);
			StringAssert.Contains("Stabilizers (3):", text);
			StringAssert.Contains("(2,0)  effectiveness 100 %  Supplied", text);
			StringAssert.Contains("[foothold]", text);
			StringAssert.Contains("(30,0)  effectiveness 0 %  Starved  store 0/20  DISCONNECTED", text);
			StringAssert.DoesNotContain("MISSION FAILED", text);
		}

		[Test]
		public void ShowsMissionFailed_WhenDefeated()
		{
			string text = SyncDebugOverlay.Build(Settings(), new SyncDiagnostics(), new List<PlacedStructure>(), 29f, true, 30f, true, 900);
			StringAssert.Contains("MISSION FAILED", text);
		}

		[Test]
		public void LongLists_AreTruncated()
		{
			SyncInfrastructureSettings settings = Settings();
			var structures = new List<PlacedStructure>();
			for (int i = 0; i < SyncDebugOverlay.MaxListedEntries + 5; i++)
				structures.Add(new PlacedStructure(StructureType.Stabilizer, new Vector2Int(i, 0)) { PlacementOrder = i + 1 });

			string text = SyncDebugOverlay.Build(settings, new SyncDiagnostics(), structures, 90f, false, 30f, false, 1);

			StringAssert.Contains("... +5 more", text);
		}

		[Test]
		public void Building_TheText_ChangesNothing()
		{
			SyncInfrastructureSettings settings = Settings();
			var d = new SyncDiagnostics();
			List<PlacedStructure> world = World(settings, d, 5);
			string before = Snapshot(world, d);

			SyncDebugOverlay.Build(settings, d, world, 50f, true, 30f, false, 5);

			Assert.AreEqual(before, Snapshot(world, d));
		}

		private static string Snapshot(List<PlacedStructure> world, SyncDiagnostics d)
		{
			var sb = new System.Text.StringBuilder();
			sb.Append(d.Ticks).Append('|').Append(d.Generated).Append('|').Append(d.Distributed).Append('|');
			foreach (PlacedStructure s in world)
				sb.Append(s.Position).Append(s.ChargesDelivered).Append(s.StoredCharges).Append(s.SupplyEffectiveness).Append(s.IsConnected).Append(';');
			return sb.ToString();
		}
	}
}
