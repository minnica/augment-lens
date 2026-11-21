using System;
using System.Collections.Generic;
using System.Linq;

namespace Aumentos;

internal sealed record BuildSwap(OpGgBuildItem Outgoing, OpGgBuildItem Incoming, string Reason)
{
    internal static BuildSwap? Find(BuildStep[] equipped, IEnumerable<BuildStep> replaceable, IEnumerable<SwapCounter> counters,
        Func<int, IReadOnlyList<BuildStep>, bool> canAdd, Func<int, OpGgBuildItem> item, Func<int, int, bool>? canReplace = null)
    {
        var active = counters.ToArray();
        var covered = active.Where(counter => equipped.Any(step => counter.Covers(step.Item.Id))).ToArray();
        var possibleRemovals = replaceable.Select(step => (Step: step,
            Kept: equipped.Where(other => other.Item.Id != step.Item.Id).ToArray())).ToArray();

        foreach (var counter in active.Except(covered))
        foreach (int incoming in counter.Picks)
        foreach (var (outgoing, kept) in possibleRemovals)
        {
            if (canReplace?.Invoke(outgoing.Item.Id, incoming) == false || !canAdd(incoming, kept) ||
                covered.Any(existing => existing.Covers(outgoing.Item.Id) &&
                !existing.Covers(incoming) && !kept.Any(step => existing.Covers(step.Item.Id))))
                continue;
            return new(outgoing.Item, item(incoming), counter.Reason);
        }
        return null;
    }
}

internal sealed record SwapCounter(string Reason, int[] Picks, Func<int, bool> Covers);
