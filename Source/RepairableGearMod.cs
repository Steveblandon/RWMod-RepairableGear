
namespace RepairableGear
{
    using UnityEngine;
    using Verse;

    public class RepairableGearMod : Mod
    {
        public RepairableGearMod(ModContentPack content)
            : base(content)
        {
            base.GetSettings<Settings>();
            Log.Message($"{Constants.MOD_NAME} :: initialized");
        }

        public override string SettingsCategory()
        {
            return Constants.MOD_NAME;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoSettingsWindowContents(inRect);
        }
    }
}