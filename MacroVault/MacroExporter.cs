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

    private const int MacrosPerSet = 100;
    private const int MaxDiscordLength = 2000;

    public MacroExporter(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
    }

    public void Dispose() => _http.Dispose();

    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>Export the macro that is currently open/selected in the Macro UI.</summary>
    public async Task<string> ExportSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
            return "No webhook URL configured. Use /mv config to set one.";

        var (set, index, text) = GetSelectedMacroText();
        if (text == null)
            return "No macro is currently selected, or the selected macro is empty.";

        string setName = set == 0 ? "Individual" : "Shared";
        string message = $"**[{setName}] Macro {index + 1}**\n{text}";

        var result = await PostToDiscordAsync(message);
        return result ? $"Exported [{setName}] Macro {index + 1} to Discord." : "Failed to post to Discord. Check your webhook URL.";
    }

    /// <summary>Export all non-empty macros from both Individual and Shared tabs.</summary>
    public async Task<string> BackupAllAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
            return "No webhook URL configured. Use /mv config to set one.";

        var lines = CollectAllMacroLines();
        if (lines.Count == 0)
            return "No non-empty macros found to backup.";

        var chunks = SplitIntoChunks(lines);
        int sent = 0;
        foreach (var chunk in chunks)
        {
            if (!await PostToDiscordAsync(chunk))
                return $"Backup partially failed after {sent} message(s). Check your webhook URL.";
            sent++;
        }

        return $"Backup complete — sent {sent} Discord message(s).";
    }

    // ── Macro reading ─────────────────────────────────────────────────────────

    private unsafe (uint set, uint index, string? text) GetSelectedMacroText()
    {
        var agent = (AgentMacro*)AgentModule.Instance()->GetAgentByInternalId(AgentId.Macro);
        if (agent == null) return (0, 0, null);

        uint set   = agent->SelectedMacroSet;
        uint index = agent->SelectedMacroIndex;

        var module = RaptureMacroModule.Instance();
        if (module == null) return (set, index, null);

        var macro = module->GetMacro(set, index);
        if (macro == null || !macro->IsNotEmpty()) return (set, index, null);

        return (set, index, FormatMacro(set, index, macro));
    }

    private unsafe List<string> CollectAllMacroLines()
    {
        var module = RaptureMacroModule.Instance();
        if (module == null) return [];

        var entries = new List<string>();

        for (uint set = 0; set <= 1; set++)
        {
            string setName = set == 0 ? "Individual" : "Shared";
            for (uint idx = 0; idx < MacrosPerSet; idx++)
            {
                var macro = module->GetMacro(set, idx);
                if (macro == null || !macro->IsNotEmpty()) continue;

                string name = macro->Name.ToString();
                if (!_config.IncludeUnnamedMacros && string.IsNullOrWhiteSpace(name)) continue;

                entries.Add(FormatMacro(set, idx, macro));
            }
        }

        return entries;
    }

    private static unsafe string FormatMacro(uint set, uint index, RaptureMacroModule.Macro* macro)
    {
        string setName = set == 0 ? "Individual" : "Shared";
        string name    = macro->Name.ToString();

        var sb = new StringBuilder();

        if (string.IsNullOrWhiteSpace(name))
            sb.AppendLine($"**[{setName}] Macro {index + 1}**");
        else
            sb.AppendLine($"**[{setName}] Macro {index + 1} \u2014 {name}**");

        sb.AppendLine("```");
        for (int i = 0; i < 15; i++)
        {
            string line = macro->Lines[i].ToString();
            if (!string.IsNullOrWhiteSpace(line))
                sb.AppendLine(line);
        }
        sb.Append("```");

        return sb.ToString();
    }

    // ── Chunking ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a list of formatted macro strings into Discord messages that each
    /// stay within the 2000-character limit.
    /// </summary>
    private static List<string> SplitIntoChunks(List<string> entries)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var entry in entries)
        {
            // Single entry too long — truncate with a note.
            if (entry.Length > MaxDiscordLength)
            {
                string truncated = entry[..(MaxDiscordLength - 20)] + "\n…(truncated)```";
                chunks.Add(truncated);
                continue;
            }

            if (current.Length + entry.Length + 1 > MaxDiscordLength)
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
                username = string.IsNullOrWhiteSpace(_config.BotUsername) ? "MacroVault" : _config.BotUsername
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
