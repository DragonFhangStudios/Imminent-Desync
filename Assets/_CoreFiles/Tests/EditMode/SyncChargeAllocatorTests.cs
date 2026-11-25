using System.Collections.Generic;
using NUnit.Framework;

namespace ImminentDesync.Tests.EditMode
{
	public class SyncChargeAllocatorTests
	{
		private static SyncChargeRequest Construction(long order, int demand) =>
			new(SyncChargeCategory.Construction, order, demand);

		[Test]
		public void NoSupply_GrantsNothing()
		{
			int[] grants = SyncChargeAllocator.Allocate(0, new[] { Construction(1, 5) });

			Assert.AreEqual(new[] { 0 }, grants);
		}

		[Test]
		public void Total_IsMinOfSupplyAndDemand_AndNoRequestGetsMoreThanItsDemand()
		{
			var requests = new[] { Construction(1, 2), Construction(2, 3), Construction(3, 0), Construction(4, -1) };

			int[] scarce = SyncChargeAllocator.Allocate(4, requests);
			int[] plenty = SyncChargeAllocator.Allocate(100, requests);

			Assert.AreEqual(new[] { 2, 2, 0, 0 }, scarce);
			Assert.AreEqual(new[] { 2, 3, 0, 0 }, plenty);
		}

		[Test]
		public void WithinCategory_LowestPlacementOrderIsServedFirst()
		{
			// Listed out of order on purpose: Order decides, not list position.
			var requests = new[] { Construction(order: 7, demand: 3), Construction(order: 2, demand: 3) };

			int[] grants = SyncChargeAllocator.Allocate(4, requests);

			Assert.AreEqual(new[] { 1, 3 }, grants);
		}

		[Test]
		public void AcrossCategories_RoundRobin_NoCategoryStarves()
		{
			var requests = new[]
			{
				Construction(1, 10),
				new SyncChargeRequest(SyncChargeCategory.StabilizerSupply, 1, 10),
				new SyncChargeRequest(SyncChargeCategory.NullifierOperation, 1, 10)
			};

			int[] grants = SyncChargeAllocator.Allocate(7, requests);

			// One charge per category per round, in category order: 3 + 2 + 2.
			Assert.AreEqual(new[] { 3, 2, 2 }, grants);
		}

		[Test]
		public void SameInputs_SameGrants_RegardlessOfListOrder()
		{
			var forward = new List<SyncChargeRequest> { Construction(1, 3), Construction(2, 3), Construction(3, 3) };
			var reversed = new List<SyncChargeRequest> { Construction(3, 3), Construction(2, 3), Construction(1, 3) };

			int[] a = SyncChargeAllocator.Allocate(5, forward);
			int[] b = SyncChargeAllocator.Allocate(5, forward);
			int[] r = SyncChargeAllocator.Allocate(5, reversed);

			Assert.AreEqual(a, b);
			Assert.AreEqual(new[] { 3, 2, 0 }, a);
			Assert.AreEqual(new[] { 0, 2, 3 }, r);
		}
	}
}
