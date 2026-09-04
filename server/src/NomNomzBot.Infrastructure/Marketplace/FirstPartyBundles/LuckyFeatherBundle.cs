// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO.Compression;
using System.Reflection;
using NomNomzBot.Application.Contracts.Marketplace;

namespace NomNomzBot.Infrastructure.Marketplace.FirstPartyBundles;

/// <summary>
/// The "Lucky Feather" first-party marketplace preset (marketplace.md D1) — a channel-points chest-steal
/// game, assembled entirely from generic primitives that already ship: <c>IScriptStorageService</c> holds
/// who has the feather; a <c>run_code</c> script steals it and schedules its own auto-hide through
/// <c>IScheduledPipelineService</c>; the overlay is a Vue widget that listens for the two events the steal
/// and expiry scripts push via <c>widget.emit</c>; the 7TV paint on each holder comes from
/// <c>ScriptHostBridge.GetUser</c>'s folded <c>paint</c> field (§4 of the feature slice) — nothing here is a
/// new bespoke game engine, a new scheduler, or a new widget-catalogue entry.
///
/// <para>
/// This is the FIRST first-party bundle: rather than exporting from a live channel's entities (the normal
/// <c>BundleExportService</c> path), it assembles the SAME ZIP shape by hand — the manifest + per-type JSON
/// entries — so <c>IBundleImportService.ImportAsync</c>/<c>InspectAsync</c> accept it with zero special
/// casing. Any drift from the real export conventions (entry paths, JSON dialect, slugs) would only ever
/// surface as an import failure, so <see cref="BuildZipAsync"/> reuses <see cref="BundleConventions"/> for
/// every one of those mechanics rather than re-deriving them.
/// </para>
/// </summary>
public static class LuckyFeatherBundle
{
    public const string WidgetName = "Lucky Feather";
    public const string StealScriptName = "Lucky Feather Steal";
    public const string ExpiryScriptName = "Lucky Feather Expiry";
    public const string StealPipelineName = "Lucky Feather Steal";
    public const string ExpiryPipelineName = "Lucky Feather Expiry";

    /// <summary>The storage key the steal/expiry scripts share — the "holder-of-the-feather" state.</summary>
    public const string HolderStorageKey = "feather.holder";

    /// <summary>Seconds the feather stays with a thief before the expiry pipeline auto-hides it.</summary>
    public const int HoldDurationSeconds = 120;

    // The steal script: reads the current holder, resolves the triggering chatter (with their 7TV paint
    // folded onto user.get's response — no second capability call), writes them as the new holder, pushes a
    // "steal" widget event carrying both the previous and new holder (each optionally paint-decorated), and
    // schedules the expiry pipeline through the SAME generic scheduling primitive a voice-swap auto-revert
    // uses. A thief who already holds the feather is a no-op — stealing your own feather does nothing.
    private const string StealScriptSource = """
        var previousRaw = nnz.api.storage.get('feather.holder');
        var previousHolder = previousRaw ? JSON.parse(previousRaw) : null;

        var thief = nnz.api.user.get();
        if (thief && (!previousHolder || previousHolder.id !== thief.id)) {
            var newHolder = {
                id: thief.id,
                displayName: thief.displayName,
                avatarUrl: thief.avatarUrl,
                paint: thief.paint || null
            };
            nnz.api.storage.set('feather.holder', JSON.stringify(newHolder));
            nnz.api.widget.emit('Lucky Feather', 'steal', { previousHolder: previousHolder, newHolder: newHolder });
            nnz.api.schedule.pipeline('Lucky Feather Expiry', 120);
        }
        """;

    // The expiry script: the feather goes back into hiding. Clears the shared storage key and tells the
    // overlay to drop the holder card — the "feather auto-hide" IScheduledPipelineService's own doc names as
    // its worked example.
    private const string ExpiryScriptSource = """
        nnz.api.storage.delete('feather.holder');
        nnz.api.widget.emit('Lucky Feather', 'hide', {});
        """;

