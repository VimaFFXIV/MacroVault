using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace MacroVault;

/// <summary>
/// Handles reading in-game macros and posting them to a Discord webhook.
/// </summary>
public sealed class MacroExporter : IDisposable
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly HttpClient _http;

    private const int MacrosPerSet    = 100;
    private const int MaxDiscordChars = 2000;

    public MacroExporter(Configuration config, IPluginLog log)
    {
        _config = config;
        _log    = log;
        _http   = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
    }

    public void Dispose() => _http.Dispose();

    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>Export the macro currently open/selected in the Macro UI.</summary>
    public async Task<string> ExportSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
            return "No webhook URL configured. Use /mv to open settings.";

        MacroInfo? info = GetSelectedMacroInfo();
        if (info == null)
            return "No macro is currently selected, or the selected macro is empty.";

        string message = FormatMacro(info, queuePosition: null);
        bool ok = await PostToDiscordAsync(message);
        return ok
            ? $"Exported {info.DisplayLabel} to Discord."
            : "Failed to post to Discord. Check your webhook URL.";
    }

    /// <summary>Export all non-empty macros from both tabs in slot order.</summary>
    public async Task<string> BackupAllAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
            return "No webhook URL configured. Use /mv to open settings.";

        var macros = ReadAllMacros();
        if (!_config.IncludeUnnamedMacros)
            macros.RemoveAll(m => string.IsNullOrWhiteSpace(m.Name));

        if (macros.Count == 0)
            return "No non-empty macros found to backup.";

        var formatted = macros.ConvertAll(m => FormatMacro(m, queuePosition: null));
        var chunks = BuildChunks(formatted, header: null);

        int sent = 0;
        foreach (var chunk in chunks)
        {
            if (!await PostToDiscordAsync(chunk))
                return $"Backup partially failed after {sent} message(s). Check webhook URL.";
            sent++;
        }
        return $"Backup complete — {macros.Count} macro(s) in {sent} Discord message(s).";
    }

    /// <summary>
    /// Export a user-ordered list of macros with an optional set title.
    /// The title appears as a bold header in Discord; macros are numbered by queue position.
    /// </summary>
    public async Task<string> ExportSetAsync(List<MacroInfo> items, string setTitle)
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
            return "No webhook URL configured. Use /mv to open settings.";
        if (items.Count == 0)
            return "The export queue is empty.";

        string? header = string.IsNullOrWhiteSpace(setTitle)
            ? null
            : $"**\u2500\u2500 {setTitle.Trim()} \u2500\u2500**";

        var formatted = new List<string>(items.Count);
        for (int i = 0; i < items.Count; i++)
            formatted.Add(FormatMacro(items[i], queuePosition: i + 1));

        var chunks = BuildChunks(formatted, header);

        int sent = 0;
        foreach (var chunk in chunks)
        {
            if (!await PostToDiscordAsync(chunk))
                return $"Export partially failed after {sent} message(s). Check webhook URL.";
            sent++;
        }
        return $"Exported {items.Count} macro(s){(string.IsNullOrWhiteSpace(setTitle) ? "" : $" as \"{setTitle.Trim()}\"")} in {sent} Discord message(s).";
    }

    // ── Game memory reads ─────────────────────────────────────────────────────

    /// <summary>Read all non-empty macro slots from both sets into MacroInfo objects.</summary>
    public unsafe List<MacroInfo> ReadAllMacros()
    {
        var result = new List<MacroInfo>();
        var module = RaptureMacroModule.Instance();
        if (module == null) return result;

        for (uint set = 0; set <= 1; set++)
        {
            for (uint idx = 0; idx < MacrosPerSet; idx++)
            {
                var macro = module->GetMacro(set, idx);
                if (macro == null || !macro->IsNotEmpty()) continue;

                var lines = new List<string>(15);
                for (int i = 0; i < 15; i++)
                {
                    string line = macro->Lines[i].ToString();
                    if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
                }

                result.Add(new MacroInfo
                {
                    Set   = set,
                    Index = idx,
                    Name  = macro->Name.ToString(),
                    Lines = lines.ToArray(),
                });
            }
        }
        return result;
    }

    private unsafe MacroInfo? GetSelectedMacroInfo()
    {
        var agent = (AgentMacro*)AgentModule.Instance()->GetAgentByInternalId(AgentId.Macro);
        if (agent == null) return null;

        uint set   = agent->SelectedMacroSet;
        uint index = agent->SelectedMacroIndex;

        var module = RaptureMacroModule.Instance();
        if (module == null) return null;

        var macro = module->GetMacro(set, index);
        if (macro == null || !macro->IsNotEmpty()) return null;

        var lines = new List<string>(15);
        for (int i = 0; i < 15; i++)
        {
            string line = macro->Lines[i].ToString();
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
        }

        return new MacroInfo
        {
            Set   = set,
            Index = index,
            Name  = macro->Name.ToString(),
            Lines = lines.ToArray(),
        };
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    private static string FormatMacro(MacroInfo info, int? queuePosition)
    {
        var sb = new StringBuilder();

        // Header line — e.g.  **2. [Individual] #5 — My Macro**
        sb.Append("**");
        if (queuePosition.HasValue)
            sb.Append($"{queuePosition}. ");
        sb.Append($"[{info.SetName}] {info.SlotLabel}");
        if (!string.IsNullOrWhiteSpace(info.Name))
            sb.Append($" \u2014 {info.Name}");
        sb.AppendLine("**");

        sb.AppendLine("```");
        foreach (var line in info.Lines)
            sb.AppendLine(line);
        sb.Append("```");

        return sb.ToString();
    }

    // ── Chunking ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Pack formatted macro strings into Discord messages that stay under 2000 chars.
    /// If a header is provided it is prepended to the first chunk.
    /// </summary>
    private static List<string> BuildChunks(List<string> entries, string? header)
    {
        var chunks  = new List<string>();
        var current = new StringBuilder();

        if (!string.IsNullOrEmpty(header))
        {
            current.AppendLine(header);
            current.AppendLine();
        }

        foreach (var entry in entries)
        {
            // Single entry larger than limit — truncate
            if (entry.Length > MaxDiscordChars)
            {
                if (current.Length > 0) { chunks.Add(current.ToString()); current.Clear(); }
                chunks.Add(entry[..(MaxDiscordChars - 22)] + "\n\u2026(truncated)```");
                continue;
            }

            int needed = current.Length + (current.Length > 0 ? 1 : 0) + entry.Length;
            if (needed > MaxDiscordChars)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.AppendLine();
            current.Append(entry);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }

    // ── HTTP ──────────────────────────────────────────────────────────────────

    private async Task<bool> PostToDiscordAsync(string content)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                content,
                username = string.IsNullOrWhiteSpace(_config.BotUsername) ? "MacroVault" : _config.BotUsername,
            });

            using var response = await _http.PostAsync(
                _config.WebhookUrl,
                new StringContent(payload, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _log.Warning($"[MacroVault] Discord webhook returned {(int)response.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[MacroVault] Failed to post to Discord webhook.");
            return false;
        }
    }
}
