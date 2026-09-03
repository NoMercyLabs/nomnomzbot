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
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Wires the <c>channel.raid.start</c> event response to a small pipeline — switch to the ending scene
/// and post the raid call-out — so the countdown reacts to Twitch CONFIRMING the raid rather than to the
/// command optimistically assuming it took.
///
/// <para>These steps used to live in the <c>!raid</c> command itself (see <see cref="RaidFlowSeeder"/>),
/// which meant a raid Twitch rejected — target offline, bad name, no permission — still switched the
/// scene and spammed chat. Hanging them off <c>channel.raid.start</c> makes the whole flow deterministic:
/// the command starts a validated raid, this event drives the countdown, and <c>channel.raid.out</c>
/// (the raid actually executing) stops the stream and the music via
/// <see cref="RaidCommitFlowSeeder"/>.</para>
/// </summary>
/// <remarks>
/// Idempotent on the same terms as <see cref="RaidCommitFlowSeeder"/>: only the untouched default stub is
/// wired up; a row already pointed at a BUILT pipeline, or carrying the streamer's own chat message, is
/// their configuration and is never touched. Order 84 — after the other two raid seeders, no FK
/// dependency, just keeping the raid flow adjacent.
/// </remarks>
public sealed class RaidStartFlowSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public RaidStartFlowSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 84;

    private const string EventType = "channel.raid.start";

    /// <summary>The startup <see cref="ISeeder"/> pass: seeds every channel.</summary>
    public Task SeedAsync(CancellationToken ct = default) => SeedAsync(broadcasterId: null, ct);

    /// <summary>
    /// Seeds the raid-countdown flow for a single channel or, when <paramref name="broadcasterId"/> is
    /// null, every channel whose <c>channel.raid.start</c> response is still the untouched default.
    /// </summary>
    public async Task SeedAsync(Guid? broadcasterId, CancellationToken ct = default)
    {
        List<Guid> channelIds = broadcasterId is { } id
            ? [id]
            : await _db.Channels.Select(c => c.Id).ToListAsync(ct);

        if (channelIds.Count == 0)
            return;

        List<EventResponse> responses = await _db
            .EventResponses.Where(r =>
                channelIds.Contains(r.BroadcasterId) && r.EventType == EventType
            )
            .ToListAsync(ct);

        HashSet<Guid> pipelineIdsWithSteps =
        [
            .. await _db
                .PipelineSteps.Where(s => channelIds.Contains(s.BroadcasterId))
                .Select(s => s.PipelineId)
                .Distinct()
                .ToListAsync(ct),
        ];

        foreach (EventResponse response in responses)
        {
            // Already wired to a BUILT pipeline — the streamer's own (or a prior run of this seeder).
            if (
                response is { ResponseType: "pipeline", PipelineId: { } pid }
                && pipelineIdsWithSteps.Contains(pid)
            )
                continue;

            // A non-empty chat_message is the streamer's own customization (this is exactly what
            // Stoney's channel carried before tonight — the legacy bot's own hard-coded "We have
            // raided out to..." line) — never overwritten, only the untouched empty default is.
            if (
                response.ResponseType == "chat_message"
                && !string.IsNullOrWhiteSpace(response.Message)
            )
                continue;

            Guid channelId = response.BroadcasterId;
            Guid pipelineId = Guid.CreateVersion7();

            // Materialized BEFORE insert: EventResponseExecutor's "pipeline" ResponseType executes
            // Pipeline.GraphJsonCache (unlike the flat !raid chat-command path, which reads
            // PipelineSteps directly) — an empty cache falls back to "{}" and the pipeline runs no
            // steps at all. PipelineGraphBuilder needs each step's real Id, so build the rows first.
            int order = 0;
            List<PipelineStep> steps =
            [
                .. BuildSteps()
                    .Select(step => new PipelineStep
                    {
                        Id = Guid.CreateVersion7(),
                        PipelineId = pipelineId,
                        BroadcasterId = channelId,
                        ActionType = step.ActionType,
                        ConfigJson = step.ConfigJson,
                        ContinueOnError = step.ContinueOnError,
                        Order = order++,
                        IsEnabled = true,
                    }),
            ];

            Pipeline pipeline = new()
            {
                Id = pipelineId,
                BroadcasterId = channelId,
                Name = "Raid starting",
                Description =
                    "Switches to the ending scene and calls the raid out in chat, once Twitch confirms "
                    + "the raid has started. Every step is an ordinary block.",
                TriggerKind = "event",
                IsEnabled = true,
                GraphJsonCache = PipelineGraphBuilder.BuildGraphJson(steps),
            };
            _db.Pipelines.Add(pipeline);
            _db.PipelineSteps.AddRange(steps);

            response.ResponseType = "pipeline";
            response.PipelineId = pipeline.Id;
            // Message is left as-is (only null/whitespace can reach this point — the non-empty branch
            // above already `continue`d) rather than reassigned, so this write path carries no
            // TemplatedUserContent content and the guard has nothing to validate here.
            response.IsEnabled = true;
        }

        await _db.SaveChangesAsync(ct);
    }

    private sealed record SeedStep(
        string ActionType,
        string ConfigJson,
        bool ContinueOnError = false
    );

    private static IEnumerable<SeedStep> BuildSteps()
    {
        // ContinueOnError: an OBS hiccup here must never take down the chat call-out. Confirmed live
        // 2026-09-01 in the old command-driven version: "OBS connection closed" on this one step killed
        // the whole rest of the raid flow while Twitch's clock kept running.
        yield return new("obs_switch_scene", """{"scene":"Ending"}""", ContinueOnError: true);
        // {user} is the TARGET channel, seeded by OutgoingRaidStartedAlertHandler. The command version
        // used {args.1} — the raw text the streamer typed — which was whatever they typed, correct
        // capitalisation or not, and was posted even when Twitch rejected the raid.
        yield return new(
            "send_message",
            """{"message":"We're heading out to {user}, thanks for watching!"}"""
        );
    }
}
