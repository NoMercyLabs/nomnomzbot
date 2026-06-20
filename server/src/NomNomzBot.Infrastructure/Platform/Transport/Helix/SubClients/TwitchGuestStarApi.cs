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
/// The Helix "Guest Star" sub-client (BETA — twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
///
/// Most Guest Star endpoints require both <c>broadcaster_id</c> and <c>moderator_id</c>. The tenant manages
/// their own session with their own token, so the single resolved Twitch id is sent for both — the same
/// convention as <see cref="TwitchModerationApi"/>.
/// </summary>
public sealed class TwitchGuestStarApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchGuestStarApi
{
    public async Task<Result<TwitchGuestStarChannelSettings>> GetChannelSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelReadGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchGuestStarChannelSettings>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchGuestStarChannelSettings>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "guest_star/channel_settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchGuestStarChannelSettings>(request, ct);
    }

    public async Task<Result> UpdateChannelSettingsAsync(
        Guid broadcasterId,
        UpdateGuestStarSettingsRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Put,
            "guest_star/channel_settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)],
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(helixRequest, ct);
    }

    public async Task<Result<TwitchGuestStarSession>> GetSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelReadGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchGuestStarSession>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchGuestStarSession>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "guest_star/session",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchGuestStarSession>(request, ct);
    }

    public async Task<Result<TwitchGuestStarSession>> CreateSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchGuestStarSession>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchGuestStarSession>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "guest_star/session",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchGuestStarSession>(request, ct);
    }

    public async Task<Result<TwitchGuestStarSession>> EndSessionAsync(
        Guid broadcasterId,
        string sessionId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchGuestStarSession>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchGuestStarSession>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "guest_star/session",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("session_id", sessionId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchGuestStarSession>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchGuestStarInvite>>> GetInvitesAsync(
        Guid broadcasterId,
        string sessionId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelReadGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchGuestStarInvite>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchGuestStarInvite>>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "guest_star/invites",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("broadcaster_id", channel.Value),
                new("moderator_id", channel.Value),
                new("session_id", sessionId),
            ]
        );

        return await transport.GetListAsync<TwitchGuestStarInvite>(request, ct);
    }

    public async Task<Result> SendInviteAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "guest_star/invites",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("broadcaster_id", channel.Value),
                new("moderator_id", channel.Value),
                new("session_id", sessionId),
                new("guest_id", guestTwitchUserId),
            ],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> DeleteInviteAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "guest_star/invites",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("broadcaster_id", channel.Value),
                new("moderator_id", channel.Value),
                new("session_id", sessionId),
                new("guest_id", guestTwitchUserId),
            ],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> AssignSlotAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        string slotId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "guest_star/slot",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("broadcaster_id", channel.Value),
                new("moderator_id", channel.Value),
                new("session_id", sessionId),
                new("guest_id", guestTwitchUserId),
                new("slot_id", slotId),
            ],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> UpdateSlotAsync(
        Guid broadcasterId,
        string sessionId,
        string sourceSlotId,
        string? destinationSlotId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
            new("session_id", sessionId),
            new("source_slot_id", sourceSlotId),
        ];
        if (destinationSlotId is not null)
            query.Add(new("destination_slot_id", destinationSlotId));

        TwitchHelixRequest request = new(
            HttpMethod.Patch,
            "guest_star/slot",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> DeleteSlotAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        string slotId,
        bool? shouldReinviteGuest,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
            new("session_id", sessionId),
            new("guest_id", guestTwitchUserId),
            new("slot_id", slotId),
        ];
        if (shouldReinviteGuest is not null)
            query.Add(new("should_reinvite_guest", shouldReinviteGuest.Value ? "true" : "false"));

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "guest_star/slot",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result> UpdateSlotSettingsAsync(
        Guid broadcasterId,
        string sessionId,
        string slotId,
        bool? isAudioEnabled,
        bool? isVideoEnabled,
        bool? isLive,
        int? volume,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageGuestStar,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
            new("session_id", sessionId),
            new("slot_id", slotId),
        ];
        if (isAudioEnabled is not null)
            query.Add(new("is_audio_enabled", isAudioEnabled.Value ? "true" : "false"));
        if (isVideoEnabled is not null)
            query.Add(new("is_video_enabled", isVideoEnabled.Value ? "true" : "false"));
        if (isLive is not null)
            query.Add(new("is_live", isLive.Value ? "true" : "false"));
        if (volume is not null)
            query.Add(new("volume", volume.Value.ToString()));

        TwitchHelixRequest request = new(
            HttpMethod.Patch,
            "guest_star/slot_settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
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
}
