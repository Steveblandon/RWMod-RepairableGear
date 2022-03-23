namespace RepairableGear
{
    using System;
    using System.Collections.Generic;
    using RimWorld;
    using Verse;

    public static partial class PawnExtensions
    {
        public static bool RepairChanceAllowed(this Pawn pawn, Thing item)
        {
            float adjustedRepairChance = pawn.GetAdjustedRepairChance(item);
            //Log.Message($"Checking repair chance... {adjustedRepairChance} >= {Settings.MinimumRepairChance}?");
            return adjustedRepairChance >= Settings.MinimumRepairChance;
        }

        public static int GetAdjustedRepairChanceToDisplay(this Pawn pawn, Thing repairableThing, float adjustedSuccessChance = -1f)
        {
            if (adjustedSuccessChance <= -1f)
            {
                adjustedSuccessChance = pawn.GetAdjustedRepairChance(repairableThing);
            }

            return (int)Math.Round(adjustedSuccessChance * 100);
        }

        public static float GetAdjustedRepairChance(this Pawn pawn, Thing repairableThing)
        {
            float baseValue = pawn.GetStatValue(DefDatabase<StatDef>.GetNamed(Constants.STATDEF_REPAIR_SUCCESS));
            float conditionMultiplier = Math.Min(repairableThing.HitPoints / (float)repairableThing.MaxHitPoints, 1f);
            float qualityMultiplier = GetRepairChanceQualityMultiplier(repairableThing);
            float researchMultiplier = GetRepairChanceResearchMultiplier(repairableThing);
            float techLevelMultiplier = GetRepairChanceTechLevelBasedMultiplier(repairableThing, pawn);

            // if techLevel of thing is higher, but item has been researched, make sure there is no penalty
            techLevelMultiplier = researchMultiplier >= 1f ? 1f : techLevelMultiplier;

            // ignore research level multiplier if just doing simple maintenance
            researchMultiplier = repairableThing.CanBeMaintenanced() ? 1f : researchMultiplier;

            float adjustedValue = baseValue * conditionMultiplier * qualityMultiplier * researchMultiplier * techLevelMultiplier * Settings.RepairChanceMultiplier;

            Utils.DebugLog($"thing repair chance = {baseValue} * {conditionMultiplier} * {qualityMultiplier} * {researchMultiplier} * {techLevelMultiplier} = {adjustedValue}");
            return adjustedValue;
        }

        private static float GetRepairChanceQualityMultiplier(Thing repairableThing)
        {
            float qualityMultiplier = 1f;

            if (repairableThing.TryGetQuality(out QualityCategory quality))
            {
                switch (quality)
                {
                /* these values are balanced on having a lvl 20 crafter having at least 70% chance to repair a legendary item at 50% condition.
                 * each preceeding quality has a designated crafting lvl to achieve the same until lvl 9.
                 * at lvl 9, a solid professional has at least 70% chance to repair a poor item at 50% condition.
                 * these values are also balanced on base success chance, so if gain per level in repair item success chance is changed, it will
                 * throw off this balancing.
                */
                    case QualityCategory.Awful:
                        qualityMultiplier = 1.8f;
                        break;
                    case QualityCategory.Poor:
                        qualityMultiplier = 1.5f;
                        break;
                    case QualityCategory.Normal:
                        qualityMultiplier = 1.3f;
                        break;
                    case QualityCategory.Good:
                        qualityMultiplier = 1.1f;
                        break;
                    case QualityCategory.Excellent:
                        qualityMultiplier = 1f;
                        break;
                    case QualityCategory.Masterwork:
                        qualityMultiplier = 0.9f;
                        break;
                    case QualityCategory.Legendary:
                        qualityMultiplier = 0.8f;
                        break;
                }
            }

            return qualityMultiplier;
        }

        private static float GetRepairChanceResearchMultiplier(Thing repairableThing)
        {
            float techMultiplier = 1f;

            List<RecipeDef> recipes = repairableThing.def.AllRecipes;
            RecipeMakerProperties recipeMakerProps = repairableThing.def.recipeMaker;
            List<ResearchProjectDef> requiredResearch = null;

            CachedThingProps cachedThingProps = ThingCache.GetOrAdd(repairableThing.ThingID);
            if (cachedThingProps.RequiredResearch.IsSet)
            {
                Utils.DebugLog($"Hit cache for required research ID: {repairableThing.ThingID}");
                requiredResearch = cachedThingProps.RequiredResearch.Value;
            }
            else
            {
                cachedThingProps.RequiredResearch = new Settable<List<ResearchProjectDef>>(new List<ResearchProjectDef>());
                requiredResearch = cachedThingProps.RequiredResearch.Value;

                if (recipeMakerProps != null)
                {
                    if (recipeMakerProps.researchPrerequisites != null)
                    {
                        requiredResearch.AddRange(recipeMakerProps.researchPrerequisites);
                    }

                    if (recipeMakerProps.researchPrerequisite != null)
                    {
                        requiredResearch.Add(recipeMakerProps.researchPrerequisite);
                    }
                }

                if (!recipes.NullOrEmpty())
                {
                    foreach (RecipeDef recipe in recipes)
                    {
                        if (recipe.researchPrerequisites != null)
                        {
                            requiredResearch.AddRange(recipe.researchPrerequisites);
                        }

                        if (recipe.researchPrerequisite != null)
                        {
                            requiredResearch.Add(recipe.researchPrerequisite);
                        }
                    }
                }
            }

            if (!requiredResearch.NullOrEmpty())
            {
                foreach (ResearchProjectDef researchProject in requiredResearch)
                {
                    if (!researchProject.IsFinished)
                    {
                        techMultiplier = 0.5f;
                    }
                }
            }

            // if item is not craftable, research penalty still applies
            if (recipes.NullOrEmpty() && requiredResearch.NullOrEmpty() && (recipeMakerProps == null || recipeMakerProps.recipeUsers.NullOrEmpty()))
            {
                techMultiplier = 0.5f;
            }

            Utils.DebugLog($"recipeMaker null? {recipeMakerProps == null}, recipeUsers empty? {recipeMakerProps?.recipeUsers.NullOrEmpty()} recipes empty? {recipes.NullOrEmpty()}, requiredResearch empty? {requiredResearch.NullOrEmpty()}");

            return techMultiplier;
        }

        private static float GetRepairChanceTechLevelBasedMultiplier(Thing repairableThing, Pawn pawn)
        {
            int techlevelDiff = (int)repairableThing.def.techLevel - (int)pawn.Faction.def.techLevel;

            if (techlevelDiff <= 0)
            {
                return 1f;
            }
            else if (techlevelDiff == 1)
            {
                return 0.5f;
            }
            else
            {
                return 0.1f;
            }
        }
    }
}
