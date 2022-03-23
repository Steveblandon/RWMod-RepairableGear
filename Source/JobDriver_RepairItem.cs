
namespace RepairableGear
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using RimWorld;
    using UnityEngine;
    using Verse;
    using Verse.AI;

    internal class JobDriver_RepairItem : JobDriver
    {
        private const TargetIndex TI_WORKBENCH = TargetIndex.A;
        private const TargetIndex TI_REPAIRABLE_THING = TargetIndex.B;
        private const TargetIndex TI_CELL_STORE = TargetIndex.C;

        private WorkTracker CurWork;

        private static Dictionary<string, WorkTracker> WorkTrackers = new Dictionary<string, WorkTracker>();

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref WorkTrackers, "WorkTrackers", LookMode.Value, LookMode.Deep);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!this.pawn.Reserve(this.job.GetTarget(TI_WORKBENCH), job, 1, -1, null, errorOnFailed))
                return false;

            this.pawn.ReserveAsManyAsPossible(this.job.GetTargetQueue(TI_REPAIRABLE_THING), job);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var gotoBillGiver = Toils_Goto.GotoThing(TI_WORKBENCH, PathEndMode.InteractionCell);
            IBillGiver billGiver = this.job.GetTarget(TI_WORKBENCH).Thing as IBillGiver;
            var itemTargetQueue = this.job.GetTargetQueue(TI_REPAIRABLE_THING);
            var firstTargetInfo = itemTargetQueue.First();
            var repairableThing = firstTargetInfo.Thing;

            this.FailOnConditions(billGiver);

            yield return Toils_Reserve.Reserve(TI_WORKBENCH);

            // check if there is something on workbench that wasn't removed by workgiver
            if (!billGiver.IngredientStackCells.EnumerableNullOrEmpty())
            {
                List<Thing> ingredientThings = billGiver.IngredientStackCells.SelectMany(spot => pawn.Map.thingGrid.ThingsListAt(spot)).ToList();
                foreach (Thing thing in ingredientThings)
                {
                    if (thing.Label != ((Thing)billGiver).Label)
                    {
                        Utils.DebugLog($"removing {thing.Label} from workbench.");
                        yield return new Toil() { initAction = () => this.job.SetTarget(TargetIndex.B, thing) };
                        yield return Toils_Reserve.Reserve(TargetIndex.B);
                        yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TI_REPAIRABLE_THING);
                        Toil grabAndDropThing = new Toil();
                        grabAndDropThing.initAction = delegate ()
                        {
                            Thing thingToGrabAndDrop = this.job.GetTarget(TargetIndex.B).Thing;
                            this.pawn.carryTracker.TryStartCarry(thingToGrabAndDrop, thingToGrabAndDrop.stackCount, true);
                            this.pawn.carryTracker.TryDropCarriedThing(this.pawn.Position, ThingPlaceMode.Near, out thingToGrabAndDrop, null);
                        };
                        yield return grabAndDropThing;
                        yield return Toils_Reserve.Release(TargetIndex.B);
                    }
                }

                yield return new Toil() { initAction = () => this.job.SetTarget(TargetIndex.B, repairableThing) };
            }

            yield return Toils_Reserve.ReserveQueue(TI_REPAIRABLE_THING);
            var extract = Toils_JobTransforms.ExtractNextTargetFromQueue(TI_REPAIRABLE_THING);
            yield return extract;
            var getToHaulTarget = Toils_Goto.GotoThing(TI_REPAIRABLE_THING, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TI_REPAIRABLE_THING);
            yield return getToHaulTarget;
            yield return Toils_Haul.StartCarryThing(TI_REPAIRABLE_THING);
            yield return JumpToCollectNextIntoHandsForBill(getToHaulTarget, TI_REPAIRABLE_THING);
            yield return Toils_Goto.GotoThing(TI_WORKBENCH, PathEndMode.InteractionCell).FailOnDestroyedOrNull(TI_REPAIRABLE_THING);
            var findPlaceTarget = Toils_JobTransforms.SetTargetToIngredientPlaceCell(TI_WORKBENCH, TI_REPAIRABLE_THING, TI_CELL_STORE);
            yield return findPlaceTarget;
            yield return Toils_Haul.PlaceHauledThingInCell(TI_CELL_STORE, findPlaceTarget, false);
            yield return Toils_Jump.JumpIfHaveTargetInQueue(TI_REPAIRABLE_THING, extract);
            yield return gotoBillGiver;
            yield return this.RepairThing(repairableThing);
            yield return this.FindStoreCell();
            yield return Toils_Haul.StartCarryThing(TI_REPAIRABLE_THING);
            yield return Toils_Reserve.Reserve(TI_CELL_STORE);
            yield return Toils_Haul.CarryHauledThingToCell(TI_CELL_STORE);
            yield return Toils_Haul.PlaceHauledThingInCell(TI_CELL_STORE, null, true);
            yield return Toils_Reserve.Release(TI_CELL_STORE);
            yield return Toils_Reserve.Release(TI_WORKBENCH);
        }

        private Toil RepairThing(Thing repairableThing)
        {
            var worktableBuilding = this.job.GetTarget(TI_WORKBENCH).Thing as Building_WorkTable;
            RecipeDef recipeDef = this.job.RecipeDef ?? DefDatabase<RecipeDef>.GetNamed(Constants.RECIPEDEF_GENERIC);
            string workerId = WorkTrackers.TryGetValue(repairableThing.ThingID, out WorkTracker work) ? work.WorkerId : null;

            if (!WorkTrackers.ContainsKey(repairableThing.ThingID) || this.pawn.ThingID != workerId)
            {
                Utils.DebugLog($"workerId={workerId}, pawnId={this.pawn.ThingID}, totalWorkFound?{WorkTrackers.ContainsKey(repairableThing.ThingID)}");
                Utils.DebugLog($"{repairableThing.ThingID}, no existing work tracker found, calculating new one...");
                WorkTrackers[repairableThing.ThingID] = new WorkTracker();
                WorkTrackers[repairableThing.ThingID].TotalWork = this.GetAdjustedWorkAmount(repairableThing, recipeDef);
                WorkTrackers[repairableThing.ThingID].WorkLeft = WorkTrackers[repairableThing.ThingID].TotalWork;
                WorkTrackers[repairableThing.ThingID].WorkerId = this.pawn.ThingID;
                Utils.DebugLog($"id={repairableThing.ThingID}, workerId={WorkTrackers[repairableThing.ThingID].WorkerId}, totalWork={WorkTrackers[repairableThing.ThingID].TotalWork}, workLeft={WorkTrackers[repairableThing.ThingID].WorkLeft}");
            }
            else
            {
                Utils.DebugLog($"{repairableThing.ThingID}, found existing work amount...");
            }

            var repairToil = new Toil
            {
                initAction = () =>
                {
                    if (this.job.bill != null)
                    {
                        this.job.bill.Notify_DoBillStarted(pawn);
                    }

                    this.CurWork = WorkTrackers[repairableThing.ThingID];
                    Utils.DebugLog($"Commencing repair job... total work={this.CurWork.TotalWork}, workLeft={this.CurWork.WorkLeft}");
                    this.CurWork.TickToCheckWorkSuccessChance = this.CurWork.TotalWork - Math.Min((this.CurWork.TotalWork / 3f) + (float)(new System.Random()).NextDouble(), this.CurWork.TotalWork);
                    this.CurWork.RepairCost = repairableThing.GetRepairCostList();
                },

                tickAction = () =>
                {
                    // make sure workbench is still valid
                    if (!worktableBuilding.CurrentlyUsableForBills() 
                    || !worktableBuilding.Spawned 
                    || (this.job.bill != null && this.job.bill.suspended))
                    {
                        this.pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true, true);
                        return;
                    }

                    if (recipeDef?.workSkill != null)
                    {
                        this.pawn.skills.Learn(recipeDef.workSkill, Settings.SkillExpGainPerTick * recipeDef.workSkillLearnFactor, false);
                    }

                    if (this.job.bill != null)
                    {
                        this.job.bill.Notify_PawnDidWork(pawn);
                    }

                    this.pawn.GainComfortFromCellIfPossible(true);

                    float workPerTick = this.pawn.GetStatValue(StatDefOf.WorkSpeedGlobal) * worktableBuilding.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor);
                    this.CurWork.WorkLeft -= workPerTick;

                    if (this.CurWork.WorkLeft <= this.CurWork.TickToCheckWorkSuccessChance && this.CurWork.ResultNotificationLabel.NullOrEmpty())
                    {
                        this.RollForRepairChance(repairableThing, worktableBuilding);
                    }

                    if (this.CurWork.WorkLeft > 0f) return;
                    else this.ApplyRepairActionResult(repairableThing, worktableBuilding, recipeDef);

                    MoteMaker.ThrowText(repairableThing.DrawPos, this.Map, this.CurWork.ResultNotificationLabel, timeBeforeStartFadeout: 6f);

                    if (this.job.bill != null)
                    {
                        var list = new List<Thing> { repairableThing };
                        this.job.bill.Notify_IterationCompleted(pawn, list);
                        RecordsUtility.Notify_BillDone(pawn, list);
                    }

                    WorkTrackers.Remove(repairableThing.ThingID);
                    this.CurWork = null;
                    this.ReadyForNextToil();
                },
            };

            repairToil.defaultCompleteMode = ToilCompleteMode.Never;
            repairToil.WithEffect(() => recipeDef.effectWorking, TI_WORKBENCH);
            repairToil.PlaySustainerOrSound(() => recipeDef.soundWorking);
            repairToil.WithProgressBar(TI_WORKBENCH, delegate
            {
                return 1f - this.CurWork.WorkLeft / this.CurWork.TotalWork;
            }, false, -0.5f);

            return repairToil;
        }

        private Toil FindStoreCell()
        {
            Toil toil = new Toil();
            toil.initAction = delegate ()
            {
                IntVec3 cellFound = IntVec3.Invalid;
                Thing repairableThing = this.job.GetTarget(TI_REPAIRABLE_THING).Thing;

                if (this.job.bill == null)
                {
                    this.pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true, true);
                }
                else if (this.job.bill.GetStoreMode() == BillStoreModeDefOf.DropOnFloor)
                {
                    if (!GenPlace.TryPlaceThing(repairableThing, this.pawn.Position, this.pawn.Map, ThingPlaceMode.Near, null, null, default(Rot4)))
                    {
                        Log.Error(string.Concat(new object[]
                        {
                            pawn,
                            " could not drop recipe product ",
                            repairableThing,
                            " near ",
                            pawn.Position
                        }));
                    }
                }
                else if (this.job.bill.GetStoreMode() == BillStoreModeDefOf.BestStockpile)
                {
                    StoreUtility.TryFindBestBetterStoreCellFor(repairableThing, this.pawn, this.pawn.Map, StoragePriority.Unstored, this.pawn.Faction, out cellFound, true);
                }
                else if (this.job.bill.GetStoreMode() == BillStoreModeDefOf.SpecificStockpile)
                {
                    StoreUtility.TryFindBestBetterStoreCellForIn(repairableThing, this.pawn, this.pawn.Map, StoragePriority.Unstored, this.pawn.Faction, this.job.bill.GetStoreZone().slotGroup, out cellFound, true);
                }
                else
                {
                    Log.ErrorOnce("Unknown store mode", 9158246);
                }

                if (cellFound.IsValid)
                {
                    this.pawn.carryTracker.TryStartCarry(repairableThing);
                    this.job.SetTarget(TI_CELL_STORE, cellFound);
                    this.job.count = 99999;
                    return;
                }

                this.pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true, true);
            };

            return toil;
        }

        private static Toil JumpToCollectNextIntoHandsForBill(Toil gotoGetTargetToil, TargetIndex ind)
        {
            var toil = new Toil();
            toil.initAction = () =>
            {
                const float maxDist = 8;
                var actor = toil.actor;
                var curJob = actor.jobs.curJob;
                var targetQueue = curJob.GetTargetQueue(ind);

                if (targetQueue.NullOrEmpty())
                    return;

                if (actor.carryTracker.CarriedThing == null)
                {
                    Log.Error("JumpToAlsoCollectTargetInQueue run on " + actor + " who is not carrying something.");
                    return;
                }

                if (actor.carryTracker.Full)
                    return;

                for (var i = 0; i < targetQueue.Count; i++)
                {
                    if (!GenAI.CanUseItemForWork(actor, targetQueue[i].Thing))
                        continue;

                    if (!targetQueue[i].Thing.CanStackWith(actor.carryTracker.CarriedThing))
                        continue;

                    if ((actor.Position - targetQueue[i].Thing.Position).LengthHorizontalSquared > maxDist * maxDist)
                        continue;

                    var numInHands = actor.carryTracker.CarriedThing?.stackCount ?? 0;
                    var numToTake = Mathf.Min(Mathf.Min(curJob.countQueue[i], targetQueue[i].Thing.def.stackLimit - numInHands), actor.carryTracker.AvailableStackSpace(targetQueue[i].Thing.def));

                    if (numToTake <= 0)
                        continue;

                    curJob.count = numToTake;
                    curJob.SetTarget(ind, targetQueue[i].Thing);

                    List<int> intList;
                    int index2;
                    (intList = curJob.countQueue)[index2 = i] = intList[index2] - numToTake;

                    if (curJob.countQueue[i] == 0)
                    {
                        curJob.countQueue.RemoveAt(i);
                        targetQueue.RemoveAt(i);
                    }
                    actor.jobs.curDriver.JumpToToil(gotoGetTargetToil);
                    break;

                }
            };

            return toil;
        }

        private void RollForRepairChance(Thing repairableThing, Building_WorkTable worktableBuilding)
        {
            Utils.DebugLog($"workLeft = {this.CurWork.WorkLeft}, TickToCheckWorkSuccessChance = {this.CurWork.TickToCheckWorkSuccessChance}");

            float adjustedSuccessChance = this.pawn.GetAdjustedRepairChance(repairableThing);
            int adjustedSuccessChanceToDisplay = this.pawn.GetAdjustedRepairChanceToDisplay(repairableThing, adjustedSuccessChance);
            float failPartChance = Math.Max(1 - adjustedSuccessChance, 0f) / 3f;
            float minimumRollForPartialSuccess = 1f - adjustedSuccessChance;
            float miniumRollForFullSuccess = minimumRollForPartialSuccess + (3 * adjustedSuccessChance / 4);

            float[] successRange = new float[] { miniumRollForFullSuccess, 1f };
            float[] partialSuccessRange = new float[] { minimumRollForPartialSuccess, miniumRollForFullSuccess };
            float[] failNoCostRange = new float[] { failPartChance * 2f, Math.Min(failPartChance * 3f, minimumRollForPartialSuccess) };
            float[] failSoftRange = new float[] { failPartChance, failPartChance * 2f };
            float[] failHardRange = new float[] { 0f, failPartChance };

            float roll = (float)(new System.Random()).NextDouble();

            Utils.DebugLog($"roll: {roll}, successChances: {Utils.Stringify(successRange)}, {Utils.Stringify(partialSuccessRange)}; failChances: {Utils.Stringify(failNoCostRange)}, {Utils.Stringify(failSoftRange)}, {Utils.Stringify(failHardRange)}");

            this.CurWork.ResultNotificationLabel = "RG_TextMote_Fail".Translate(adjustedSuccessChanceToDisplay);

            if (failPartChance <= 0f || this.IsValueInRange(roll, successRange))
            {
                Utils.DebugLog("repair successful p1");
                this.CurWork.ResultNotificationLabel = "RG_TextMote_Success".Translate(adjustedSuccessChanceToDisplay);
                this.CurWork.RollResult = RollResult.Successful;
            }
            else if (this.IsValueInRange(roll, partialSuccessRange))
            {
                Utils.DebugLog("repair partially successful p1");
                int newHitPoints = repairableThing.HitPoints + repairableThing.RepairableMaxHitPoints(Settings.PartialSuccessRepairAmount);
                bool itemFullyRestored = newHitPoints >= repairableThing.RepairableMaxHitPoints();
                this.CurWork.ResultNotificationLabel = (itemFullyRestored ? "RG_TextMote_Success" : "RG_TextMote_PartialSuccess").Translate(adjustedSuccessChanceToDisplay);
                this.CurWork.RollResult = RollResult.PartialSuccess;
            }
            else if (this.IsValueInRange(roll, failSoftRange))
            {
                Utils.DebugLog("repair failed soft p1");
                this.CurWork.WorkLeft = 0f;
                this.CurWork.RollResult = RollResult.FailSoft;
            }
            else if (this.IsValueInRange(roll, failHardRange))
            {
                Utils.DebugLog("repair failed hard p1");
                this.CurWork.WorkLeft = 0f;
                this.CurWork.RollResult = RollResult.FailHard;
            }
            else
            {
                Utils.DebugLog("repair wasted p1");
                this.CurWork.ResultNotificationLabel = "RG_TextMote_FailNoCost".Translate(adjustedSuccessChanceToDisplay);
                this.CurWork.RollResult = RollResult.FailNoCost;
            }
        }

        private void ApplyRepairActionResult(Thing repairableThing, Building_WorkTable worktableBuilding, RecipeDef recipeDef)
        {
            Utils.DebugLog($"null check: repairableThing:{repairableThing}, worktableBuilding:{worktableBuilding}, recipeDef:{recipeDef}, rollResult:{this.CurWork.RollResult}");

            if (this.CurWork.RollResult == RollResult.Successful)
            {
                Utils.DebugLog("repair successful p2");
                this.pawn.skills.Learn(recipeDef.workSkill, Settings.SuccessSkillExpBonus * recipeDef.workSkillLearnFactor, false);
                float conditionRecovered = (repairableThing.RepairableMaxHitPoints() - repairableThing.HitPoints) / (float) repairableThing.RepairableMaxHitPoints();
                Find.World.GetComponent<QualityDegradation>().Update(repairableThing, conditionRecovered);
                repairableThing.HitPoints = repairableThing.RepairableMaxHitPoints();
                repairableThing.ConsumeRepairCost(this.pawn, worktableBuilding.IngredientStackCells, this.CurWork.RepairCost);
                repairableThing.RemoveArmorTaint();
            }
            else if (this.CurWork.RollResult == RollResult.PartialSuccess)
            {
                Utils.DebugLog("repair partially successful p2");
                int hitpointsRecovered = Math.Min(repairableThing.RepairableMaxHitPoints(Settings.PartialSuccessRepairAmount), repairableThing.RepairableMaxHitPoints() - repairableThing.HitPoints);
                float conditionRecovered = hitpointsRecovered / (float)repairableThing.RepairableMaxHitPoints();
                Find.World.GetComponent<QualityDegradation>().Update(repairableThing, conditionRecovered);
                repairableThing.HitPoints += hitpointsRecovered;
                repairableThing.ConsumeRepairCost(this.pawn, worktableBuilding.IngredientStackCells, this.CurWork.RepairCost);
                repairableThing.RemoveArmorTaint();
            }
            else if (this.CurWork.RollResult == RollResult.FailSoft)
            {
                Utils.DebugLog("repair failed soft p2");
                repairableThing.ConsumeRepairCost(this.pawn, worktableBuilding.IngredientStackCells, this.CurWork.RepairCost, amount: 0.5f);
                repairableThing.HitPoints -= (int)Math.Ceiling(.05f * repairableThing.MaxHitPoints);
            }
            else if (this.CurWork.RollResult == RollResult.FailHard)
            {
                Utils.DebugLog("repair failed hard p2");
                repairableThing.ConsumeRepairCost(this.pawn, worktableBuilding.IngredientStackCells, this.CurWork.RepairCost, amount: 0.5f);
                repairableThing.HitPoints -= (int)Math.Ceiling(.1f * repairableThing.MaxHitPoints);
            }
            else
            {
                Utils.DebugLog("repair wasted p2");
            }
        }

        private void FailOnConditions(IBillGiver billGiver)
        {
            this.FailOnDestroyedNullOrForbidden(TI_WORKBENCH);
            this.FailOnBurningImmobile(TI_WORKBENCH);
            this.FailOn(delegate ()
            {
                if (billGiver != null)
                {
                    if (this.job.bill != null && this.job.bill.DeletedOrDereferenced)
                    {
                        return true;
                    }
                    if (!billGiver.CurrentlyUsableForBills())
                    {
                        return true;
                    }
                }
                return false;
            });
        }

        private bool IsValueInRange(float value, float[] range)
        {
            if (range == null || range.Count() != 2)
            {
                Utils.DebugLog($"successChance roll value range checker failed. null? {range == null}, count: {range.Count()}");
                return false;
            }

            return range.First() <= value && value < range.Last();
        }

        private float GetAdjustedWorkAmount(Thing repairableThing, RecipeDef recipeDef)
        {
            float originalWorkAmount = 0f;

            if (!repairableThing.def.AllRecipes.NullOrEmpty() && repairableThing.Stuff != null)
            {
                foreach (var recipe in repairableThing.def.AllRecipes)
                {
                    originalWorkAmount = Math.Max(originalWorkAmount, recipe.WorkAmountTotal(repairableThing.Stuff));
                }

                Utils.DebugLog($"{repairableThing.ThingID} original work amount = {originalWorkAmount}, determined by recipe");
            }
            else if (repairableThing.def.recipeMaker != null && repairableThing.def.recipeMaker.workAmount > 0)
            {
                originalWorkAmount = repairableThing.def.recipeMaker.workAmount;

                Utils.DebugLog($"{repairableThing.ThingID} original work amount = {originalWorkAmount}, determined by recipeMaker");
            }
            else
            {
                float workToMake = repairableThing.GetStatValue(StatDefOf.WorkToMake);
                
                if (workToMake > 0)
                {
                    originalWorkAmount = workToMake;
                    Utils.DebugLog($"{repairableThing.ThingID} original work amount = {originalWorkAmount}, determined by WorkToMake stat");
                }
            }

            float adjustedWorkAmount = originalWorkAmount;

            if (originalWorkAmount <= 0f)
            {
                adjustedWorkAmount = Math.Max(recipeDef.workAmount, 2000f);

                Utils.DebugLog($"{repairableThing.ThingID} work amount = {originalWorkAmount}, invalid... setting to default value = {adjustedWorkAmount}");
            }

            float conditionMultiplier = 1.1f - (repairableThing.HitPoints / (float)repairableThing.RepairableMaxHitPoints());

            adjustedWorkAmount *= conditionMultiplier;

            Utils.DebugLog($"conditionMultiplier={conditionMultiplier}, adjustedWorkAmount={adjustedWorkAmount}");

            float skillMultiplier = 1 - (this.pawn.skills.GetSkill(recipeDef.workSkill).Level * 3.75f / 100f);

            adjustedWorkAmount *= skillMultiplier;

            Utils.DebugLog($"skillMultiplier={skillMultiplier}, adjustedWorkAmount={adjustedWorkAmount}");

            return adjustedWorkAmount;
        }
    }

    internal enum RollResult
    {
        FailHard,
        FailSoft,
        FailNoCost,
        PartialSuccess,
        Successful
    }
}
