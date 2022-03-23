
namespace RepairableGear
{
    using System.Collections.Generic;
    using Verse;

    public class CompProperties_RepairableThing : CompProperties
    {
        public CompProperties_RepairableThing()
        {
            this.compClass = typeof(CompRepairableThing);
        }

        public List<ThingDef> WorktableDefs = new List<ThingDef>();

        public WorkTypeDef WorkTypeDef;

        public JobDef JobDef;
    }
}
