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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Behavioural tests for the DTO-agnostic Helix send pipeline: the generic <c>data[]</c>/pagination envelope
/// parses and exposes the cursor; a 401 triggers exactly one refresh-and-retry that uses the refreshed token;
/// a second 401 fails closed and emits the re-auth event; and non-success statuses map to the typed codes.
/// </summary>
public class TwitchHelixTransportTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-1111-7111-8111-000000000001");

    private sealed record Followers(
        [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] string UserId,
        [property: System.Text.Json.Serialization.JsonPropertyName("user_name")] string UserName
    );

    /// <summary>Wires the transport over an auth-handler→wire chain so the wire records the bearer actually sent.</summary>
    private static (
        TwitchHelixTransport Transport,
        RecordingHelixHandler Wire,
        FakeTwitchTokenResolver Resolver,
        CapturingEventBus Bus
    ) Build(IEnumerable<Func<HttpResponseMessage>> responses)
    {
        RecordingHelixHandler wire = new(responses);
        TwitchAuthHeaderHandler auth = new(
            Options.Create(new TwitchOptions { ClientId = "client-id" })
        )
        {
            InnerHandler = wire,
        };
        HttpClient httpClient = new(auth);

        FakeTwitchTokenResolver resolver = new();
        CapturingEventBus bus = new();
        TwitchHelixTransport transport = new(
            new SingleClientFactory(httpClient),
            resolver,
            bus,
            NullLogger<TwitchHelixTransport>.Instance
        );
        return (transport, wire, resolver, bus);
    }

    [Fact]
    public async Task GetPageAsync_DeserializesDataAndExposesCursor()
    {
        const string body = """
            {
              "total": 9,
              "data": [
                { "user_id": "1", "user_name": "alice" },
                { "user_id": "2", "user_name": "bob" }
              ],
              "pagination": { "cursor": "next-page-cursor" }
            }
            """;
        (TwitchHelixTransport transport, _, _, _) = Build([
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, body),
        ]);

        Result<TwitchPage<Followers>> result = await transport.GetPageAsync<Followers>(
            new TwitchHelixRequest(HttpMethod.Get, "channels/followers", TwitchHelixAuth.App)
        );

        result.IsSuccess.Should().BeTrue();
        TwitchPage<Followers> page = result.Value;
        page.Items.Should().HaveCount(2);
        page.Items[0].UserId.Should().Be("1");
        page.Items[0].UserName.Should().Be("alice");
        page.NextCursor.Should().Be("next-page-cursor");
        page.Total.Should().Be(9);
    }

    [Fact]
    public async Task GetPageAsync_FinalPage_HasNullCursor()
    {
        const string body = """{ "total": 1, "data": [ { "user_id": "1", "user_name": "z" } ] }""";
        (TwitchHelixTransport transport, _, _, _) = Build([
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, body),
        ]);

        Result<TwitchPage<Followers>> result = await transport.GetPageAsync<Followers>(
            new TwitchHelixRequest(HttpMethod.Get, "channels/followers", TwitchHelixAuth.App)
        );

        result.Value.NextCursor.Should().BeNull();
        result.Value.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetTotalAsync_ReadsTopLevelTotal()
    {
        (TwitchHelixTransport transport, _, _, _) = Build([
            () =>
                RecordingHelixHandler.Json(HttpStatusCode.OK, """{ "total": 1234, "data": [] }"""),
        ]);

        Result<int> result = await transport.GetTotalAsync(
            new TwitchHelixRequest(HttpMethod.Get, "subscriptions", TwitchHelixAuth.User, Tenant)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1234);
    }

    [Fact]
    public async Task GetSingleAsync_EmptyData_ReturnsNotFound()
    {
        (TwitchHelixTransport transport, _, _, _) = Build([
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, """{ "data": [] }"""),
        ]);

        Result<Followers> result = await transport.GetSingleAsync<Followers>(
            new TwitchHelixRequest(HttpMethod.Get, "users", TwitchHelixAuth.App)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
    }

    [Fact]
    public async Task SendCore_On401_RefreshesExactlyOnce_AndUsesRefreshedToken()
    {
        // First attempt: 401. After one refresh, second attempt: 200.
        (
            TwitchHelixTransport transport,
            RecordingHelixHandler wire,
            FakeTwitchTokenResolver resolver,
            _
        ) = Build([
            () => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, """{ "data": [] }"""),
        ]);

        Result<IReadOnlyList<Followers>> result = await transport.GetListAsync<Followers>(
            new TwitchHelixRequest(
                HttpMethod.Get,
                "channels/followers",
                TwitchHelixAuth.User,
                Tenant
            )
        );

        result.IsSuccess.Should().BeTrue();
        resolver.RefreshCallCount.Should().Be(1); // exactly one refresh
        wire.CallCount.Should().Be(2); // original + one retry

        // The first call carried the initial token; the retry carried the refreshed token.
        wire.Requests[0].AuthorizationParameter.Should().Be("initial-token");
        wire.Requests[1].AuthorizationParameter.Should().Be("refreshed-token");
    }

    [Fact]
    public async Task SendCore_PersistentlyUnauthorized_FailsAndEmitsReauthEvent()
    {
        // 401 even after refresh — fails closed, one refresh attempt, re-auth event published.
        (
            TwitchHelixTransport transport,
            RecordingHelixHandler wire,
            FakeTwitchTokenResolver resolver,
            CapturingEventBus bus
        ) = Build([
            () => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            () => new HttpResponseMessage(HttpStatusCode.Unauthorized),
        ]);

        Result result = await transport.SendAsync(
            new TwitchHelixRequest(HttpMethod.Post, "moderation/bans", TwitchHelixAuth.User, Tenant)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Unauthorized);
        resolver.RefreshCallCount.Should().Be(1);
        wire.CallCount.Should().Be(2);
        bus.EventsOf<TwitchHelixReauthRequiredEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_MapsStatusToTypedErrorCodes()
    {
        (TwitchHelixTransport transport, _, _, _) = Build([
            () => new HttpResponseMessage(HttpStatusCode.NotFound),
        ]);

        Result result = await transport.SendAsync(
            new TwitchHelixRequest(HttpMethod.Delete, "channels/vips", TwitchHelixAuth.User, Tenant)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
    }

    [Fact]
    public async Task SendAsync_AppToken401_DoesNotRefresh()
    {
        // App/bot-token calls have no broadcaster to refresh — a 401 fails without a refresh attempt.
        (
            TwitchHelixTransport transport,
            RecordingHelixHandler wire,
            FakeTwitchTokenResolver resolver,
            _
        ) = Build([() => new HttpResponseMessage(HttpStatusCode.Unauthorized)]);

        Result result = await transport.SendAsync(
            new TwitchHelixRequest(HttpMethod.Get, "users", TwitchHelixAuth.App)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Unauthorized);
        resolver.RefreshCallCount.Should().Be(0);
        wire.CallCount.Should().Be(1);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
