namespace RepairableGear
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using RimWorld;
    using Verse;
    using Verse.AI;

    public static partial class ThingExtensions
    {
        private static readonly float[] RepairableConditionRange = { 0.25f, 1f };
        private static float[] MaintenanceOnlyConditionRange = { 0.75f, 1f };
        private static readonly FieldInfo ApparelTainted = typeof(Apparel).GetField("wornByCorpseInt",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static bool IsRepairable(this Thing thing)
        {
            //check quality, thingCategories, stuff, defName
            List<ThingDefCount> repairCost = thing.GetRepairCostList();
            bool hasQuality = thing.TryGetQuality(out QualityCategory quality);

            if (!hasQuality
                || !thing.def.IsAllowed()
                || (!repairCost.NullOrEmpty() && repairCost.Count == 1 && !repairCost.First().ThingDef.IsAllowed()))
            {
                Utils.DebugLog($"{thing.def.defName} not allowed. hasQuality? {hasQuality}; things:{Utils.Stringify(thing.def.thingCategories)}; stuff:{Utils.Stringify(thing.def.stuffCategories)}");
                return false;
            }

            return thing.def.category == ThingCategory.Item;
        }

        public static bool IsRepairable(this Thing thing, Pawn pawn, Thing bench = null, Bill bill = null, bool checkMinimumRepairChance = false)
        {
            if (!thing.Spawned
                || thing.IsForbidden(pawn)
                || thing.IsBurning()
                || thing.HitPoints < thing.RepairableMinimumHitPoints()
                || thing.HitPoints >= thing.RepairableMaxHitPoints()
                || !thing.IsRepairable()
                || !pawn.CanReserve(thing))
            {
                return false;
            }
            else if (checkMinimumRepairChance && !pawn.RepairChanceAllowed(thing))
            {
                return false;
            }
            else if (bench != null && bill != null)
            {
                IntVec3 benchDistanceFromThing = thing.Position - bench.Position;
                if (!thing.Position.InHorDistOf(bench.Position, bill.ingredientSearchRadius) || !bill.ingredientFilter.Allows(thing))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsMaintenanceOnlyThing(this Thing thing)
        {
            if (Settings.RealisticPreIndustrialWeaponMaintenanceDisabled)
            {
                return false;
            }

            CachedThingProps cachedThingProps = ThingCache.GetOrAdd(thing.ThingID);
            if (cachedThingProps.IsMaintenanceOnlyThing.IsSet)
            {
                Utils.DebugLog($"Hit cache for maintenanceOnlyThing ID: {thing.ThingID}");
                return cachedThingProps.IsMaintenanceOnlyThing.Value;
            }
            else
            {
                bool maintenanceOnlyThing = thing.def.IsMeleeWeapon;

                if (!maintenanceOnlyThing && !thing.def.weaponTags.NullOrEmpty())
                {
                    foreach (string tagPart in MaintenanceOnlyThingTagParts)
                    {
                        if (thing.def.weaponTags.ContainsHint(tagPart))
                        {
                            maintenanceOnlyThing = true;
                            break;
                        }
                    }
                }

                maintenanceOnlyThing = maintenanceOnlyThing && (int)thing.def.techLevel < (int)TechLevel.Industrial;
                cachedThingProps.IsMaintenanceOnlyThing = new Settable<bool>(maintenanceOnlyThing);
                return maintenanceOnlyThing;
            }
        }

        /// <summary>
        /// Uses template matching to arrive to a probabilistic conclusion if something is armor.
        /// </summary>
        /// <param name="thing">The thing.</param>
        /// <returns>bool</returns>
        public static bool IsArmor(this Thing thing)
        {
            CachedThingProps cachedThingProps = ThingCache.GetOrAdd(thing.ThingID);
            if (cachedThingProps.IsArmor.IsSet)
            {
                Utils.DebugLog($"Hit cache for isArmor ID: {thing.ThingID}");
                return cachedThingProps.IsArmor.Value;
            }
            else
            {
                int totalChecks = 0;
                int successfulCheckCount = 0;

                // apparel check
                totalChecks++;
                successfulCheckCount += (thing as Apparel) != null ? 1 : 0;
                Utils.DebugLog($"IsArmor? {successfulCheckCount}/{totalChecks}");

                // defName check
                totalChecks++;
                successfulCheckCount += thing.def.defName.ContainsHint(ArmorHints) ? 1 : 0;
                Utils.DebugLog($"IsArmor? {successfulCheckCount}/{totalChecks}");

                // thingCategories check
                if (!thing.def.thingCategories.NullOrEmpty())
                {
                    totalChecks++;
                    foreach (ThingCategoryDef categoryDef in thing.def.thingCategories)
                    {
                        if (categoryDef.defName.ContainsHint(ArmorHints))
                        {
                            successfulCheckCount++;
                            break;
                        }
                    }
                }
                Utils.DebugLog($"IsArmor? {successfulCheckCount}/{totalChecks}");

                // tradeTags check
                if (!thing.def.tradeTags.NullOrEmpty())
                {
                    totalChecks++;
                    foreach (string tradeTag in thing.def.tradeTags)
                    {
                        if (tradeTag.ContainsHint(ArmorHints))
                        {
                            successfulCheckCount++;
                            break;
                        }
                    }
                }
                Utils.DebugLog($"IsArmor? {successfulCheckCount}/{totalChecks}");

                bool isArmor = successfulCheckCount / (float)totalChecks >= 0.5f;
                cachedThingProps.IsArmor = new Settable<bool>(isArmor);
                return isArmor;
            }
        }

        public static bool CanBeMaintenanced(this Thing thing)
        {
            float condition = thing.HitPoints / (float)thing.RepairableMaxHitPoints();

            if (condition >= Settings.CostFreeThreshold
                || (thing.IsMaintenanceOnlyThing() && thing.HitPoints >= thing.RepairableMinimumHitPoints()))
            {
                return true;
            }

            return false;
        }

        public static int RepairableMinimumHitPoints(this Thing thing)
        {
            float[] range = RepairableConditionRange;
            range[0] = Math.Min(Settings.IrreparablyDamagedThreshold, range.Last());

            if (thing.IsMaintenanceOnlyThing())
            {
                range = MaintenanceOnlyConditionRange;
            }

            return (int)Math.Max(Math.Round(range.First() * thing.MaxHitPoints), 0);
        }

        public static int RepairableMaxHitPoints(this Thing thing, float multiplier = 0f)
        {
            float[] range = RepairableConditionRange;
            if (thing.IsMaintenanceOnlyThing())
            {
                range = MaintenanceOnlyConditionRange;
            }

            return (int)Math.Min(Math.Round((multiplier > 0f ? multiplier : range.Last()) * thing.MaxHitPoints), thing.MaxHitPoints);
        }

        public static List<ThingDefCount> GetRepairCostList(this Thing thing)
        {
            float condition = thing.HitPoints / (float)thing.MaxHitPoints;
            List<ThingDefCount> repairCost = new List<ThingDefCount>();

            CachedThingProps cachedThingProps = ThingCache.GetOrAdd(thing.ThingID);
            if (cachedThingProps.RepairCost.IsSet && cachedThingProps.RepairCost.Value.Key == condition)
            {
                Utils.DebugLog($"Hit cache for repairCost ID: {thing.ThingID}");
                return cachedThingProps.RepairCost.Value.Value;
            }
            else
            {
                cachedThingProps.RepairCost = new Settable<KeyValuePair<float, List<ThingDefCount>>>(
                    new KeyValuePair<float, List<ThingDefCount>>(condition, repairCost));
            }

            List<ThingDefCountClass> fullCost = thing.CostListAdjusted();

            if (thing.CanBeMaintenanced() || fullCost.NullOrEmpty())
            {
                return repairCost;
            }

            float itemConditionCostFactor = 1f - condition;

            foreach (var thingCount in fullCost)
            {
                int adjustedCount = (int)Math.Round(thingCount.count * itemConditionCostFactor);


                if (adjustedCount > 0)
                {
                    Utils.DebugLog($"repairCost for {thing.Label} : {thingCount.Label}, adjustedCount={adjustedCount}, preFlooring={thingCount.count * itemConditionCostFactor}");
                    repairCost.Add(new ThingDefCount(thingCount.thingDef, adjustedCount));
                }
            }

            return repairCost;
        }

        public static void ConsumeRepairCost(
            this Thing thing,
            Pawn pawn,
            IEnumerable<IntVec3> ingredientStackCells,
            List<ThingDefCount> repairCost,
            float amount = 1f)
        {
            if (0 > amount || amount > 1f)
            {
                Utils.DebugLog($"asked to consume more or less than there is... normalizing.");
                amount = 1f;
            }
            List<Thing> ingredientThings = ingredientStackCells.SelectMany(spot => pawn.Map.thingGrid.ThingsListAt(spot)).ToList();
            ISet<string> repairCostThingLabels = new HashSet<string>(repairCost.Select(t => t.ThingDef.label));

            foreach (Thing t in ingredientThings)
            {
                if (repairCostThingLabels.Contains(t.def.label))
                {
                    int amountToRemove = (int)Math.Round(t.stackCount * amount);

                    if (amountToRemove >= t.stackCount)
                    {
                        t.DeSpawn();
                        t.Destroy();
                    }
                    else
                    {
                        t.stackCount -= amountToRemove;
                    }
                }
            }
        }

        public static void RemoveArmorTaint(this Thing thing)
        {
            Apparel apparel = thing as Apparel;
            if (!Settings.TaintRemovalDisabled && apparel != null && thing.IsArmor())
            {
                ApparelTainted.SetValue(apparel, false);
            }
        }

        private static bool ContainsHint(this List<string> strs, string hint)
        {
            foreach (string str in strs)
            {
                if (str.Contains(hint))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsHint(this string str, IEnumerable<string> hintSource)
        {
            foreach (string hint in hintSource)
            {
                if (str.Contains(hint))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowed(this ThingDef thingDef)
        {
            if (!thingDef.thingCategories.NullOrEmpty())
            {
                foreach (ThingCategoryDef thingCategory in thingDef.thingCategories)
                {
                    if (Disallowed.Contains(thingCategory.defName))
                    {
                        return false;
                    }
                }
            }

            if (!thingDef.stuffCategories.NullOrEmpty() && thingDef.stuffCategories.Count == 1)
            {
                if (Disallowed.Contains(thingDef.stuffCategories.First().defName))
                {
                    return false;
                }
            }

            if (thingDef.stuffProps != null && !thingDef.stuffProps.categories.NullOrEmpty())
            {
                foreach (StuffCategoryDef stuffCategory in thingDef.stuffProps.categories)
                {
                    if (Disallowed.Contains(stuffCategory.defName))
                    {
                        return false;
                    }
                }
            }

            return !Disallowed.Contains(thingDef.defName);
        }

        #region filters
        private static readonly IEnumerable<string> ArmorHints = new List<string>()
        {
            "Armor",
            "armor",
            "Armour",
            "armour",
        };

        private static readonly IEnumerable<string> MaintenanceOnlyThingTagParts = new List<string>()
        {
            "Neolithic",
            "MedievalMelee",
        };

        // Disallowed list recognizes thingCategories, stuffCategories, and defNames
        private static readonly ISet<string> Disallowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Grenades",
            // things made out of the following are most likely neolithic or medieval, covered by maintenance only mechanism
            // "Woody",
            //"WoodLog",
            //"Stony",
            //"StoneBlocks",
        };
        #endregion
    }
}
