// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.MediaShare.Entities;

namespace NomNomzBot.Infrastructure.MediaShare.Builtins;

/// <summary>
/// Chat builtin <c>!media &lt;url&gt;</c> (media-share.md §4) — submits a Twitch clip / YouTube video for
/// the caller and replies with the queued / needs-approval / rejection status. Everyone by default (the
/// service enforces enablement, eligibility, cost, and cooldown).
/// </summary>
public sealed class MediaBuiltin : IBuiltinCommand
{
    private readonly IMediaShareService _media;
    private readonly IUserService _users;

    public MediaBuiltin(IMediaShareService media, IUserService users)
    {
        _media = media;
        _users = users;
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
            return Result.Success("Usage: !media <twitch clip or youtube url>");

        Result<UserDto> caller = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (caller.IsFailure || !Guid.TryParse(caller.Value.Id, out Guid viewerUserId))
            return Result.Success("media: your account could not be resolved.");

        Result<MediaShareRequestDto> result = await _media.SubmitAsync(
            context.BroadcasterId,
            viewerUserId,
            new SubmitMediaRequest(url),
            ct
        );
        if (result.IsFailure)
            return Result.Success($"media: {result.ErrorMessage}");

        string label = result.Value.Title ?? "your clip";
        return Result.Success(
            result.Value.Status == MediaShareStatus.Approved
                ? $"Added {label} to the queue!"
                : $"Submitted {label} — a mod will review it shortly."
        );
    }
}
