using System;

namespace MacroVault;

/// <summary>
/// Lightweight snapshot of one in-game macro slot, read from game memory.
/// Immutable once created so it can be safely queued and passed between threads.
/// </summary>
public sealed class MacroInfo
{
    public uint   Set   { get; init; }   // 0 = Individual, 1 = Shared
    public uint   Index { get; init; }   // 0-99 slot number
    public string Name  { get; init; } = string.Empty;
    public string[] Lines { get; init; } = Array.Empty<string>();

    public string SetName    => Set == 0 ? "Individual" : "Shared";
    public string SlotLabel  => $"#{Index + 1}";

    /// <summary>Short label shown in the UI browser and queue list.</summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Name)
        ? $"[{SetName}] {SlotLabel}"
        : $"[{SetName}] {SlotLabel} \u2014 {Name}";
}
