// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Nodes;
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
            BundleFormat.AssetType => $"assets/{slug}.json",
            BundleFormat.CustomDataSourceType => $"custom-data-sources/{slug}.json",
            BundleFormat.EventResponseType => $"event-responses/{slug}.json",
            BundleFormat.RewardType => $"rewards/{slug}.json",
            BundleFormat.TimerType => $"timers/{slug}.json",
            BundleFormat.ChatTriggerType => $"chat-triggers/{slug}.json",
            BundleFormat.PickListType => $"pick-lists/{slug}.json",
            BundleFormat.CodeScriptType => $"code-scripts/{slug}.json",
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

    /// <summary>ZIP entry path of a media asset's binary payload, next to its metadata document.</summary>
    public static string AssetPayloadPath(string slug, string mimeType) =>
        $"assets/{slug}{AssetExtension(mimeType)}";

    public static string AssetExtension(string mimeType) =>
        mimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => AudioExtension(mimeType),
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

    // ── run_code graph re-link mechanics (export: id → name, import: name → id) ─────────────────

    /// <summary>The pipeline action key of a sandboxed-script step.</summary>
    public const string RunCodeActionType = "run_code";

    /// <summary>The tenant-local script binding of a <c>run_code</c> step — must never travel in a bundle.</summary>
    public const string CodeScriptIdParam = "code_script_id";

    /// <summary>The portable script binding a bundled <c>run_code</c> step carries instead of the id.</summary>
    public const string CodeScriptNameParam = "code_script_name";

    /// <summary>
    /// Parses a pipeline graph document into a mutable node tree (System.Text.Json — the library the engine
    /// and <c>PipelineService</c> read/write graphs with), case-insensitive on property names like the
    /// engine's deserializer. Null/blank/malformed input yields null: the caller leaves the document as-is.
    /// </summary>
    public static JsonObject? ParseGraph(string? graphJson)
    {
        if (string.IsNullOrWhiteSpace(graphJson))
            return null;
        try
        {
            return JsonNode.Parse(
                    graphJson,
                    new JsonNodeOptions { PropertyNameCaseInsensitive = true }
                ) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every <c>run_code</c> step's parameter bag in a graph document, across both graph spellings the
    /// system reads: the engine's steps form (<c>{"steps":[{"action":{"type":"run_code",…}}]}</c>), where
    /// params sit flat on the action object, and the builder's nodes form
    /// (<c>{"nodes":[{"type":"run_code","config":{…}}]}</c>), where they sit in the node's <c>config</c>
    /// object (the node itself when it has none). Mutating a returned bag mutates the parsed graph.
    /// </summary>
    public static IReadOnlyList<JsonObject> RunCodeParameterBags(JsonObject graph)
    {
        List<JsonObject> bags = [];

        if (graph["steps"] is JsonArray steps)
            foreach (JsonNode? step in steps)
                if (
                    step is JsonObject stepObject
                    && stepObject["action"] is JsonObject action
                    && IsRunCode(action["type"])
                )
                    bags.Add(action);

        if (graph["nodes"] is JsonArray nodes)
            foreach (JsonNode? node in nodes)
                if (node is JsonObject nodeObject && IsRunCode(nodeObject["type"]))
                    bags.Add(nodeObject["config"] as JsonObject ?? nodeObject);

        return bags;
    }

    /// <summary>The string value of a graph parameter, or null when absent/non-string.</summary>
    public static string? GetStringParam(JsonObject bag, string key) =>
        bag[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static bool IsRunCode(JsonNode? type) =>
        type is JsonValue value
        && value.TryGetValue(out string? text)
        && string.Equals(text, RunCodeActionType, StringComparison.OrdinalIgnoreCase);

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

    // The declared-capability catalog for bundled code scripts: script capability keys (custom-code.md §3.1)
    // → the plain-language capability each implies, listed by inspect next to the pipeline-action ones. An
    // unknown key still surfaces (fail-open on visibility) via the generic fallback in ScriptCapability.
    private static readonly IReadOnlyDictionary<string, string> ScriptCapabilities = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["chat.send"] = "sends chat messages",
        ["chat.reply"] = "sends chat messages",
        ["user.get"] = "reads user profiles",
        ["economy.read"] = "reads channel currency",
        ["music.queue"] = "controls music playback",
        ["music.nowPlaying"] = "controls music playback",
        ["http.fetch"] = "makes outbound HTTP requests",
        ["storage.get"] = "stores script data",
        ["storage.set"] = "stores script data",
        ["storage.delete"] = "stores script data",
        ["storage.list"] = "stores script data",
        ["tts.speak"] = "plays audio on the overlay",
        ["tts.voice.get"] = "manages viewer TTS voices",
        ["tts.voice.set"] = "manages viewer TTS voices",
        ["stats.viewer"] = "reads viewer statistics",
        ["widget.emit"] = "pushes events to overlay widgets",
        ["reward.get"] = "manages channel point rewards",
        ["reward.update"] = "manages channel point rewards",
    };

    /// <summary>The plain-language capability a script capability key implies (generic fallback for unknown keys).</summary>
    public static string ScriptCapability(string key) =>
        ScriptCapabilities.TryGetValue(key, out string? capability)
            ? capability
            : $"custom code capability '{key}'";

    /// <summary>
    /// The D4 capability summary of a parsed bundle — what its content can do once installed, surfaced to
    /// the importer before install and carried on <c>BundleInstalledEvent</c>.
    /// </summary>
    public static IReadOnlyList<string> CollectCapabilities(
        IReadOnlyList<PipelineExport> pipelines,
        IReadOnlyList<CommandExport> commands,
        IReadOnlyList<CodeScriptExport> codeScripts,
        bool hasWidgets,
        bool hasSounds,
        bool hasAssets,
        bool hasDataSources,
        bool hasEventResponses,
        bool hasRewards,
        bool hasTimers,
        bool hasChatTriggers,
        bool hasPickLists
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

        if (
            (commands.Any(c => c.Tier == "code") || codeScripts.Count > 0)
            && !capabilities.Contains("executes custom code")
        )
            capabilities.Add("executes custom code");

        // The capabilities each bundled script's source declares, next to the pipeline-action ones (D4).
        foreach (string capability in codeScripts.SelectMany(s => s.DeclaredCapabilities))
        {
            string mapped = ScriptCapability(capability);
            if (!capabilities.Contains(mapped))
                capabilities.Add(mapped);
        }

        if (hasWidgets)
            capabilities.Add("adds overlay widgets (installed as unverified custom widgets)");
        if (hasSounds)
            capabilities.Add("adds sound clips");
        if (hasAssets)
            capabilities.Add("adds media assets (images/audio for overlays)");
        if (hasDataSources)
            capabilities.Add("connects an external data source (credential must be re-entered)");
        if (hasEventResponses)
            capabilities.Add(
                "responds to channel events (overwrites the existing per-event responses)"
            );
        if (hasRewards)
            capabilities.Add(
                "adds channel point rewards (created locally; synced to Twitch on demand)"
            );
        if (hasTimers)
            capabilities.Add("adds chat timers");
        if (hasChatTriggers)
            capabilities.Add("reacts to chat keywords");
        if (hasPickLists)
            capabilities.Add("adds pick lists");

        return capabilities;
    }
}
