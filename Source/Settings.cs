namespace RepairableGear
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using Verse;

    internal class Settings : ModSettings
    {
        private static DifficultyTracker difficultyTracker = new DifficultyTracker();

        // === BOUNDS ===
        private const float MINIMUM_REPAIR_CHANCE = 0.1f;
        private const float MINIMUM_REPAIR_AMOUNT = 0.05f;
        private const float MINIMUM_IRREPARABLY_DAMAGED_TRESHOLD = 0.12f;

        // === HIDDEN SETTINGS ===
        internal static float SkillExpGainPerTick = 0.05f;
        internal static float SuccessSkillExpBonus = SkillExpGainPerTick * 500;

        // === VISIBLE SETTINGS ===
        internal static bool TaintRemovalDisabled;
        internal static bool RealisticPreIndustrialWeaponMaintenanceDisabled;
        internal static float MinimumRepairChance = MINIMUM_REPAIR_CHANCE;
        internal static float PartialSuccessRepairAmount;
        internal static float IrreparablyDamagedThreshold;
        internal static float CostFreeThreshold;
        internal static float GenericQualityDegradationMultiplier;
        internal static float MasterQualityDegradationMultiplier;
        internal static float RepairChanceMultiplier;

        public Settings() : base()
        {
            AddDifficultyLevelsForSetting(nameof(PartialSuccessRepairAmount), ref PartialSuccessRepairAmount, easy: 0.5f, normal: 0.25f, challenging: 0.15f);
            AddDifficultyLevelsForSetting(nameof(IrreparablyDamagedThreshold), ref IrreparablyDamagedThreshold, easy: 0.12f, normal: 0.25f, challenging: 0.35f);
            AddDifficultyLevelsForSetting(nameof(CostFreeThreshold), ref CostFreeThreshold, easy: 0.9f, normal: 0.95f, challenging: 0.99f);
            AddDifficultyLevelsForSetting(nameof(GenericQualityDegradationMultiplier), ref GenericQualityDegradationMultiplier, easy: 0.2f, normal: 0.5f, challenging: 0.8f);
            AddDifficultyLevelsForSetting(nameof(MasterQualityDegradationMultiplier), ref MasterQualityDegradationMultiplier, easy: 0.1f, normal: 0.25f, challenging: 0.4f);
            AddDifficultyLevelsForSetting(nameof(RepairChanceMultiplier), ref RepairChanceMultiplier, easy: 2.0f, normal: 1.0f, challenging: 1.0f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref TaintRemovalDisabled, nameof(TaintRemovalDisabled), defaultValue: false, forceSave: true);
            Scribe_Values.Look(ref RealisticPreIndustrialWeaponMaintenanceDisabled, nameof(RealisticPreIndustrialWeaponMaintenanceDisabled), defaultValue: false, forceSave: true);
            Scribe_Values.Look(ref MinimumRepairChance, nameof(MinimumRepairChance), defaultValue: MINIMUM_REPAIR_CHANCE, forceSave: true);
            Scribe_Values.Look(ref PartialSuccessRepairAmount, nameof(PartialSuccessRepairAmount), defaultValue: MINIMUM_REPAIR_AMOUNT, forceSave: true);
            Scribe_Values.Look(ref IrreparablyDamagedThreshold, nameof(IrreparablyDamagedThreshold), defaultValue: MINIMUM_IRREPARABLY_DAMAGED_TRESHOLD, forceSave: true);
            Scribe_Values.Look(ref CostFreeThreshold, nameof(CostFreeThreshold), defaultValue: MINIMUM_IRREPARABLY_DAMAGED_TRESHOLD, forceSave: true);
            Scribe_Values.Look(ref GenericQualityDegradationMultiplier, nameof(GenericQualityDegradationMultiplier), defaultValue: difficultyTracker.GetValue(nameof(GenericQualityDegradationMultiplier)), forceSave: true);
            Scribe_Values.Look(ref MasterQualityDegradationMultiplier, nameof(MasterQualityDegradationMultiplier), defaultValue: difficultyTracker.GetValue(nameof(MasterQualityDegradationMultiplier)), forceSave: true);
            Scribe_Values.Look(ref RepairChanceMultiplier, nameof(RepairChanceMultiplier), defaultValue: difficultyTracker.GetValue(nameof(RepairChanceMultiplier)), forceSave: true);
        }

        public static void DoSettingsWindowContents(Rect rect)
        {
            Listing_Standard listing_Standard = new Listing_Standard(GameFont.Small)
            {
                ColumnWidth = rect.width
            };
            listing_Standard.Begin(rect);

            listing_Standard.CheckboxLabeled("RG_Settings_TaintRemoval".Translate(), ref TaintRemovalDisabled);

            listing_Standard.Gap(6f);

            listing_Standard.CheckboxLabeled("RG_Settings_RealisticPreIndustrialWeaponMaintenanceDisabled".Translate(), 
                ref RealisticPreIndustrialWeaponMaintenanceDisabled,
                tooltip: "RG_Settings_RealisticPreIndustrialWeaponMaintenanceDisabled_Tooltip".Translate());

            listing_Standard.Gap(6f);

            listing_Standard.Label($"RG_Settings_IrreparablyDamagedThreshold".Translate(int.Parse((Math.Round(IrreparablyDamagedThreshold * 100)).ToString())),
                tooltip: "RG_Settings_IrreparablyDamagedThreshold_Tooltip".Translate());
            IrreparablyDamagedThreshold = listing_Standard.Slider(IrreparablyDamagedThreshold, MINIMUM_IRREPARABLY_DAMAGED_TRESHOLD, 1f);

            listing_Standard.Gap(6f);

            listing_Standard.Label($"RG_Settings_CostFreeThreshold".Translate(int.Parse((Math.Round(CostFreeThreshold * 100)).ToString())),
                tooltip: "RG_Settings_CostFreeThreshold_Tooltip".Translate());
            CostFreeThreshold = listing_Standard.Slider(CostFreeThreshold, MINIMUM_IRREPARABLY_DAMAGED_TRESHOLD, 1f);

            listing_Standard.Gap(6f);

            listing_Standard.Label($"RG_Settings_PartialSuccessRepairAmount".Translate(int.Parse((Math.Round(PartialSuccessRepairAmount * 100)).ToString())),
                tooltip: "RG_Settings_PartialSuccessRepairAmount_Tooltip".Translate());
            PartialSuccessRepairAmount = listing_Standard.Slider(PartialSuccessRepairAmount, MINIMUM_REPAIR_AMOUNT, 1f);

            listing_Standard.Gap(6f);

            listing_Standard.Label($"RG_Settings_QualityDegradation".Translate(int.Parse((Math.Round(GenericQualityDegradationMultiplier * 100)).ToString())),
                tooltip: "RG_Settings_QualityDegradation_Tooltip".Translate());
            GenericQualityDegradationMultiplier = listing_Standard.Slider(GenericQualityDegradationMultiplier, 0f, 1f);

            listing_Standard.Gap(6f);

            listing_Standard.Label($"RG_Settings_QualityDegradation_Master".Translate(int.Parse((Math.Round(MasterQualityDegradationMultiplier * 100)).ToString())),
                tooltip: "RG_Settings_QualityDegradation_Tooltip".Translate());
            MasterQualityDegradationMultiplier = listing_Standard.Slider(MasterQualityDegradationMultiplier, 0f, 1f);

            listing_Standard.Gap(6f);

            listing_Standard.Label($"RG_Settings_MinimumChance".Translate(int.Parse((Math.Round(MinimumRepairChance * 100)).ToString())),
                tooltip: "RG_Settings_MinimumChance_Tooltip".Translate());
            MinimumRepairChance = listing_Standard.Slider(MinimumRepairChance, MINIMUM_REPAIR_CHANCE, 1f);

            listing_Standard.Gap(6f);

            listing_Standard.Label($"RG_Settings_RepairChance".Translate(string.Format("{0:0.0}", RepairChanceMultiplier)).ToString(),
                tooltip: "RG_Settings_RepairChance_Tooltip".Translate());
            RepairChanceMultiplier = listing_Standard.Slider(RepairChanceMultiplier, 0.1f, 10f);

            listing_Standard.Gap(6f);

            if (listing_Standard.ButtonText("RG_DifficultyEasy".Translate()))
            {
                ConfigureDifficulty(DifficultyLevel.Easy);
            }
            if (listing_Standard.ButtonText("RG_DifficultyNormal".Translate()))
            {
                ConfigureDifficulty(DifficultyLevel.Normal);
            }
            if (listing_Standard.ButtonText("RG_DifficultyChallenging".Translate()))
            {
                ConfigureDifficulty(DifficultyLevel.Challenging);
            }

            listing_Standard.End();
        }



        private static void ConfigureDifficulty(DifficultyLevel difficultyLevel)
        {
            PartialSuccessRepairAmount = difficultyTracker.GetValue(nameof(PartialSuccessRepairAmount), difficultyLevel);
            IrreparablyDamagedThreshold = difficultyTracker.GetValue(nameof(IrreparablyDamagedThreshold), difficultyLevel);
            CostFreeThreshold = difficultyTracker.GetValue(nameof(CostFreeThreshold), difficultyLevel);
            GenericQualityDegradationMultiplier = difficultyTracker.GetValue(nameof(GenericQualityDegradationMultiplier), difficultyLevel);
            MasterQualityDegradationMultiplier = difficultyTracker.GetValue(nameof(MasterQualityDegradationMultiplier), difficultyLevel);
            RepairChanceMultiplier = difficultyTracker.GetValue(nameof(RepairChanceMultiplier), difficultyLevel);
        }

        private static void AddDifficultyLevelsForSetting(string settingName, ref float setting, float easy, float normal, float challenging)
        {
            if (difficultyTracker.GetValue(settingName, DifficultyLevel.Normal) == 0f)
            {
                difficultyTracker.AddDifficultyLevelsForSetting(settingName, easy, normal, challenging);
            }

            if (setting == 0f)
            {
                setting = difficultyTracker.GetValue(settingName, DifficultyLevel.Normal);
            }
        }

        private class DifficultyTracker
        {
            private Dictionary<string, Dictionary<DifficultyLevel, float>> Cache;

            public DifficultyTracker()
            {
                this.Cache = new Dictionary<string, Dictionary<DifficultyLevel, float>>();
            }

            public float GetValue(string settingName, DifficultyLevel difficultyLevel = DifficultyLevel.Normal)
            {
                this.Cache.TryGetValue(settingName, out Dictionary<DifficultyLevel, float> difficulty);

                if (difficulty.EnumerableNullOrEmpty())
                {
                    difficulty = new Dictionary<DifficultyLevel, float>(0);
                }

                return difficulty.TryGetValue(difficultyLevel, 0f);
            }

            public void AddDifficultyLevelsForSetting(string settingName, float easy, float normal, float challenging)
            {
                this.Cache[settingName] = new Dictionary<DifficultyLevel, float>()
                {
                    { DifficultyLevel.Easy,  easy},
                    { DifficultyLevel.Normal,  normal},
                    { DifficultyLevel.Challenging,  challenging},
                };
            }
        }

        private enum DifficultyLevel
        {
            Easy,
            Normal,
            Challenging
        }
    }
}

