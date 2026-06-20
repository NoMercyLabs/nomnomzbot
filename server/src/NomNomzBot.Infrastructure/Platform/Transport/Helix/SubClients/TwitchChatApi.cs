// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;

/// <summary>
/// The Helix "Chat" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
///
/// The moderator-scoped endpoints require both <c>broadcaster_id</c> and <c>moderator_id</c>. The tenant
/// moderates their own channel with their own token, so the single resolved Twitch id is sent for both.
/// Acting as the bot identity (a separate moderator id and token) is a future enhancement.
/// </summary>
public sealed class TwitchChatApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchChatApi
{
    public async Task<Result> SendAnnouncementAsync(
        Guid broadcasterId,
        string message,
        string? color,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageAnnouncements,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "chat/announcements",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new SendAnnouncementBody(message, color),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> SendShoutoutAsync(
        Guid broadcasterId,
        string toTwitchBroadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageShoutouts,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "chat/shoutouts",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("from_broadcaster_id", channel.Value),
                new("to_broadcaster_id", toTwitchBroadcasterId),
                new("moderator_id", channel.Value),
            ],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchChatSettings>> GetChatSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchChatSettings>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchChatSettings>(request, ct);
    }

    public async Task<Result<TwitchChatSettings>> UpdateChatSettingsAsync(
        Guid broadcasterId,
        UpdateChatSettingsRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageChatSettings,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchChatSettings>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchChatSettings>(default!);

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Patch,
            "chat/settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchChatSettings>(helixRequest, ct);
    }

    public async Task<Result<TwitchUserChatColor>> GetUserChatColorAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> user = await ResolveAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user.WithValue<TwitchUserChatColor>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/color",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("user_id", user.Value)]
        );

        return await transport.GetSingleAsync<TwitchUserChatColor>(request, ct);
    }

    public async Task<Result> UpdateUserChatColorAsync(
        Guid broadcasterId,
        string color,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.UserManageChatColor, ct);
        if (scope.IsFailure)
            return scope;

        Result<string> user = await ResolveAsync(broadcasterId, ct);
        if (user.IsFailure)
            return user;

        TwitchHelixRequest request = new(
            HttpMethod.Put,
            "chat/color",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("user_id", user.Value), new("color", color)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchPinnedChatMessage>> GetPinnedMessagesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPinnedChatMessage>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "chat/pinned_messages",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchPinnedChatMessage>(request, ct);
    }

    public async Task<Result<TwitchPinnedChatMessage>> PinMessageAsync(
        Guid broadcasterId,
        string messageId,
        int? durationSeconds,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageChatMessages,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPinnedChatMessage>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPinnedChatMessage>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "chat/pinned_messages",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new PinMessageBody(messageId, durationSeconds),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchPinnedChatMessage>(request, ct);
    }

    public async Task<Result<TwitchPinnedChatMessage>> UpdatePinnedMessageAsync(
        Guid broadcasterId,
        int? durationSeconds,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageChatMessages,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPinnedChatMessage>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPinnedChatMessage>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Patch,
            "chat/pinned_messages",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new UpdatePinnedMessageBody(durationSeconds),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchPinnedChatMessage>(request, ct);
    }

    public async Task<Result> UnpinMessageAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageChatMessages,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "chat/pinned_messages",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch channel id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? channelId = await identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        return channelId is null
            ? Result.Failure<string>("Channel is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(channelId);
    }

    /// <summary>Pre-checks a required user-token scope, short-circuiting with <c>missing_scope</c> when absent.</summary>
    private async Task<Result> RequireScopeAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct
    )
    {
        bool granted = await tokens.HasScopeAsync(broadcasterId, scope, ct);
        return granted
            ? Result.Success()
            : Result.Failure($"Missing required scope '{scope}'.", TwitchErrorCodes.MissingScope);
    }

    // ── Request body envelopes ──
    // These mirror the exact JSON the Helix chat endpoints expect; the transport's naming policy emits
    // them as snake_case, so the property names below are the camelCase originals of the wire fields. They
    // are private because they are pure transport shapes — never part of the public contract surface.

    /// <summary>Send Chat Announcement body — the announcement text and an optional color tint (omitted ⇒ default).</summary>
    private sealed record SendAnnouncementBody(string Message, string? Color);

    /// <summary>Pin Chat Message body — the message to pin and an optional pin duration in seconds.</summary>
    private sealed record PinMessageBody(string MessageId, int? DurationSeconds);

    /// <summary>Update Pinned Chat Message body — the new pin duration in seconds (omitted ⇒ unchanged).</summary>
    private sealed record UpdatePinnedMessageBody(int? DurationSeconds);
}
