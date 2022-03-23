namespace RepairableGear
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using RimWorld;
    using UnityEngine;
    using Verse;
    using Verse.AI;

    internal class WorkGiver_RepairItem : WorkGiver_Scanner
    {
        private readonly List<ThingCount> RepairCostThings;
        private static readonly List<Thing> RepairCostRelevantThings;
        private static readonly List<Thing> RepairCostNewRelevantThings;
        private static readonly HashSet<Thing> RepairCostAssignedThings;
        private static readonly DefCountList AvailableCounts;
        private static readonly IntRange ReCheckFailedBillTicksRange;

        public WorkGiver_RepairItem()
        {
            RepairCostThings = new List<ThingCount>();
        }

        static WorkGiver_RepairItem()
        {
            ReCheckFailedBillTicksRange = new IntRange(500, 600);
            RepairCostRelevantThings = new List<Thing>();
            RepairCostNewRelevantThings = new List<Thing>();
            RepairCostAssignedThings = new HashSet<Thing>();
            AvailableCounts = new DefCountList();
        }

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Some;
        }

        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                if (def.fixedBillGiverDefs != null && def.fixedBillGiverDefs.Count == 1)
                    return ThingRequest.ForDef(def.fixedBillGiverDefs[0]);

                return ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver);
            }
        }

        public override Job JobOnThing(Pawn pawn, Thing workbench, bool forced = false)
        {
            //NOTE: JobOnThing() gets called by all workbenches it seems, the null returns below take care of this
            IBillGiver billGiver = workbench as IBillGiver;

            if (billGiver == null
            || !this.ThingIsUsableBillGiver(workbench)
            || !billGiver.CurrentlyUsableForBills()
            || !billGiver.BillStack.AnyShouldDoNow
            || workbench.IsBurning()
            || workbench.IsForbidden(pawn))
                return null;

            if (!pawn.CanReserve(workbench))
                return null;

            if (!pawn.CanReserveAndReach(workbench.InteractionCell, PathEndMode.OnCell, Danger.Some))
                return null;

            billGiver.BillStack.RemoveIncompletableBills();

            // clears off workbench
            var jobHaul = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, billGiver, null);
            if (jobHaul != null)
                return jobHaul;

            foreach (var bill in billGiver.BillStack)
            {
                if ((bill.recipe.requiredGiverWorkType != null && bill.recipe.requiredGiverWorkType != def.workType) ||
                    (Find.TickManager.TicksGame < bill.lastIngredientSearchFailTicks + ReCheckFailedBillTicksRange.RandomInRange && FloatMenuMakerMap.makingFor != pawn) ||
                    !bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                    continue;

                if (!bill.recipe.PawnSatisfiesSkillRequirements(pawn))
                {
                    JobFailReason.Is("RG_RepairItem_NoSkill".Translate());
                    return null;
                }

                var repairableThings = FindRepairableThings(pawn, workbench, bill);
                if (repairableThings == null)
                {
                    JobFailReason.Is("RG_RepairItem_NoItems".Translate());
                    return null;
                }

                foreach (var item in repairableThings)
                {
                    if (TryFindBestIngredients(pawn, workbench, item, RepairCostThings, bill))
                    {
                        return CreateRepairJob(workbench, item, RepairCostThings, bill);
                    }
                }

                if (FloatMenuMakerMap.makingFor != pawn)
                    bill.lastIngredientSearchFailTicks = Find.TickManager.TicksGame;
            }

            JobFailReason.Is("RG_RepairItem_NoItems".Translate());
            return null;
        }

        public static bool TryFindBestIngredients(
            Pawn pawn, 
            Thing workbench, 
            Thing repairableThing, 
            List<ThingCount> ingredients, 
            Bill bill = null,
            List<ThingDefCount> repairCost = null)
        {
            ingredients.Clear();

            var neededIngredients = repairCost ?? repairableThing.GetRepairCostList();
            if (neededIngredients.NullOrEmpty() && repairableThing.CostListAdjusted().NullOrEmpty())
            {
                // no needed ingredients is empty because there is no existing crafting cost, this should not count as a free repair.
                return false;
            }
            else if (neededIngredients.NullOrEmpty() && !repairableThing.CostListAdjusted().NullOrEmpty())
            {
                // free repair!
                return true;
            }

            var rootRegion = pawn.Map.regionGrid.GetValidRegionAt(GetBillGiverRootCell(workbench, pawn));
            if (rootRegion == null)
            {
                return false;
            }

            RepairCostRelevantThings.Clear();
            var foundAll = false;

            Predicate<Thing> baseValidator = t =>
            {
                if (!t.Spawned
                || t.IsForbidden(pawn)
                || (bill != null && (t.Position - workbench.Position).LengthHorizontalSquared >= bill.ingredientSearchRadius * (double)bill.ingredientSearchRadius)
                || !neededIngredients.Any(ingred => ingred.ThingDef == t.def)
                || !pawn.CanReserve(t))
                {
                    return false;
                }

                return (bill != null && !bill.CheckIngredientsIfSociallyProper) || t.IsSociallyProper(pawn);
            };

            var billGiverIsPawn = workbench is Pawn;

            RegionProcessor regionProcessor = r =>
            {
                RepairCostNewRelevantThings.Clear();
                var thingList = r.ListerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver));
                foreach (var thing in thingList)
                {
                    if (baseValidator(thing) && (!thing.def.IsMedicine || !billGiverIsPawn))
                        RepairCostNewRelevantThings.Add(thing);
                }

                if (RepairCostNewRelevantThings.Count <= 0)
                    return false;

                int Comparison(Thing t1, Thing t2) => (t1.Position - pawn.Position).LengthHorizontalSquared.CompareTo((t2.Position - pawn.Position).LengthHorizontalSquared);

                RepairCostNewRelevantThings.Sort(Comparison);
                RepairCostRelevantThings.AddRange(RepairCostNewRelevantThings);
                RepairCostNewRelevantThings.Clear();

                if (TryFindBestBillIngredientsInSet_NoMix(RepairCostRelevantThings, neededIngredients, ingredients))
                {
                    foundAll = true;
                    return true;
                }
                return false;
            };

            RegionEntryPredicate entryCondition = (from, to) => to.Allows(TraverseParms.For(pawn), false);

            RegionTraverser.BreadthFirstTraverse(rootRegion, entryCondition, regionProcessor, 99999);

            return foundAll;
        }

        public static Job CreateRepairJob(Thing workbench, Thing repairableThing, IList<ThingCount> repairCostThings, Bill bill = null, JobDef jobDef = null)
        {
            jobDef = jobDef ?? DefDatabase<JobDef>.GetNamed(Constants.JOBDEF_REPAIR);
            var job = new Job(jobDef, workbench)
            {
                haulMode = HaulMode.ToCellNonStorage,
                bill = bill,
                targetQueueB = new List<LocalTargetInfo>(repairCostThings.Count),
                countQueue = new List<int>(repairCostThings.Count)
            };

            job.targetQueueB.Add(repairableThing);
            job.countQueue.Add(1);

            for (var index = 0; index < repairCostThings.Count; ++index)
            {
                if (repairCostThings[index].Count > 0)
                {
                    job.targetQueueB.Add(repairCostThings[index].Thing);
                    job.countQueue.Add(repairCostThings[index].Count);
                }
            }

            return job;
        }

        private static List<Thing> FindRepairableThings(Pawn pawn, Thing workbench, Bill bill)
        {
            List<Thing> validItems = new List<Thing>();
            List<Thing> relevantItems = new List<Thing>();

            //get the root region that the bench is in.
            Region rootRegion = pawn.Map.regionGrid.GetValidRegionAt(GetBillGiverRootCell(workbench, pawn));
            if (rootRegion == null)
                return validItems;

            //Predicate: if the pawn can enter the region from the root, consider checking it.
            RegionEntryPredicate regionEntryCondition = (from, to) => to.Allows(TraverseParms.For(pawn), false);

            //Delegate: process the current region being scanned.
            RegionProcessor regionProcessor = region =>
            {
                // gets a list of haulable things from the region.
                var regionItems = region.ListerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways));

                // find out which of these items are relevant.
                relevantItems.AddRange(regionItems.Where(item => item.IsRepairable(pawn, bench: workbench, bill: bill, checkMinimumRepairChance: true)));

                // nothing valid in this region
                if (relevantItems.Count <= 0)
                    return false;

                // Comparison: which item is closer to the pawn?
                Comparison<Thing> comparison = (t1, t2) =>
                        (t1.Position - pawn.Position).LengthHorizontalSquared
                        .CompareTo((t2.Position - pawn.Position).LengthHorizontalSquared);

                relevantItems.Sort(comparison);

                validItems.AddRange(relevantItems);

                relevantItems.Clear();

                // returning true stops the traverse, we want to exaust it
                return false;
            };

            // run the region traverse
            RegionTraverser.BreadthFirstTraverse(rootRegion, regionEntryCondition, regionProcessor, 99999);

            return validItems;
        }

        private static bool TryFindBestBillIngredientsInSet_NoMix(
            List<Thing> availableThings, 
            List<ThingDefCount> neededThings, 
            List<ThingCount> foundIngredients)
        {
            foundIngredients.Clear();
            RepairCostAssignedThings.Clear();
            AvailableCounts.Clear();
            AvailableCounts.GenerateFrom(availableThings);
            foreach (var ingredientCount in neededThings)
            {
                var flag = false;
                for (var index2 = 0; index2 < AvailableCounts.Count; ++index2)
                {
                    float f = ingredientCount.Count;

                    if (!(f <= (double)AvailableCounts.GetCount(index2)) || ingredientCount.ThingDef != AvailableCounts.GetDef(index2))
                        continue;

                    foreach (var item in availableThings)
                    {
                        if (item.def != AvailableCounts.GetDef(index2) || RepairCostAssignedThings.Contains(item))
                            continue;

                        var countToAdd = Mathf.Min(Mathf.FloorToInt(f), item.stackCount);
                        ThingCountUtility.AddToList(foundIngredients, item, countToAdd);
                        f -= countToAdd;
                        RepairCostAssignedThings.Add(item);
                        if (f < 1.0 / 1000.0)
                        {
                            flag = true;
                            var val = AvailableCounts.GetCount(index2) - ingredientCount.Count;
                            AvailableCounts.SetCount(index2, val);
                            break;
                        }
                    }
                    if (flag)
                        break;
                }
                if (!flag)
                    return false;
            }
            return true;
        }

        private static IntVec3 GetBillGiverRootCell(Thing billGiver, Pawn forPawn)
        {
            Building building = billGiver as Building;
            if (building == null)
                return billGiver.Position;

            if (building.def.hasInteractionCell)
                return building.InteractionCell;

            Log.Error("Tried to find bill ingredients for " + billGiver + " which has no interaction cell.");
            return forPawn.Position;
        }

        private bool ThingIsUsableBillGiver(Thing thing)
        {
            var pawn1 = thing as Pawn;
            var corpse = thing as Corpse;
            Pawn pawn2 = null;
            if (corpse != null)
                pawn2 = corpse.InnerPawn;
            return this.def.fixedBillGiverDefs != null && this.def.fixedBillGiverDefs.Contains(thing.def) ||
                    pawn1 != null &&
                    (this.def.billGiversAllHumanlikes && pawn1.RaceProps.Humanlike || this.def.billGiversAllMechanoids && pawn1.RaceProps.IsMechanoid ||
                    this.def.billGiversAllAnimals && pawn1.RaceProps.Animal) ||
                    corpse != null && pawn2 != null &&
                    (this.def.billGiversAllHumanlikesCorpses && pawn2.RaceProps.Humanlike ||
                    this.def.billGiversAllMechanoidsCorpses && pawn2.RaceProps.IsMechanoid || this.def.billGiversAllAnimalsCorpses && pawn2.RaceProps.Animal);
        }

        private class DefCountList
        {
            private readonly List<ThingDef> _defs;
            private readonly List<float> _counts;

            public int Count => _defs.Count;

            private float this[ThingDef def]
            {
                get
                {
                    var index = _defs.IndexOf(def);
                    if (index < 0)
                        return 0.0f;
                    return _counts[index];
                }
                set
                {
                    var index = _defs.IndexOf(def);
                    if (index < 0)
                    {
                        _defs.Add(def);
                        _counts.Add(value);
                        index = _defs.Count - 1;
                    }
                    else
                        _counts[index] = value;
                    CheckRemove(index);
                }
            }

            public DefCountList()
            {
                _defs = new List<ThingDef>();
                _counts = new List<float>();
            }

            public float GetCount(int index)
            {
                return _counts[index];
            }

            public void SetCount(int index, float val)
            {
                _counts[index] = val;
                CheckRemove(index);
            }

            public ThingDef GetDef(int index)
            {
                return _defs[index];
            }

            private void CheckRemove(int index)
            {
                if (Math.Abs(_counts[index]) > 0.001f)
                    return;
                _counts.RemoveAt(index);
                _defs.RemoveAt(index);
            }

            public void Clear()
            {
                _defs.Clear();
                _counts.Clear();
            }

            public void GenerateFrom(List<Thing> things)
            {
                Clear();
                foreach (var thing in things)
                    this[thing.def] += thing.stackCount;
            }
        }
    }
}
