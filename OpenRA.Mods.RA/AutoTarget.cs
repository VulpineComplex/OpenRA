﻿#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using OpenRA.FileFormats;
using OpenRA.Traits;
using System.Drawing;
using System.Linq;

namespace OpenRA.Mods.RA
{
	[Desc("The actor will automatically engage the enemy when it is in range.")]
	public class AutoTargetInfo : ITraitInfo, Requires<AttackBaseInfo>
	{
		[Desc("It will try to hunt down the enemy if it is not set to defend.")]
		public readonly bool AllowMovement = true;
		[Desc("Set to a value >1 to override weapons maximum range for this.")]
		public readonly int ScanRadius = -1;
		public readonly UnitStance InitialStance = UnitStance.AttackAnything;

		[Desc("Ticks to wait until next AutoTarget: attempt.")]
		public readonly int MinimumScanTimeInterval = 3;
		[Desc("Ticks to wait until next AutoTarget: attempt.")]
		public readonly int MaximumScanTimeInterval = 8;

		public object Create(ActorInitializer init) { return new AutoTarget(init.self, this); }
	}

	public enum UnitStance { HoldFire, ReturnFire, Defend, AttackAnything };

	public class AutoTarget : INotifyIdle, INotifyDamage, ITick, IResolveOrder, ISync
	{
		readonly AutoTargetInfo Info;
		readonly AttackBase attack;

		[Sync] public int nextScanTime = 0;
		public UnitStance stance;
		[Sync] public int stanceNumber { get { return (int)stance; } }
		public UnitStance predictedStance;		/* NOT SYNCED: do not refer to this anywhere other than UI code */
		[Sync] public int AggressorID;

		public AutoTarget(Actor self, AutoTargetInfo info)
		{
			Info = info;
			attack = self.Trait<AttackBase>();
			stance = Info.InitialStance;
			predictedStance = stance;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "SetUnitStance")
				stance = (UnitStance)order.TargetLocation.X;
		}

		public void Damaged(Actor self, AttackInfo e)
		{
			if (!self.IsIdle) return;
			if (e.Attacker.Destroyed) return;

			if (stance < UnitStance.ReturnFire) return;

			// not a lot we can do about things we can't hurt... although maybe we should automatically run away?
			var attack = self.Trait<AttackBase>();
			if (!attack.HasAnyValidWeapons(Target.FromActor(e.Attacker))) return;

			// don't retaliate against own units force-firing on us. it's usually not what the player wanted.
			if (e.Attacker.AppearsFriendlyTo(self)) return;

			if (e.Damage < 0) return;	// don't retaliate against healers

			AggressorID = (int)e.Attacker.ActorID;

			attack.AttackTarget(Target.FromActor(e.Attacker), false, Info.AllowMovement && stance != UnitStance.Defend);
		}

		public void TickIdle(Actor self)
		{
			if (stance < UnitStance.Defend) return;

			var target = ScanForTarget(self, null);
			if (target != null)
			{
				var t = Target.FromActor(target);
				self.SetTargetLine(t, Color.Red, false);
				attack.AttackTarget(t, false, Info.AllowMovement && stance != UnitStance.Defend);
			}
		}

		public void Tick(Actor self)
		{
			if (nextScanTime > 0)
				--nextScanTime;
		}

		public Actor ScanForTarget(Actor self, Actor currentTarget)
		{
			var range = Info.ScanRadius > 0 ? WRange.FromCells(Info.ScanRadius) : attack.GetMaximumRange();
			if (self.IsIdle || currentTarget == null || !Target.FromActor(currentTarget).IsInRange(self.CenterPosition, range))
				if (nextScanTime <= 0)
					return ChooseTarget(self, range);

			return currentTarget;
		}

		public void ScanAndAttack(Actor self)
		{
			var targetActor = ScanForTarget(self, null);
			if (targetActor != null)
				attack.AttackTarget(Target.FromActor(targetActor), false, Info.AllowMovement && stance != UnitStance.Defend);
		}

		Actor ChooseTarget(Actor self, WRange range)
		{
			nextScanTime = self.World.SharedRandom.Next(Info.MinimumScanTimeInterval, Info.MaximumScanTimeInterval);
			var inRange = self.World.FindActorsInCircle(self.CenterPosition, range);

			if (self.Owner.HasFogVisibility())
			{
				return inRange
					.Where(a => a.AppearsHostileTo(self))
					.Where(a => !a.HasTrait<AutoTargetIgnore>())
					.Where(a => attack.HasAnyValidWeapons(Target.FromActor(a)))
					.ClosestTo(self);
			}
			else
			{
				return inRange
					.Where(a => a.AppearsHostileTo(self))
					.Where(a => !a.HasTrait<AutoTargetIgnore>())
					.Where(a => attack.HasAnyValidWeapons(Target.FromActor(a)))
					.Where(a => self.Owner.Shroud.IsTargetable(a))
					.ClosestTo(self);
			}
		}
	}

	[Desc("Will not get automatically targeted by enemy (like walls)")]
	class AutoTargetIgnoreInfo : TraitInfo<AutoTargetIgnore> { }
	class AutoTargetIgnore { }
	
}
