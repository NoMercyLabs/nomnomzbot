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
/// Behavioural tests for the Chat sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates on the required scope, and builds the exact Helix request (verb / path / auth / query / body) —
/// including sending the tenant id for both <c>broadcaster_id</c> and <c>moderator_id</c> where required.
/// The capturing transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchChatApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");
    private const string TwitchId = "44322889";

    private static TwitchChatApi Build(CapturingHelixTransport transport, params string[] scopes) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    // ── Send Announcement ──

    [Fact]
    public async Task SendAnnouncement_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport); // no scopes granted

        Result result = await api.SendAnnouncementAsync(Tenant, "hello", "purple");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAnnouncement_WithScope_BuildsUserPost_WithModeratorId_AndBody()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport, TwitchScopes.ModeratorManageAnnouncements);

        Result result = await api.SendAnnouncementAsync(Tenant, "stream is live", "blue");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("chat/announcements");
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
    public async Task SendAnnouncement_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ModeratorManageAnnouncements)
        );

        Result result = await api.SendAnnouncementAsync(Tenant, "hi", null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    // ── Send Shoutout ──

    [Fact]
    public async Task SendShoutout_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport);

        Result result = await api.SendShoutoutAsync(Tenant, "12345");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendShoutout_WithScope_BuildsFromToAndModeratorQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport, TwitchScopes.ModeratorManageShoutouts);

        Result result = await api.SendShoutoutAsync(Tenant, "98765");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("chat/shoutouts");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "from_broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "to_broadcaster_id" && q.Value == "98765");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    // ── Get Chat Settings (no scope-gate) ──

    [Fact]
    public async Task GetChatSettings_NoScope_BuildsUserGet_WithModeratorId_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchChatSettings(
                TwitchId,
                EmoteMode: true,
                FollowerMode: true,
                FollowerModeDuration: 30,
                ModeratorId: TwitchId,
                NonModeratorChatDelay: false,
                NonModeratorChatDelayDuration: null,
                SlowMode: true,
                SlowModeWaitTime: 10,
                SubscriberMode: false,
                UniqueChatMode: false
            ),
        };
        TwitchChatApi api = Build(transport); // no scopes — read must not gate

        Result<TwitchChatSettings> result = await api.GetChatSettingsAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.EmoteMode.Should().BeTrue();
        result.Value.SlowModeWaitTime.Should().Be(10);
        result.Value.FollowerModeDuration.Should().Be(30);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("chat/settings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    // ── Update Chat Settings ──

    [Fact]
    public async Task UpdateChatSettings_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport);

        Result<TwitchChatSettings> result = await api.UpdateChatSettingsAsync(
            Tenant,
            new UpdateChatSettingsRequest(SlowMode: true)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateChatSettings_WithScope_BuildsUserPatch_WithBody_AndModeratorId()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchChatSettings(
                TwitchId,
                EmoteMode: false,
                FollowerMode: false,
                FollowerModeDuration: null,
                ModeratorId: TwitchId,
                NonModeratorChatDelay: false,
                NonModeratorChatDelayDuration: null,
                SlowMode: true,
                SlowModeWaitTime: 5,
                SubscriberMode: false,
                UniqueChatMode: false
            ),
        };
        TwitchChatApi api = Build(transport, TwitchScopes.ModeratorManageChatSettings);
        UpdateChatSettingsRequest request = new(SlowMode: true, SlowModeWaitTime: 5);

        Result<TwitchChatSettings> result = await api.UpdateChatSettingsAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.SlowModeWaitTime.Should().Be(5);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("chat/settings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    // ── Get User Chat Color (no scope-gate) ──

    [Fact]
    public async Task GetUserChatColor_NoScope_BuildsUserGet_WithUserId_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchUserChatColor(TwitchId, "login", "Name", "#9146FF"),
        };
        TwitchChatApi api = Build(transport); // no scopes — read must not gate

        Result<TwitchUserChatColor> result = await api.GetUserChatColorAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Color.Should().Be("#9146FF");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("chat/color");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "user_id" && q.Value == TwitchId);
    }

    // ── Update User Chat Color ──

    [Fact]
    public async Task UpdateUserChatColor_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport);

        Result result = await api.UpdateUserChatColorAsync(Tenant, "blue");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateUserChatColor_WithScope_BuildsUserPut_WithUserIdAndColorQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport, TwitchScopes.UserManageChatColor);

        Result result = await api.UpdateUserChatColorAsync(Tenant, "hot_pink");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Put);
        transport.LastRequest.Path.Should().Be("chat/color");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "color" && q.Value == "hot_pink");
    }

    // ── Get Pinned Message (no scope-gate) ──

    [Fact]
    public async Task GetPinnedMessages_NoScope_BuildsUserGet_WithBroadcasterIdOnly_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchPinnedChatMessage(
                "msg-1",
                "pinned text",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(5),
                IsPinnedByBroadcaster: true,
                IsPinnedByModerator: false
            ),
        };
        TwitchChatApi api = Build(transport); // no scopes — read must not gate

        Result<TwitchPinnedChatMessage> result = await api.GetPinnedMessagesAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageId.Should().Be("msg-1");
        result.Value.IsPinnedByBroadcaster.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("chat/pinned_messages");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    // ── Pin Message ──

    [Fact]
    public async Task PinMessage_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport);

        Result<TwitchPinnedChatMessage> result = await api.PinMessageAsync(Tenant, "msg-1", 60);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PinMessage_WithScope_BuildsUserPost_WithModeratorId_AndBody()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchPinnedChatMessage(
                "msg-1",
                "pinned text",
                DateTimeOffset.UnixEpoch,
                null,
                IsPinnedByBroadcaster: false,
                IsPinnedByModerator: true
            ),
        };
        TwitchChatApi api = Build(transport, TwitchScopes.ModeratorManageChatMessages);

        Result<TwitchPinnedChatMessage> result = await api.PinMessageAsync(Tenant, "msg-1", 60);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPinnedByModerator.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("chat/pinned_messages");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().NotBeNull();
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    // ── Update Pinned Message ──

    [Fact]
    public async Task UpdatePinnedMessage_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport);

        Result<TwitchPinnedChatMessage> result = await api.UpdatePinnedMessageAsync(Tenant, 120);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdatePinnedMessage_WithScope_BuildsUserPatch_WithModeratorId_NoMessageIdParam()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchPinnedChatMessage(
                "msg-1",
                "pinned text",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(2),
                IsPinnedByBroadcaster: true,
                IsPinnedByModerator: false
            ),
        };
        TwitchChatApi api = Build(transport, TwitchScopes.ModeratorManageChatMessages);

        Result<TwitchPinnedChatMessage> result = await api.UpdatePinnedMessageAsync(Tenant, 120);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("chat/pinned_messages");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().NotBeNull();
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "message_id");
    }

    // ── Unpin Message ──

    [Fact]
    public async Task UnpinMessage_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport);

        Result result = await api.UnpinMessageAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UnpinMessage_WithScope_BuildsUserDelete_WithModeratorId_NoMessageIdParam()
    {
        CapturingHelixTransport transport = new();
        TwitchChatApi api = Build(transport, TwitchScopes.ModeratorManageChatMessages);

        Result result = await api.UnpinMessageAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("chat/pinned_messages");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "message_id");
    }
}
