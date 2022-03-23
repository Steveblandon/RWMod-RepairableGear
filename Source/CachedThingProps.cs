
namespace RepairableGear
{
    using System.Collections.Generic;
    using Verse;

    public class CachedThingProps
    {
        public CachedThingProps(string id)
        {
            this.Id = id;
        }

        public string Id { get; set; }

        public Settable<bool> IsMaintenanceOnlyThing { get; set; } = new Settable<bool>();

        public Settable<bool> IsArmor { get; set; } = new Settable<bool>();

        public Settable<List<ResearchProjectDef>> RequiredResearch { get; set; } = new Settable<List<ResearchProjectDef>>();

        public Settable<KeyValuePair<float, List<ThingDefCount>>> RepairCost { get; set; } = new Settable<KeyValuePair<float, List<ThingDefCount>>>();
    }
}
