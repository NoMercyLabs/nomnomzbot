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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Seeds the <c>!raid</c> raid-out flow as an ORDINARY editable pipeline built purely from generic
/// blocks — <c>start_raid</c>, <c>obs_switch_scene</c>, <c>send_message</c>, <c>wait</c>. Nothing here
/// is a bespoke "raid feature": every step is a block the pipeline builder already offers, so the
/// streamer can reorder it, retime it, swap the OBS scene, drop the emote lines, or delete the whole
/// thing.
/// <para>
/// Deliberately does NOT try to predict when Twitch actually commits the raid (a fixed countdown):
/// three live recalibrations of that guessed offset (90s -> 103s -> 116s) each came back reported as
/// still wrong by the same margin, because there is no way to observe Twitch's internal timer from the
/// outside. Stopping the stream, pausing the music, and the final "we've raided out" line instead fire
/// REACTIVELY off the <c>channel.raid.out</c> event response — see <see cref="RaidCommitFlowSeeder"/> —
/// which needs no predicted offset at all, since Twitch redirects viewers on its own clock regardless
/// of when this bot stops its own stream.
/// </para>
/// <para>
/// Every step is a plain TOP-LEVEL leaf — never a <c>detached_step</c>/<c>try</c> block. Confirmed live
/// 2026-09-01: <c>!raid</c> executes through <c>ChatMessageHandler</c>'s flat
/// <c>Command.PipelineGraphJson</c> graph (never by <c>PipelineId</c>), and that flat reader has NO
/// concept of nested blocks at all — a <c>detached_step</c> wrapper row there just reads back as an
/// ordinary step whose <c>ActionType</c> is the literal string <c>"detached_step"</c>, which has no
/// registered action and fails closed immediately. <see cref="PipelineStep.ContinueOnError"/> is the
/// ONE thing the flat runtime actually understands for "a failure here must not abort the rest of the
/// raid" — set directly on <c>obs_switch_scene</c>, the one step in THIS pipeline that legitimately can
/// fail without it mattering, matching the legacy bot's own fire-and-forget
/// <c>_ = SwitchToEndingScene(...)</c> without relying on a block-kind the hot chat path can't run.
/// </para>
/// </summary>
/// <remarks>
/// Idempotent: upserts by the natural key <c>(BroadcasterId, NameNormalized)</c>. A channel that already
/// has a <c>raid</c> command is left completely alone — the streamer's own edits are never overwritten.
/// Order 82 — after <see cref="DefaultCommandsSeeder"/> (80), because it FK-references Channel rows.
/// </remarks>
public sealed class RaidFlowSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public RaidFlowSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 82;

    private const string CommandName = "raid";

    /// <summary>The startup <see cref="ISeeder"/> pass: seeds every channel.</summary>
    public Task SeedAsync(CancellationToken ct = default) => SeedAsync(broadcasterId: null, ct);

    /// <summary>
    /// Seeds the raid flow for a single channel or, when <paramref name="broadcasterId"/> is null, every
    /// channel that does not already have a <c>raid</c> command.
    /// </summary>
    public async Task SeedAsync(Guid? broadcasterId, CancellationToken ct = default)
    {
        List<Guid> channelIds = broadcasterId is { } id
            ? [id]
            : await _db.Channels.Select(c => c.Id).ToListAsync(ct);

        if (channelIds.Count == 0)
            return;

        List<Command> existing = await _db
            .Commands.Where(c =>
                channelIds.Contains(c.BroadcasterId) && c.NameNormalized == CommandName
            )
            .ToListAsync(ct);

        // A command that already carries a BUILT pipeline is the streamer's own and is never touched. One
        // whose pipeline has no steps is a STUB, not a finished flow: skipping it (the original rule, which
        // looked only at whether the name existed) left `!raid` running an empty pipeline — the command
        // answered nothing at all on stream while the seeder considered itself satisfied.
        HashSet<Guid> pipelineIdsWithSteps =
        [
            .. await _db
                .PipelineSteps.Where(s => channelIds.Contains(s.BroadcasterId))
                .Select(s => s.PipelineId)
                .Distinct()
                .ToListAsync(ct),
        ];

        Dictionary<Guid, Command> stubs = existing
            .Where(c => c.PipelineId is null || !pipelineIdsWithSteps.Contains(c.PipelineId.Value))
            .GroupBy(c => c.BroadcasterId)
            .ToDictionary(g => g.Key, g => g.First());

        HashSet<Guid> alreadyBuilt =
        [
            .. existing
                .Where(c => c.PipelineId is { } pid && pipelineIdsWithSteps.Contains(pid))
                .Select(c => c.BroadcasterId),
        ];

        foreach (Guid channelId in channelIds)
        {
            if (alreadyBuilt.Contains(channelId))
                continue;

            stubs.TryGetValue(channelId, out Command? stub);

            Pipeline? pipeline = stub?.PipelineId is { } stubPipelineId
                ? await _db.Pipelines.FirstOrDefaultAsync(
                    p => p.Id == stubPipelineId && p.BroadcasterId == channelId,
                    ct
                )
                : null;

            if (pipeline is null)
            {
                pipeline = new()
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = channelId,
                    Name = "Raid out",
                    Description =
                        "Starts a Twitch raid, switches OBS to the ending scene, then says goodbye in "
                        + "chat. Every step is an ordinary block — reorder, retime or remove any of it. "
                        + "Stopping the stream, pausing the music and confirming the raid happen "
                        + "separately once Twitch reports the raid as under way (see \"Raid committed\").",
                    TriggerKind = "command",
                    IsEnabled = true,
                };
                _db.Pipelines.Add(pipeline);
            }

            int order = 0;
            foreach (SeedStep step in BuildSteps())
            {
                _db.PipelineSteps.Add(
                    new()
                    {
                        Id = Guid.CreateVersion7(),
                        PipelineId = pipeline.Id,
                        BroadcasterId = channelId,
                        ActionType = step.ActionType,
                        ConfigJson = step.ConfigJson,
                        ContinueOnError = step.ContinueOnError,
                        Order = order++,
                        IsEnabled = true,
                    }
                );
            }

            // The streamer's own stub row is kept (its name, wording and any tweaks) and simply pointed at
            // the flow that now exists; only a channel with no raid command at all gets a new row.
            if (stub is not null)
            {
                stub.PipelineId = pipeline.Id;
                stub.Tier = "pipeline";
                stub.IsEnabled = true;
                continue;
            }

            _db.Commands.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = channelId,
                    Name = CommandName,
                    NameNormalized = CommandName,
                    Description = "Raid another live channel.",
                    Tier = "pipeline",
                    PipelineId = pipeline.Id,
                    // Raiding hands your entire audience to someone else and ends the stream — the
                    // broadcaster alone decides that, never a moderator.
                    MinPermissionLevel = PermissionLevel.Broadcaster.ToLevelValue(),
                    IsEnabled = true,
                }
            );
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>The wire-shape action graph for THIS flow, exactly as seeded onto a fresh channel — the v1
    /// payload <c>PipelinePlatformContentSeeder</c> (S-ADMIN-2d) publishes as the <c>pipeline</c>-kind
    /// platform content definition keyed <c>raid_out</c>. Built from fresh, unpersisted
    /// <see cref="PipelineStep"/> rows (never touches the DB) so both the tenant-seeding path above and the
    /// platform-content payload come from the SAME step list — they can never drift apart.</summary>
    internal static string BuildPlatformContentPayloadJson()
    {
        int order = 0;
        List<PipelineStep> steps =
        [
            .. BuildSteps()
                .Select(step => new PipelineStep
                {
                    Id = Guid.NewGuid(),
                    ActionType = step.ActionType,
                    ConfigJson = step.ConfigJson,
                    ContinueOnError = step.ContinueOnError,
                    Order = order++,
                    IsEnabled = true,
                }),
        ];
        return PipelineGraphBuilder.BuildGraphJson(steps);
    }

    private sealed record SeedStep(
        string ActionType,
        string ConfigJson,
        bool ContinueOnError = false
    );

    /// <summary>
    /// The flow, in order. The raid call goes first; everything after it is chat theatre — no timing is
    /// predicted anywhere in this pipeline (see the class doc comment for why).
    /// </summary>
    private static IEnumerable<SeedStep> BuildSteps()
    {
        // Start the raid, and nothing else. Everything that used to follow — the ending scene, the
        // call-out messages — was fired optimistically here, before Twitch had confirmed anything, so a
        // raid it rejected (target offline, bad name, no permission) still switched the scene and posted
        // to chat. Those steps now hang off channel.raid.start (RaidStartFlowSeeder), and stopping the
        // stream and the music off channel.raid.out (RaidCommitFlowSeeder), which fires when the raid
        // has actually executed.
        //
        // No countdown and no wait_until_raid_fires: Twitch's own timer commits the raid on ITS clock,
        // and both halves of the flow are now driven by Twitch's own events. Three live recalibrations
        // of a fixed offset (90s -> 103s -> 116s) each came back still wrong by the same margin — the
        // fixed wait was the wrong tool, not an under-tuned constant.
        yield return new("start_raid", """{"target":"{args.1}"}""");
    }
}
