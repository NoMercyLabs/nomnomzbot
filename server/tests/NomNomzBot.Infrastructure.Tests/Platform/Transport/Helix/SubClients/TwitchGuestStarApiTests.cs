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
/// Behavioural tests for the Guest Star (BETA) sub-client: each method resolves the tenant Guid to the
/// Twitch id, gates on the required scope, sends the single resolved id for both <c>broadcaster_id</c> and
/// <c>moderator_id</c> where the endpoint requires it, and builds the exact Helix request
/// (verb / path / auth / query / body). The capturing transport asserts the request shape, the nested
/// session/guest/slot/invite DTO mapping, and the scope short-circuit paths — all with no HTTP.
/// </summary>
public class TwitchGuestStarApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-7777-7777-8777-000000000007");
    private const string TwitchId = "44322889";
    private const string SessionId = "2KFRQbFtpmfyD3IevNRnCzOPRJI";
    private const string GuestId = "9876543";
    private const string SlotId = "1";

    private static TwitchGuestStarApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchGuestStarSession SampleSession() =>
        new(
            SessionId,
            [
                new TwitchGuestStarGuest(
                    SlotId,
                    IsLive: true,
                    UserId: GuestId,
                    UserDisplayName: "CoolGuest",
                    UserLogin: "coolguest",
                    Volume: 80,
                    AssignedAt: DateTimeOffset.UnixEpoch,
                    AudioSettings: new TwitchGuestStarMediaSettings(true, true, true),
                    VideoSettings: new TwitchGuestStarMediaSettings(true, false, true)
                ),
            ]
        );

    // ── Get Channel Settings ──

    [Fact]
    public async Task GetChannelSettings_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result<TwitchGuestStarChannelSettings> result = await api.GetChannelSettingsAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetChannelSettings_WithScope_BuildsUserGet_WithModeratorId_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchGuestStarChannelSettings(
                IsModeratorSendLiveEnabled: true,
                SlotCount: 5,
                IsBrowserSourceAudioEnabled: false,
                GroupLayout: "TILED_LAYOUT",
                BrowserSourceToken: "tok-123"
            ),
        };
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelReadGuestStar);

        Result<TwitchGuestStarChannelSettings> result = await api.GetChannelSettingsAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.SlotCount.Should().Be(5);
        result.Value.GroupLayout.Should().Be("TILED_LAYOUT");
        result.Value.BrowserSourceToken.Should().Be("tok-123");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("guest_star/channel_settings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetChannelSettings_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelReadGuestStar)
        );

        Result<TwitchGuestStarChannelSettings> result = await api.GetChannelSettingsAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    // ── Update Channel Settings ──

    [Fact]
    public async Task UpdateChannelSettings_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result result = await api.UpdateChannelSettingsAsync(
            Tenant,
            new UpdateGuestStarSettingsRequest(SlotCount: 4)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateChannelSettings_WithScope_BuildsUserPut_BroadcasterOnly_WithBody()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);
        UpdateGuestStarSettingsRequest request = new(
            IsModeratorSendLiveEnabled: true,
            SlotCount: 6,
            GroupLayout: "SCREENSHARE_LAYOUT"
        );

        Result result = await api.UpdateChannelSettingsAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Put);
        transport.LastRequest.Path.Should().Be("guest_star/channel_settings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "moderator_id");
    }

    // ── Get Session ──

    [Fact]
    public async Task GetSession_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result<TwitchGuestStarSession> result = await api.GetSessionAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetSession_WithScope_BuildsUserGet_WithModeratorId_MapsNestedGuests()
    {
        CapturingHelixTransport transport = new() { SingleResult = SampleSession() };
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelReadGuestStar);

        Result<TwitchGuestStarSession> result = await api.GetSessionAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(SessionId);
        result.Value.Guests.Should().ContainSingle();
        TwitchGuestStarGuest guest = result.Value.Guests[0];
        guest.SlotId.Should().Be(SlotId);
        guest.UserId.Should().Be(GuestId);
        guest.Volume.Should().Be(80);
        guest.AudioSettings.IsGuestEnabled.Should().BeTrue();
        guest.VideoSettings.IsGuestEnabled.Should().BeFalse();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("guest_star/session");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
    }

    // ── Create Session ──

    [Fact]
    public async Task CreateSession_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result<TwitchGuestStarSession> result = await api.CreateSessionAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateSession_WithScope_BuildsUserPost_BroadcasterOnly_ReturnsSession()
    {
        CapturingHelixTransport transport = new() { SingleResult = SampleSession() };
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result<TwitchGuestStarSession> result = await api.CreateSessionAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(SessionId);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("guest_star/session");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "moderator_id");
    }

    // ── End Session ──

    [Fact]
    public async Task EndSession_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result<TwitchGuestStarSession> result = await api.EndSessionAsync(Tenant, SessionId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EndSession_WithScope_BuildsUserDelete_WithSessionId_ReturnsSession()
    {
        CapturingHelixTransport transport = new() { SingleResult = SampleSession() };
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result<TwitchGuestStarSession> result = await api.EndSessionAsync(Tenant, SessionId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(SessionId);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("guest_star/session");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "session_id" && q.Value == SessionId);
    }

    // ── Get Invites ──

    [Fact]
    public async Task GetInvites_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result<IReadOnlyList<TwitchGuestStarInvite>> result = await api.GetInvitesAsync(
            Tenant,
            SessionId
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetInvites_WithScope_BuildsUserGet_WithModeratorAndSession_MapsList()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchGuestStarInvite>
            {
                new(
                    UserId: GuestId,
                    InvitedAt: DateTimeOffset.UnixEpoch,
                    Status: "INVITED",
                    IsVideoEnabled: true,
                    IsAudioEnabled: false,
                    IsVideoAvailable: true,
                    IsAudioAvailable: true
                ),
            },
        };
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelReadGuestStar);

        Result<IReadOnlyList<TwitchGuestStarInvite>> result = await api.GetInvitesAsync(
            Tenant,
            SessionId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        TwitchGuestStarInvite invite = result.Value[0];
        invite.UserId.Should().Be(GuestId);
        invite.Status.Should().Be("INVITED");
        invite.IsVideoEnabled.Should().BeTrue();
        invite.IsAudioEnabled.Should().BeFalse();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("guest_star/invites");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "session_id" && q.Value == SessionId);
    }

    // ── Send Invite ──

    [Fact]
    public async Task SendInvite_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result result = await api.SendInviteAsync(Tenant, SessionId, GuestId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendInvite_WithScope_BuildsUserPost_WithModeratorSessionGuest()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.SendInviteAsync(Tenant, SessionId, GuestId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("guest_star/invites");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "session_id" && q.Value == SessionId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "guest_id" && q.Value == GuestId);
    }

    // ── Delete Invite ──

    [Fact]
    public async Task DeleteInvite_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result result = await api.DeleteInviteAsync(Tenant, SessionId, GuestId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteInvite_WithScope_BuildsUserDelete_WithModeratorSessionGuest()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.DeleteInviteAsync(Tenant, SessionId, GuestId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("guest_star/invites");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "session_id" && q.Value == SessionId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "guest_id" && q.Value == GuestId);
    }

    // ── Assign Slot ──

    [Fact]
    public async Task AssignSlot_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result result = await api.AssignSlotAsync(Tenant, SessionId, GuestId, SlotId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AssignSlot_WithScope_BuildsUserPost_WithModeratorSessionGuestSlot()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.AssignSlotAsync(Tenant, SessionId, GuestId, SlotId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("guest_star/slot");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "session_id" && q.Value == SessionId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "guest_id" && q.Value == GuestId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "slot_id" && q.Value == SlotId);
    }

    // ── Update Slot ──

    [Fact]
    public async Task UpdateSlot_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result result = await api.UpdateSlotAsync(Tenant, SessionId, SlotId, "2");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateSlot_WithDestination_BuildsUserPatch_WithSourceAndDestination()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.UpdateSlotAsync(Tenant, SessionId, SlotId, "2");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("guest_star/slot");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "source_slot_id" && q.Value == SlotId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "destination_slot_id" && q.Value == "2");
    }

    [Fact]
    public async Task UpdateSlot_NullDestination_OmitsDestinationSlotId()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.UpdateSlotAsync(Tenant, SessionId, SlotId, null);

        result.IsSuccess.Should().BeTrue();
        transport
            .LastRequest!.Query.Should()
            .Contain(q => q.Key == "source_slot_id" && q.Value == SlotId);
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "destination_slot_id");
    }

    // ── Delete Slot ──

    [Fact]
    public async Task DeleteSlot_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result result = await api.DeleteSlotAsync(Tenant, SessionId, GuestId, SlotId, true);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteSlot_WithReinviteFlag_BuildsUserDelete_WithAllKeys_AndBoolString()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.DeleteSlotAsync(Tenant, SessionId, GuestId, SlotId, true);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("guest_star/slot");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "session_id" && q.Value == SessionId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "guest_id" && q.Value == GuestId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "slot_id" && q.Value == SlotId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "should_reinvite_guest" && q.Value == "true");
    }

    [Fact]
    public async Task DeleteSlot_NullReinviteFlag_OmitsShouldReinviteGuest()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.DeleteSlotAsync(Tenant, SessionId, GuestId, SlotId, null);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "should_reinvite_guest");
    }

    // ── Update Slot Settings ──

    [Fact]
    public async Task UpdateSlotSettings_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport);

        Result result = await api.UpdateSlotSettingsAsync(
            Tenant,
            SessionId,
            SlotId,
            isAudioEnabled: true,
            isVideoEnabled: null,
            isLive: null,
            volume: null
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateSlotSettings_WithScope_BuildsUserPatch_OnlySuppliedSettings()
    {
        CapturingHelixTransport transport = new();
        TwitchGuestStarApi api = Build(transport, TwitchScopes.ChannelManageGuestStar);

        Result result = await api.UpdateSlotSettingsAsync(
            Tenant,
            SessionId,
            SlotId,
            isAudioEnabled: false,
            isVideoEnabled: true,
            isLive: null,
            volume: 50
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("guest_star/slot_settings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "moderator_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "session_id" && q.Value == SessionId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "slot_id" && q.Value == SlotId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "is_audio_enabled" && q.Value == "false");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "is_video_enabled" && q.Value == "true");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "volume" && q.Value == "50");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "is_live");
    }
}
