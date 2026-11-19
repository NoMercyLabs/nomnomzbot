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

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the Guest Star endpoints hand the channel's tenant id and the request values to the Helix client,
/// return the client's data in the standard envelope, map Twitch failures to their HTTP status, and sit
/// behind the right action key. Only the Helix client is faked: it is the outside world.
/// </summary>
public sealed class GuestStarControllerTests
{
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string SessionId = "session-1";
    private const string GuestId = "guest-42";
    private const string SlotId = "2";

    private static TwitchGuestStarSession SampleSession() =>
        new(
            SessionId,
            [
                new TwitchGuestStarGuest(
                    SlotId,
                    true,
                    GuestId,
                    "GuestName",
                    "guestname",
                    80,
                    DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
                    new TwitchGuestStarMediaSettings(true, true, true),
                    new TwitchGuestStarMediaSettings(true, false, true)
                ),
            ]
        );

    private static T Data<T>(IActionResult result)
    {
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<T> body = ok.Value.Should().BeOfType<StatusResponseDto<T>>().Subject;
        body.Status.Should().Be("ok");
        return body.Data!;
    }

    private static void IsOkStatusOnly(IActionResult result)
    {
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<StatusResponseDto<object>>().Which.Status.Should().Be("ok");
    }

    [Fact]
    public async Task GetSettings_returns_the_channel_settings_for_the_channel()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        TwitchGuestStarChannelSettings settings = new(true, 4, false, "TILED_LAYOUT", "token-x");
        api.GetChannelSettingsAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(settings));
        GuestStarController controller = new(api);

        IActionResult result = await controller.GetSettings(ChannelId, CancellationToken.None);

        Data<TwitchGuestStarChannelSettings>(result).Should().Be(settings);
    }

    [Fact]
    public async Task UpdateSettings_passes_the_body_through_unchanged()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.UpdateChannelSettingsAsync(
                ChannelId,
                Arg.Any<UpdateGuestStarSettingsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        GuestStarController controller = new(api);
        UpdateGuestStarSettingsRequest body = new(
            IsModeratorSendLiveEnabled: true,
            SlotCount: 3,
            GroupLayout: "SIDEBAR_LAYOUT"
        );

        IActionResult result = await controller.UpdateSettings(
            ChannelId,
            body,
            CancellationToken.None
        );

        IsOkStatusOnly(result);
        await api.Received(1)
            .UpdateChannelSettingsAsync(ChannelId, body, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSession_returns_the_guests_in_the_session()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.GetSessionAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleSession()));
        GuestStarController controller = new(api);

        IActionResult result = await controller.GetSession(ChannelId, CancellationToken.None);

        TwitchGuestStarSession session = Data<TwitchGuestStarSession>(result);
        session.Id.Should().Be(SessionId);
        session.Guests.Should().ContainSingle().Which.UserId.Should().Be(GuestId);
    }

    [Fact]
    public async Task CreateSession_starts_a_session_and_returns_it()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.CreateSessionAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleSession()));
        GuestStarController controller = new(api);

        IActionResult result = await controller.CreateSession(ChannelId, CancellationToken.None);

        Data<TwitchGuestStarSession>(result).Id.Should().Be(SessionId);
        await api.Received(1).CreateSessionAsync(ChannelId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EndSession_ends_the_named_session_and_returns_it()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.EndSessionAsync(ChannelId, SessionId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleSession()));
        GuestStarController controller = new(api);

        IActionResult result = await controller.EndSession(
            ChannelId,
            SessionId,
            CancellationToken.None
        );

        Data<TwitchGuestStarSession>(result).Id.Should().Be(SessionId);
        await api.Received(1).EndSessionAsync(ChannelId, SessionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInvites_returns_the_invites_of_the_named_session()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        IReadOnlyList<TwitchGuestStarInvite> invites =
        [
            new(GuestId, DateTimeOffset.UnixEpoch, "READY", true, false, true, true),
        ];
        api.GetInvitesAsync(ChannelId, SessionId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(invites));
        GuestStarController controller = new(api);

        IActionResult result = await controller.GetInvites(
            ChannelId,
            SessionId,
            CancellationToken.None
        );

        Data<IReadOnlyList<TwitchGuestStarInvite>>(result).Should().Equal(invites);
    }

    [Fact]
    public async Task SendInvite_sends_the_guest_id_from_the_body()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.SendInviteAsync(ChannelId, SessionId, GuestId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        GuestStarController controller = new(api);

        IActionResult result = await controller.SendInvite(
            ChannelId,
            SessionId,
            new GuestStarController.InviteGuestRequest(GuestId),
            CancellationToken.None
        );

        IsOkStatusOnly(result);
        await api.Received(1)
            .SendInviteAsync(ChannelId, SessionId, GuestId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteInvite_revokes_the_invite_of_the_guest_in_the_route()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.DeleteInviteAsync(ChannelId, SessionId, GuestId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        GuestStarController controller = new(api);

        IActionResult result = await controller.DeleteInvite(
            ChannelId,
            SessionId,
            GuestId,
            CancellationToken.None
        );

        IsOkStatusOnly(result);
        await api.Received(1)
            .DeleteInviteAsync(ChannelId, SessionId, GuestId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignSlot_places_the_guest_in_the_slot_from_the_body()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.AssignSlotAsync(ChannelId, SessionId, GuestId, SlotId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        GuestStarController controller = new(api);

        IActionResult result = await controller.AssignSlot(
            ChannelId,
            SessionId,
            new GuestStarController.AssignSlotRequest(GuestId, SlotId),
            CancellationToken.None
        );

        IsOkStatusOnly(result);
        await api.Received(1)
            .AssignSlotAsync(ChannelId, SessionId, GuestId, SlotId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("3")]
    [InlineData(null)]
    public async Task MoveSlot_moves_the_route_slot_to_the_destination_or_to_the_empty_interface(
        string? destination
    )
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.UpdateSlotAsync(ChannelId, SessionId, SlotId, destination, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        GuestStarController controller = new(api);

        IActionResult result = await controller.MoveSlot(
            ChannelId,
            SessionId,
            SlotId,
            new GuestStarController.MoveSlotRequest(destination),
            CancellationToken.None
        );

        IsOkStatusOnly(result);
        await api.Received(1)
            .UpdateSlotAsync(
                ChannelId,
                SessionId,
                SlotId,
                destination,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task DeleteSlot_removes_the_guest_from_the_route_slot(bool? reinvite)
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.DeleteSlotAsync(
                ChannelId,
                SessionId,
                GuestId,
                SlotId,
                reinvite,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        GuestStarController controller = new(api);

        IActionResult result = await controller.DeleteSlot(
            ChannelId,
            SessionId,
            SlotId,
            GuestId,
            reinvite,
            CancellationToken.None
        );

        IsOkStatusOnly(result);
        await api.Received(1)
            .DeleteSlotAsync(
                ChannelId,
                SessionId,
                GuestId,
                SlotId,
                reinvite,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task UpdateSlotSettings_sends_only_the_values_in_the_body()
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.UpdateSlotSettingsAsync(
                ChannelId,
                SessionId,
                SlotId,
                false,
                null,
                true,
                55,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        GuestStarController controller = new(api);

        IActionResult result = await controller.UpdateSlotSettings(
            ChannelId,
            SessionId,
            SlotId,
            new GuestStarController.UpdateSlotSettingsRequest(
                IsAudioEnabled: false,
                IsLive: true,
                Volume: 55
            ),
            CancellationToken.None
        );

        IsOkStatusOnly(result);
        await api.Received(1)
            .UpdateSlotSettingsAsync(
                ChannelId,
                SessionId,
                SlotId,
                false,
                null,
                true,
                55,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(TwitchErrorCodes.MissingScope, 403)]
    [InlineData(TwitchErrorCodes.NotFound, 404)]
    [InlineData(TwitchErrorCodes.NoToken, 409)]
    [InlineData(TwitchErrorCodes.RateLimited, 429)]
    [InlineData(TwitchErrorCodes.TwitchError, 502)]
    public async Task A_twitch_failure_maps_to_its_http_status_with_the_message(
        string code,
        int status
    )
    {
        ITwitchGuestStarApi api = Substitute.For<ITwitchGuestStarApi>();
        api.GetSessionAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchGuestStarSession>("Twitch said no.", code));
        GuestStarController controller = new(api);

        IActionResult result = await controller.GetSession(ChannelId, CancellationToken.None);

        ObjectResult failed = result.Should().BeAssignableTo<ObjectResult>().Subject;
        failed.StatusCode.Should().Be(status);
        failed
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Which.Message.Should()
            .Contain("Twitch said no.");
    }

    [Theory]
    [InlineData(nameof(GuestStarController.GetSettings), "guest-star:read")]
    [InlineData(nameof(GuestStarController.UpdateSettings), "guest-star:write")]
    [InlineData(nameof(GuestStarController.GetSession), "guest-star:read")]
    [InlineData(nameof(GuestStarController.CreateSession), "guest-star:write")]
    [InlineData(nameof(GuestStarController.EndSession), "guest-star:write")]
    [InlineData(nameof(GuestStarController.GetInvites), "guest-star:read")]
    [InlineData(nameof(GuestStarController.SendInvite), "guest-star:write")]
    [InlineData(nameof(GuestStarController.DeleteInvite), "guest-star:write")]
    [InlineData(nameof(GuestStarController.AssignSlot), "guest-star:write")]
    [InlineData(nameof(GuestStarController.MoveSlot), "guest-star:write")]
    [InlineData(nameof(GuestStarController.DeleteSlot), "guest-star:write")]
    [InlineData(nameof(GuestStarController.UpdateSlotSettings), "guest-star:write")]
    public void Every_endpoint_is_gated_on_the_read_or_write_key(string method, string key)
    {
        MethodInfo action = typeof(GuestStarController).GetMethod(
            method,
            BindingFlags.Public | BindingFlags.Instance
        )!;

        action
            .GetCustomAttribute<RequireActionAttribute>()
            .Should()
            .NotBeNull()
            .And.Subject.As<RequireActionAttribute>()
            .ActionKey.Should()
            .Be(key);
    }

    [Fact]
    public void The_controller_exposes_exactly_twelve_endpoints()
    {
        MethodInfo[] endpoints = typeof(GuestStarController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToArray();

        endpoints.Should().HaveCount(12);
    }
}
