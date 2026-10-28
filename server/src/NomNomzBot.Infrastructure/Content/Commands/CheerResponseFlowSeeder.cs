// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Gives a channel's untouched <c>channel.cheer</c> response a thank-you that quotes the viewer, and reads
/// the viewer's own message aloud in the viewer's own saved voice.
///
/// <para>The platform default row is one chat line and no steps, and its text-to-speech flag would speak the
/// whole line in the bot's voice. This seeder wires the channel's own row to a two-step pipeline instead:
/// the chat thank-you (with the <c>{also_said}</c> quote) and a <c>play_tts</c> of <c>{message}</c> with no
/// <c>as</c>, which is the viewer's own voice. The spoken step carries a condition so a cheer without a
/// message says nothing.</para>
/// </summary>
/// <remarks>
/// The pipeline cannot reach the tone catalogue at run time, so the chat line is picked from the catalogue
/// once, in the channel's own personality tone, when the row is wired. Idempotent on the same terms as
/// <see cref="RaidResponseFlowSeeder"/>: only a row that still follows the platform default is wired up, and a
/// row already pointing at a pipeline with steps is left alone. Order 87, after the raid seeders.
/// </remarks>
public sealed class CheerResponseFlowSeeder(IApplicationDbContext db) : ISeeder
{
    private const string EventType = "channel.cheer";
    private const string QuoteVariable = "{also_said}";

    public int Order => 87;

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
        Dictionary<Guid, string> toneByChannel = await db
            .Channels.Where(c => channelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Personality, ct);

        foreach (EventResponse response in responses)
        {
            if (response.PipelineId is { } pid && pipelineIdsWithSteps.Contains(pid))
                continue;

            toneByChannel.TryGetValue(response.BroadcasterId, out string? tone);
            Guid pipelineId = Guid.CreateVersion7();
            List<PipelineStep> steps = BuildSteps(pipelineId, response.BroadcasterId, tone);

            // GraphJsonCache is what EventResponseExecutor runs: an empty cache runs no steps at all.
            db.Pipelines.Add(
                new()
                {
                    Id = pipelineId,
                    BroadcasterId = response.BroadcasterId,
                    Name = "Cheer response",
                    Description =
                        "Thanks the viewer in chat with their message quoted, and reads that message "
                        + "aloud in their own voice. Every step is an ordinary block.",
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

    private static List<PipelineStep> BuildSteps(Guid pipelineId, Guid channelId, string? tone)
    {
        string line =
            EventResponseToneCatalog.Pick(tone, EventType)
            ?? "{user} cheered {bits} bits! Thank you!";
        if (!line.Contains(QuoteVariable, StringComparison.Ordinal))
            line += QuoteVariable;

        PipelineStep thanks = NewStep(
            pipelineId,
            channelId,
            0,
            "send_message",
            $$"""{"message":"{{line}}"}"""
        );
        PipelineStep spoken = NewStep(
            pipelineId,
            channelId,
            1,
            "play_tts",
            """{"text":"{message}"}"""
        );
        spoken.Conditions =
        [
            new()
            {
                PipelineStepId = spoken.Id,
                BroadcasterId = channelId,
                ConditionType = "comparison",
                LeftOperand = "{message}",
                Operator = "ne",
                RightOperand = "",
                Order = 0,
            },
        ];
        return [thanks, spoken];
    }

    private static PipelineStep NewStep(
        Guid pipelineId,
        Guid channelId,
        int order,
        string actionType,
        string configJson
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            PipelineId = pipelineId,
            BroadcasterId = channelId,
            ActionType = actionType,
            ConfigJson = configJson,
            Order = order,
            IsEnabled = true,
        };
}
