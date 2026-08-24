// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Owner-blocking bug (2026-08-24): the streamer device login rendered the code and "waiting for approval"
/// correctly, but never completed after a real Twitch approval. Root cause: once Twitch authorizes the device
/// code, <see cref="AuthService.PollTwitchDeviceLoginAsync"/> still has to exchange the token for the Twitch
/// user (<c>GET /helix/users</c>) and persist the session — and when THAT step failed, the method returned a
/// <see cref="Result{T}.Failure"/>, which the API controller turned into a non-200 HTTP response. The KMP
/// client's poll loop tolerates any HTTP-level failure silently for up to the device code's full expiry window
/// (30 minutes) with zero feedback — exactly the "approves and it hangs forever" symptom, because a genuinely
/// terminal failure was indistinguishable from a transient network blip.
///
/// The fix: a post-authorization exchange failure must still resolve to a <see cref="Result{T}.Success"/>
/// carrying a terminal <see cref="DeviceLoginStatus.Error"/> status (HTTP 200), exactly like the transport-level
/// terminal statuses (<c>expired</c>/<c>denied</c>) already do — so the poll loop's existing terminal-status
/// handling fires immediately instead of the tolerate-until-deadline branch.
/// </summary>
public sealed class AuthServiceDevicePollTerminalErrorTests
{
    private const string DeviceCode = "device-code-1";

    [Fact]
    public async Task Poll_whose_post_authorization_exchange_fails_resolves_to_a_terminal_error_status_not_an_HTTP_failure()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync("twitch", Arg.Any<CancellationToken>())
            .Returns("byoc-client-id");

        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        deviceCode
            .PollOnceAsync(
                DeviceCode,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new DevicePollOutcome(
                    DevicePollStatus.Authorized,
                    new TokenResult(
                        "access-token",
                        "refresh-token",
                        DateTime.UtcNow.AddHours(4),
                        ["user:read:email"]
                    )
                )
            );

        // The post-authorization Helix user lookup fails (e.g. the token/client-id pairing is rejected, or
        // Twitch is briefly unavailable) — this is the exact condition that used to surface as a hard
        // Result.Failure instead of a terminal poll status.
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(new FailingHelixHandler()));

        IConfiguration config = new ConfigurationBuilder().Build();

        AuthService service = new(
            db,
            Substitute.For<ITwitchAuthService>(),
            deviceCode,
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
            credentials,
            httpClientFactory,
            config,
            new(DeploymentMode.SelfHostFull),
            TimeProvider.System,
            new(),
            Substitute.For<IPlatformOwnerPrincipalMinter>(),
            NullLogger<AuthService>.Instance
        );

        Result<DeviceLoginPollDto> result = await service.PollTwitchDeviceLoginAsync(
            DeviceCode,
            new AuthContextDto("web", "127.0.0.1", "test-agent")
        );

        // The behavior that broke the flow: this must be a 200-mappable Success, never a Failure the
        // controller turns into a non-200 the poll loop silently tolerates for 30 minutes.
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DeviceLoginStatus.Error);
        result.Value.Auth.Should().BeNull();
    }

    private sealed class FailingHelixHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
}
