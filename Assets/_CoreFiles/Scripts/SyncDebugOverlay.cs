using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Text-only Sync debug overlay (roadmap increment 2A, playability pass). GridManager shows it when the
// backquote key is toggled. Pure C#: it only reads simulation state, so it can be tested without a scene
// and can never change behavior.
public static class SyncDebugOverlay
{
	public const int MaxListedEntries = 12;

	private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

	public static string Build(
		SyncInfrastructureSettings settings,
		SyncDiagnostics diagnostics,
		IReadOnlyList<PlacedStructure> structures,
		float realityIntegrity,
		bool defeatEnabled,
		float defeatThreshold,
		bool missionDefeated,
		long tick)
	{
		var sb = new StringBuilder();

		sb.Append("SYNC DEBUG   tick ").Append(tick.ToString(Inv)).AppendLine("   (` hides)");
		sb.Append("Reality Integrity ").Append(Pct(realityIntegrity, "F1"));
		sb.Append(defeatEnabled
			? $"   | S4 defeat below {Pct(defeatThreshold, "F0")}: ON"
			: "   | S4 defeat: off");
		sb.AppendLine();

		if (missionDefeated)
			sb.AppendLine("MISSION FAILED");

		if (settings == null || !settings.enabled)
		{
			sb.Append("Sync Infrastructure: off (instant, free building)");
			return sb.ToString();
		}

		SyncDiagnostics d = diagnostics ?? new SyncDiagnostics();

		sb.Append("Charges this tick: generated ").Append(d.Generated)
			.Append(" | delivered ").Append(d.Distributed)
			.Append(" | unused ").Append(d.Unused).AppendLine();
		sb.Append("  construction ").Append(d.ConstructionDelivered).Append(" of ").Append(d.ConstructionDemand)
			.Append(" wanted (share reserved ").Append(d.ConstructionReserved).Append(')');

		if (settings.SupplyActive)
		{
			sb.Append(" | upkeep supply ").Append(d.SupplyDelivered).Append(" of ").Append(d.SupplyDemand)
				.Append(" wanted (foothold reserved ").Append(d.FootholdReserved).Append(')');
		}

		sb.AppendLine();

		if (settings.SupplyActive)
		{
			sb.Append("Upkeep: ").Append(settings.stabilizerUpkeepPerTick).Append(" per Stabilizer per tick")
				.Append(" | foothold: ").Append(settings.footholdStabilizerCount).Append(" nearest the Core")
				.Append(" | construction share ").Append(Pct(settings.constructionMinimumShare * 100f, "F0"))
				.AppendLine();
		}
		else
		{
			sb.AppendLine("Upkeep: off (U2 supply disabled; Stabilizers need no charges)");
		}

		var blueprints = new List<PlacedStructure>();
		var stabilizers = new List<PlacedStructure>();

		if (structures != null)
		{
			foreach (PlacedStructure s in structures)
			{
				if (s.UnderConstruction)
					blueprints.Add(s);
				else if (s.Type == StructureType.Stabilizer)
					stabilizers.Add(s);
			}
		}

		var foothold = new HashSet<PlacedStructure>();

		if (settings.SupplyActive && structures != null)
		{
			var list = structures as List<PlacedStructure> ?? new List<PlacedStructure>(structures);
			foreach (PlacedStructure s in SyncInfrastructure.FindFoothold(list, settings))
				foothold.Add(s);
		}

		sb.Append("Blueprints (").Append(blueprints.Count).AppendLine("):");
		for (int i = 0; i < blueprints.Count && i < MaxListedEntries; i++)
		{
			PlacedStructure b = blueprints[i];
			float progress = b.ChargesRequired > 0 ? 100f * b.ChargesDelivered / b.ChargesRequired : 0f;
			sb.Append("  ").Append(b.Type).Append(' ').Append(Cell(b))
				.Append("  ").Append(b.ChargesDelivered).Append('/').Append(b.ChargesRequired)
				.Append(" (").Append(Pct(progress, "F0")).Append(")  ").Append(b.Construction).AppendLine();
		}
		More(sb, blueprints.Count);

		sb.Append("Stabilizers (").Append(stabilizers.Count).AppendLine("):");
		for (int i = 0; i < stabilizers.Count && i < MaxListedEntries; i++)
		{
			PlacedStructure s = stabilizers[i];
			sb.Append("  ").Append(Cell(s)).Append("  effectiveness ").Append(Pct(s.SupplyEffectiveness * 100f, "F0"));

			if (settings.SupplyActive)
			{
				sb.Append("  ").Append(s.Supply)
					.Append("  store ").Append(s.StoredCharges).Append('/').Append(settings.stabilizerStoreCapacity);
				if (foothold.Contains(s))
					sb.Append("  [foothold]");
			}

			if (!s.IsConnected)
				sb.Append("  DISCONNECTED");

			sb.AppendLine();
		}
		More(sb, stabilizers.Count);

		return sb.ToString().TrimEnd();
	}

	private static string Cell(PlacedStructure s) => $"({s.Position.x.ToString(Inv)},{s.Position.y.ToString(Inv)})";

	private static string Pct(float value, string format) => value.ToString(format, Inv) + " %";

	private static void More(StringBuilder sb, int count)
	{
		if (count > MaxListedEntries)
			sb.Append("  ... +").Append(count - MaxListedEntries).AppendLine(" more");
	}
}