    /// <summary>Builds the portable bundle ZIP, byte-identical in shape to one <c>BundleExportService</c> would
    /// produce for the same content — <c>IBundleImportService</c> never needs to know this one wasn't exported
    /// from a real channel.</summary>
    public static async Task<System.IO.Stream> BuildZipAsync(CancellationToken ct = default)
    {
        MemoryStream buffer = new();
        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            List<BundleManifestItem> items = [];

            // Code scripts first — the pipelines below re-link to them BY NAME (code_script:<name> edges),
            // and the importer creates every code script before it touches a run_code-bearing pipeline.
            items.Add(
                await WriteCodeScriptAsync(
                    archive,
                    StealScriptName,
                    "Steals the Lucky Feather for whoever triggers this pipeline.",
                    StealScriptSource,
                    ["storage.get", "storage.set", "user.get", "widget.emit", "schedule.pipeline"],
                    ct
                )
            );
            items.Add(
                await WriteCodeScriptAsync(
                    archive,
                    ExpiryScriptName,
                    "Auto-hides the Lucky Feather when its hold duration elapses.",
                    ExpiryScriptSource,
                    ["storage.delete", "widget.emit"],
                    ct
                )
            );

            items.Add(
                await WritePipelineAsync(
                    archive,
                    StealPipelineName,
                    "Runs the steal script and schedules the auto-hide.",
                    StealScriptName,
                    ct
                )
            );
            items.Add(
                await WritePipelineAsync(
                    archive,
                    ExpiryPipelineName,
                    "Clears the feather's holder and hides the overlay card.",
                    ExpiryScriptName,
                    ct
                )
            );

            items.Add(await WriteWidgetAsync(archive, ct));

            BundleManifest manifest = new()
            {
                Metadata = new(
                    Name: "Lucky Feather",
                    Version: "1.0.0",
                    Author: "NoMercy Labs",
                    License: "AGPL-3.0-or-later",
                    Description: "A channel-points chest-steal game: whoever holds the feather can have it stolen."
                ),
                Items = items,
            };
            await using System.IO.Stream entry = archive
                .CreateEntry(BundleFormat.ManifestEntryName)
                .Open();
            await using StreamWriter writer = new(entry);
            await writer.WriteAsync(BundleConventions.Serialize(manifest).AsMemory(), ct);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static async Task<BundleManifestItem> WriteCodeScriptAsync(
        ZipArchive archive,
        string name,
        string description,
        string source,
        IReadOnlyList<string> declaredCapabilities,
        CancellationToken ct
    )
    {
        CodeScriptExport export = new()
        {
            Name = name,
            Description = description,
            Language = "javascript",
            Files = new Dictionary<string, string> { ["index.ts"] = source },
            Manifest = new("index.ts", "script", "javascript", []),
            DeclaredCapabilities = declaredCapabilities,
        };
        string entryPath = BundleConventions.EntryPath(
            BundleFormat.CodeScriptType,
            BundleConventions.Slug(name)
        );
        await WriteJsonEntryAsync(archive, entryPath, export, ct);
        return new(BundleFormat.CodeScriptType, name, entryPath, []);
    }

    private static async Task<BundleManifestItem> WritePipelineAsync(
        ZipArchive archive,
        string name,
        string description,
        string boundScriptName,
        CancellationToken ct
    )
    {
        string graphJson =
            """{"nodes":[{"id":"n1","type":"run_code","config":{"code_script_name":"BOUND_SCRIPT_NAME"}}]}""".Replace(
                "BOUND_SCRIPT_NAME",
                boundScriptName
            );
        PipelineExport export = new()
        {
            Name = name,
            Description = description,
            TriggerKind = "manual",
            IsEnabled = true, // D4 still lands it disabled at import time — a run_code pipeline always does.
            GraphJson = graphJson,
        };
        string entryPath = BundleConventions.EntryPath(
            BundleFormat.PipelineType,
            BundleConventions.Slug(name)
        );
        await WriteJsonEntryAsync(archive, entryPath, export, ct);
        return new(
            BundleFormat.PipelineType,
            name,
            entryPath,
            [$"{BundleFormat.CodeScriptType}:{boundScriptName}"]
        );
    }

    private static async Task<BundleManifestItem> WriteWidgetAsync(
        ZipArchive archive,
        CancellationToken ct
    )
    {
        WidgetExport export = new()
        {
            Name = WidgetName,
            Description = "Shows who currently holds the Lucky Feather, and announces every steal.",
            Framework = "vue",
            Settings = new Dictionary<string, object?>
            {
                ["idleText"] = "The feather is hidden…",
                ["stolenTemplate"] = "",
                ["bannerDurationMs"] = 5000,
                ["accentColor"] = "#f4b942",
            },
            EventSubscriptions = [],
            SourceCode = ReadWidgetSource(),
        };
        string entryPath = BundleConventions.EntryPath(
            BundleFormat.WidgetType,
            BundleConventions.Slug(WidgetName)
        );
        await WriteJsonEntryAsync(archive, entryPath, export, ct);
        return new(BundleFormat.WidgetType, WidgetName, entryPath, []);
    }

    // The widget's authored Vue SFC ships as an embedded resource (Content/Widgets/Assets/*.vue is already
    // globbed as EmbeddedResource for the first-party catalogue's own assets) — reading it here, rather than
    // duplicating the source as a string literal, means the bundle can never drift from what actually ships.
    private static string ReadWidgetSource()
    {
        Assembly assembly = typeof(LuckyFeatherBundle).Assembly;
        const string resourceName =
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.lucky_feather.vue";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded Lucky Feather widget asset '{resourceName}' was not found in the assembly."
            );
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryPath,
        T value,
        CancellationToken ct
    )
    {
        await using System.IO.Stream entry = archive.CreateEntry(entryPath).Open();
        await using StreamWriter writer = new(entry);
        await writer.WriteAsync(BundleConventions.Serialize(value).AsMemory(), ct);
    }
}
