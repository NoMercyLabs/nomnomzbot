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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Infrastructure.AutomationApi.Events;
using NomNomzBot.Infrastructure.AutomationApi.Stream;
using NomNomzBot.Infrastructure.EventStore;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

public sealed class AutomationHistoryServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f401");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f402");
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);

    [Fact]
    public void The_real_registration_binds_the_history_service_once_per_request()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=history_di_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        List<ServiceDescriptor> bindings = services
            .Where(descriptor => descriptor.ServiceType == typeof(IAutomationHistoryService))
            .ToList();

        bindings.Should().ContainSingle();
        bindings[0].ImplementationType.Should().Be(typeof(AutomationHistoryService));
        bindings[0].Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Returns_live_frames_in_occurrence_order_with_chat_text_and_event_projection()
    {
        await using AutomationTestDbContext db = AutomationTestDbContext.New();
        AddEvent(db, 1, Channel, From.AddMinutes(2), Chat("chat-1"));
        AddChat(db, "chat-1", Channel, false);
        AddEvent(db, 2, Channel, From.AddMinutes(1), Tip());
        await db.SaveChangesAsync();

        Result<AutomationHistoryPage> result = await Service(db)
            .ReadAsync(Principal(Channel), From, To, null);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Items.Select(item => item.Type)
            .Should()
            .Equal("Supporter.Received", "Twitch.ChatMessage");
        JsonElement chat = JsonSerializer.SerializeToElement(result.Value.Items[1]);
        chat.GetProperty("op").GetString().Should().Be("event");
        chat.GetProperty("broadcasterId").GetString().Should().Be(Channel.ToString("D"));
        chat.GetProperty("data").GetProperty("message").GetString().Should().Be("hello");
        JsonElement tip = JsonSerializer.SerializeToElement(result.Value.Items[0]);
        tip.GetProperty("data").TryGetProperty("supporterUserId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Cursor_paginates_equal_timestamps_without_duplicates_and_rejects_cross_channel_reuse()
    {
        await using AutomationTestDbContext db = AutomationTestDbContext.New();
        AddEvent(db, 1, Channel, From.AddMinutes(1), Tip());
        AddEvent(db, 2, Channel, From.AddMinutes(1), Tip());
        AddEvent(db, 3, Channel, From.AddMinutes(1), Tip());
        AddEvent(db, 4, OtherChannel, From.AddMinutes(1), Tip());
        await db.SaveChangesAsync();

        AutomationHistoryService service = Service(db);
        Result<AutomationHistoryPage> first = await service.ReadAsync(
            Principal(Channel),
            From,
            To,
            null,
            2
        );
        first.Value.Items.Should().HaveCount(2);
        first.Value.NextCursor.Should().NotBeNullOrEmpty();
        Result<AutomationHistoryPage> second = await service.ReadAsync(
            Principal(Channel),
            From,
            To,
            first.Value.NextCursor,
            2
        );
        second.Value.Items.Should().ContainSingle();
        second.Value.NextCursor.Should().BeNull();
        Result<AutomationHistoryPage> foreign = await service.ReadAsync(
            Principal(OtherChannel),
            From,
            To,
            first.Value.NextCursor,
            2
        );
        foreign.IsFailure.Should().BeTrue();
        foreign.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Refuses_more_than_24_hours_and_non_utc_instants()
    {
        await using AutomationTestDbContext db = AutomationTestDbContext.New();
        AutomationHistoryService service = Service(db);

        Result<AutomationHistoryPage> longRange = await service.ReadAsync(
            Principal(Channel),
            From,
            To.AddTicks(1),
            null,
            0
        );
        Result<AutomationHistoryPage> local = await service.ReadAsync(
            Principal(Channel),
            From.ToOffset(TimeSpan.FromHours(2)),
            To,
            null,
            0
        );

        longRange.ErrorCode.Should().Be("VALIDATION_FAILED");
        local.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Refuses_a_token_without_events_scope_before_reading()
    {
        await using AutomationTestDbContext db = AutomationTestDbContext.New();
        IRateLimiterPartitionStore limiter = Substitute.For<IRateLimiterPartitionStore>();
        Result<AutomationHistoryPage> result = await Service(db, limiter)
            .ReadAsync(Principal(Channel, ["read"]), From, To, null);
        result.ErrorCode.Should().Be("FORBIDDEN");
        await limiter.DidNotReceiveWithAnyArgs().AcquireAsync(default!, default, default);
    }

    [Fact]
    public async Task Excludes_other_channel_and_deleted_chat_even_when_journal_retains_them()
    {
        await using AutomationTestDbContext db = AutomationTestDbContext.New();
        AddEvent(db, 1, Channel, From.AddMinutes(1), Chat("deleted"));
        AddChat(db, "deleted", Channel, true);
        AddEvent(db, 2, Channel, From.AddMinutes(2), Chat("visible"));
        AddChat(db, "visible", Channel, false);
        AddEvent(db, 3, OtherChannel, From.AddMinutes(3), Tip());
        await db.SaveChangesAsync();

        Result<AutomationHistoryPage> result = await Service(db)
            .ReadAsync(Principal(Channel), From, To, null);
        result.Value.Items.Should().ContainSingle();
        JsonElement frame = JsonSerializer.SerializeToElement(result.Value.Items[0]);
        frame.GetProperty("data").GetProperty("messageId").GetString().Should().Be("visible");
    }

    [Fact]
    public async Task Defaults_to_500_and_caps_requested_page_size_at_1000()
    {
        await using AutomationTestDbContext db = AutomationTestDbContext.New();
        for (long position = 1; position <= 1001; position++)
            AddEvent(db, position, Channel, From.AddSeconds(position), Tip());
        await db.SaveChangesAsync();

        AutomationHistoryService service = Service(db);
        Result<AutomationHistoryPage> defaultPage = await service.ReadAsync(
            Principal(Channel),
            From,
            To,
            null,
            0
        );
        Result<AutomationHistoryPage> cappedPage = await service.ReadAsync(
            Principal(Channel),
            From,
            To,
            null,
            5000
        );

        defaultPage.Value.Items.Should().HaveCount(500);
        defaultPage.Value.NextCursor.Should().NotBeNull();
        cappedPage.Value.Items.Should().HaveCount(1000);
        cappedPage.Value.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task Rate_limit_denial_returns_retry_after_without_reading_or_unprotecting_events()
    {
        await using AutomationTestDbContext db = AutomationTestDbContext.New();
        AddEvent(db, 1, Channel, From.AddMinutes(1), Tip());
        await db.SaveChangesAsync();
        IRateLimiterPartitionStore limiter = Substitute.For<IRateLimiterPartitionStore>();
        IEventPayloadProtector protector = Substitute.For<IEventPayloadProtector>();
        limiter
            .AcquireAsync(
                Arg.Any<string>(),
                60,
                TimeSpan.FromMinutes(1),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new RateLimitLease(false, 0, TimeSpan.FromSeconds(17))));

        Result<AutomationHistoryPage> result = await Service(db, limiter, protector)
            .ReadAsync(Principal(Channel), From, To, null);

        result.ErrorCode.Should().Be("RATE_LIMITED");
        result.ErrorDetail.Should().Be("17");
        await limiter
            .Received(1)
            .AcquireAsync(
                Arg.Is<string>(key => key.EndsWith(":history")),
                60,
                TimeSpan.FromMinutes(1),
                Arg.Any<CancellationToken>()
            );
        await protector.DidNotReceiveWithAnyArgs().UnprotectAsync(default!);
    }

    private static AutomationHistoryService Service(
        AutomationTestDbContext db,
        IRateLimiterPartitionStore? limiter = null,
        IEventPayloadProtector? protector = null
    )
    {
        IEventPayloadProtector activeProtector =
            protector ?? Substitute.For<IEventPayloadProtector>();
        activeProtector
            .UnprotectAsync(Arg.Any<EventRecord>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Result.Success(((EventRecord)call[0]!).PayloadJson)));
        IRateLimiterPartitionStore activeLimiter =
            limiter ?? Substitute.For<IRateLimiterPartitionStore>();
        if (limiter is null)
            activeLimiter
                .AcquireAsync(
                    Arg.Any<string>(),
                    Arg.Any<int>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Task.FromResult(new RateLimitLease(true, 99, TimeSpan.Zero)));
        DomainEventTypeRegistry types = new();
        JournaledDomainEventReader reader = new(
            activeProtector,
            new EventUpcasterRegistry([]),
            types
        );
        return new(
            db,
            new AutomationEventRegistry([
                new ChatMessageEventDescriptor(),
                new SupporterReceivedEventDescriptor(),
            ]),
            reader,
            activeLimiter,
            types
        );
    }

    private static AutomationPrincipal Principal(
        Guid channel,
        IReadOnlyList<string>? scopes = null
    ) => new(channel, Guid.NewGuid(), "test", scopes ?? ["events"], null);

    private static ChatMessageReceivedEvent Chat(string id) =>
        new()
        {
            BroadcasterId = Channel,
            MessageId = id,
            TwitchBroadcasterId = "123",
            UserId = "456",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "hello",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static SupporterEventReceived Tip() =>
        new()
        {
            BroadcasterId = Channel,
            SourceKey = "streamlabs",
            Kind = "tip",
            SupporterDisplayName = "GenerousViewer",
            SupporterUserId = Guid.NewGuid(),
            AmountMinor = 500,
            Currency = "EUR",
            IsRecurring = false,
            SupporterEventId = Guid.NewGuid(),
        };

    private static void AddEvent(
        AutomationTestDbContext db,
        long position,
        Guid channel,
        DateTimeOffset when,
        object value
    ) =>
        db.EventJournals.Add(
            new EventJournal
            {
                BroadcasterId = channel,
                StreamPosition = position,
                EventId = Guid.NewGuid(),
                EventType = value.GetType().Name,
                EventVersion = 1,
                Source = "domain",
                Payload = Newtonsoft.Json.JsonConvert.SerializeObject(value),
                Metadata = "{}",
                OccurredAt = when.UtcDateTime,
                RecordedAt = when.UtcDateTime,
            }
        );

    private static void AddChat(
        AutomationTestDbContext db,
        string id,
        Guid channel,
        bool deleted
    ) =>
        db.ChatMessages.Add(
            new ChatMessage
            {
                Id = id,
                BroadcasterId = channel,
                UserId = "456",
                Username = "viewer",
                DisplayName = "Viewer",
                UserType = "viewer",
                Message = "hello",
                DeletedAt = deleted ? From.AddHours(2).UtcDateTime : null,
            }
        );
}
