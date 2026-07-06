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
/// The Helix "Moderation" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
///
/// Many moderation endpoints require both <c>broadcaster_id</c> and <c>moderator_id</c>. The tenant moderates
/// their own channel with their own token, so the single resolved Twitch id is sent for both. Acting as the
/// bot identity (a separate moderator id and token) is a future enhancement.
/// </summary>
public sealed class TwitchModerationApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchModerationApi
{
    public Task<Result<TwitchBanResult>> BanUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string? reason,
        CancellationToken ct = default
    ) => BanInternalAsync(broadcasterId, targetTwitchUserId, null, reason, ct);

    public Task<Result<TwitchBanResult>> TimeoutUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        int durationSeconds,
        string? reason,
        CancellationToken ct = default
    ) => BanInternalAsync(broadcasterId, targetTwitchUserId, durationSeconds, reason, ct);

    private async Task<Result<TwitchBanResult>> BanInternalAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        int? durationSeconds,
        string? reason,
        CancellationToken ct
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageBannedUsers,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchBanResult>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchBanResult>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/bans",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new BanUserBody(new BanUserData(targetTwitchUserId, durationSeconds, reason)),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchBanResult>(request, ct);
    }

    public async Task<Result<TwitchBanResult>> BanAsOperatorAsync(
        Guid operatorUserId,
        string broadcasterTwitchId,
        string targetTwitchUserId,
        string? reason,
        CancellationToken ct = default
    )
    {
        // The operator's OWN Twitch id is the moderator_id, and the operator's OWN token signs the call
        // (Auth.Operator resolves it from OperatorUserId). broadcasterTwitchId is the raw Twitch id of the channel
        // being moderated — which may not be a tenant — so it is NEVER resolved from a Guid. Twitch enforces that
        // the operator actually moderates that channel, so there is no privilege escalation.
        string? operatorTwitchId = await identity.GetTwitchUserIdAsync(operatorUserId, ct);
        if (string.IsNullOrEmpty(operatorTwitchId))
            return Result.Failure<TwitchBanResult>(
                "You have no linked Twitch identity to moderate as.",
                TwitchErrorCodes.NoToken
            );

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/bans",
            TwitchHelixAuth.Operator,
            Query:
            [
                new("broadcaster_id", broadcasterTwitchId),
                new("moderator_id", operatorTwitchId),
            ],
            Body: new BanUserBody(new BanUserData(targetTwitchUserId, null, reason)),
            Priority: TwitchCallPriority.UserInteractive,
            OperatorUserId: operatorUserId
        );

        return await transport.SendWithResultAsync<TwitchBanResult>(request, ct);
    }

    public async Task<Result> UnbanUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageBannedUsers,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "moderation/bans",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("broadcaster_id", channel.Value),
                new("moderator_id", channel.Value),
                new("user_id", targetTwitchUserId),
            ],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchBannedUser>>> GetBannedUsersAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ModerationRead, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchBannedUser>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchBannedUser>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "moderation/banned",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchBannedUser>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchUnbanRequest>>> GetUnbanRequestsAsync(
        Guid broadcasterId,
        string status,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorReadUnbanRequests,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchUnbanRequest>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchUnbanRequest>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
            new("status", status),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "moderation/unban_requests",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchUnbanRequest>(request, ct);
    }

    public async Task<Result<TwitchUnbanRequest>> ResolveUnbanRequestAsync(
        Guid broadcasterId,
        string unbanRequestId,
        string status,
        string? resolutionText,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageUnbanRequests,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchUnbanRequest>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchUnbanRequest>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
            new("unban_request_id", unbanRequestId),
            new("status", status),
        ];
        if (resolutionText is not null)
            query.Add(new("resolution_text", resolutionText));

        TwitchHelixRequest request = new(
            HttpMethod.Patch,
            "moderation/unban_requests",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchUnbanRequest>(request, ct);
    }

    public async Task<Result<TwitchPage<TwitchBlockedTerm>>> GetBlockedTermsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorReadBlockedTerms,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchBlockedTerm>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchBlockedTerm>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "moderation/blocked_terms",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchBlockedTerm>(request, ct);
    }

    public async Task<Result<TwitchBlockedTerm>> AddBlockedTermAsync(
        Guid broadcasterId,
        string text,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageBlockedTerms,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchBlockedTerm>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchBlockedTerm>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/blocked_terms",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new AddBlockedTermBody(text),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchBlockedTerm>(request, ct);
    }

    public async Task<Result> RemoveBlockedTermAsync(
        Guid broadcasterId,
        string blockedTermId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageBlockedTerms,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "moderation/blocked_terms",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("broadcaster_id", channel.Value),
                new("moderator_id", channel.Value),
                new("id", blockedTermId),
            ],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public Task<Result> DeleteChatMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken ct = default
    ) => DeleteChatInternalAsync(broadcasterId, messageId, ct);

    public Task<Result> DeleteAllChatMessagesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => DeleteChatInternalAsync(broadcasterId, null, ct);

    private async Task<Result> DeleteChatInternalAsync(
        Guid broadcasterId,
        string? messageId,
        CancellationToken ct
    )
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

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("moderator_id", channel.Value),
        ];
        if (messageId is not null)
            query.Add(new("message_id", messageId));

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "moderation/chat",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchShieldModeStatus>> GetShieldModeStatusAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorReadShieldMode,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchShieldModeStatus>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchShieldModeStatus>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "moderation/shield_mode",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchShieldModeStatus>(request, ct);
    }

    public async Task<Result<TwitchShieldModeStatus>> UpdateShieldModeStatusAsync(
        Guid broadcasterId,
        bool isActive,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageShieldMode,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchShieldModeStatus>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchShieldModeStatus>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Put,
            "moderation/shield_mode",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new UpdateShieldModeBody(isActive),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchShieldModeStatus>(request, ct);
    }

    public async Task<Result<TwitchWarningResult>> WarnChatUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string reason,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageWarnings,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchWarningResult>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchWarningResult>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/warnings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new WarnUserBody(new WarnUserData(targetTwitchUserId, reason)),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchWarningResult>(request, ct);
    }

    public Task<Result<TwitchSuspiciousUserStatus>> AddSuspiciousStatusAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string status,
        CancellationToken ct = default
    ) => SuspiciousAddInternalAsync(broadcasterId, targetTwitchUserId, status, ct);

    private async Task<Result<TwitchSuspiciousUserStatus>> SuspiciousAddInternalAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string status,
        CancellationToken ct
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageSuspiciousUsers,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchSuspiciousUserStatus>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchSuspiciousUserStatus>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/suspicious_users",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: new SuspiciousUserStatusRequest(targetTwitchUserId, status),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchSuspiciousUserStatus>(request, ct);
    }

    public async Task<Result<TwitchSuspiciousUserStatus>> RemoveSuspiciousStatusAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageSuspiciousUsers,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchSuspiciousUserStatus>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchSuspiciousUserStatus>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "moderation/suspicious_users",
            TwitchHelixAuth.User,
            broadcasterId,
            Query:
            [
                new("broadcaster_id", channel.Value),
                new("moderator_id", channel.Value),
                new("user_id", targetTwitchUserId),
            ],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchSuspiciousUserStatus>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchAutoModStatus>>> CheckAutoModStatusAsync(
        Guid broadcasterId,
        IReadOnlyList<(string MsgId, string MsgText)> messages,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ModerationRead, ct);
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchAutoModStatus>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchAutoModStatus>>(default!);

        List<CheckAutoModData> data = [];
        foreach ((string msgId, string msgText) in messages)
            data.Add(new CheckAutoModData(msgId, msgText));

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/enforcements/status",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)],
            Body: new CheckAutoModBody(data),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.GetListAsync<TwitchAutoModStatus>(request, ct);
    }

    public async Task<Result> ManageHeldAutoModMessageAsync(
        Guid broadcasterId,
        string messageId,
        bool approve,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageAutoMod,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "moderation/automod/message",
            TwitchHelixAuth.User,
            broadcasterId,
            Body: new ManageHeldAutoModBody(channel.Value, messageId, approve ? "ALLOW" : "DENY"),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchAutoModSettings>> GetAutoModSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorReadAutoModSettings,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchAutoModSettings>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchAutoModSettings>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "moderation/automod/settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchAutoModSettings>(request, ct);
    }

    public async Task<Result<TwitchAutoModSettings>> UpdateAutoModSettingsAsync(
        Guid broadcasterId,
        UpdateAutoModSettingsRequest settings,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ModeratorManageAutoModSettings,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchAutoModSettings>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchAutoModSettings>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Put,
            "moderation/automod/settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("moderator_id", channel.Value)],
            Body: settings,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchAutoModSettings>(request, ct);
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
    // These mirror the exact JSON the Helix moderation endpoints expect; the transport's naming policy emits
    // them as snake_case, so the property names below are the camelCase originals of the wire fields. They are
    // private because they are pure transport shapes — never part of the public contract surface.

    /// <summary>Ban User / Timeout User body — a single object under <c>data</c> (<c>duration</c> omitted ⇒ permanent ban).</summary>
    private sealed record BanUserBody(BanUserData Data);

    private sealed record BanUserData(string UserId, int? Duration, string? Reason);

    /// <summary>Add Blocked Term body — the single term text.</summary>
    private sealed record AddBlockedTermBody(string Text);

    /// <summary>Update Shield Mode Status body — the desired active state.</summary>
    private sealed record UpdateShieldModeBody(bool IsActive);

    /// <summary>Warn Chat User body — a single object under <c>data</c> (the target user and the reason).</summary>
    private sealed record WarnUserBody(WarnUserData Data);

    private sealed record WarnUserData(string UserId, string Reason);

    /// <summary>Manage Held AutoMod Message body — the moderator id, the held message id, and the ALLOW/DENY action.</summary>
    private sealed record ManageHeldAutoModBody(string UserId, string MsgId, string Action);

    /// <summary>Check AutoMod Status body — the messages to test, each carrying its own id and text under <c>data</c>.</summary>
    private sealed record CheckAutoModBody(IReadOnlyList<CheckAutoModData> Data);

    private sealed record CheckAutoModData(string MsgId, string MsgText);
}
