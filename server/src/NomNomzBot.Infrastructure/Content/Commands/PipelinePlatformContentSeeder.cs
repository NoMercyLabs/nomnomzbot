// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Registers the three system pipelines <see cref="RaidFlowSeeder"/>/<see cref="RaidStartFlowSeeder"/>/
/// <see cref="RaidCommitFlowSeeder"/> already ship as <c>Kind = "pipeline"</c>
/// <see cref="PlatformContentDefinition"/>/<see cref="PlatformContentVersion"/> v1 rows (S-ADMIN-2d), then
/// backfills provenance onto every already-seeded tenant <see cref="PipelineEntity"/> row for those flows —
/// the platform-admin.md §7 exit condition applied to pipelines: without this pass a pre-existing tenant row
/// reads as tenant-authored (<c>PlatformSourceDefinitionId = null</c>) and is silently excluded from every
/// future <c>update_in_place_where_untouched</c> publish (exactly the gap S-ADMIN-2c first shipped for
/// widgets — see <c>PlatformContentService</c>'s class doc).
/// </summary>
/// <remarks>
/// <para>
/// <c>EventResponseDefaultsSeeder</c> is deliberately NOT a source here: it seeds a disabled, plain
/// <c>chat_message</c> <see cref="EventResponse"/> stub per event type (never a <see cref="PipelineEntity"/>,
/// never a built graph) — there is no pipeline-shaped content it produces for this spine to register or
/// fan out to. The task brief that named it alongside the three raid seeders does not match what the seeder
/// actually builds; this seeder only registers the three flows that are genuinely backed by a
/// <see cref="PipelineEntity"/> with real steps.
/// </para>
/// <para>
/// Backfill identifies each flow's tenant row by the SAME natural key its own seeder already uses for
/// idempotency — never by <see cref="PipelineEntity.Name"/> text (the seeder principle's "never match by
/// name alone" guardrail): <c>raid_out</c> via the tenant's <c>raid</c> <see cref="Command"/>
/// (<see cref="Command.NameNormalized"/>) -> <see cref="Command.PipelineId"/>; <c>raid_start</c>/
/// <c>raid_commit</c> via the tenant's <c>channel.raid.start</c>/<c>channel.raid.out</c>
/// <see cref="EventResponse"/> rows with <c>ResponseType = "pipeline"</c> -> <see cref="EventResponse.PipelineId"/>.
/// A channel whose command/event-response was renamed away from the seeded default, or that has no built
/// pipeline yet, is left alone — nothing is guessed. Only rows with <c>PlatformSourceDefinitionId == null</c>
/// are touched (adopt-empty-stub, never overwrite). Runs on every boot (not just once) so a tenant seeded
/// by an EARLIER boot (before this slice existed) gets backfilled the first time this seeder runs after
/// upgrading.
/// </para>
/// Order 85 — after <see cref="RaidFlowSeeder"/> (82), <see cref="RaidCommitFlowSeeder"/> (83) and
/// <see cref="RaidStartFlowSeeder"/> (84): the definitions it creates don't need their tenant pipelines to
/// exist yet, but the backfill half does.
/// </remarks>
public sealed class PipelinePlatformContentSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public PipelinePlatformContentSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 85;

    private sealed record FlowKey(string Key, string DisplayName, Func<string> BuildPayloadJson);

    private static readonly FlowKey[] Flows =
    [
        new("raid_out", "Raid out", RaidFlowSeeder.BuildPlatformContentPayloadJson),
        new("raid_start", "Raid starting", RaidStartFlowSeeder.BuildPlatformContentPayloadJson),
        new("raid_commit", "Raid committed", RaidCommitFlowSeeder.BuildPlatformContentPayloadJson),
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        Dictionary<string, PlatformContentDefinition> definitionsByKey =
            await EnsureDefinitionsAsync(ct);

        await BackfillRaidOutAsync(definitionsByKey["raid_out"], ct);
        await BackfillEventResponseFlowAsync(
            definitionsByKey["raid_start"],
            "channel.raid.start",
            ct
        );
        await BackfillEventResponseFlowAsync(
            definitionsByKey["raid_commit"],
            "channel.raid.out",
            ct
        );
    }

    /// <summary>Creates the <c>pipeline</c>-kind definition + published v1 version for each flow that
    /// doesn't already have one. Definition creation has no tenant-data dependency — it runs from the
    /// seeders' own static step lists — so it is safe on a fresh install with zero channels.</summary>
    private async Task<Dictionary<string, PlatformContentDefinition>> EnsureDefinitionsAsync(
        CancellationToken ct
    )
    {
        Dictionary<string, PlatformContentDefinition> existing = await _db
            .PlatformContentDefinitions.Where(d =>
                d.Kind == PlatformContentKinds.Pipeline && Flows.Select(f => f.Key).Contains(d.Key)
            )
            .ToDictionaryAsync(d => d.Key, StringComparer.Ordinal, ct);

        Dictionary<string, PlatformContentDefinition> byKey = new(StringComparer.Ordinal);
        bool anyCreated = false;

        foreach (FlowKey flow in Flows)
        {
            if (existing.TryGetValue(flow.Key, out PlatformContentDefinition? found))
            {
                byKey[flow.Key] = found;
                continue;
            }

            DateTime now = DateTime.UtcNow;
            string payloadJson = flow.BuildPayloadJson();
            string hash = PlatformContentHash.ComputeHash(payloadJson);

            PlatformContentDefinition created = new()
            {
                Kind = PlatformContentKinds.Pipeline,
                Key = flow.Key,
                DisplayName = flow.DisplayName,
                CreatedAt = now,
                // No IamPrincipal exists for a system seed pass — Guid.Empty marks "seeded, not authored
                // by a human operator" (same convention as PlatformContentDefinitionSeeder).
                CreatedByPrincipalId = Guid.Empty,
            };
            _db.PlatformContentDefinitions.Add(created);

            PlatformContentVersion version = new()
            {
                DefinitionId = created.Id,
                Version = 1,
                ContentHash = hash,
                PayloadJson = payloadJson,
                DraftedAt = now,
                DraftedByPrincipalId = Guid.Empty,
                PublishedAt = now,
                PublishedByPrincipalId = Guid.Empty,
            };
            _db.PlatformContentVersions.Add(version);

            created.CurrentVersionId = version.Id;
            created.LatestDraftVersionId = version.Id;

            byKey[flow.Key] = created;
            anyCreated = true;
        }

        if (anyCreated)
            await _db.SaveChangesAsync(ct);

        return byKey;
    }

    /// <summary>Stamps provenance on every tenant's <c>raid</c> command's pipeline — the flat command-graph
    /// flow seeded by <see cref="RaidFlowSeeder"/>.</summary>
    private async Task BackfillRaidOutAsync(
        PlatformContentDefinition definition,
        CancellationToken ct
    )
    {
        List<Guid> pipelineIds = await _db
            .Commands.Where(c => c.NameNormalized == "raid" && c.PipelineId != null)
            .Select(c => c.PipelineId!.Value)
            .Distinct()
            .ToListAsync(ct);

        await StampUnstampedAsync(definition, pipelineIds, ct);
    }

    /// <summary>Stamps provenance on every tenant's pipeline wired to <paramref name="eventType"/> via a
    /// <c>ResponseType = "pipeline"</c> <see cref="EventResponse"/> row — the shape
    /// <see cref="RaidStartFlowSeeder"/>/<see cref="RaidCommitFlowSeeder"/> both seed.</summary>
    private async Task BackfillEventResponseFlowAsync(
        PlatformContentDefinition definition,
        string eventType,
        CancellationToken ct
    )
    {
        List<Guid> pipelineIds = await _db
            .EventResponses.Where(r =>
                r.EventType == eventType && r.ResponseType == "pipeline" && r.PipelineId != null
            )
            .Select(r => r.PipelineId!.Value)
            .Distinct()
            .ToListAsync(ct);

        await StampUnstampedAsync(definition, pipelineIds, ct);
    }

    private async Task StampUnstampedAsync(
        PlatformContentDefinition definition,
        List<Guid> pipelineIds,
        CancellationToken ct
    )
    {
        if (pipelineIds.Count == 0)
            return;

        List<PipelineEntity> unstamped = await _db
            .Pipelines.Where(p =>
                pipelineIds.Contains(p.Id) && p.PlatformSourceDefinitionId == null
            )
            .ToListAsync(ct);

        if (unstamped.Count == 0)
            return;

        DateTime syncedAt = DateTime.UtcNow;
        foreach (PipelineEntity row in unstamped)
        {
            row.PlatformSourceDefinitionId = definition.Id;
            row.PlatformSourceVersion = 1;
            // Hashed from the ROW'S OWN current GraphJsonCache, never the definition's payload hash: a
            // flat-command-graph pipeline (RaidFlowSeeder's "raid_out") never populates GraphJsonCache at
            // all — only its PipelineStep rows — so its live hash is PlatformContentHash.ComputeHash(null)
            // ("{}"'s hash), not the payload's hash. Stamping the row's OWN current hash is always
            // self-consistent (this backfill's snapshot reads back as "untouched" the moment it lands,
            // regardless of whether the cache is populated yet) — the same "capture what's actually there,
            // never assume" caution BackfillWidgetPlatformSourceProvenance applied by leaving its hash null.
            row.PlatformSourceHash = PlatformContentHash.ComputeHash(row.GraphJsonCache);
            row.PlatformSourceSyncedAt = syncedAt;
        }

        await _db.SaveChangesAsync(ct);
    }
}
