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
/// Gives a channel's untouched <c>channel.raid</c> response the three things the old bot did for an
/// incoming raid: the chat welcome, the same line spoken, and a priority shoutout of the raider.
///
/// <para>The platform default row carries one chat line (or the tone catalogue's line) and no steps, so a
/// channel that follows it welcomes raiders in chat only. This seeder wires the channel's own row to a small
/// pipeline instead, and the row stops following the platform default — the executor ignores a pipeline on a
/// row that still follows it.</para>
/// </summary>
/// <remarks>
/// Idempotent on the same terms as <see cref="RaidStartFlowSeeder"/>: only a row that still follows the
/// platform default is wired up. A row the channel saved itself (its own message, its own pipeline, or an
/// off switch) has <c>FollowsPlatformDefault = false</c> and is never touched, and a row already pointing at
/// a BUILT pipeline is left alone. Order 86, after the other raid seeders.
/// </remarks>
public sealed class RaidResponseFlowSeeder(IApplicationDbContext db) : ISeeder
{
    private const string EventType = "channel.raid";

    private const string WelcomeLine =
        "{user} just raided with {viewers} viewers! Welcome raiders!";

    public int Order => 86;

    /// <summary>The startup <see cref="ISeeder"/> pass: seeds every channel.</summary>
    public Task SeedAsync(CancellationToken ct = default) => SeedAsync(broadcasterId: null, ct);

    /// <summary>Seeds a single channel or, when <paramref name="broadcasterId"/> is null, every channel.</summary>
    public async Task SeedAsync(Guid? broadcasterId, CancellationToken ct = default)
    {
        List<EventResponse> responses = await db
            .EventResponses.Where(r =>
                r.EventType == EventType
                && r.FollowsPlatformDefault
                && (broadcasterId == null || r.BroadcasterId == broadcasterId)
            )
            .ToListAsync(ct);

        if (responses.Count == 0)
            return;

        List<Guid> channelIds = responses.Select(r => r.BroadcasterId).Distinct().ToList();
        HashSet<Guid> pipelineIdsWithSteps =
        [
            .. await db
                .PipelineSteps.Where(s => channelIds.Contains(s.BroadcasterId))
                .Select(s => s.PipelineId)
                .Distinct()
                .ToListAsync(ct),
        ];

        foreach (EventResponse response in responses)
        {
            if (response.PipelineId is { } pid && pipelineIdsWithSteps.Contains(pid))
                continue;

            Guid pipelineId = Guid.CreateVersion7();
            int order = 0;
            List<PipelineStep> steps =
            [
                .. BuildSteps()
                    .Select(step => new PipelineStep
                    {
                        Id = Guid.CreateVersion7(),
                        PipelineId = pipelineId,
                        BroadcasterId = response.BroadcasterId,
                        ActionType = step.ActionType,
                        ConfigJson = step.ConfigJson,
                        Order = order++,
                        IsEnabled = true,
                    }),
            ];

            // GraphJsonCache is what EventResponseExecutor runs: an empty cache runs no steps at all.
            db.Pipelines.Add(
                new()
                {
                    Id = pipelineId,
                    BroadcasterId = response.BroadcasterId,
                    Name = "Raid response",
                    Description =
                        "Welcomes the raiders in chat, says it out loud and shouts out the raider. "
                        + "Every step is an ordinary block.",
                    TriggerKind = "event",
                    IsEnabled = true,
                    GraphJsonCache = PipelineGraphBuilder.BuildGraphJson(steps),
                }
            );
            db.PipelineSteps.AddRange(steps);

            response.ResponseType = "pipeline";
            response.PipelineId = pipelineId;
            response.IsEnabled = true;
            response.FollowsPlatformDefault = false;
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed record SeedStep(string ActionType, string ConfigJson);

    // The shoutout needs no flag for the raid case: ShoutoutAction reads event.name = channel.raid and
    // then queues it ahead of auto-shoutouts and speaks it.
    private static IEnumerable<SeedStep> BuildSteps()
    {
        yield return new("send_message", $$"""{"message":"{{WelcomeLine}}"}""");
        yield return new("play_tts", $$"""{"text":"{{WelcomeLine}}"}""");
        yield return new("shoutout", """{"user_id":"{user.id}"}""");
    }
}
