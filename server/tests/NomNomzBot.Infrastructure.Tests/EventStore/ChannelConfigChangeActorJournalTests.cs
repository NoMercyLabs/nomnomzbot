// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.AutomationApi;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// On Stoney's channel nine commands were switched off by nine API calls in 0.45 s and the journal recorded
/// nobody: 0 of 216 config-change rows carried an actor. These tests drive a real command update through the
/// real bus decorator into the real journal and read the row back, so they fail when the acting principal is
/// not stamped on it.
/// </summary>
public sealed class ChannelConfigChangeActorJournalTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly Guid Person = Guid.Parse("0192a000-0000-7000-8000-000000000f02");
    private static readonly Guid SanctionedPerson = Guid.Parse(
        "0192a000-0000-7000-8000-000000000f03"
    );
    private static readonly FakeTimeProvider Clock = new(
        new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)
    );

    private sealed class UnmarkedFact : DomainEventBase;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteTestDatabase _database = SqliteTestDatabase.Open();
        private readonly ServiceProvider _provider;

        public Harness()
        {
            Sanctions = new();
            Http = new();

            ServiceCollection services = new();
            services.AddScoped<IApplicationDbContext>(_ => _database.NewContext());
            services.AddScoped<IUnitOfWork>(sp => new EventStoreTestUnitOfWork(
                (EventStoreTestDbContext)sp.GetRequiredService<IApplicationDbContext>()
            ));
            services.AddScoped<ITenantSequenceAllocator>(sp => new TenantSequenceAllocator(
                sp.GetRequiredService<IApplicationDbContext>()
            ));
            services.AddSingleton<IEventUpcasterRegistry>(new EventUpcasterRegistry([]));
            services.AddSingleton<TimeProvider>(Clock);
            services.AddScoped<IEventJournal>(sp => new EventJournalService(
                sp.GetRequiredService<IApplicationDbContext>(),
                sp.GetRequiredService<ITenantSequenceAllocator>(),
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<TimeProvider>(),
                new PassthroughEventPayloadProtector(),
                Substitute.For<ICurrentUserService>()
            ));
            services.AddScoped<IEventStoreSubscriber>(sp => new EventStoreSubscriber(
                sp.GetRequiredService<IEventJournal>(),
                sp.GetRequiredService<IEventUpcasterRegistry>()
            ));
            services.AddSingleton<IJournalActorAccessor>(new JournalActorAccessor(Http, Sanctions));
            _provider = services.BuildServiceProvider();

            Bus = new JournalingEventBusDecorator(
                new RecordingEventBus(),
                _provider,
                NullLogger<JournalingEventBusDecorator>.Instance
            );
        }

        public HttpContextAccessor Http { get; }

        public OutboundSanctionAccessor Sanctions { get; }

        public IEventBus Bus { get; }

        public void SignInAsPerson(Guid userId) =>
            Http.HttpContext = new DefaultHttpContext
            {
                User = new(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                        "Test"
                    )
                ),
            };

        public void SignInWithApiKey(Guid tokenId) =>
            Http.HttpContext = new DefaultHttpContext
            {
                User = new(
                    new ClaimsIdentity(
                        [new Claim(AutomationClaimTypes.TokenId, tokenId.ToString())],
                        "AutomationToken"
                    )
                ),
            };

        public async Task<EventRecord> LastRowAsync(string eventType)
        {
            await using EventStoreTestDbContext verify = _database.NewContext();
            EventJournalService journal = new(
                verify,
                new TenantSequenceAllocator(verify),
                new EventStoreTestUnitOfWork(verify),
                Clock,
                new PassthroughEventPayloadProtector(),
                Substitute.For<ICurrentUserService>()
            );
            Result<IReadOnlyList<EventRecord>> rows = await journal.ReadStreamAsync(
                Channel,
                0,
                100
            );
            return rows.Value.Last(r => r.EventType == eventType);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            _database.Dispose();
        }
    }

    private static async Task<CommandService> NewCommandServiceAsync(IEventBus bus)
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "actor-journal-channel",
                NameNormalized = "actor-journal-channel",
            }
        );
        await db.SaveChangesAsync();

        CommandService service = new(
            db,
            Substitute.For<IChannelRegistry>(),
            bus,
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([])
        );
        Result<CommandDto> created = await service.CreateAsync(
            Channel.ToString(),
            new CreateCommandDto { Name = "hug", TemplateResponse = "hug!" }
        );
        created.IsSuccess.Should().BeTrue();
        return service;
    }

    private static Task<Result<CommandDto>> SwitchOffAsync(CommandService service) =>
        service.UpdateAsync(Channel.ToString(), "hug", new UpdateCommandDto { IsEnabled = false });

    [Fact]
    public async Task A_command_update_made_in_a_person_session_journals_that_user_as_the_actor()
    {
        await using Harness harness = new();
        CommandService service = await NewCommandServiceAsync(harness.Bus);
        harness.SignInAsPerson(Person);

        (await SwitchOffAsync(service)).IsSuccess.Should().BeTrue();

        EventRecord row = await harness.LastRowAsync(nameof(ChannelConfigChangedEvent));
        row.ActorUserId.Should().Be(Person);
        JournalActorMetadata.ReadKind(row.MetadataJson).Should().Be(JournalActorKind.Person);
        row.PayloadJson.Should().Contain("\"Action\":\"updated\"");
    }

    [Fact]
    public async Task A_command_update_made_with_an_api_key_journals_the_key_id_and_no_user()
    {
        await using Harness harness = new();
        CommandService service = await NewCommandServiceAsync(harness.Bus);
        Guid tokenId = Guid.Parse("0192a000-0000-7000-8000-000000000f09");
        harness.SignInWithApiKey(tokenId);

        (await SwitchOffAsync(service)).IsSuccess.Should().BeTrue();

        EventRecord row = await harness.LastRowAsync(nameof(ChannelConfigChangedEvent));
        row.ActorUserId.Should().BeNull("an API key is not a user");
        JournalActorMetadata.ReadKind(row.MetadataJson).Should().Be(JournalActorKind.ApiKey);
        JournalActorMetadata.ReadTokenId(row.MetadataJson).Should().Be(tokenId.ToString());
    }

    [Fact]
    public async Task A_change_with_no_request_and_no_sanction_is_journaled_as_the_system()
    {
        await using Harness harness = new();
        CommandService service = await NewCommandServiceAsync(harness.Bus);

        (await SwitchOffAsync(service)).IsSuccess.Should().BeTrue();

        EventRecord row = await harness.LastRowAsync(nameof(ChannelConfigChangedEvent));
        row.ActorUserId.Should().BeNull();
        JournalActorMetadata.ReadKind(row.MetadataJson).Should().Be(JournalActorKind.System);
    }

    [Fact]
    public async Task A_background_change_under_a_user_action_sanction_journals_that_person()
    {
        await using Harness harness = new();
        CommandService service = await NewCommandServiceAsync(harness.Bus);

        using (
            harness.Sanctions.Begin(OutboundSanction.UserAction("commands:write", SanctionedPerson))
        )
        {
            (await SwitchOffAsync(service)).IsSuccess.Should().BeTrue();
        }

        EventRecord row = await harness.LastRowAsync(nameof(ChannelConfigChangedEvent));
        row.ActorUserId.Should().Be(SanctionedPerson);
        JournalActorMetadata.ReadKind(row.MetadataJson).Should().Be(JournalActorKind.Person);
    }

    [Fact]
    public async Task A_change_under_a_channel_configuration_sanction_is_journaled_as_the_system()
    {
        await using Harness harness = new();
        CommandService service = await NewCommandServiceAsync(harness.Bus);

        using (harness.Sanctions.Begin(OutboundSanction.ChannelConfiguration("timer:ad-break")))
        {
            (await SwitchOffAsync(service)).IsSuccess.Should().BeTrue();
        }

        EventRecord row = await harness.LastRowAsync(nameof(ChannelConfigChangedEvent));
        row.ActorUserId.Should().BeNull();
        JournalActorMetadata.ReadKind(row.MetadataJson).Should().Be(JournalActorKind.System);
    }

    [Fact]
    public async Task A_fact_that_is_not_a_change_gets_no_actor_stamped()
    {
        await using Harness harness = new();
        harness.SignInAsPerson(Person);

        await harness.Bus.PublishAsync(new UnmarkedFact { BroadcasterId = Channel });

        EventRecord row = await harness.LastRowAsync(nameof(UnmarkedFact));
        row.ActorUserId.Should().BeNull("nobody on this side did a platform fact");
        JournalActorMetadata.ReadKind(row.MetadataJson).Should().BeNull();
    }

    [Fact]
    public async Task The_journal_read_dto_carries_the_actor_kind_and_key_id()
    {
        await using Harness harness = new();
        CommandService service = await NewCommandServiceAsync(harness.Bus);
        Guid tokenId = Guid.Parse("0192a000-0000-7000-8000-000000000f09");
        harness.SignInWithApiKey(tokenId);
        await SwitchOffAsync(service);

        EventRecord row = await harness.LastRowAsync(nameof(ChannelConfigChangedEvent));
        EventJournalEntryDto dto = EventJournalEntryDto.From(row);

        dto.ActorKind.Should().Be(JournalActorKind.ApiKey);
        dto.ActorTokenId.Should().Be(tokenId.ToString());
        dto.ActorUserId.Should().BeNull();
    }
}
