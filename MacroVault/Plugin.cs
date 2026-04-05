using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MacroVault.Windows;

namespace MacroVault;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandExport = "/mvexport";
    private const string CommandBackup = "/mvbackup";
    private const string CommandConfig = "/mv";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    private readonly MacroExporter _exporter;
    private readonly WindowSystem _windowSystem = new("MacroVault");
    private readonly MainWindow _mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _exporter = new MacroExporter(Configuration, Log);

        _mainWindow = new MainWindow(Configuration, _exporter);
        _windowSystem.AddWindow(_mainWindow);

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += _mainWindow.Toggle;

        CommandManager.AddHandler(CommandExport, new CommandInfo(OnExport)
        {
            HelpMessage = "Export the currently selected macro to Discord.",
        });

        CommandManager.AddHandler(CommandBackup, new CommandInfo(OnBackup)
        {
            HelpMessage = "Export all non-empty macros (both tabs) to Discord.",
        });

        CommandManager.AddHandler(CommandConfig, new CommandInfo(OnConfig)
        {
            HelpMessage = "Open MacroVault settings. Use 'config' subcommand or just /mv.",
        });
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandExport);
        CommandManager.RemoveHandler(CommandBackup);
        CommandManager.RemoveHandler(CommandConfig);

        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= _mainWindow.Toggle;

        _exporter.Dispose();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private void OnExport(string command, string args)
    {
        ChatGui.Print("[MacroVault] Exporting selected macro...");
        _ = Task.Run(async () =>
        {
            var result = await _exporter.ExportSelectedAsync();
            ChatGui.Print($"[MacroVault] {result}");
        });
    }

    private void OnBackup(string command, string args)
    {
        ChatGui.Print("[MacroVault] Starting full macro backup...");
        _ = Task.Run(async () =>
        {
            var result = await _exporter.BackupAllAsync();
            ChatGui.Print($"[MacroVault] {result}");
        });
    }

    private void OnConfig(string command, string args)
    {
        _mainWindow.Toggle();
    }
}
