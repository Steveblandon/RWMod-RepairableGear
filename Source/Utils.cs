

namespace RepairableGear
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using RimWorld;
    using Verse;

    internal static class Utils
    {
        public static string Stringify(float[] arr)
        {
            return Stringify<float>(new List<float>(arr), v => v.ToString());
        }
        public static string Stringify(List<StuffCategoryDef> defs)
        {
            return Stringify(defs, def => def.defName);
        }

        public static string Stringify(List<ThingCategoryDef> defs)
        {
            return Stringify(defs, def => def.defName);
        }

        public static string Stringify<T>(List<T> items, Func<T, string> itemLabel)
        {
            if (items.NullOrEmpty())
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("{ ");

            foreach (T value in items)
            {
                if (builder.Length > 2)
                {
                    builder.Append(", ");
                }
                builder.Append(itemLabel(value));
            }

            builder.Append(" }");

            return builder.ToString();
        }

        public static double GetDistanceSquared(IntVec3 p1, IntVec3 p2)
        {
            int num = Math.Abs(p1.x - p2.x);
            int num2 = Math.Abs(p1.y - p2.y);
            int num3 = Math.Abs(p1.z - p2.z);
            return (double)(num * num + num2 * num2 + num3 * num3);
        }

        public static void DebugLog(string message)
        {
            if (DebugSettings.godMode)
            {
                string log = $"{Constants.MOD_NAME}:: {message}";

                // don't spam
                if (!LogTracker.Contains(log))
                {
                    Log.Message(log);
                    LogTracker.Add(log);
                }
            }
        }

        private static class LogTracker
        {
            private static int LogTrailMax = 7;
            private static Queue<string> LogTrail = new Queue<string>();
            private static ISet<string> LogContainer = new HashSet<string>();

            public static bool Contains(string value)
            {
                return LogContainer.Contains(value);
            }

            public static void Add(string value)
            {
                if (LogTrail.Count >= LogTrailMax)
                {
                    LogContainer.Remove(LogTrail.Dequeue());
                }
                else
                {
                    LogTrail.Enqueue(value);
                    LogContainer.Add(value);
                }
            }
        }
    }
}
