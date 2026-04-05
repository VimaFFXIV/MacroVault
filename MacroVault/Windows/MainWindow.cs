using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MacroVault.Windows;

public class MainWindow : Window
{
    private readonly Configuration _config;
    private readonly MacroExporter _exporter;

    // ── Browser state ─────────────────────────────────────────────────────────
    private List<MacroInfo> _allMacros = new();
    private bool _needsRefresh = true;

    // ── Queue state ───────────────────────────────────────────────────────────
    private readonly List<MacroInfo> _queue = new();
    private string _setTitle = string.Empty;

    // ── Settings buffers ──────────────────────────────────────────────────────
    private string _webhookBuf  = string.Empty;
    private string _usernameBuf = string.Empty;

    // ── Shared status line ────────────────────────────────────────────────────
    private string _status = string.Empty;

    public MainWindow(Configuration config, MacroExporter exporter)
        : base("MacroVault##MacroVaultMain")
    {
        _config   = config;
        _exporter = exporter;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnOpen()
    {
        _webhookBuf  = _config.WebhookUrl;
        _usernameBuf = _config.BotUsername;
        _needsRefresh = true;
    }

    public override void Draw()
    {
        // Lazy-refresh on first draw after open (safe to call on game thread)
        if (_needsRefresh)
        {
            _allMacros    = _exporter.ReadAllMacros();
            _needsRefresh = false;
        }

        if (ImGui.BeginTabBar("##mvtabs"))
        {
            if (ImGui.BeginTabItem("Export Set"))
            {
                DrawExportSetTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Quick Backup"))
            {
                DrawQuickBackupTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    // ── Export Set tab ────────────────────────────────────────────────────────

    private void DrawExportSetTab()
    {
        float avail   = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        // Reserve one frame-height row at the bottom for the export button + status
        float panelH  = ImGui.GetContentRegionAvail().Y
                        - ImGui.GetFrameHeightWithSpacing() * 2f
                        - ImGui.GetStyle().ItemSpacing.Y;
        float halfW   = (avail - spacing) / 2f;

        // ── Left: Macro Browser ───────────────────────────────────────────────
        ImGui.BeginChild("##browser", new Vector2(halfW, panelH), true);
        DrawBrowserPanel();
        ImGui.EndChild();

        ImGui.SameLine();

        // ── Right: Export Queue ───────────────────────────────────────────────
        ImGui.BeginChild("##queue", new Vector2(halfW, panelH), true);
        DrawQueuePanel();
        ImGui.EndChild();

        // ── Bottom row: set title + export button + status ────────────────────
        ImGui.Spacing();

        ImGui.Text("Set Title:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("##settitle", ref _setTitle, 128);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Optional name shown as a bold header above your macros in Discord.");

        ImGui.SameLine();

        bool canExport = _queue.Count > 0 && !string.IsNullOrWhiteSpace(_config.WebhookUrl);
        if (!canExport) ImGui.BeginDisabled();

        if (ImGui.Button("Export Set to Discord", new Vector2(180, 0)))
        {
            var snapshot = new List<MacroInfo>(_queue);
            string title = _setTitle;
            _status = "Exporting\u2026";
            _ = Task.Run(async () =>
            {
                var result = await _exporter.ExportSetAsync(snapshot, title);
                _status = result;
                Plugin.ChatGui.Print($"[MacroVault] {result}");
            });
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
                ImGui.SetTooltip("Set a webhook URL in the Settings tab first.");
            else if (_queue.Count == 0)
                ImGui.SetTooltip("Add macros to the queue using the browser on the left.");
        }

        if (!canExport) ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_status))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(_status);
        }
    }

    private void DrawBrowserPanel()
    {
        ImGui.TextDisabled("Macro Browser");
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh"))
        {
            _allMacros = _exporter.ReadAllMacros();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Re-scan game memory for macros (use after editing macros in-game).");
        ImGui.Separator();

        if (ImGui.BeginTabBar("##sets"))
        {
            if (ImGui.BeginTabItem("Individual"))
            {
                DrawMacroList(0);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Shared"))
            {
                DrawMacroList(1);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawMacroList(uint set)
    {
        bool any = false;
        foreach (var m in _allMacros)
        {
            if (m.Set != set) continue;
            any = true;

            if (ImGui.SmallButton($"Add##{m.Set}_{m.Index}"))
                _queue.Add(m);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Add to export queue.");

            ImGui.SameLine();
            ImGui.TextUnformatted(m.DisplayLabel);

            // Show line count as a dim hint
            if (m.Lines.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({m.Lines.Length} line{(m.Lines.Length == 1 ? "" : "s")})");
            }
        }
        if (!any)
            ImGui.TextDisabled("No macros found in this tab.");
    }

    private void DrawQueuePanel()
    {
        ImGui.TextDisabled($"Export Queue ({_queue.Count} macro{(_queue.Count == 1 ? "" : "s")})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear All"))
            _queue.Clear();
        ImGui.Separator();

        if (_queue.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Empty. Click \u201cAdd\u201d next to a macro\nin the browser to queue it.");
            return;
        }

        for (int i = 0; i < _queue.Count; i++)
        {
            var m = _queue[i];
            ImGui.PushID(i);

            // ▲ Up
            bool canUp = i > 0;
            if (!canUp) ImGui.BeginDisabled();
            if (ImGui.SmallButton("\u25b2")) { _queue.RemoveAt(i); _queue.Insert(i - 1, m); ImGui.PopID(); continue; }
            if (!canUp) ImGui.EndDisabled();

            ImGui.SameLine();

            // ▼ Down
            bool canDown = i < _queue.Count - 1;
            if (!canDown) ImGui.BeginDisabled();
            if (ImGui.SmallButton("\u25bc")) { _queue.RemoveAt(i); _queue.Insert(i + 1, m); ImGui.PopID(); i++; continue; }
            if (!canDown) ImGui.EndDisabled();

            ImGui.SameLine();

            // ✕ Remove
            if (ImGui.SmallButton("\u00d7")) { _queue.RemoveAt(i); ImGui.PopID(); i--; continue; }

            ImGui.SameLine();
            ImGui.Text($"{i + 1}.");
            ImGui.SameLine();
            ImGui.TextUnformatted(m.DisplayLabel);

            ImGui.PopID();
        }
    }

    // ── Quick Backup tab ──────────────────────────────────────────────────────

    private void DrawQuickBackupTab()
    {
        ImGui.Spacing();
        ImGui.TextDisabled("Exports every non-empty macro from both Individual and Shared tabs.");
        ImGui.TextDisabled("Macros appear in slot order (Individual 1\u2013100, then Shared 1\u2013100).");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool noWebhook = string.IsNullOrWhiteSpace(_config.WebhookUrl);
        if (noWebhook)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "No webhook URL — go to the Settings tab to configure one.");
            return;
        }

        var includeUnnamed = _config.IncludeUnnamedMacros;
        if (ImGui.Checkbox("Include macros with no name set", ref includeUnnamed))
        {
            _config.IncludeUnnamedMacros = includeUnnamed;
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When unchecked, macros whose Name field is empty are skipped.");

        ImGui.Spacing();

        if (ImGui.Button("Backup All Macros to Discord", new Vector2(220, 0)))
        {
            _status = "Starting backup\u2026";
            Plugin.ChatGui.Print("[MacroVault] Starting full macro backup...");
            _ = Task.Run(async () =>
            {
                var result = await _exporter.BackupAllAsync();
                _status = result;
                Plugin.ChatGui.Print($"[MacroVault] {result}");
            });
        }

        if (!string.IsNullOrEmpty(_status))
        {
            ImGui.Spacing();
            ImGui.TextDisabled(_status);
        }
    }

    // ── Settings tab ──────────────────────────────────────────────────────────

    private void DrawSettingsTab()
    {
        ImGui.Spacing();

        ImGui.Text("Discord Webhook URL:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##webhook", ref _webhookBuf, 512))
        {
            _config.WebhookUrl = _webhookBuf.Trim();
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Server Settings \u2192 Integrations \u2192 Webhooks \u2192 Copy Webhook URL");

        ImGui.Spacing();

        ImGui.Text("Discord display name:");
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("##username", ref _usernameBuf, 80))
        {
            _config.BotUsername = _usernameBuf.Trim();
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The name shown on Discord messages sent by this plugin.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("Commands:");
        ImGui.TextDisabled("  /mvexport  \u2014 export the currently selected macro");
        ImGui.TextDisabled("  /mvbackup  \u2014 backup all macros (same as Quick Backup tab)");
        ImGui.TextDisabled("  /mv        \u2014 open this window");
    }
}
