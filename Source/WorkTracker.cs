namespace RepairableGear
{
    using System.Collections.Generic;
    using Verse;

    internal class WorkTracker: IExposable
    {
        public string WorkerId { get { return this._workerId; } set { this._workerId = value; } }

        public float TotalWork { get { return this._totalWork; } set { this._totalWork = value; } }

        public float WorkLeft { get { return this._workLeft; } set { this._workLeft = value; } }

        public RollResult RollResult { get { return this._rollResult; } set { this._rollResult = value; } }

        public float TickToCheckWorkSuccessChance { get { return this._ticksToCheckWorkSuccessChance; } set { this._ticksToCheckWorkSuccessChance = value; } }

        public string ResultNotificationLabel { get { return this._resultnotificationLabel; } set { this._resultnotificationLabel = value; } }

        public List<ThingDefCount> RepairCost { get; set; }

        private string _workerId;
        private float _totalWork;
        private float _workLeft;
        private RollResult _rollResult;
        private float _ticksToCheckWorkSuccessChance;
        private string _resultnotificationLabel;

        public void ExposeData()
        {
            Scribe_Values.Look(ref _workerId, nameof(WorkerId), defaultValue: default(string));
            Scribe_Values.Look(ref _totalWork, nameof(TotalWork), defaultValue: default(float));
            Scribe_Values.Look(ref _workLeft, nameof(WorkLeft), defaultValue: default(float));
            Scribe_Values.Look(ref _rollResult, nameof(RollResult), defaultValue: default(RollResult));
            Scribe_Values.Look(ref _ticksToCheckWorkSuccessChance, nameof(TickToCheckWorkSuccessChance), defaultValue: default(float));
            Scribe_Values.Look(ref _resultnotificationLabel, nameof(ResultNotificationLabel), defaultValue: default(string));
        }
    }
}
