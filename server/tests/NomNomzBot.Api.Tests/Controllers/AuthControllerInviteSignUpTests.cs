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

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Caching;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-INVITE-TIER sign-up round trip: a signed-out visitor opens an invite link and signs in with Twitch. The
/// code rides in the server-side OAuth state (never through Twitch), survives the round trip through the real
/// <see cref="TwitchOAuthStateService"/>, and the callback redeems it once on the channel the sign-in just
/// opened. The redirect fragment tells the dashboard what happened; a failed redeem never blocks the sign-in.
/// </summary>
public sealed class AuthControllerInviteSignUpTests
{
    private static readonly Guid UserId = Guid.Parse("0199f000-0000-7000-8000-0000000000e1");

    [Fact]
    public async Task A_pending_invite_survives_sign_up_and_redeems_once_on_the_new_channel()
    {
        IInviteCodeService invites = Substitute.For<IInviteCodeService>();
        invites
            .RedeemForOwnerAsync(UserId, "ABCDEF1234", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RedeemInviteCodeResultDto(false, "unlimited", null)));

        RedirectResult redirect = await SignUpAsync("ABCDEF1234", invites);

        await invites
            .Received(1)
            .RedeemForOwnerAsync(UserId, "ABCDEF1234", Arg.Any<CancellationToken>());
        redirect.Url.Should().Contain("#access_token=acc-tok");
        redirect.Url.Should().Contain("&invite=redeemed");
        redirect.Url.Should().NotContain("invite_reason");
    }

    [Fact]
    public async Task A_failed_redeem_still_signs_the_streamer_in_and_says_why()
    {
        IInviteCodeService invites = Substitute.For<IInviteCodeService>();
        invites
            .RedeemForOwnerAsync(UserId, "USEDUP0001", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<RedeemInviteCodeResultDto>(
                    "Invite code is exhausted.",
                    "RATE_LIMITED"
                )
            );

        RedirectResult redirect = await SignUpAsync("USEDUP0001", invites);

        redirect.Url.Should().Contain("#access_token=acc-tok");
        redirect.Url.Should().Contain("&invite=failed");
        redirect
            .Url.Should()
            .Contain($"&invite_reason={Uri.EscapeDataString("Invite code is exhausted.")}");
    }

    [Fact]
    public async Task A_sign_in_without_an_invite_redeems_nothing_and_adds_nothing_to_the_fragment()
    {
        IInviteCodeService invites = Substitute.For<IInviteCodeService>();

        RedirectResult redirect = await SignUpAsync(invite: null, invites);

        await invites
            .DidNotReceiveWithAnyArgs()
            .RedeemForOwnerAsync(default, default!, Arg.Any<CancellationToken>());
        redirect.Url.Should().NotContain("invite");
    }

    [Theory]
    [InlineData("not a code!")]
    [InlineData("<script>")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_malformed_invite_is_dropped_before_it_enters_the_state(string invite)
    {
        IInviteCodeService invites = Substitute.For<IInviteCodeService>();

        RedirectResult redirect = await SignUpAsync(invite, invites);

        await invites
            .DidNotReceiveWithAnyArgs()
            .RedeemForOwnerAsync(default, default!, Arg.Any<CancellationToken>());
        redirect.Url.Should().NotContain("invite");
    }

    private static async Task<RedirectResult> SignUpAsync(
        string? invite,
        IInviteCodeService invites
    )
    {
        IAuthService auth = Substitute.For<IAuthService>();
        TwitchOAuthStateService state = new(
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );
        string issued = string.Empty;
        auth.GetTwitchOAuthUrl(
                Arg.Any<TwitchLoginRole>(),
                Arg.Do<string>(s => issued = s),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Guid?>()
            )
            .Returns(Result.Success("https://id.twitch.tv/oauth2/authorize?x=1"));
        auth.HandleTwitchCallbackAsync(
                Arg.Any<OAuthCallbackDto>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(SignedIn()));

        await Controller(auth, state).StartTwitchOAuth(null, "web", "/app", null, null, invite);
        IActionResult result = await Controller(auth, state)
            .HandleTwitchCallback("code", issued, invites, default);

        return result.Should().BeOfType<RedirectResult>().Subject;
    }

    private static AuthResultDto SignedIn() =>
        new(
            "acc-tok",
            "ref-tok",
            DateTime.UtcNow.AddHours(1),
            new(
                UserId.ToString(),
                "newstreamer",
                "NewStreamer",
                ProfileImageUrl: null,
                Email: null,
                CreatedAt: DateTime.UtcNow,
                LastLoginAt: DateTime.UtcNow
            )
        );

    private static AuthController Controller(IAuthService auth, ITwitchOAuthStateService state)
    {
        DefaultHttpContext http = new();
        http.Request.Scheme = "https";
        http.Request.Host = new("dev.nomnomz.bot");
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://dev.nomnomz.bot" }
            )
            .Build();

        return new(
            Substitute.For<IUserService>(),
            auth,
            config,
            TestHostEnvironment.Production,
            TimeProvider.System,
            state,
            Substitute.For<ILoginProviderRegistry>(),
            Substitute.For<IUserIdentityService>(),
            [],
            [],
            Substitute.For<IExternalLoginService>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISystemCredentialsProvider>(),
            Substitute.For<IImpersonationSessionService>(),
            Substitute.For<IViewAsScope>()
        )
        {
            ControllerContext = new() { HttpContext = http },
        };
    }
}
