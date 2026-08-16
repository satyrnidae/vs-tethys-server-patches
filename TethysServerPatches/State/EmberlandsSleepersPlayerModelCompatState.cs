using System.Collections.Concurrent;

namespace TethysServerPatches.State;

static class EmberlandsSleepersPlayerModelCompatState
{
    public static readonly ConcurrentDictionary<string, (string SkinModel, float? EntitySize)> Pending = new();
}
