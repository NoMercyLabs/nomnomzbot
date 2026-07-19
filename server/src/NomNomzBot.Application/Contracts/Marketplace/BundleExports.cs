// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Marketplace;

// The versioned per-type export contracts (marketplace.md D1). Each is a strict ALLOWLIST of portable
// definition/config fields (D2): no ids, no tenant references, no tokens/secrets/PII, no runtime counters.
// Export maps entity → contract through these shapes; import maps contract → the owning module service's
// create request, so each entity's own validation runs.

/// <summary>A portable pipeline definition. The graph JSON is the builder document; step configs are already
/// credential-free (enforced at save time by <c>ICommandConfigValidator</c>).</summary>
public sealed record PipelineExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string TriggerKind { get; init; } = "manual";
    public bool IsEnabled { get; init; } = true;

    /// <summary>The visual-builder graph document (raw JSON), or null for an empty pipeline.</summary>
    public string? GraphJson { get; init; }
}

/// <summary>
/// A portable command definition — the fields the module create/update surface can recreate. A command that
/// executes a bundled pipeline carries <see cref="PipelineName"/>; the importer re-links it to the pipeline
/// imported from the same bundle (never to an id).
/// </summary>
public sealed record CommandExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }

    /// <summary>template | pipeline | code.</summary>
    public string Tier { get; init; } = "template";

    public int MinPermissionLevel { get; init; }
    public string? TemplateResponse { get; init; }
    public IReadOnlyList<string>? TemplateResponses { get; init; }

    /// <summary>The bundled pipeline this command executes, by manifest item name; null when standalone.</summary>
    public string? PipelineName { get; init; }

    public int CooldownSeconds { get; init; }
    public bool CooldownPerUser { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// A portable widget definition: config + the latest authored source. Never the overlay token, gallery
/// linkage, or compiled bundle — an imported widget is a <c>custom</c> (unverified, fail-closed CSP tier)
/// widget recompiled on the importing instance.
/// </summary>
public sealed record WidgetExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>vanilla | vue | react | svelte.</summary>
    public string Framework { get; init; } = "vanilla";

    public IReadOnlyDictionary<string, object?> Settings { get; init; } =
        new Dictionary<string, object?>();

    public IReadOnlyList<string> EventSubscriptions { get; init; } = [];

    /// <summary>The latest authored source; null when the widget never had an authored version.</summary>
    public string? SourceCode { get; init; }
}

/// <summary>
/// A portable sound clip: the library metadata plus the audio payload as a sibling ZIP entry (never a
/// storage key). The importer re-uploads the payload through the sound module, so format/size validation
/// and duration probing run again on the importing instance.
/// </summary>
public sealed record SoundExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>audio/mpeg | audio/ogg | audio/wav.</summary>
    public required string MimeType { get; init; }

    public int DefaultVolume { get; init; } = 80;

    /// <summary>ZIP entry path of the audio payload (inside <c>/sounds/</c>).</summary>
    public required string AudioPath { get; init; }
}

/// <summary>
/// A portable event response definition. Event responses are keyed per <see cref="EventType"/>, not named —
/// a channel has at most one response per event — so import is an UPSERT by event type (overwriting the
/// channel's existing/seeded row) rather than a create with rename-on-collision. A pipeline-typed response
/// carries <see cref="PipelineName"/> and is re-linked to the pipeline imported from the same bundle.
/// </summary>
public sealed record EventResponseExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;

    /// <summary>The event this response fires on (e.g. <c>channel.follow</c>) — the item's identity.</summary>
    public required string EventType { get; init; }

    /// <summary>chat_message | overlay | pipeline | none.</summary>
    public string ResponseType { get; init; } = "chat_message";

    public string? Message { get; init; }

    /// <summary>The bundled pipeline this response executes, by manifest item name; null when none.</summary>
    public string? PipelineName { get; init; }

    /// <summary>Additional key-value metadata (e.g. overlay widget id); template data, never credentials.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// A portable channel-point reward DEFINITION. The Twitch reward id is NEVER exported (D2) — an imported
