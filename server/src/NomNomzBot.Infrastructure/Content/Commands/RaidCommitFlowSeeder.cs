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
/// Wires the <c>channel.raid.out</c> event response to a small pipeline — stop the stream, pause the
/// music, confirm the raid in chat — so those three actions fire REACTIVELY off Twitch's own signal
/// that the raid has HAPPENED, instead of a guessed elapsed-time offset (see <see cref="RaidFlowSeeder"/>
/// for why the fixed-countdown approach was abandoned 2026-09-02).
///
/// <para>That signal is the <c>channel.raid</c> subscription keyed on <c>from_broadcaster_user_id</c>,
/// which Twitch sends once the raid executes and the viewers have moved;
/// <c>OutgoingRaidAlertHandler</c> turns it into this response and seeds
/// <c>user</c>/<c>user.id</c>/<c>user.name</c>/<c>viewers</c> as template variables. It used to be
/// <c>channel.moderate</c>'s <c>raid</c> action instead — which fires when the countdown STARTS, so this
/// pipeline ended the broadcast at the beginning of the raid, taking the countdown and the outro with it.
/// The countdown moment is now <c>channel.raid.start</c>, and anything that belongs during the raid rather
/// than after it goes there.</para>
/// </summary>
/// <remarks>
/// Idempotent: <see cref="EventResponseDefaultsSeeder"/> (Order 81) already guarantees a
/// <c>channel.raid.out</c> row exists for every channel (disabled, <c>chat_message</c>, empty
/// <c>Message</c>) before this seeder runs. A row already pointed at a BUILT pipeline, or carrying the
/// streamer's own non-empty chat message, is the streamer's own configuration and is never touched —
/// only the untouched default stub gets wired up. Order 83 — after <see cref="RaidFlowSeeder"/> (82),
/// no FK dependency between them, just keeping the raid-flow seeders adjacent.
/// </remarks>
public sealed class RaidCommitFlowSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public RaidCommitFlowSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 83;

    private const string EventType = "channel.raid.out";

    /// <summary>The startup <see cref="ISeeder"/> pass: seeds every channel.</summary>
    public Task SeedAsync(CancellationToken ct = default) => SeedAsync(broadcasterId: null, ct);

    /// <summary>
    /// Seeds the raid-commit flow for a single channel or, when <paramref name="broadcasterId"/> is
    /// null, every channel whose <c>channel.raid.out</c> response is still the untouched default.
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
                Name = "Raid committed",
                Description =
                    "Stops the stream, pauses the music, and confirms the raid in chat once Twitch "
                    + "reports the raid as under way. Every step is an ordinary block.",
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
        // ContinueOnError on both — one failing (a flaky OBS bridge, say) must never stop the other,
        // matching the legacy bot's two separate try/catches around StopStreaming and PauseSpotify.
        yield return new("obs_streaming", """{"action":"stop"}""", ContinueOnError: true);
        yield return new("music_pause", "{}", ContinueOnError: true);
        // {user} is the display name OutgoingRaidAlertHandler seeds for channel.raid.out (matches
        // {user.id}/{user.name}/{viewers}, per EventResponsePresetCatalog).
        yield return new(
            "send_message",
            """{"message":"We've raided out to {user}! Thanks for joining!"}"""
        );
    }
}
