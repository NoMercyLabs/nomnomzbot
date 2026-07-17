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
