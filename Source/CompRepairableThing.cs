namespace RepairableGear
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using RimWorld;
    using Verse;
    using Verse.AI;

    public class CompRepairableThing : ThingComp
    {
        public CompProperties_RepairableThing Props
        {
            get
            {
                return (CompProperties_RepairableThing)this.props;
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption preexistingMenuOption in base.CompFloatMenuOptions(selPawn))
            {
                Utils.DebugLog($"adding floatMenuOption from base: {preexistingMenuOption.Label}");
                yield return preexistingMenuOption;
            }

            Utils.DebugLog($"right-clicked RepairableThing with Comp: {this.parent.Label}, ID: {this.parent.ThingID}");

            if (this.parent.HitPoints >= this.parent.RepairableMaxHitPoints() || selPawn.drafter.Drafted)
            {
                yield break;
            }

            Building workbench = null;
            bool maintenanceOnly = false;
            bool repairAffordable = true;
            List<ThingDefCount> expectedRepairCost = null;
            List<ThingCount> foundIngredients = new List<ThingCount>();
            string divider = ": ";
            string unrepairablePrefix = "RG_RepairItem_NotPossible".Translate(this.parent.LabelShort) + divider;
            StringBuilder label = new StringBuilder();
            bool repairable = this.parent.IsRepairable(selPawn);
            bool assignedToWorkType = selPawn.workSettings.GetPriority(this.Props.WorkTypeDef) > 0;

            if (!repairable || !assignedToWorkType)
            {
                label.Append(unrepairablePrefix);

                if (!selPawn.CanReserve(this.parent))
                {
                    label.Append("CannotUseReserved".Translate());
                }
                else if (!selPawn.CanReach(this.parent, PathEndMode.Touch, Danger.Deadly))
                {
                    label.Append("CannotUseNoPath".Translate());
                }
                else if (!assignedToWorkType)
                {
                    label.Append("RG_RepairItem_NotAssignedToWorkType".Translate(this.Props.WorkTypeDef.gerundLabel));
                }
                else if (this.parent.HitPoints < this.parent.RepairableMinimumHitPoints())
                {
                    label.Append("RG_RepairItem_TooDamaged".Translate());
                }
                else if (this.parent.IsForbidden(selPawn) || this.parent.IsBurning())
                {
                    label.Append("ForbiddenLower".Translate());
                }
            }
            else
            {
                workbench = this.GetClosestWorkbench(selPawn);

                if (workbench == null)
                {
                    label.Append(unrepairablePrefix).Append("RG_RepairItem_NoRepairTableFound".Translate());
                }
                else
                {
                    maintenanceOnly = this.parent.CanBeMaintenanced();

                    if (!maintenanceOnly)
                    {
                        expectedRepairCost = this.parent.GetRepairCostList();

                        if (expectedRepairCost != null && expectedRepairCost.Any())
                        {
                            repairAffordable = WorkGiver_RepairItem.TryFindBestIngredients(selPawn, workbench, this.parent, foundIngredients, repairCost: expectedRepairCost);
                        }
                        else
                        {
                            Utils.DebugLog($"{this.parent.Label} can't be maintenanced and no repair cost found, most likely an uncraftable... no message hint exists. Exiting gracefully.");
                            yield break;
                        }

                        if (!repairAffordable)
                        {
                            label.Append(unrepairablePrefix).Append("RG_RepairItem_MissingMaterials".Translate()).Append($" ({GetMaterialsString(expectedRepairCost)})");
                        }
                    }
                }
            }

            FloatMenuOption repairItemMenuOption = null;

            if (label.Length == 0)
            {
                if (!repairable)
                {
                    Utils.DebugLog($"{this.parent.Label} is not repairable... no message hint exists. Exiting gracefully.");
                    yield break; //catchall; break if it can't be repaired for any other reason
                }
                else if (repairable && workbench != null)
                {
                    label.Append("RG_RepairItem".Translate(this.parent.LabelShort)).Append(divider);

                    if (maintenanceOnly)
                    {
                        label.Append("RG_RepairItem_Maintenance".Translate());
                    }
                    else if (foundIngredients.Any())
                    {
                        label.Append($"{GetMaterialsString(foundIngredients)}");
                    }

                    label.Append($" ({"RG_RepairItem_Chance".Translate(selPawn.GetAdjustedRepairChanceToDisplay(this.parent))})");
                    Action action = () => this.TryGiveRepairJobToPawn(selPawn, workbench, foundIngredients);
                    repairItemMenuOption = new FloatMenuOption(label.ToString(), action);
                }
            }
            else
            {
                repairItemMenuOption = new FloatMenuOption(label.ToString(), null);
            }

            yield return FloatMenuUtility.DecoratePrioritizedTask(repairItemMenuOption, selPawn, this.parent);
            yield break;
        }

        private bool TryGiveRepairJobToPawn(Pawn pawn, Building workbench, List<ThingCount> repairCost)
        {
            Job job = WorkGiver_RepairItem.CreateRepairJob(workbench, this.parent, repairCost, jobDef: this.Props.JobDef);
            return pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private Building GetClosestWorkbench(Pawn pawn)
        {
            List<Building> validWorkBenches = this.GetValidWorkBenches(pawn);

            if (validWorkBenches.NullOrEmpty())
            {
                return null;
            }

            Building workBench = null;
            double closestDistanceFromPawn = double.MaxValue;

            foreach (Building building in validWorkBenches)
            {
                double distanceFromPawn = Utils.GetDistanceSquared(pawn.Position, building.Position);
                if (distanceFromPawn < closestDistanceFromPawn)
                {
                    workBench = building;
                    closestDistanceFromPawn = distanceFromPawn;
                }
            }

            return workBench;
        }

        private List<Building> GetValidWorkBenches(Pawn pawn)
        {
            if (!this.parent.Spawned || this.parent.Map == null || !pawn.Spawned || pawn.Map == null)
            {
                return null;
            }

            Map map = pawn.Map;
            List<Building> list = new List<Building>();

            foreach (ThingDef def in this.Props.WorktableDefs)
            {
                IEnumerable<Building> enumerable = map.listerBuildings.AllBuildingsColonistOfDef(def);
                foreach (Building building in enumerable)
                {
                    if (building.Spawned
                        && !building.IsBurning()
                        && !building.IsBrokenDown()
                        && !building.IsForbidden(pawn)
                        && !building.IsDangerousFor(pawn)
                        && map.reservationManager.CanReserve(pawn, building, 1, -1, null, false))
                    {
                        list.Add(building);
                    }
                }
            }
            return list;
        }

        private string GetMaterialsString(List<ThingCount> thingCounts)
        {
            return GetMaterialsString(thingCounts, thingCount => thingCount.Thing.LabelCapNoCount, thingCount => thingCount.Count);
        }

        private string GetMaterialsString(List<ThingDefCount> thingCounts)
        {
            return GetMaterialsString(
                thingCounts,
                (thingCount) =>
                {
                    int spaceIndex = thingCount.LabelCap.LastIndexOf(" ");
                    return thingCount.LabelCap.Substring(0, spaceIndex > -1 ? spaceIndex : thingCount.LabelCap.Length);
                },
                thingCount => thingCount.Count);
        }

        private string GetMaterialsString<T>(List<T> thingCounts, Func<T, string> label, Func<T, int> count)
        {
            // remove material duplicates first
            IDictionary<string, int> labelToCountPairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (thingCounts.NullOrEmpty())
            {
                return string.Empty;
            }

            foreach (T item in thingCounts)
            {
                labelToCountPairs[label(item)] = labelToCountPairs.TryGetValue(label(item), 0) + count(item);
            }

            StringBuilder materials = new StringBuilder();

            // then format the actual string
            foreach (var pair in labelToCountPairs)
            {
                if (materials.Length > 0)
                {
                    materials.Append(", ");
                }

                string itemLabel = pair.Key;
                int itemCount = pair.Value;

                materials.AppendFormat($"{itemCount}x {itemLabel}");
            }

            return materials.ToString();
        }
    }
}
