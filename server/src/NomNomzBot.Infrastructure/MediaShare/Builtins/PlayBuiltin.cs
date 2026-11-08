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

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;

namespace NomNomzBot.Infrastructure.MediaShare.Builtins;

/// <summary>
/// Chat builtin <c>!play</c> (moderators and up): reply to a message that holds a Twitch clip or YouTube link
/// and that link joins the media queue approved, or give the link as the argument. The moderator who runs it
/// is credited as the requester (the replied-to author carries no user id in the command context).
/// </summary>
public sealed class PlayBuiltin : IBuiltinCommand
{
    private readonly IMediaShareService _media;
    private readonly IUserService _users;
    private readonly MediaSubmitReplies _replies;

    public PlayBuiltin(
        IMediaShareService media,
        IUserService users,
        IBuiltinResponseComposer composer
    )
    {
        _media = media;
        _users = users;
        _replies = new(composer);
    }

    public string BuiltinKey => "play";
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => 10; // Moderator on the unified ladder.

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        // A reply starts with the parent author's @mention, which holds no link and is skipped by the search.
        string? link =
            MediaLinkExtractor.FirstLink(context.Args)
            ?? MediaLinkExtractor.FirstLink(context.ReplyParentMessageBody);
        if (link is null)
            return await _replies.ReplyAsync(
                context,
                BuiltinResponseSlots.Media.PlayUsage,
                "Usage: reply to a message that holds a twitch clip or youtube link with !play, or !play <link>",
                null,
                ct
            );

        Result<UserDto> caller = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (caller.IsFailure || !Guid.TryParse(caller.Value.Id, out Guid moderatorUserId))
            return await _replies.ReplyAsync(
                context,
                BuiltinResponseSlots.Media.AccountUnresolved,
                "Your account could not be resolved.",
                null,
                ct
            );

        Result<MediaShareRequestDto> result = await _media.SubmitAsync(
            context.BroadcasterId,
            moderatorUserId,
            new(link) { ApproveWithoutReview = true },
            ct
        );
        return result.IsFailure
            ? await _replies.FailureAsync(context, result.ErrorCode, ct)
            : await _replies.SubmittedAsync(context, result.Value, ct);
    }
}
