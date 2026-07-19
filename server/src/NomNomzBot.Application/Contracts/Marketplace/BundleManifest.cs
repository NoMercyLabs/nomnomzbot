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

/// <summary>
/// The bundle format constants (marketplace.md D1). A bundle ZIP is <c>/manifest.json</c> (a
/// <see cref="BundleManifest"/>) plus one entry per item under its per-type folder. Every contract in this
/// folder is <c>SchemaVersion</c>-stamped; validation is "deserialize into the typed contract + validate",
/// no JSON-Schema engine.
/// </summary>
public static class BundleFormat
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Hard cap on an importable ZIP (compressed size); larger uploads are rejected outright.</summary>
    public const long MaxBundleBytes = 20 * 1024 * 1024;

    public const string ManifestEntryName = "manifest.json";

    // The exportable item types (D1) — the `Type` of an `ExportItemRef` and of a manifest item.
    public const string PipelineType = "pipeline";
    public const string CommandType = "command";
    public const string WidgetType = "widget";
    public const string SoundType = "sound";
    public const string AssetType = "asset";
    public const string CustomDataSourceType = "custom_data_source";
    public const string EventResponseType = "event_response";
    public const string RewardType = "reward";
    public const string TimerType = "timer";
    public const string ChatTriggerType = "chat_trigger";
    public const string PickListType = "pick_list";
    public const string CodeScriptType = "code_script";
}

/// <summary>The author-supplied metadata of a bundle (name/version/attribution).</summary>
public sealed record BundleMetadata(
    string Name,
    string Version,
    string? Author,
    string? License,
    string? Description
);

/// <summary>One item listed in the manifest: its type, name, ZIP entry path, and dependency edges.</summary>
public sealed record BundleManifestItem(
    string Type,
    string Name,
    string Path,
    IReadOnlyList<string> Dependencies
);

/// <summary>
/// The <c>/manifest.json</c> of a bundle ZIP (marketplace.md D1): the schema version, the author metadata,
/// and the item list with per-item dependency edges (<c>"&lt;type&gt;:&lt;name&gt;"</c>).
/// </summary>
public sealed record BundleManifest
{
    public int SchemaVersion { get; init; } = BundleFormat.CurrentSchemaVersion;
    public required BundleMetadata Metadata { get; init; }
    public IReadOnlyList<BundleManifestItem> Items { get; init; } = [];
}
