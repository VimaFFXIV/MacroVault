using Dalamud.Configuration;

namespace MacroVault;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary>Discord webhook URL to post macros to.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Username that appears on Discord messages.</summary>
    public string BotUsername { get; set; } = "MacroVault";

    /// <summary>Whether to include macros with no name (only raw lines).</summary>
    public bool IncludeUnnamedMacros { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
