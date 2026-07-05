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
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Interfaces;
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
    ) Build(IEnumerable<Func<HttpResponseMessage>> responses, string? dbClientId = null)
    {
        RecordingHelixHandler wire = new(responses);
        // The auth-handler config fallback is "config-client-id"; when the credentials provider supplies a
        // (wizard-vaulted) id it must win — proving the Helix Client-Id is the dynamically-resolved one.
        TwitchAuthHeaderHandler auth = new(
            Options.Create(new TwitchOptions { ClientId = "config-client-id" })
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
            new StubCredentialsProvider(dbClientId),
            bus,
            NullLogger<TwitchHelixTransport>.Instance
        );
        return (transport, wire, resolver, bus);
    }

    /// <summary>
    /// A credentials provider double at the resolution seam: it returns the seeded <c>client_id</c> from
    /// <see cref="ISystemCredentialsProvider.GetValueAsync"/> (null = unconfigured → handler falls back to
    /// config). The real DB→config resolution is covered by <c>SystemCredentialsProviderTests</c>.
    /// </summary>
    private sealed class StubCredentialsProvider(string? clientId) : ISystemCredentialsProvider
    {
        public Task<SystemAppCredentials?> GetAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<SystemAppCredentials?>(
                clientId is null ? null : new SystemAppCredentials(clientId, "secret")
            );

        public Task<string?> GetValueAsync(
            string provider,
            string field,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(clientId);

        public Task<string?> GetClientIdAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(clientId);
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
    public async Task SendCore_SendsTheDynamicallyResolvedClientId_OverConfig()
    {
        // The credentials provider supplies a wizard-vaulted client id; it must be the Client-Id on the wire,
        // overriding the auth-handler's config fallback — proving Helix uses the dynamically-resolved app id.
        (TwitchHelixTransport transport, RecordingHelixHandler wire, _, _) = Build(
            [() => RecordingHelixHandler.Json(HttpStatusCode.OK, """{ "data": [] }""")],
            dbClientId: "wizard-vaulted-client-id"
        );

        await transport.GetListAsync<Followers>(
            new TwitchHelixRequest(HttpMethod.Get, "users", TwitchHelixAuth.App)
        );

        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.ClientId.Should().Be("wizard-vaulted-client-id");
    }

    [Fact]
    public async Task SendCore_FallsBackToConfigClientId_WhenNoVaultedIdResolves()
    {
        // No credentials provider value (e.g. a pure env/appsettings deployment): the config Client-Id stands.
        (TwitchHelixTransport transport, RecordingHelixHandler wire, _, _) = Build([
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, """{ "data": [] }"""),
        ]);

        await transport.GetListAsync<Followers>(
            new TwitchHelixRequest(HttpMethod.Get, "users", TwitchHelixAuth.App)
        );

        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.ClientId.Should().Be("config-client-id");
    }

    [Fact]
    public async Task GetRawAsync_ReturnsTextCalendarBodyVerbatim()
    {
        // The schedule iCalendar endpoint returns RFC 5545 text (Content-Type text/calendar), not the
        // JSON envelope — the raw path must hand the body back byte-for-byte.
        const string ical = """
            BEGIN:VCALENDAR
            PRODID:-//twitch.tv//StreamSchedule//1.0
            VERSION:2.0
            CALSCALE:GREGORIAN
            NAME:TwitchDev
            BEGIN:VEVENT
            UID:e4acc724-371f-402c-81ca-23ada79759d4
            SUMMARY:TwitchDev Monthly Update // July 1, 2021
            END:VEVENT
            END:VCALENDAR
            """;
        (TwitchHelixTransport transport, RecordingHelixHandler wire, _, _) = Build([
            () => RecordingHelixHandler.Text(HttpStatusCode.OK, ical, "text/calendar"),
        ]);

        Result<string> result = await transport.GetRawAsync(
            new TwitchHelixRequest(
                HttpMethod.Get,
                "schedule/icalendar",
                TwitchHelixAuth.App,
                Query: [new("broadcaster_id", "141981764")]
            )
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ical);
        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Uri.AbsolutePath.Should().Be("/helix/schedule/icalendar");
        sent.Uri.Query.Should().Be("?broadcaster_id=141981764");
    }

    [Fact]
    public async Task GetRawAsync_NonSuccess_MapsToTypedError()
    {
        // 400 (e.g. a bad broadcaster_id on the iCalendar endpoint) maps into the closed error-code set
        // exactly like the JSON sends do.
        (TwitchHelixTransport transport, _, _, _) = Build([
            () =>
                RecordingHelixHandler.Json(
                    HttpStatusCode.BadRequest,
                    """{ "error": "Bad Request", "status": 400, "message": "The ID in the broadcaster_id query parameter is not valid." }"""
                ),
        ]);

        Result<string> result = await transport.GetRawAsync(
            new TwitchHelixRequest(HttpMethod.Get, "schedule/icalendar", TwitchHelixAuth.App)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.TwitchError);
    }

    [Fact]
    public async Task GetSingleAsync_DataAsObject_ParsesSingleNestedSchedule()
    {
        // Get Channel Stream Schedule sends data as a single nested OBJECT, not an array — the envelope's
        // object-or-array converter must wrap it into the one element GetSingleAsync returns.
        const string body = """
            {
              "data": {
                "segments": [
                  {
                    "id": "eyJzZWdtZW50SUQiOiJlNGFjYzcyNCJ9",
                    "start_time": "2021-07-01T18:00:00Z",
                    "end_time": "2021-07-01T19:00:00Z",
                    "title": "TwitchDev Monthly Update // July 1, 2021",
                    "canceled_until": null,
                    "category": { "id": "509670", "name": "Science & Technology" },
                    "is_recurring": false
                  }
                ],
                "broadcaster_id": "141981764",
                "broadcaster_name": "TwitchDev",
                "broadcaster_login": "twitchdev",
                "vacation": null
              },
              "pagination": {}
            }
            """;
        (TwitchHelixTransport transport, _, _, _) = Build([
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, body),
        ]);

        Result<TwitchSchedule> result = await transport.GetSingleAsync<TwitchSchedule>(
            new TwitchHelixRequest(HttpMethod.Get, "schedule", TwitchHelixAuth.App)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.BroadcasterId.Should().Be("141981764");
        result.Value.BroadcasterLogin.Should().Be("twitchdev");
        result.Value.Vacation.Should().BeNull();
        TwitchScheduleSegment segment = result.Value.Segments.Should().ContainSingle().Subject;
        segment.Title.Should().Be("TwitchDev Monthly Update // July 1, 2021");
        segment.StartTime.Should().Be(new DateTimeOffset(2021, 7, 1, 18, 0, 0, TimeSpan.Zero));
        segment.Category!.Name.Should().Be("Science & Technology");
        segment.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task GetSingleAsync_DataAsObject_ParsesActiveExtensionSlotMaps()
    {
        // Get User Active Extensions: data is an object of slot maps keyed "1", "2", … — numeric keys must
        // pass through untouched, inactive slots carry only "active", and component slots add x/y.
        const string body = """
            {
              "data": {
                "panel": {
                  "1": { "active": true, "id": "rh6jq1q334hqc2rr1qlzqbvwlfl3x0", "version": "1.1.0", "name": "TopClip" },
                  "2": { "active": false }
                },
                "overlay": {
                  "1": { "active": true, "id": "zfh2irvx2jb4s60f02jq0ajm8vwgka", "version": "1.0.19", "name": "Streamlabs" }
                },
                "component": {
                  "1": { "active": true, "id": "lqnf3zxk0rv0g7gq92mtmnirjz2cjj", "version": "0.0.1", "name": "Dev Experience Test", "x": 0, "y": 0 },
                  "2": { "active": false }
                }
              }
            }
            """;
        (TwitchHelixTransport transport, _, _, _) = Build([
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, body),
        ]);

        Result<TwitchActiveExtensions> result =
            await transport.GetSingleAsync<TwitchActiveExtensions>(
                new TwitchHelixRequest(
                    HttpMethod.Get,
                    "users/extensions",
                    TwitchHelixAuth.App,
                    Query: [new("user_id", "141981764")]
                )
            );

        result.IsSuccess.Should().BeTrue();
        TwitchActiveExtensions active = result.Value;
        active.Panel.Should().HaveCount(2);
        active.Panel["1"].Active.Should().BeTrue();
        active.Panel["1"].Id.Should().Be("rh6jq1q334hqc2rr1qlzqbvwlfl3x0");
        active.Panel["1"].Version.Should().Be("1.1.0");
        active.Panel["1"].Name.Should().Be("TopClip");
        active.Panel["2"].Active.Should().BeFalse();
        active.Panel["2"].Id.Should().BeNull();
        active.Overlay["1"].Name.Should().Be("Streamlabs");
        active.Component["1"].X.Should().Be(0);
        active.Component["1"].Y.Should().Be(0);
        active.Component["2"].Active.Should().BeFalse();
    }

    [Fact]
    public async Task SendWithResultAsync_SerializesDataWrappedExtensionBody_SnakeCase_OmittingNulls()
    {
        // Update User Extensions wraps the slot maps in a top-level "data" object; the wire body must be
        // snake_case, keep the numeric slot keys verbatim, and omit every unset field/map.
        (TwitchHelixTransport transport, RecordingHelixHandler wire, _, _) = Build([
            () =>
                RecordingHelixHandler.Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "data": {
                        "panel": { "1": { "active": true, "id": "rh6jq1q334hqc2rr1qlzqbvwlfl3x0", "version": "1.1.0", "name": "TopClip" } },
                        "overlay": {},
                        "component": { "1": { "active": true, "id": "lqnf3zxk0rv0g7gq92mtmnirjz2cjj", "version": "0.0.1", "name": "Dev Experience Test", "x": 0, "y": 0 } }
                      }
                    }
                    """
                ),
        ]);
        UpdateUserExtensionsRequest request = new(
            new UpdateUserExtensionsData(
                Panel: new Dictionary<string, TwitchExtensionSlotUpdate>
                {
                    ["1"] = new(true, "rh6jq1q334hqc2rr1qlzqbvwlfl3x0", "1.1.0"),
                    ["2"] = new(false),
                },
                Component: new Dictionary<string, TwitchExtensionSlotUpdate>
                {
                    ["1"] = new(true, "lqnf3zxk0rv0g7gq92mtmnirjz2cjj", "0.0.1", X: 0, Y: 0),
                }
            )
        );

        Result<TwitchActiveExtensions> result =
            await transport.SendWithResultAsync<TwitchActiveExtensions>(
                new TwitchHelixRequest(
                    HttpMethod.Put,
                    "users/extensions",
                    TwitchHelixAuth.User,
                    Tenant,
                    Body: request
                )
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.Panel["1"].Name.Should().Be("TopClip");
        result.Value.Component["1"].X.Should().Be(0);

        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.Body.Should().NotBeNull();
        using JsonDocument doc = JsonDocument.Parse(sent.Body!);
        JsonElement data = doc.RootElement.GetProperty("data");
        data.GetProperty("panel")
            .GetProperty("1")
            .GetProperty("active")
            .GetBoolean()
            .Should()
            .BeTrue();
        data.GetProperty("panel")
            .GetProperty("1")
            .GetProperty("id")
            .GetString()
            .Should()
            .Be("rh6jq1q334hqc2rr1qlzqbvwlfl3x0");
        data.GetProperty("panel")
            .GetProperty("1")
            .GetProperty("version")
            .GetString()
            .Should()
            .Be("1.1.0");
        // A deactivation slot carries only "active": false — no id/version keys.
        data.GetProperty("panel")
            .GetProperty("2")
            .GetProperty("active")
            .GetBoolean()
            .Should()
            .BeFalse();
        data.GetProperty("panel")
            .GetProperty("2")
            .TryGetProperty("id", out JsonElement _)
            .Should()
            .BeFalse();
        // Component slots serialize their placement coordinate.
        data.GetProperty("component").GetProperty("1").GetProperty("x").GetInt32().Should().Be(0);
        data.GetProperty("component").GetProperty("1").GetProperty("y").GetInt32().Should().Be(0);
        // The unset overlay map is omitted entirely (WhenWritingNull).
        data.TryGetProperty("overlay", out JsonElement _).Should().BeFalse();
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
    public async Task SendAsync_BotToken401_RefreshesOnceAndRetries()
    {
        // The bot/platform token (BroadcasterId == null) is a real user token that carries a refresh token, so
        // a 401 must trigger exactly one refresh-and-retry — the same recovery a broadcaster call gets. This is
        // the fix for the silent dead-bot-token storm: the old gate skipped refresh whenever there was no
        // broadcaster, so the bot account could never recover and never surfaced needs_reauth.
        (
            TwitchHelixTransport transport,
            RecordingHelixHandler wire,
            FakeTwitchTokenResolver resolver,
            _
        ) = Build([
            () => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, """{ "data": [] }"""),
        ]);

        Result result = await transport.SendAsync(
            new TwitchHelixRequest(HttpMethod.Get, "users", TwitchHelixAuth.App)
        );

        result.IsSuccess.Should().BeTrue();
        resolver.RefreshCallCount.Should().Be(1); // exactly one refresh, then retry
        wire.CallCount.Should().Be(2); // original + one retry
        wire.Requests[0].AuthorizationParameter.Should().Be("initial-token");
        wire.Requests[1].AuthorizationParameter.Should().Be("refreshed-token");
    }

    [Fact]
    public async Task SendAsync_BotTokenPersistently401_FailsAndEmitsReauthEvent()
    {
        // Bot token still 401 after the single refresh-and-retry (a dead refresh token): fail closed and emit
        // the re-auth signal, exactly like the broadcaster path — no BroadcasterId required.
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
            new TwitchHelixRequest(HttpMethod.Get, "users", TwitchHelixAuth.App)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Unauthorized);
        resolver.RefreshCallCount.Should().Be(1);
        wire.CallCount.Should().Be(2);
        bus.EventsOf<TwitchHelixReauthRequiredEvent>().Should().ContainSingle();
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
