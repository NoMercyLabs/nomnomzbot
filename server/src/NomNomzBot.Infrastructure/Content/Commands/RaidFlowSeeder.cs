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
/// <para>
/// Every step is a plain TOP-LEVEL leaf — never a <c>detached_step</c>/<c>try</c> block. Confirmed live
/// 2026-09-01: <c>!raid</c> executes through <c>ChatMessageHandler</c>'s flat
/// <c>Command.PipelineGraphJson</c> graph (never by <c>PipelineId</c>), and that flat reader has NO
/// concept of nested blocks at all — a <c>detached_step</c> wrapper row there just reads back as an
/// ordinary step whose <c>ActionType</c> is the literal string <c>"detached_step"</c>, which has no
/// registered action and fails closed immediately. <see cref="PipelineStep.ContinueOnError"/> is the
/// ONE thing the flat runtime actually understands for "a failure here must not abort the rest of the
/// raid" — set directly on the three steps that legitimately can fail without it mattering
/// (<c>obs_switch_scene</c>, <c>obs_streaming</c>, <c>music_pause</c>), matching the legacy bot's own
/// per-action try/catch (and, for the scene switch, its fire-and-forget
/// <c>_ = SwitchToEndingScene(...)</c>) without relying on a block-kind the hot chat path can't run.
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

    private sealed record SeedStep(
        string ActionType,
        string ConfigJson,
        bool ContinueOnError = false
    );

    /// <summary>
    /// The flow, in order. The raid fires first because Twitch's 90s timer starts the moment it returns;
    /// everything after it is chat theatre timed to land just before Twitch pulls the audience across.
    /// </summary>
    private static IEnumerable<SeedStep> BuildSteps()
    {
        yield return new("start_raid", """{"target":"{args.1}"}""");
        // ContinueOnError=true: matches the legacy bot's fire-and-forget `_ = SwitchToEndingScene(...)` —
        // an OBS hiccup here must never take down the countdown, "RAID LIVE!", or stopping the
        // stream/music (confirmed live 2026-09-01: without this, "OBS connection closed" on this ONE
        // step killed the entire rest of the raid while Twitch's clock kept ticking).
        yield return new("obs_switch_scene", """{"scene":"Ending"}""", ContinueOnError: true);
        // {args.1} is the template engine's own single-brace token syntax (matches start_raid's
        // {"target":"{args.1}"} above) — confirmed live 2026-09-01: an earlier version of this line
        // wrapped it in an EXTRA decorative brace pair (meant to double-escape it inside this raw C#
        // interpolated string), which the resolver only stripped the inner layer of, so chat saw the
        // literal text "RAID INCOMING to {jddoesdev}!" instead of the resolved name.
        yield return new(
            "send_message",
            $$"""{"message":"RAID INCOMING to {args.1}! Raiding in {{TwitchRaidWindowSeconds - 2}} seconds..."}"""
        );
        yield return new("wait", """{"seconds":1}""");
        yield return new(
            "send_message",
            """{"message":"Big bird raid stoney90Hmmm Big bird raid stoney90Hmmm Big bird raid stoney90Hmmm"}"""
        );
        yield return new("wait", """{"seconds":1}""");
        yield return new(
            "send_message",
            """{"message":"Big bird raid 🦅 Big bird raid 🦅 Big bird raid 🦅"}"""
        );

        // Countdown. Only the last stretch is announced — a "45 seconds left" line this early reads as
        // spam, so the earlier waits are silent and the chat only starts counting at 15s.
        // 2s of intro lines have already elapsed, and the announced countdown must open at T+73 so
        // "Raid in 15" is genuinely 15 seconds before Twitch fires at T+90 (less the 2s safety margin).
        yield return new("wait", """{"seconds":40}""");
        yield return new("wait", """{"seconds":31}""");
        foreach (int secondsLeft in new[] { 15, 10, 5, 3, 2, 1 })
        {
            yield return new(
                "send_message",
                $$"""{"message":"Raid in {{secondsLeft}} second{{(secondsLeft == 1 ? "" : "s")}}..."}"""
            );
            yield return new("wait", $$"""{"seconds":{{WaitAfter(secondsLeft)}}}""");
        }

        yield return new(
            "send_message",
            """{"message":"RAID LIVE! We're heading over now! Let's go!"}"""
        );
        // ContinueOnError on both — one failing (e.g. the same OBS bridge drop that can hit the scene
        // switch earlier) must never stop the other, matching the legacy bot's two separate try/catches
        // around StopStreaming and PauseSpotify.
        yield return new("obs_streaming", """{"action":"stop"}""", ContinueOnError: true);
        yield return new("music_pause", "{}", ContinueOnError: true);
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
