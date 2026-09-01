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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the S100a reliability tracking on <see cref="CustomDataPollService"/> (custom-events.md §6, mirroring
/// the outbound-webhook fix S099a): a failed fetch persists a real error, increments the consecutive-failure
/// count, and schedules a future, capped <c>NextRetryAt</c>; a source that crosses the failure threshold is
/// auto-disabled and the poller stops attempting it on subsequent scan passes.
/// </summary>
public sealed class CustomDataPollReliabilityTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private static (
        CustomDataPollService Sut,
        AuthDbContext Db,
        RecordingHandler Handler,
        FakeTimeProvider Clock
    ) Build(HttpStatusCode status)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((string?)null);

        ICustomDataIngestService ingest = Substitute.For<ICustomDataIngestService>();
        ingest
            .IngestAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        RecordingHandler handler = new(status);
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new(handler));

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
        return (sut, db, handler, clock);
    }

    private static async Task<Guid> SeedSourceAsync(
        AuthDbContext db,
        int consecutiveFailureCount = 0,
        DateTime? nextRetryAt = null
    )
    {
        CustomDataSource source = new()
        {
            BroadcasterId = Channel,
            Name = "heartrate",
            DisplayName = "Heart Rate",
            SourceKind = "poll",
            EndpointUrl = "https://api.example.com/heart",
            FieldMapJson = "{}",
            PollIntervalSeconds = 5,
            IsEnabled = true,
            ConsecutiveFailureCount = consecutiveFailureCount,
            NextRetryAt = nextRetryAt,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        db.CustomDataSources.Add(source);
        db.HttpEgressAllowlists.Add(
            new()
            {
                BroadcasterId = Channel,
                Fqdn = "api.example.com",
                IsEnabled = true,
                MaxResponseBytes = 65536,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
        return source.Id;
    }

    [Fact]
    public async Task A_failed_fetch_records_the_error_and_schedules_a_capped_future_retry()
    {
        (CustomDataPollService sut, AuthDbContext db, RecordingHandler _, FakeTimeProvider _) =
            Build(HttpStatusCode.InternalServerError);
        Guid sourceId = await SeedSourceAsync(db);

        await sut.PollDueSourcesAsync();

        CustomDataSource source = await db.CustomDataSources.SingleAsync(s => s.Id == sourceId);
        source.ConsecutiveFailureCount.Should().Be(1);
        source.LastError.Should().NotBeNullOrWhiteSpace();
        source.LastAttemptAt.Should().Be(Now.UtcDateTime);
        source.NextRetryAt.Should().NotBeNull();
        source.NextRetryAt!.Value.Should().BeAfter(Now.UtcDateTime);
        // Capped: even at a high failure count the delay never exceeds the 1-hour ceiling (S099a parity).
        (source.NextRetryAt!.Value - Now.UtcDateTime)
            .Should()
            .BeLessThanOrEqualTo(TimeSpan.FromHours(1));
        source.IsEnabled.Should().BeTrue(); // below the auto-disable threshold
    }

    [Fact]
    public async Task A_source_crossing_the_failure_threshold_is_disabled_and_the_poller_stops_attempting_it()
    {
        (
            CustomDataPollService sut,
            AuthDbContext db,
            RecordingHandler handler,
            FakeTimeProvider clock
        ) = Build(HttpStatusCode.InternalServerError);
        // One failure short of the threshold; NextRetryAt already elapsed so this pass is due immediately.
        Guid sourceId = await SeedSourceAsync(
            db,
            consecutiveFailureCount: 19,
            nextRetryAt: Now.UtcDateTime.AddSeconds(-1)
        );

        await sut.PollDueSourcesAsync();

        CustomDataSource source = await db.CustomDataSources.SingleAsync(s => s.Id == sourceId);
        source.ConsecutiveFailureCount.Should().Be(20);
        source.IsEnabled.Should().BeFalse();
        source.DisabledAt.Should().NotBeNull();
        source.DisabledReason.Should().NotBeNullOrWhiteSpace();
        handler.CallCount.Should().Be(1);

        // Next scan pass, well past any interval/backoff: the disabled source must not be fetched again — the
        // poll query only selects IsEnabled sources, so auto-disable is also how the poller stops attempting it.
        clock.Advance(TimeSpan.FromHours(2));
        await sut.PollDueSourcesAsync();

        handler.CallCount.Should().Be(1); // no second attempt
    }
}
