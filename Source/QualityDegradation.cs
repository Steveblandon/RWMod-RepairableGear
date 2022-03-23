namespace RepairableGear
{
    using System.Collections.Generic;
    using System.Linq;
    using RimWorld;
    using RimWorld.Planet;
    using Verse;

    public class QualityDegradation : WorldComponent
    {
        private static readonly float[] DegradationRange = new float[] { 0f, 1f };
        private Dictionary<string, float> Cache = new Dictionary<string, float>();
        private static readonly QualityCategory[] qualityCategories = new QualityCategory[]
        {
            QualityCategory.Awful,
            QualityCategory.Poor,
            QualityCategory.Normal,
            QualityCategory.Good,
            QualityCategory.Excellent,
            QualityCategory.Masterwork,
            QualityCategory.Legendary
        };

        public QualityDegradation(World world) : base(world)
        {        
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref this.Cache, nameof(QualityDegradation), LookMode.Value, LookMode.Value);
            Utils.DebugLog($"{nameof(QualityDegradation)} worldComp data saved.");
        }

        public void Update(Thing thing, float amount)
        {
            if (!QualityUtility.TryGetQuality(thing, out QualityCategory currentQuality))
            {
                Utils.DebugLog($"{nameof(QualityDegradation)} no quality found for {thing.ThingID}");
                return;
            }

            float settingsMultiplier = currentQuality > QualityCategory.Excellent ? Settings.MasterQualityDegradationMultiplier : Settings.GenericQualityDegradationMultiplier;
            float degradation = amount * settingsMultiplier;

            if (degradation == 0)
            {
                Utils.DebugLog($"{nameof(QualityDegradation)} no degradation on {thing.ThingID}. Is it disabled?");
                return;
            }

            this.Cache.TryGetValue(thing.ThingID, out float previousTotalDegradation);
            previousTotalDegradation = previousTotalDegradation > 0f ? previousTotalDegradation : DegradationRange.Last();
            float currentTotalDegradation = previousTotalDegradation - degradation;

            if (currentTotalDegradation <= 0f)
            {
                currentTotalDegradation = DegradationRange.Last();

                if (currentQuality != QualityCategory.Awful)
                {
                    string thingLabel = thing.LabelCap;
                    QualityCategory newQuality = qualityCategories[((int)currentQuality)-1];
                    thing.TryGetComp<CompQuality>().SetQuality(newQuality, ArtGenerationContext.Colony);
                    QualityUtility.TryGetQuality(thing, out currentQuality);
                    if (currentQuality == newQuality)
                    {
                        Messages.Message("RG_Messages_QualityDegraded".Translate(thingLabel), thing, MessageTypeDefOf.CautionInput);
                    }
                    else
                    {
                        Utils.DebugLog($"{nameof(QualityDegradation)} quality should have degraded, but qualityComp didn't update.");
                    }
                }
                else
                {
                    Utils.DebugLog($"{nameof(QualityDegradation)} quality degraded, but quality is already at lowest value '{currentQuality}'");
                }
            }

            this.Cache[thing.ThingID] = currentTotalDegradation;
            Utils.DebugLog($"{nameof(QualityDegradation)} '{thing.ThingID}' previous degradation: {previousTotalDegradation}, current: {currentTotalDegradation}, change: {degradation}");
        }

        public float Get(Thing thing)
        {
            this.Cache.TryGetValue(thing.ThingID, out float value);
            return value > 0f ? value : DegradationRange.Last();
        }
    }
}
