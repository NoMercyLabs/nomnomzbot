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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// The refresh, logout and logout-all endpoints over the REAL <c>AuthService</c>, session store and revocation
/// store (only Twitch and the event bus are substitutes): what a pressed logout really does to the sessions, to
/// the access tokens already handed out, and to the event bus.
/// </summary>
public sealed class AuthSessionEndpointTests : IAsyncLifetime
{
    private static readonly Guid Viewer = Guid.Parse("0192b000-0000-7000-8000-000000000f01");
    private static readonly Guid Admin = Guid.Parse("0192b000-0000-7000-8000-000000000f02");

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(Admin);
        await _host.SeedAsync(db =>
        {
            db.Users.Add(NewUser(Viewer, "session_viewer"));
            db.Users.Add(NewUser(Admin, "session_admin"));
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Logout_ends_only_that_session_and_publishes_the_logout_event()
    {
        SessionTokensDto ended = await _host.LoginAsync(Viewer, null);
        SessionTokensDto other = await _host.LoginAsync(Viewer, null);
        (await MeStatusAsync(ended.AccessToken)).Should().Be(HttpStatusCode.OK);

        HttpResponseMessage response = await PostAsync("/api/v1/auth/logout", ended.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.IsSessionLiveAsync(ended.RawRefreshToken)).Should().BeFalse();
        (await _host.IsSessionLiveAsync(other.RawRefreshToken)).Should().BeTrue();
        (await MeStatusAsync(ended.AccessToken))
            .Should()
            .Be(
                HttpStatusCode.Unauthorized,
                "the access token already handed out dies with its sid"
            );
        (await MeStatusAsync(other.AccessToken)).Should().Be(HttpStatusCode.OK);
        await _host
            .EventBus.Received(1)
            .PublishAsync(
                Arg.Is<UserLoggedOutEvent>(e =>
                    e.UserId == Viewer
                    && e.SessionId == ended.SessionId
                    && e.Reason == AuthEnums.RefreshTokenRevokedReason.Logout
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Logout_all_revokes_every_session_and_every_access_token_of_the_user()
    {
        SessionTokensDto first = await _host.LoginAsync(Viewer, null);
        SessionTokensDto second = await _host.LoginAsync(Viewer, null);
        SessionTokensDto adminSession = await _host.LoginAsync(Admin, null);

        HttpResponseMessage response = await PostAsync(
            "/api/v1/auth/logout/all",
            first.AccessToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.IsSessionLiveAsync(first.RawRefreshToken)).Should().BeFalse();
        (await _host.IsSessionLiveAsync(second.RawRefreshToken)).Should().BeFalse();
        (await MeStatusAsync(first.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await MeStatusAsync(second.AccessToken))
            .Should()
            .Be(
                HttpStatusCode.Unauthorized,
                "the other session's access token is sid-revoked too, not only its refresh token"
            );
        (await MeStatusAsync(adminSession.AccessToken))
            .Should()
            .Be(HttpStatusCode.OK, "another user's session is not touched");
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_answers_the_sessions_user()
    {
        SessionTokensDto login = await _host.LoginAsync(Viewer, null);

        HttpResponseMessage response = await PostAsync(
            "/api/v1/auth/refresh",
            bearer: null,
            cookies: $"nnz_refresh_token={login.RawRefreshToken}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("session_viewer");
        string accessToken = JsonNode.Parse(body)!["data"]!["accessToken"]!.GetValue<string>();
        (await MeStatusAsync(accessToken)).Should().Be(HttpStatusCode.OK);
        (await _host.IsSessionLiveAsync(login.RawRefreshToken))
            .Should()
            .BeFalse("the presented refresh token is consumed by the rotation");
    }

    [Fact]
    public async Task Refresh_for_a_user_who_no_longer_exists_is_not_found_and_hands_out_nothing()
    {
        SessionTokensDto login = await _host.LoginAsync(Viewer, null);
        await _host.SeedAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            await db.Users.Where(u => u.Id == Viewer).ExecuteDeleteAsync();
        });

        HttpResponseMessage response = await PostAsync(
            "/api/v1/auth/refresh",
            bearer: null,
            cookies: $"nnz_refresh_token={login.RawRefreshToken}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response
            .Headers.Contains("Set-Cookie")
            .Should()
            .BeFalse("no new refresh cookie is set when the user is gone");
        (await _host.ReadAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == Viewer)))
            .Should()
            .Be(1, "no successor refresh token was minted");
    }

    private async Task<HttpStatusCode> MeStatusAsync(string accessToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _host.Client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<HttpResponseMessage> PostAsync(
        string path,
        string? bearer,
        string? cookies = null
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (cookies is not null)
            request.Headers.Add("Cookie", cookies);
        request.Headers.Add("Origin", "http://localhost");
        request.Content = JsonContent.Create(new { });
        return await _host.Client.SendAsync(request);
    }

    private static User NewUser(Guid id, string username) =>
        new()
        {
            Id = id,
            TwitchUserId = id.ToString("N")[^10..],
            Username = username,
            UsernameNormalized = username,
            DisplayName = username,
            Color = "#1E90FF",
        };
}
