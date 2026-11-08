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
/// Chat builtin <c>!media &lt;url&gt;</c> (media-share.md §4) — submits a Twitch clip / YouTube video for
/// the caller and replies with the queued / needs-approval / rejection status. Everyone by default (the
/// service enforces enablement, eligibility, cost, and cooldown). A moderator or the broadcaster is approved
/// without review. Every reply is a slot of the <c>media</c> group; the service's error CODE picks the slot.
/// </summary>
public sealed class MediaBuiltin : IBuiltinCommand
{
    private const int ModeratorLevel = 10; // Moderator on the unified ladder.

    private readonly IMediaShareService _media;
    private readonly IUserService _users;
    private readonly MediaSubmitReplies _replies;

    public MediaBuiltin(
        IMediaShareService media,
        IUserService users,
        IBuiltinResponseComposer composer
    )
    {
        _media = media;
        _users = users;
        _replies = new(composer);
    }

    public string BuiltinKey => "media";
    public int DefaultCooldownSeconds => 0; // the per-user cooldown is enforced in the service.
    public int DefaultMinPermissionLevel => 0; // Everyone.

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string url = context.Args.Trim();
        if (url.Length == 0)
            return await _replies.ReplyAsync(
                context,
                BuiltinResponseSlots.Media.Usage,
                "Usage: !media <twitch clip or youtube url>",
                null,
                ct
            );

        Result<UserDto> caller = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (caller.IsFailure || !Guid.TryParse(caller.Value.Id, out Guid viewerUserId))
            return await _replies.ReplyAsync(
                context,
                BuiltinResponseSlots.Media.AccountUnresolved,
                "Your account could not be resolved.",
                null,
                ct
            );

        Result<MediaShareRequestDto> result = await _media.SubmitAsync(
            context.BroadcasterId,
            viewerUserId,
            new(url) { ApproveWithoutReview = context.RoleLevel >= ModeratorLevel },
            ct
        );
        return result.IsFailure
            ? await _replies.FailureAsync(context, result.ErrorCode, ct)
            : await _replies.SubmittedAsync(context, result.Value, ct);
    }
}
