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
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!whisper &lt;user&gt; &lt;message&gt;</c> (legacy parity, S068c) — mod/broadcaster-only. Resolves the
/// target login via <see cref="ITwitchUsersApi.GetUsersByLoginsAsync"/> (the same lookup
/// <see cref="AccountAgeBuiltin"/> and <see cref="UpdateUserInfoBuiltin"/> already use), then sends the
/// message through <see cref="IPlatformDirectMessageSender"/> (S065's multi-bind DM abstraction — the same
/// one <c>GiveawayFulfillment</c> uses for code-pool delivery), picking the Twitch sender since the caller's
/// chat message itself only ever arrives over Twitch today.
/// </summary>
public sealed class WhisperBuiltin : IBuiltinCommand
{
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly IReadOnlyDictionary<string, IPlatformDirectMessageSender> _dmSendersByProvider;
    private readonly IBuiltinResponseComposer _composer;

    public WhisperBuiltin(
        ITwitchUsersApi twitchUsers,
        IEnumerable<IPlatformDirectMessageSender> dmSenders,
        IBuiltinResponseComposer composer
    )
    {
        _twitchUsers = twitchUsers;
        _dmSendersByProvider = dmSenders.ToDictionary(s => s.Provider);
        _composer = composer;
    }

    public string BuiltinKey => "whisper";
    public int DefaultCooldownSeconds => 3;

    // Moderator on the unified ladder (0/2/4/6/10/…) — a viewer must not be able to make the bot DM
    // an arbitrary third party.
    public int DefaultMinPermissionLevel => 10; // mod+

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string[] parts = context.Args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            string usage = await _composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.Whisper.Key,
                    Slot = BuiltinResponseSlots.Whisper.Usage,
                    NeutralFallback = "Usage: !whisper <user> <message>",
                },
                ct
            );
            return Result.Success(usage);
        }

        string targetLogin = MentionParser.ParseUserMention(parts[0]).ToLowerInvariant();
        string message = parts[1];

        Result<IReadOnlyList<TwitchUser>> lookup = await _twitchUsers.GetUsersByLoginsAsync(
            [targetLogin],
            ct
        );
        if (lookup.IsFailure)
        {
            string twitchUnavailable = await _composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.Whisper.Key,
                    Slot = BuiltinResponseSlots.Whisper.TwitchUnavailable,
                    NeutralFallback = "Twitch did not answer just now — try again in a moment.",
                },
                ct
            );
            return Result.Success(twitchUnavailable);
        }

        TwitchUser? target = lookup.Value.FirstOrDefault();
        if (target is null)
        {
            string notFound = await _composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.Whisper.Key,
                    Slot = BuiltinResponseSlots.Whisper.NotFound,
                    NeutralFallback = $"Could not find a Twitch user named \"{targetLogin}\".",
                    Variables = new Dictionary<string, string> { ["user"] = targetLogin },
                },
                ct
            );
            return Result.Success(notFound);
        }

        if (
            !_dmSendersByProvider.TryGetValue(
                AuthEnums.Platform.Twitch,
                out IPlatformDirectMessageSender? sender
            )
        )
        {
            string notAvailable = await _composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.Whisper.Key,
                    Slot = BuiltinResponseSlots.Whisper.NotAvailable,
                    NeutralFallback = "Whispering isn't available right now.",
                },
                ct
            );
            return Result.Success(notAvailable);
        }

        Result sent = await sender.SendAsync(context.BroadcasterId, target.Id, message, ct);
        return Result.Success(
            sent.IsSuccess
                ? $"Whispered {target.DisplayName}."
                : sent.ErrorMessage ?? $"Could not whisper {target.DisplayName}."
        );
    }
}
