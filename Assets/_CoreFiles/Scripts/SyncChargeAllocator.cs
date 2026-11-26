using System.Collections.Generic;

// Sync Charges, increment 1 (opt-in). Pure C#: no Unity objects, no frame time, no randomness.
//
// Consumer categories for automatic allocation (Option A): Construction (increment 1), StabilizerSupply
// (U2, increment 2) and NullifierOperation (N1-A, increment 3). Each is used only while its opt-in is on.
public enum SyncChargeCategory
{
	Construction = 0,
	StabilizerSupply = 1,
	NullifierOperation = 2
}

// One consumer's request for this simulation tick.
public struct SyncChargeRequest
{
	public SyncChargeCategory Category;

	// Lower values are served first within a category (placement order).
	public long Order;

	// Whole charges wanted this tick. Zero or less means "not requesting".
	public int Demand;

	public SyncChargeRequest(SyncChargeCategory category, long order, int demand)
	{
		Category = category;
		Order = order;
		Demand = demand;
	}
}

// Automatic, deterministic Sync Charge allocation (Option A; no player priority controls).
//
// Charges are whole units handed out one at a time:
// - round-robin across categories, in enum order (Construction, StabilizerSupply, NullifierOperation);
// - within a category, to the requester with the lowest Order until its Demand is met, then the next.
//   Equal Order values fall back to list position.
// So no category with demand is starved while another is served, and building spam inside one category
// delays later blueprints instead of spreading charges thinly across all of them.
// The result depends only on the inputs.
public static class SyncChargeAllocator
{
	private static readonly SyncChargeCategory[] CategoryOrder =
	{
		SyncChargeCategory.Construction,
		SyncChargeCategory.StabilizerSupply,
		SyncChargeCategory.NullifierOperation
	};

	// Returns the grant for each request, by index. The grants never exceed a request's Demand,
	// and their sum is min(available, total positive demand).
	public static int[] Allocate(int available, IReadOnlyList<SyncChargeRequest> requests)
	{
		int[] grants = new int[requests.Count];

		if (available <= 0 || requests.Count == 0)
			return grants;

		// Per category: request indices sorted by (Order, index).
		var queues = new List<int>[CategoryOrder.Length];

		for (int c = 0; c < CategoryOrder.Length; c++)
		{
			queues[c] = new List<int>();
		}

		for (int i = 0; i < requests.Count; i++)
		{
			if (requests[i].Demand <= 0)
				continue;

			int c = CategoryIndex(requests[i].Category);

			if (c >= 0)
			{
				queues[c].Add(i);
			}
		}

		for (int c = 0; c < queues.Length; c++)
		{
			queues[c].Sort((a, b) =>
			{
				int byOrder = requests[a].Order.CompareTo(requests[b].Order);
				return byOrder != 0 ? byOrder : a.CompareTo(b);
			});
		}

		int[] heads = new int[queues.Length];

		while (available > 0)
		{
			bool grantedThisRound = false;

			for (int c = 0; c < queues.Length && available > 0; c++)
			{
				List<int> queue = queues[c];

				while (heads[c] < queue.Count && grants[queue[heads[c]]] >= requests[queue[heads[c]]].Demand)
				{
					heads[c]++;
				}

				if (heads[c] >= queue.Count)
					continue;

				grants[queue[heads[c]]]++;
				available--;
				grantedThisRound = true;
			}

			if (!grantedThisRound)
				break;
		}

		return grants;
	}

	private static int CategoryIndex(SyncChargeCategory category)
	{
		for (int c = 0; c < CategoryOrder.Length; c++)
		{
			if (CategoryOrder[c] == category)
				return c;
		}

		return -1;
	}
}
