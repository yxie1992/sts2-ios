using System.Collections;

namespace CombatSolver;

// Keeps the upstream capture-window contract without reflecting List<T>._version.
// The BCL enumerator already captures that version and checks it in MoveNext,
// including on an empty list and after a same-count item replacement.
internal sealed class LiveCollectionModifiedException : InvalidOperationException
{
    internal LiveCollectionModifiedException(string collectionName, string detail)
        : base($"Live collection '{collectionName}' changed during combat root capture ({detail}). " +
               "The root snapshot was rejected; the live combat state stays untouched.")
        => CollectionName = collectionName;

    public string CollectionName { get; }
}

internal static class LiveCollectionGuard
{
    internal static Action? CaptureWindowProbeForTesting { get; set; }

    public static T[] SnapshotStable<T>(List<T> list, string collectionName)
    {
        using var guard = list.GetEnumerator();
        int beforeCount = list.Count;
        T[] snapshot = list.ToArray();
        try { guard.MoveNext(); }
        catch (InvalidOperationException)
        {
            throw Changed(collectionName, beforeCount, list.Count, "snapshot");
        }
        if (beforeCount != list.Count)
            throw Changed(collectionName, beforeCount, list.Count, "snapshot");
        return snapshot;
    }

    public static Window BeginWindow<T>(params (string Name, List<T> List)[] lists)
    {
        var entries = new (string Name, ICollection List, int Count, IEnumerator Guard)[lists.Length];
        for (int index = 0; index < lists.Length; index++)
        {
            var (name, list) = lists[index];
            // Call the concrete List<T> method: its interface implementation may
            // return a shared empty enumerator that does not track mutations.
            IEnumerator guard = list.GetEnumerator();
            entries[index] = (name, list, list.Count, guard);
        }
        return new Window(entries);
    }

    private static LiveCollectionModifiedException Changed(string name, int before, int after, string phase)
        => new(name, $"count {before}->{after}, collection version changed, phase={phase}");

    internal sealed class Window
    {
        private readonly (string Name, ICollection List, int Count, IEnumerator Guard)[] _entries;

        internal Window((string Name, ICollection List, int Count, IEnumerator Guard)[] entries)
        {
            _entries = entries;
            CaptureWindowProbeForTesting?.Invoke();
        }

        public void Verify()
        {
            foreach (var (name, list, count, guard) in _entries)
            {
                try { guard.MoveNext(); }
                catch (InvalidOperationException)
                {
                    throw Changed(name, count, list.Count, "window");
                }
                if (list.Count != count)
                    throw Changed(name, count, list.Count, "window");
            }
        }
    }
}
