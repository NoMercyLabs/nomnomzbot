// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Song-request action: searches for the query and adds the best match to the queue.
///
/// Parameters:
///   query — search query (required). Supports {variable} substitution.
///
/// Usage example:
///   { "type": "song_request", "query": "{args}" }
/// </summary>
public sealed class SongRequestAction : ICommandAction
{
    private readonly IMusicService _music;
    private readonly IChatProvider _chat;
    private readonly IBuiltinResponseComposer _composer;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<SongRequestAction> _logger;

    public string ActionType => "song_request";

    public LocalizedText Category => new("pipeline.category.music");

    public LocalizedText Description => new("pipeline.song_request.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "query",
                PipelineActionFieldKind.Text,
                Required: true,
                Description: new("pipeline.song_request.query.help")
            ),
        ];

    public SongRequestAction(
        IMusicService music,
        IChatProvider chat,
        IBuiltinResponseComposer composer,
        IChannelRegistry registry,
        ILogger<SongRequestAction> logger
    )
    {
        _composer = composer;
        _registry = registry;
        _music = music;
        _chat = chat;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string query = ResolveParam(action.GetString("query") ?? string.Empty, ctx.Variables);
        if (string.IsNullOrWhiteSpace(query))
            return ActionResult.Failure("song_request requires a non-empty 'query'");

        // One resolve: a track link lands on its exact track, a search phrase falls through to the
        // provider's search — then straight into the fair queue (music-sr.md §3.9).
        // The pipeline's own user_role condition reads this same variable — see UserRoleCondition.
        int requesterRoleLevel = ChatRole
            .Parse(ctx.Variables.GetValueOrDefault("user.role", "viewer"))
            .ToLevelValue();
        Result<MusicTrack> requested = await _music.RequestTrackAsync(
            ctx.BroadcasterId.ToString(),
            query,
            ctx.TriggeredByDisplayName,
            requesterRoleLevel,
            ctx.CancellationToken,
            // A reward redemption is a viewer's request too, so it counts toward their history.
            ctx.TriggeredByUserId
        );

        SongRequestReplyContext replyContext = new(
            ctx.BroadcasterId,
            PersonalityTone.Normalize(_registry.Get(ctx.BroadcasterId)?.Personality),
            ctx.TriggeredByDisplayName,
            requesterRoleLevel,
            IsReward: ctx.RedemptionId is not null
        );

        if (requested.IsFailure)
        {
            string refusal = await new SongRequestRefusalReplies(_composer).RefusalReplyAsync(
                replyContext,
                query,
                requested,
                ctx.CancellationToken
            );
            ctx.RepliedToChat |= await _chat.SendMessageAsync(
                ctx.BroadcasterId,
                $"@{ctx.TriggeredByDisplayName} {refusal}",
                ctx.CancellationToken
            );
            return ActionResult.Failure(requested.ErrorMessage ?? "failed to add track to queue");
        }

        MusicTrack track = requested.Value;

        // RequestTrackAsync's result carries no code (it is a search-result shape shared with SearchAsync);
        // the code lives on the fair-queue entry the admission gate just created, so read it back off the
        // queue snapshot — the caller's newest entry matching what was just queued.
        string code = await ResolveJustQueuedCodeAsync(ctx, track);

        Dictionary<string, string> variables = new()
        {
            ["user"] = ctx.TriggeredByDisplayName,
            ["track.name"] = track.Name,
            ["track.artist"] = track.Artist,
        };
        string confirmation;
        if (string.IsNullOrEmpty(code))
        {
            variables["track.link"] = TrackLinks.ToWebLink(track.Uri);
            confirmation = await ComposeAsync(
                replyContext,
                BuiltinResponseSlots.SongRequest.Added,
                "Added {track.name} by {track.artist} to the queue. {track.link}",
                variables,
                ctx.CancellationToken
            );
        }
        else
        {
            variables["request.code"] = code;
            confirmation = await ComposeAsync(
                replyContext,
                BuiltinResponseSlots.SongRequest.AddedWithCode,
                "Added to queue: {track.name} by {track.artist} (code {request.code})",
                variables,
                ctx.CancellationToken
            );
        }

        ctx.RepliedToChat |= await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"@{ctx.TriggeredByDisplayName} {confirmation}",
            ctx.CancellationToken
        );
        return ActionResult.Success($"queued: {track.Name}");
    }

    private Task<string> ComposeAsync(
        SongRequestReplyContext replyContext,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct
    ) =>
        _composer.ComposeAsync(
            new()
            {
                BroadcasterId = replyContext.BroadcasterId,
                Personality = replyContext.Personality,
                BuiltinKey = BuiltinResponseSlots.SongRequest.Key,
                Slot = slot,
                NeutralFallback = neutralFallback,
                Variables = variables,
            },
            ct
        );

    /// <summary>The just-admitted request's short speakable code (Domain SongCode), or empty when it
    /// cannot be found — e.g. a provider that dequeues faster than this read, or a legacy entry.</summary>
    private async Task<string> ResolveJustQueuedCodeAsync(
        PipelineExecutionContext ctx,
        MusicTrack track
    )
    {
        MusicQueue queue = await _music.GetQueueAsync(
            ctx.BroadcasterId.ToString(),
            ctx.CancellationToken
        );
        for (int i = queue.Queue.Count - 1; i >= 0; i--)
        {
            MusicQueueItem candidate = queue.Queue[i];
            if (
                string.Equals(
                    candidate.RequestedBy,
                    ctx.TriggeredByDisplayName,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(candidate.TrackName, track.Name, StringComparison.Ordinal)
                && string.Equals(candidate.Artist, track.Artist, StringComparison.Ordinal)
            )
                return candidate.Code;
        }

        return string.Empty;
    }

    private static string ResolveParam(string value, Dictionary<string, string> vars)
    {
        if (value.StartsWith('{') && value.EndsWith('}'))
            vars.TryGetValue(value[1..^1], out value!);
        return value;
    }
}
