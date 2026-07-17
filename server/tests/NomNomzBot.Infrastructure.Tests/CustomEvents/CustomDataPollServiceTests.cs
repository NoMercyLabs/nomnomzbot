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
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the poll ingress fetcher (custom-events.md §6). The SSRF egress gate is the load-bearing assertion:
/// a due source is fetched and ingested ONLY when its endpoint host is on an enabled H.7 allowlist row for the
/// channel; a non-allowlisted host is skipped with no fetch and no ingest; and a source inside its interval is
/// left alone. Ingest is substituted (the real path is proven elsewhere) so these tests isolate the fetch+gate.
/// </summary>
public sealed class CustomDataPollServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Records every outbound call and replays a canned response, so a test can assert the fetch happened.</summary>
    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private static (
        CustomDataPollService Sut,
        AuthDbContext Db,
        ICustomDataIngestService Ingest,
        RecordingHandler Handler,
        FakeTimeProvider Clock
    ) Build(HttpStatusCode status = HttpStatusCode.OK, string body = "{\"bpm\":128}")
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("bearer-secret");

        ICustomDataIngestService ingest = Substitute.For<ICustomDataIngestService>();
        ingest
            .IngestAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        RecordingHandler handler = new(status, body);
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        FakeTimeProvider clock = new(Now);
        CustomDataPollService sut = new(
            db,
            protector,
            ingest,
            factory,
            new CustomDataPollAttemptTracker(),
            clock,
            NullLogger<CustomDataPollService>.Instance
        );
        return (sut, db, ingest, handler, clock);
    }

    private static async Task SeedSourceAsync(
        AuthDbContext db,
        string endpointUrl = "https://api.example.com/heart",
        DateTime? lastReceivedAt = null,
        int pollIntervalSeconds = 60
    )
    {
        db.CustomDataSources.Add(
            new CustomDataSource
            {
                BroadcasterId = Channel,
                Name = "heartrate",
                DisplayName = "Heart Rate",
                SourceKind = "poll",
                EndpointUrl = endpointUrl,
                FieldMapJson = "{\"bpm\":\"$.bpm\"}",
                PollIntervalSeconds = pollIntervalSeconds,
                IsEnabled = true,
                LastReceivedAt = lastReceivedAt,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedAllowlistAsync(AuthDbContext db, string fqdn = "api.example.com")
    {
        db.HttpEgressAllowlists.Add(
            new HttpEgressAllowlist
            {
                BroadcasterId = Channel,
                Fqdn = fqdn,
                IsEnabled = true,
                MaxResponseBytes = 65536,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_due_allowlisted_source_is_fetched_and_its_body_is_ingested()
    {
        (
            CustomDataPollService sut,
            AuthDbContext db,
            ICustomDataIngestService ingest,
            RecordingHandler handler,
            FakeTimeProvider _
        ) = Build(body: "{\"bpm\":128}");
        await SeedSourceAsync(db); // LastReceivedAt null → due
        await SeedAllowlistAsync(db);

        await sut.PollDueSourcesAsync();

        handler.CallCount.Should().Be(1);
        handler.LastRequestUri.Should().Be(new Uri("https://api.example.com/heart"));
        await ingest
            .Received(1)
            .IngestAsync(Channel, "heartrate", "{\"bpm\":128}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_due_source_whose_host_is_not_allowlisted_is_skipped_with_no_fetch()
    {
        (
            CustomDataPollService sut,
            AuthDbContext db,
            ICustomDataIngestService ingest,
            RecordingHandler handler,
            FakeTimeProvider _
        ) = Build();
        await SeedSourceAsync(db); // due, but no allowlist row for api.example.com
        // Deliberately allowlist a DIFFERENT host to prove the match is host-specific.
        await SeedAllowlistAsync(db, fqdn: "other.example.com");

        await sut.PollDueSourcesAsync();

        handler.CallCount.Should().Be(0); // SSRF gate blocked before any HTTP call
        await ingest
            .DidNotReceive()
            .IngestAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_source_still_inside_its_interval_is_not_polled()
    {
        (
            CustomDataPollService sut,
            AuthDbContext db,
            ICustomDataIngestService ingest,
            RecordingHandler handler,
            FakeTimeProvider _
        ) = Build();
        // Received 5 s ago with a 60 s interval → not due yet.
        await SeedSourceAsync(
            db,
            lastReceivedAt: Now.UtcDateTime.AddSeconds(-5),
            pollIntervalSeconds: 60
        );
        await SeedAllowlistAsync(db);

        await sut.PollDueSourcesAsync();

        handler.CallCount.Should().Be(0);
        await ingest
            .DidNotReceive()
            .IngestAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_persistently_failing_source_is_not_reattempted_before_its_interval_elapses()
    {
        // The endpoint always 500s: LastReceivedAt (stamped only on success) never advances, so without the
        // attempt tracker the source would be re-fetched every scan tick. The in-memory last-attempt stamp must
        // hold it back until PollIntervalSeconds elapses.
        (
            CustomDataPollService sut,
            AuthDbContext db,
            ICustomDataIngestService ingest,
            RecordingHandler handler,
            FakeTimeProvider clock
        ) = Build(status: HttpStatusCode.InternalServerError);
        await SeedSourceAsync(db, pollIntervalSeconds: 60); // LastReceivedAt null → due on the first pass
        await SeedAllowlistAsync(db);

        // First scan pass: the source is due, so it is fetched once (and fails).
        await sut.PollDueSourcesAsync();
        handler.CallCount.Should().Be(1);

        // Second scan pass 5 s later (< 60 s interval): still inside the interval since the last ATTEMPT,
        // so it must NOT be re-fetched despite LastReceivedAt never having advanced.
        clock.Advance(TimeSpan.FromSeconds(5));
        await sut.PollDueSourcesAsync();

        handler.CallCount.Should().Be(1); // exactly one attempt, not two
        await ingest
            .DidNotReceive()
            .IngestAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            ); // a 500 never reaches ingest
    }

    [Fact]
    public async Task A_source_is_repolled_once_its_interval_elapses_since_the_last_attempt()
    {
        (
            CustomDataPollService sut,
            AuthDbContext db,
            ICustomDataIngestService ingest,
            RecordingHandler handler,
            FakeTimeProvider clock
        ) = Build(body: "{\"bpm\":128}");
        await SeedSourceAsync(db, pollIntervalSeconds: 60); // due on the first pass
        await SeedAllowlistAsync(db);

        // First pass fetches and ingests once.
        await sut.PollDueSourcesAsync();
        handler.CallCount.Should().Be(1);

        // Still inside the interval 30 s later → no second fetch.
        clock.Advance(TimeSpan.FromSeconds(30));
        await sut.PollDueSourcesAsync();
        handler.CallCount.Should().Be(1);

        // Past the 60 s interval since the last attempt → due again, fetched a second time.
        clock.Advance(TimeSpan.FromSeconds(31));
        await sut.PollDueSourcesAsync();

        handler.CallCount.Should().Be(2);
        await ingest
            .Received(2)
            .IngestAsync(Channel, "heartrate", "{\"bpm\":128}", Arg.Any<CancellationToken>());
    }
}
