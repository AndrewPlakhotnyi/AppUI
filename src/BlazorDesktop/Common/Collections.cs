using System.Collections.Generic;
using System.Collections.Immutable;

namespace BlazorDesktop {
internal static class Collections {

    public static void
    AddOrAddToList<TKey, TValue>(this Dictionary<TKey, List<TValue>> dictionary, TKey key, TValue value) {
        if (dictionary.TryGetValue(key, out var values))
            values.Add(value);
        else
            dictionary[key] = new List<TValue>(){value};
    }
}
}
