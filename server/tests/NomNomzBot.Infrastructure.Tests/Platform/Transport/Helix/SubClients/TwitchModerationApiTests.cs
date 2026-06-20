// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients;

/// <summary>
/// Behavioural tests for the Moderation sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates on the required scope, and builds the exact Helix request (verb / path / auth / query / body). Most
/// endpoints require both <c>broadcaster_id</c> and <c>moderator_id</c> set to the resolved id; the tests
/// assert that. The capturing transport lets us check the request shape and short-circuits with no HTTP.
/// </summary>
public class TwitchModerationApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-4444-7444-8444-000000000004");
    private const string TwitchId = "44322889";
    private const string TargetId = "9999";

    private static TwitchModerationApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchAutoModSettings SampleSettings(int overall = 2) =>
        new(TwitchId, TwitchId, overall, 1, 2, 3, 4, 0, 1, 2, 3);

    // ── Ban / Timeout / Unban ──

    [Fact]
    public async Task BanUser_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchBanResult> result = await api.BanUserAsync(Tenant, TargetId, "spam");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task BanUser_WithScope_BuildsUserTokenPost_WithBroadcasterAndModeratorId_MapsResult()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchBanResult(
                TwitchId,
                TwitchId,
                TargetId,
                DateTimeOffset.UnixEpoch,
                null
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageBannedUsers);

        Result<TwitchBanResult> result = await api.BanUserAsync(Tenant, TargetId, "spam");

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(TargetId);
        result.Value.EndTime.Should().BeNull();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/bans");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport.LastRequest.Body.Should().NotBeNull();
    }

    [Fact]
    public async Task TimeoutUser_WithScope_BuildsPost_ToBansPath_WithModeratorId()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchBanResult(
                TwitchId,
                TwitchId,
                TargetId,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(600)
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageBannedUsers);

        Result<TwitchBanResult> result = await api.TimeoutUserAsync(
            Tenant,
            TargetId,
            600,
            "cool off"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.EndTime.Should().NotBeNull();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/bans");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task UnbanUser_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result result = await api.UnbanUserAsync(Tenant, TargetId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UnbanUser_WithScope_BuildsUserTokenDelete_WithModeratorAndTargetUserId()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageBannedUsers);

        Result result = await api.UnbanUserAsync(Tenant, TargetId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("moderation/bans");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TargetId);
    }

    // ── Banned users / unban requests ──

    [Fact]
    public async Task GetBannedUsers_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchPage<TwitchBannedUser>> result = await api.GetBannedUsersAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetBannedUsers_WithScope_BuildsPagedGet_MapsPage()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchBannedUser>(
                [
                    new TwitchBannedUser(
                        TargetId,
                        "target",
                        "Target",
                        null,
                        DateTimeOffset.UnixEpoch,
                        "spam",
                        TwitchId,
                        "mod",
                        "Mod"
                    ),
                ],
                "cursor",
                3
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModerationRead);

        Result<TwitchPage<TwitchBannedUser>> result = await api.GetBannedUsersAsync(
            Tenant,
            new TwitchPageRequest(After: "abc", PageSize: 50)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(3);
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle().Which.Reason.Should().Be("spam");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("moderation/banned");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetUnbanRequests_WithScope_BuildsGet_WithStatusAndModeratorId()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchUnbanRequest>([], null, 0),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorReadUnbanRequests);

        Result<TwitchPage<TwitchUnbanRequest>> result = await api.GetUnbanRequestsAsync(
            Tenant,
            "pending",
            new TwitchPageRequest(PageSize: 25)
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("moderation/unban_requests");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "status" && q.Value == "pending");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
    }

    [Fact]
    public async Task GetUnbanRequests_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchPage<TwitchUnbanRequest>> result = await api.GetUnbanRequestsAsync(
            Tenant,
            "pending",
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveUnbanRequest_WithScope_BuildsPatch_WithResolutionTextAndModeratorId()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchUnbanRequest(
                "ubr-1",
                TwitchId,
                "login",
                "Name",
                TwitchId,
                "mod",
                "Mod",
                TargetId,
                "target",
                "Target",
                "please",
                "approved",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                "ok"
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageUnbanRequests);

        Result<TwitchUnbanRequest> result = await api.ResolveUnbanRequestAsync(
            Tenant,
            "ubr-1",
            "approved",
            "ok"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("approved");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("moderation/unban_requests");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "unban_request_id" && q.Value == "ubr-1");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "status" && q.Value == "approved");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "resolution_text" && q.Value == "ok");
    }

    [Fact]
    public async Task ResolveUnbanRequest_NoResolutionText_OmitsThatQuery()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchUnbanRequest(
                "ubr-1",
                TwitchId,
                "login",
                "Name",
                TwitchId,
                "mod",
                "Mod",
                TargetId,
                "target",
                "Target",
                "please",
                "denied",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                ""
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageUnbanRequests);

        Result<TwitchUnbanRequest> result = await api.ResolveUnbanRequestAsync(
            Tenant,
            "ubr-1",
            "denied",
            null
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "resolution_text");
    }

    // ── Blocked terms ──

    [Fact]
    public async Task GetBlockedTerms_WithScope_BuildsPagedGet_WithModeratorId()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchBlockedTerm>([], null, 0),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorReadBlockedTerms);

        Result<TwitchPage<TwitchBlockedTerm>> result = await api.GetBlockedTermsAsync(
            Tenant,
            new TwitchPageRequest(After: "c", PageSize: 10)
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("moderation/blocked_terms");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "c");
    }

    [Fact]
    public async Task AddBlockedTerm_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchBlockedTerm> result = await api.AddBlockedTermAsync(Tenant, "bad word");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AddBlockedTerm_WithScope_BuildsUserTokenPost_WithBody_MapsTerm()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchBlockedTerm(
                TwitchId,
                TwitchId,
                "term-1",
                "bad word",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                null
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageBlockedTerms);

        Result<TwitchBlockedTerm> result = await api.AddBlockedTermAsync(Tenant, "bad word");

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("term-1");
        result.Value.Text.Should().Be("bad word");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/blocked_terms");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().NotBeNull();
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task RemoveBlockedTerm_WithScope_BuildsUserTokenDelete_WithIdAndModeratorId()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageBlockedTerms);

        Result result = await api.RemoveBlockedTermAsync(Tenant, "term-1");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("moderation/blocked_terms");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "term-1");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    // ── Chat deletion ──

    [Fact]
    public async Task DeleteChatMessage_WithScope_BuildsDelete_WithMessageIdAndModeratorId()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageChatMessages);

        Result result = await api.DeleteChatMessageAsync(Tenant, "msg-1");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("moderation/chat");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "message_id" && q.Value == "msg-1");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task DeleteAllChatMessages_WithScope_OmitsMessageId()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageChatMessages);

        Result result = await api.DeleteAllChatMessagesAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Path.Should().Be("moderation/chat");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "message_id");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task DeleteChatMessage_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result result = await api.DeleteChatMessageAsync(Tenant, "msg-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    // ── Shield mode ──

    [Fact]
    public async Task GetShieldModeStatus_WithScope_BuildsGet_MapsStatus()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchShieldModeStatus(
                true,
                TwitchId,
                "mod",
                "Mod",
                DateTimeOffset.UnixEpoch
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorReadShieldMode);

        Result<TwitchShieldModeStatus> result = await api.GetShieldModeStatusAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("moderation/shield_mode");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task UpdateShieldModeStatus_WithScope_BuildsPut_WithBody_MapsStatus()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchShieldModeStatus(
                false,
                TwitchId,
                "mod",
                "Mod",
                DateTimeOffset.UnixEpoch
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageShieldMode);

        Result<TwitchShieldModeStatus> result = await api.UpdateShieldModeStatusAsync(
            Tenant,
            false
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Put);
        transport.LastRequest.Path.Should().Be("moderation/shield_mode");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().NotBeNull();
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task UpdateShieldModeStatus_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchShieldModeStatus> result = await api.UpdateShieldModeStatusAsync(Tenant, true);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    // ── Warnings ──

    [Fact]
    public async Task WarnChatUser_WithScope_BuildsPost_WithBody_AndModeratorId_MapsResult()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchWarningResult(TwitchId, TargetId, TwitchId, "be nice"),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageWarnings);

        Result<TwitchWarningResult> result = await api.WarnChatUserAsync(
            Tenant,
            TargetId,
            "be nice"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(TargetId);
        result.Value.Reason.Should().Be("be nice");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/warnings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().NotBeNull();
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task WarnChatUser_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchWarningResult> result = await api.WarnChatUserAsync(
            Tenant,
            TargetId,
            "be nice"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    // ── Suspicious users ──

    [Fact]
    public async Task AddSuspiciousStatus_WithScope_BuildsPost_WithBody_AndModeratorId()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchSuspiciousUserStatus(
                TargetId,
                TwitchId,
                TwitchId,
                DateTimeOffset.UnixEpoch,
                "RESTRICTED",
                ["manually_added"]
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageSuspiciousUsers);

        Result<TwitchSuspiciousUserStatus> result = await api.AddSuspiciousStatusAsync(
            Tenant,
            TargetId,
            "RESTRICTED"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("RESTRICTED");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/suspicious_users");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().NotBeNull();
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task RemoveSuspiciousStatus_WithScope_BuildsDelete_WithUserIdAndModeratorId()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchSuspiciousUserStatus(
                TargetId,
                TwitchId,
                TwitchId,
                DateTimeOffset.UnixEpoch,
                "NO_TREATMENT",
                []
            ),
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageSuspiciousUsers);

        Result<TwitchSuspiciousUserStatus> result = await api.RemoveSuspiciousStatusAsync(
            Tenant,
            TargetId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("NO_TREATMENT");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("moderation/suspicious_users");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TargetId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task AddSuspiciousStatus_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchSuspiciousUserStatus> result = await api.AddSuspiciousStatusAsync(
            Tenant,
            TargetId,
            "RESTRICTED"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    // ── AutoMod ──

    [Fact]
    public async Task CheckAutoModStatus_WithScope_BuildsPost_WithBroadcasterOnly_MapsList()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchAutoModStatus> { new("m1", true), new("m2", false) },
        };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModerationRead);

        Result<IReadOnlyList<TwitchAutoModStatus>> result = await api.CheckAutoModStatusAsync(
            Tenant,
            [("m1", "hello"), ("m2", "bad")]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].IsPermitted.Should().BeTrue();
        result.Value[1].IsPermitted.Should().BeFalse();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/enforcements/status");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().NotBeNull();
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task CheckAutoModStatus_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<IReadOnlyList<TwitchAutoModStatus>> result = await api.CheckAutoModStatusAsync(
            Tenant,
            [("m1", "hi")]
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ManageHeldAutoModMessage_Approve_BuildsPost_WithBody()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageAutoMod);

        Result result = await api.ManageHeldAutoModMessageAsync(Tenant, "msg-1", approve: true);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/automod/message");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().NotBeNull();
    }

    [Fact]
    public async Task ManageHeldAutoModMessage_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result result = await api.ManageHeldAutoModMessageAsync(Tenant, "msg-1", approve: false);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAutoModSettings_WithScope_BuildsGet_WithModeratorId_MapsSettings()
    {
        CapturingHelixTransport transport = new() { SingleResult = SampleSettings(overall: 3) };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorReadAutoModSettings);

        Result<TwitchAutoModSettings> result = await api.GetAutoModSettingsAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.OverallLevel.Should().Be(3);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("moderation/automod/settings");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task UpdateAutoModSettings_WithScope_BuildsPut_WithBody_AndModeratorId_MapsSettings()
    {
        CapturingHelixTransport transport = new() { SingleResult = SampleSettings(overall: 1) };
        TwitchModerationApi api = Build(transport, TwitchScopes.ModeratorManageAutoModSettings);
        UpdateAutoModSettingsRequest request = new(OverallLevel: 1);

        Result<TwitchAutoModSettings> result = await api.UpdateAutoModSettingsAsync(
            Tenant,
            request
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.OverallLevel.Should().Be(1);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Put);
        transport.LastRequest.Path.Should().Be("moderation/automod/settings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task UpdateAutoModSettings_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchModerationApi api = Build(transport);

        Result<TwitchAutoModSettings> result = await api.UpdateAutoModSettingsAsync(
            Tenant,
            new UpdateAutoModSettingsRequest(OverallLevel: 2)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }
}
