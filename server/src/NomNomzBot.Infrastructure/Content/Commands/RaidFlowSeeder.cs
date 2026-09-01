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

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Seeds the <c>!raid</c> raid-out flow as an ORDINARY editable pipeline built purely from generic
/// blocks — <c>start_raid</c>, <c>obs_switch_scene</c>, <c>send_message</c>, <c>wait</c>,
/// <c>obs_streaming</c>, <c>music_pause</c>. Nothing here is a bespoke "raid feature": every step is a
/// block the pipeline builder already offers, so the streamer can reorder it, retime it, swap the OBS
/// scene, drop the emote lines, or delete the whole thing.
/// <para>
/// The timing is the part that is NOT arbitrary. Twitch's <c>POST /raids</c> starts a fixed
/// <b>90-second</b> server-side timer and there is no API to commit the raid earlier — it auto-fires at
/// exactly T+90s. So the raid call goes FIRST and the countdown is built to land just before that
/// moment; a countdown that finishes early leaves viewers watching silence, and one that overruns
/// announces a raid that has already happened. The waits below sum to ~88s for that reason.
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

    /// <summary>Twitch's fixed server-side raid window; the raid auto-fires at T+90s.</summary>
    private const int TwitchRaidWindowSeconds = 90;

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
                        "Starts a Twitch raid, switches OBS to the ending scene, counts down in chat, "
                        + "then stops the stream and pauses the music. Every step is an ordinary block — "
                        + "reorder, retime or remove any of it.",
                    TriggerKind = "command",
                    IsEnabled = true,
                };
                _db.Pipelines.Add(pipeline);
            }

            int order = 0;
            foreach (SeedNode node in BuildSteps())
            {
                if (node.Detached is { } detachedLeaf)
                {
                    // pipeline-tree-and-editor.md §1.1/§3.1 item #4 — the OBS scene switch rides a
                    // detached_step wrapper so its own failure or slowness can never block or abort the
                    // rest of the raid (matches the legacy bot's fire-and-forget `_ = SwitchToEndingScene(...)`).
                    PipelineStep wrapper = new()
                    {
                        Id = Guid.CreateVersion7(),
                        PipelineId = pipeline.Id,
                        BroadcasterId = channelId,
                        BlockKind = "detached_step",
                        BlockConfigJson = "{}",
                        ActionType = "detached_step",
                        Order = order++,
                        IsEnabled = true,
                    };
                    _db.PipelineSteps.Add(wrapper);
                    _db.PipelineSteps.Add(
                        new()
                        {
                            Id = Guid.CreateVersion7(),
                            PipelineId = pipeline.Id,
                            BroadcasterId = channelId,
                            ParentStepId = wrapper.Id,
                            ActionType = detachedLeaf.ActionType!,
                            ConfigJson = detachedLeaf.ConfigJson!,
                            Order = order++,
                            IsEnabled = true,
                        }
                    );
                    continue;
                }

                _db.PipelineSteps.Add(
                    new()
                    {
                        Id = Guid.CreateVersion7(),
                        PipelineId = pipeline.Id,
                        BroadcasterId = channelId,
                        ActionType = node.ActionType!,
                        ConfigJson = node.ConfigJson!,
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
                    Description = "Raid another live channel with a chat countdown.",
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

    /// <summary>One step in the seeded flow — either an ordinary blocking leaf, or a leaf wrapped in a
    /// <c>detached_step</c> block (fire-and-forget: dispatched but never awaited, its own failure never
    /// aborts the rest of the run).</summary>
    private sealed record SeedNode(string? ActionType, string? ConfigJson, SeedNode? Detached)
    {
        public static SeedNode Leaf(string actionType, string configJson) =>
            new(actionType, configJson, null);

        public static SeedNode DetachedLeaf(string actionType, string configJson) =>
            new(null, null, Leaf(actionType, configJson));
    }

    /// <summary>
    /// The flow, in order. The raid fires first because Twitch's 90s timer starts the moment it returns;
    /// everything after it is chat theatre timed to land just before Twitch pulls the audience across. The
    /// OBS scene switch sits second, detached (pipeline-tree-and-editor.md §1.1/§3.1 item #4) — mirroring
    /// the legacy bot's fire-and-forget `_ = SwitchToEndingScene(...)`, so a slow or unreachable OBS bridge
    /// can never stall or abort the countdown, the "RAID LIVE!" line, or stopping the stream/music — the
    /// entire rest of the raid must run regardless of OBS's state, exactly like it did before this flow was
    /// rebuilt as a pipeline.
    /// </summary>
    private static IEnumerable<SeedNode> BuildSteps()
    {
        yield return SeedNode.Leaf("start_raid", """{"target":"{args.1}"}""");
        yield return SeedNode.DetachedLeaf("obs_switch_scene", """{"scene":"Ending"}""");
        yield return SeedNode.Leaf(
            "send_message",
            $$"""{"message":"RAID INCOMING to {{"{{args.1}}"}}! Raiding in {{TwitchRaidWindowSeconds - 2}} seconds..."}"""
        );
        yield return SeedNode.Leaf("wait", """{"seconds":1}""");
        yield return SeedNode.Leaf(
            "send_message",
            """{"message":"Big bird raid stoney90Hmmm Big bird raid stoney90Hmmm Big bird raid stoney90Hmmm"}"""
        );
        yield return SeedNode.Leaf("wait", """{"seconds":1}""");
        yield return SeedNode.Leaf(
            "send_message",
            """{"message":"Big bird raid 🦅 Big bird raid 🦅 Big bird raid 🦅"}"""
        );

        // Countdown. Only the last stretch is announced — a "45 seconds left" line this early reads as
        // spam, so the earlier waits are silent and the chat only starts counting at 15s.
        // 2s of intro lines have already elapsed, and the announced countdown must open at T+73 so
        // "Raid in 15" is genuinely 15 seconds before Twitch fires at T+90 (less the 2s safety margin).
        yield return SeedNode.Leaf("wait", """{"seconds":40}""");
        yield return SeedNode.Leaf("wait", """{"seconds":31}""");
        foreach (int secondsLeft in new[] { 15, 10, 5, 3, 2, 1 })
        {
            yield return SeedNode.Leaf(
                "send_message",
                $$"""{"message":"Raid in {{secondsLeft}} second{{(secondsLeft == 1 ? "" : "s")}}..."}"""
            );
            yield return SeedNode.Leaf("wait", $$"""{"seconds":{{WaitAfter(secondsLeft)}}}""");
        }

        yield return SeedNode.Leaf(
            "send_message",
            """{"message":"RAID LIVE! We're heading over now! Let's go!"}"""
        );
        yield return SeedNode.Leaf("obs_streaming", """{"action":"stop"}""");
        yield return SeedNode.Leaf("music_pause", "{}");
    }

    /// <summary>How long to wait after announcing <paramref name="secondsLeft"/> before the next line —
    /// the gap to the following countdown mark, and for the final "1" the last second itself.</summary>
    private static int WaitAfter(int secondsLeft) =>
        secondsLeft switch
        {
            15 or 10 => 5,
            5 => 2,
            _ => 1,
        };
}
