// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NomNomzBot.Application.Contracts.Marketplace;

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// The shared mechanics of the bundle ZIP format (marketplace.md D1): JSON settings, entry-path layout,
/// name slugging, and the D4 capability scan. Used by both the export and import services so the two sides
/// can never drift on how a bundle is laid out or read.
/// </summary>
internal static class BundleConventions
{
    /// <summary>camelCase + indented — the on-disk JSON dialect of every bundle entry.</summary>
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, JsonSettings);

    public static T? Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, JsonSettings);

    /// <summary>ZIP entry path of an item's JSON document, per the D1 per-type folder layout.</summary>
    public static string EntryPath(string type, string slug) =>
        type switch
        {
            BundleFormat.PipelineType => $"pipelines/{slug}.json",
            BundleFormat.CommandType => $"commands/{slug}.json",
            BundleFormat.WidgetType => $"widgets/{slug}/widget.json",
            BundleFormat.SoundType => $"sounds/{slug}.json",
            BundleFormat.CustomDataSourceType => $"custom-data-sources/{slug}.json",
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unknown bundle item type."
            ),
        };

    /// <summary>ZIP entry path of a sound's audio payload, next to its metadata document.</summary>
    public static string SoundAudioPath(string slug, string mimeType) =>
        $"sounds/{slug}{AudioExtension(mimeType)}";

    public static string AudioExtension(string mimeType) =>
        mimeType switch
        {
            "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/wav" => ".wav",
            _ => ".bin",
        };

    /// <summary>Filesystem/ZIP-safe slug of an item name: lowercase, <c>[a-z0-9-_]</c> only.</summary>
    public static string Slug(string name)
    {
        char[] safe = name.Trim()
            .ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray();
        string slug = new(safe);
        return slug.Length == 0 ? "item" : slug;
    }

    /// <summary>True when a pipeline graph document contains a <c>run_code</c> action (D4 gate).</summary>
    public static bool ContainsRunCode(string? graphJson) =>
        graphJson is not null && graphJson.Contains("\"run_code\"", StringComparison.Ordinal);

    // The D4 capability catalog: pipeline-graph action keys → the plain-language capability each implies.
    // Scanned as quoted tokens in the graph document — the graph is the builder's own JSON, so an action in
    // use always appears as its quoted registry key.
    private static readonly IReadOnlyList<(string Key, string Capability)> ActionCapabilities =
    [
        ("run_code", "executes custom code"),
        ("ban", "can ban users"),
        ("timeout", "can time out users"),
        ("delete_message", "can delete chat messages"),
        ("grant_currency", "moves channel currency"),
        ("deduct_currency", "moves channel currency"),
        ("play_sound", "plays audio on the overlay"),
        ("play_tts", "plays audio on the overlay"),
        ("send_message", "sends chat messages"),
        ("send_reply", "sends chat messages"),
        ("obs_", "controls OBS"),
        ("vts_", "controls VTube Studio"),
    ];

    /// <summary>
    /// The D4 capability summary of a parsed bundle — what its content can do once installed, surfaced to
    /// the importer before install and carried on <c>BundleInstalledEvent</c>.
    /// </summary>
    public static IReadOnlyList<string> CollectCapabilities(
        IReadOnlyList<PipelineExport> pipelines,
        IReadOnlyList<CommandExport> commands,
        bool hasWidgets,
        bool hasSounds,
        bool hasDataSources
    )
    {
        List<string> capabilities = [];

        foreach ((string key, string capability) in ActionCapabilities)
        {
            bool prefix = key.EndsWith('_');
            bool present = pipelines.Any(p =>
                p.GraphJson is not null
                && (
                    prefix
                        ? p.GraphJson.Contains($"\"{key}", StringComparison.Ordinal)
                        : p.GraphJson.Contains($"\"{key}\"", StringComparison.Ordinal)
                )
            );
            if (present && !capabilities.Contains(capability))
                capabilities.Add(capability);
        }

        if (commands.Any(c => c.Tier == "code") && !capabilities.Contains("executes custom code"))
            capabilities.Add("executes custom code");

        if (hasWidgets)
            capabilities.Add("adds overlay widgets (installed as unverified custom widgets)");
        if (hasSounds)
            capabilities.Add("adds sound clips");
        if (hasDataSources)
            capabilities.Add("connects an external data source (credential must be re-entered)");

        return capabilities;
    }
}
