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
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.MediaShare.Builtins;

/// <summary>
/// The replies of the media-share chat builtins (<c>!media</c>, <c>!play</c>): every line is a slot of the
/// <c>media</c> group. The service's error CODE picks the slot; its message stays for logs and the API.
/// </summary>
internal sealed class MediaSubmitReplies(IBuiltinResponseComposer composer)
{
    public async Task<Result<string>> SubmittedAsync(
        BuiltinCommandContext context,
        MediaShareRequestDto submitted,
        CancellationToken ct
    )
    {
        Dictionary<string, string> vars = new(StringComparer.OrdinalIgnoreCase)
        {
            ["media.title"] = submitted.Title ?? "your clip",
        };
        // An approved item may already be playing when the reply is built (playback starts on approval).
        return submitted.Status is MediaShareStatus.Approved or MediaShareStatus.Playing
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
    public Task<Result<string>> FailureAsync(
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

    public async Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Media.Key,
                slot,
                neutralFallback,
                variables,
                ct
            )
        );
}
