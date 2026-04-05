using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace MacroVault.Windows;

public class MainWindow : Window
{
    private readonly Configuration _config;
    private readonly MacroExporter _exporter;

    // Temp buffer for webhook URL input
    private string _webhookBuf = string.Empty;
    private string _usernameBuf = string.Empty;
    private bool _initialized = false;

    private string _statusMessage = string.Empty;

    public MainWindow(Configuration config, MacroExporter exporter)
        : base("MacroVault##MacroVaultMain")
    {
        _config   = config;
        _exporter = exporter;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnOpen()
    {
        // Sync buffers from config each time the window opens.
        _webhookBuf  = _config.WebhookUrl;
        _usernameBuf = _config.BotUsername;
        _initialized = true;
    }

    public override void Draw()
    {
        if (!_initialized)
        {
            _webhookBuf  = _config.WebhookUrl;
            _usernameBuf = _config.BotUsername;
            _initialized = true;
        }

        // ── Header ─────────────────────────────────────────────────────────
        ImGui.TextDisabled("Export in-game macros to a Discord channel via webhook.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Webhook URL ────────────────────────────────────────────────────
        ImGui.Text("Discord Webhook URL:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##webhook", ref _webhookBuf, 512))
        {
            _config.WebhookUrl = _webhookBuf.Trim();
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Paste your Discord channel webhook URL here.\nServer Settings > Integrations > Webhooks > Copy Webhook URL");

        ImGui.Spacing();

        // ── Bot username ───────────────────────────────────────────────────
        ImGui.Text("Discord display name:");
        ImGui.SetNextItemWidth(240);
        if (ImGui.InputText("##username", ref _usernameBuf, 80))
        {
            _config.BotUsername = _usernameBuf.Trim();
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The username that appears on Discord messages.");

        ImGui.Spacing();

        // ── Options ────────────────────────────────────────────────────────
        var includeUnnamed = _config.IncludeUnnamedMacros;
        if (ImGui.Checkbox("Include macros with no name", ref includeUnnamed))
        {
            _config.IncludeUnnamedMacros = includeUnnamed;
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When backing up all macros, also export macros\nthat have no name set (only raw command lines).");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Export buttons ─────────────────────────────────────────────────
        bool noWebhook = string.IsNullOrWhiteSpace(_config.WebhookUrl);

        if (noWebhook) ImGui.BeginDisabled();

        if (ImGui.Button("Export Selected Macro", new Vector2(200, 0)))
        {
            _statusMessage = "Exporting selected macro...";
            _ = Task.Run(async () =>
            {
                var result = await _exporter.ExportSelectedAsync();
                _statusMessage = result;
                Plugin.ChatGui.Print($"[MacroVault] {result}");
            });
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(noWebhook
                ? "Set a webhook URL first."
                : "Exports the macro currently open in the Macro UI window.");

        ImGui.SameLine();

        if (ImGui.Button("Backup All Macros", new Vector2(160, 0)))
        {
            _statusMessage = "Starting full backup...";
            _ = Task.Run(async () =>
            {
                var result = await _exporter.BackupAllAsync();
                _statusMessage = result;
                Plugin.ChatGui.Print($"[MacroVault] {result}");
            });
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(noWebhook
                ? "Set a webhook URL first."
                : "Exports all non-empty macros from both Individual and Shared tabs.");

        if (noWebhook) ImGui.EndDisabled();

        // ── Status line ────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.Spacing();
            ImGui.TextDisabled(_statusMessage);
        }

        // ── Help footer ────────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("Commands:");
        ImGui.TextDisabled("  /mvexport  — export currently selected macro");
        ImGui.TextDisabled("  /mvbackup  — backup all macros");
        ImGui.TextDisabled("  /mv        — open this window");
    }
}