/// reward lands as a local, bot-manageable definition with no Twitch linkage; the target channel's existing
/// sync/recreate endpoints push it to Twitch later, so import never needs a Twitch connection.
/// </summary>
public sealed record RewardExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Title { get; init; }
    public string? Description { get; init; }

    /// <summary>The chat template announced on redemption; null when none.</summary>
    public string? Response { get; init; }

    public int Cost { get; init; }

    /// <summary>Opt-in countdown for a time-limited reward (seconds); null = a normal reward.</summary>
    public int? TimerDurationSeconds { get; init; }

    /// <summary>The bundled pipeline redeeming this reward runs, by manifest item name; null when none.</summary>
    public string? PipelineName { get; init; }

    public bool IsEnabled { get; init; } = true;
}

/// <summary>A portable chat timer definition (rotating messages and/or a bundled pipeline on an interval).</summary>
public sealed record TimerExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = [];
    public int IntervalMinutes { get; init; } = 30;
    public int MinChatActivity { get; init; }
    public bool FireOnce { get; init; }

    /// <summary>The bundled pipeline this timer fires, by manifest item name; null when none.</summary>
    public string? PipelineName { get; init; }

    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// A portable keyword chat trigger. Identified by its <see cref="Pattern"/> — the manifest item name — so
/// rename-on-collision suffixes the pattern like any other free-text name.
/// </summary>
public sealed record ChatTriggerExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Pattern { get; init; }

    /// <summary>contains | exact | starts_with | regex.</summary>
    public string MatchType { get; init; } = "contains";

    public bool CaseSensitive { get; init; }
    public string? Response { get; init; }

    /// <summary>The bundled pipeline this trigger runs, by manifest item name; null when none.</summary>
    public string? PipelineName { get; init; }

    public int CooldownSeconds { get; init; } = 30;
    public int MinPermissionLevel { get; init; }
    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// A portable named pick-list. Entries may carry template placeholders (<c>{user}</c> etc.) — they are data
/// resolved at pick time, so no capability scan applies to them.
/// </summary>
public sealed record PickListExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Items { get; init; } = [];
}

/// <summary>The project manifest of a bundled code script — <c>{ entry, kind, framework, dependencies[] }</c>.</summary>
public sealed record CodeScriptManifestExport(
    string Entry,
    string Kind,
    string Framework,
    IReadOnlyList<string> Dependencies
);

/// <summary>
/// A portable custom-code script: the full multi-file project (path → source, plus its manifest) and the
/// capabilities its source declares. Import recreates the script through the code-script module (so
/// validate-on-save compiles it again on the importing instance) and ALWAYS lands it disabled (D4) — like
/// the <c>run_code</c> pipeline convention, an explicit owner enable is required before it can run.
/// </summary>
public sealed record CodeScriptExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Language { get; init; } = "typescript";

    /// <summary>The multi-file project source: relative path → content, including the entry file.</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } =
        new Dictionary<string, string>();

    public required CodeScriptManifestExport Manifest { get; init; }

    /// <summary>The capability keys the source declares (e.g. <c>chat.send</c>) — surfaced by inspect (D4).</summary>
    public IReadOnlyList<string> DeclaredCapabilities { get; init; } = [];
}

/// <summary>
/// A portable custom data source definition. The auth secret is NEVER exported (D2) — the field simply does
/// not exist on this contract; the import lands disabled with no credential and the importer fills it in.
/// </summary>
public sealed record CustomDataSourceExport
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>push | poll | socket.</summary>
    public required string SourceKind { get; init; }

    public string? PresetKey { get; init; }
    public string? EndpointUrl { get; init; }

    public IReadOnlyDictionary<string, string> FieldMap { get; init; } =
        new Dictionary<string, string>();

    public int? PollIntervalSeconds { get; init; }
}
