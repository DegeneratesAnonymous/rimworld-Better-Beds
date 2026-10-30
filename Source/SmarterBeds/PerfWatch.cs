using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// A small stopwatch around the places this mod does real work, so how much time it takes can be
    /// seen in a real colony instead of guessed at. The numbers are shown when you hover the
    /// compatibility line at the bottom of the mod options. Measuring costs a few tens of nanoseconds.
    /// A single call over 20 ms is also written to the log once per feature.
    /// </summary>
    public static class PerfWatch
    {
        private class Stat
        {
            public long calls;
            public double totalMs;
            public double maxMs;
        }

        private const double SlowMs = 20.0;
        private static readonly Dictionary<string, Stat> Stats = new Dictionary<string, Stat>();

        public struct Scope : IDisposable
        {
            private readonly string name;
            private readonly long start;

            public Scope(string name)
            {
                this.name = name;
                start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (!Stats.TryGetValue(name, out var stat)) Stats[name] = stat = new Stat();
                stat.calls++;
                stat.totalMs += ms;
                if (ms > stat.maxMs) stat.maxMs = ms;
                if (ms > SlowMs) SafeLog.Note("slow:" + name, $"{name} took {ms:0} ms in one call (this is logged once). Hover the compatibility line in the mod options for the totals.");
            }
        }

        public static Scope Measure(string name) => new Scope(name);

        public static void Reset() => Stats.Clear();

        public static string Report()
        {
            if (Stats.Count == 0) return "";
            return "Time used by this mod since the game started:\n" + string.Join("\n",
                Stats.OrderByDescending(kv => kv.Value.totalMs).Select(kv =>
                    $"  {kv.Key}: {kv.Value.calls} calls, {kv.Value.totalMs / kv.Value.calls:0.000} ms average, {kv.Value.maxMs:0.0} ms worst, {kv.Value.totalMs:0} ms total"));
        }
    }
}
