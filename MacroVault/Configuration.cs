using System;
using Dalamud.Configuration;

namespace MacroVault;

public enum MacroFormat
{
    CodeBlock  = 0,   // ```\ncommands\n```  — syntax-highlighted, less searchable
    PlainText  = 1,   // bare lines          — fully searchable, easiest to copy-paste
    QuoteBlock = 2,   // > command           — searchable + Discord left-bar visual
}

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

    /// <summary>How macro command lines are formatted in Discord messages.</summary>
    public MacroFormat Format { get; set; } = MacroFormat.PlainText;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
