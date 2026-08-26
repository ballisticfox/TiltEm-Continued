using System;
using System.Collections.Generic;
using Xunit;

namespace TiltEm.Verification
{
    /// <summary>
    /// Turns a group of Harness checks into one xUnit test per check.
    /// </summary>
    //The groups build shared state as they go - a simulated system, a run of ticks - so each
    //runs once and is memoized, and every check it recorded is then asserted individually.
    //That keeps a failure pointing at the one invariant that broke.
    public static class CheckRunner
    {
        private static readonly Dictionary<string, IReadOnlyList<Harness.Result>> Cache =
            new Dictionary<string, IReadOnlyList<Harness.Result>>();

        private static readonly object Gate = new object();

        private static IReadOnlyList<Harness.Result> Results(string group, Action body)
        {
            lock (Gate)
            {
                IReadOnlyList<Harness.Result> results;
                if (!Cache.TryGetValue(group, out results))
                {
                    results = Harness.Capture(body);
                    Cache[group] = results;
                }

                return results;
            }
        }

        /// <summary>One row per check: its position, defect ID, and name.</summary>
        //The index is what makes a row unique - two checks may legitimately share a name.
        public static IEnumerable<object[]> Cases(string group, Action body)
        {
            IReadOnlyList<Harness.Result> results = Results(group, body);

            for (int i = 0; i < results.Count; i++)
            {
                yield return new object[] { i, results[i].Defect, results[i].Name };
            }
        }

        public static void Verify(string group, Action body, int index, string defect, string name)
        {
            Harness.Result result = Results(group, body)[index];

            Assert.True(result.Passed, defect + "  " + name
                + (string.IsNullOrEmpty(result.Detail) ? "" : Environment.NewLine + "  " + result.Detail));
        }
    }
}
