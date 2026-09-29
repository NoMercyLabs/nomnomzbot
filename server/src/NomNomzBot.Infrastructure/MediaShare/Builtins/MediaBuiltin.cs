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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.MediaShare.Builtins;

/// <summary>
/// Chat builtin <c>!media &lt;url&gt;</c> (media-share.md §4) — submits a Twitch clip / YouTube video for
/// the caller and replies with the queued / needs-approval / rejection status. Everyone by default (the
/// service enforces enablement, eligibility, cost, and cooldown). Every reply is a slot of the <c>media</c>
/// group; the service's error CODE picks the slot, its message stays for logs and the API.
/// </summary>
public sealed class MediaBuiltin : IBuiltinCommand
{
    private readonly IMediaShareService _media;
    private readonly IUserService _users;
    private readonly IBuiltinResponseComposer _composer;

    public MediaBuiltin(
        IMediaShareService media,
        IUserService users,
        IBuiltinResponseComposer composer
    )
    {
        _media = media;
        _users = users;
        _composer = composer;
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
            return await ReplyAsync(
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
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Media.AccountUnresolved,
                "Your account could not be resolved.",
                null,
                ct
            );

        Result<MediaShareRequestDto> result = await _media.SubmitAsync(
            context.BroadcasterId,
            viewerUserId,
            new(url),
            ct
        );
        if (result.IsFailure)
            return await FailureAsync(context, result.ErrorCode, ct);

        Dictionary<string, string> vars = new(StringComparer.OrdinalIgnoreCase)
        {
            ["media.title"] = result.Value.Title ?? "your clip",
        };
        return result.Value.Status == MediaShareStatus.Approved
            ? await ReplyAsync(
                context,
                BuiltinResponseSlots.Media.Queued,
                "Added {media.title} to the queue!",
                vars,
                ct
            )
            : await ReplyAsync(
                context,
                BuiltinResponseSlots.Media.Pending,
                "Submitted {media.title} — a mod will review it shortly.",
                vars,
                ct
            );
    }

    /// <summary>Maps the service's error code to the slot that words it; an unlisted code gets the generic slot.</summary>
    private Task<Result<string>> FailureAsync(
        BuiltinCommandContext context,
        string? errorCode,
        CancellationToken ct
    ) =>
        errorCode switch
        {
            "DISABLED" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.Disabled,
                "Media share is not enabled on this channel.",
                null,
                ct
            ),
            "SOURCE_NOT_ALLOWED" or "VALIDATION_FAILED" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.SourceNotAllowed,
                "Only Twitch clips and YouTube videos that this channel accepts can be queued.",
                null,
                ct
            ),
            "NOT_FOUND" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.NotFound,
                "I couldn't find that clip or video.",
                null,
                ct
            ),
            "DURATION_EXCEEDED" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.TooLong,
                "That clip is longer than this channel allows.",
                null,
                ct
            ),
            "NOT_ELIGIBLE" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.NotEligible,
                "You can't submit media on this channel right now.",
                null,
                ct
            ),
            "COOLDOWN" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.Cooldown,
                "You're submitting too fast — wait a moment before the next one.",
                null,
                ct
            ),
            "QUEUE_FULL" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.QueueFull,
                "The media queue is full — try again after some items play.",
                null,
                ct
            ),
            "SERVICE_UNAVAILABLE" => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.Unavailable,
                "I couldn't look that up right now — try again in a moment.",
                null,
                ct
            ),
            _ => ReplyAsync(
                context,
                BuiltinResponseSlots.Media.Failed,
                "I couldn't submit that. Check your points and try again.",
                null,
                ct
            ),
        };

    private async Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await _composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Media.Key,
                slot,
                neutralFallback,
                variables,
                ct
            )
        );
}
