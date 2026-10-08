// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The wiring between the chat handler, the enforcement arm and the campaign record: an account is
/// listed as actioned only when the bot's own timeout succeeded at the platform. Everything else is the
/// real chain over an in-memory database; only the platform edges and the per-message verdict are fakes.
/// </summary>
public class SpamDefenseHandlerCampaignTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000d1");
    private static readonly Guid Owner = Guid.Parse("0199c000-0000-7000-8000-0000000000d2");
    private static readonly DateTimeOffset T0 = new(2026, 9, 3, 23, 0, 0, TimeSpan.Zero);
    private const string Skeleton = "bestviewersonbigfollowscom";

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(T0);
    private readonly IModerationService _moderation = Substitute.For<IModerationService>();
    private readonly ITwitchModerationApi _twitch = Substitute.For<ITwitchModerationApi>();
    private readonly ISpamDefenseService _spamDefense = Substitute.For<ISpamDefenseService>();
    private readonly ServiceProvider _services;
    private readonly SpamDefenseHandler _handler;

    private SpamDecision _decision = new(SpamOutcome.None, SpamOutcome.None, false, "ordinary");
    private bool _timeoutSucceeds = true;

    public SpamDefenseHandlerCampaignTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using (AppDbContext db = NewDbContext())
        {
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
            db.Channels.Add(
                new Channel
                {
                    Id = Channel,
                    OwnerUserId = Owner,
                    Provider = AuthEnums.Platform.Twitch,
                    ExternalChannelId = "chan-ext",
                    Name = "chan",
                    NameNormalized = "chan",
                }
            );
            db.SaveChanges();
        }

        _twitch
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Result.Success()));

        _moderation
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromResult(
                    _timeoutSucceeds
                        ? Result.Success(new ModerationActionResult(true, null))
                        : Result.Failure<ModerationActionResult>("twitch_unavailable")
                )
            );

        _moderation
            .GetAutomodConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                Task.FromResult(
                    Result.Success(
                        new AutomodConfigDto(
                            new AutomodLinkFilterDto(false, []),
                            new AutomodCapsFilterDto(false, 0),
                            new AutomodBannedPhrasesDto(false, []),
                            new AutomodEmoteSpamDto(false, 0),
                            HeatTimeoutSeconds: 600
                        )
                    )
                )
            );

        _moderation
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Result.Success(new ModerationActionResult(true, null))));

        _spamDefense
            .EvaluateAsync(Arg.Any<SpamEvaluationRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                Task.FromResult<SpamEvaluationResult?>(
                    new SpamEvaluationResult(
                        _decision,
                        SpamConfidence.Zero,
                        SpamTrustTier.Untrusted,
                        [],
                        null,
                        Skeleton,
                        new SpamDefenseSettings()
                    )
                )
            );

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_moderation);
        services.AddSingleton(_twitch);
        services.AddSingleton(_spamDefense);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<SpamCampaignReversalExecutor>();
        services.AddScoped<SpamEnforcementExecutor>();
        services.AddScoped<SpamCorrelationService>();
        _services = services.BuildServiceProvider();

        _handler = new SpamDefenseHandler(
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SpamDefenseHandler>.Instance
        );
    }

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private Task ChatAsync(string userId) =>
        _handler.HandleAsync(
            new ChatMessageReceivedEvent
            {
                BroadcasterId = Channel,
                MessageId = $"msg-{userId}",
                TwitchBroadcasterId = "chan-ext",
                UserId = userId,
                UserDisplayName = userId,
                UserLogin = userId,
                Message = "best viewers on bigfollows . com",
                Fragments = [],
                Badges = [],
                IsSubscriber = false,
                IsVip = false,
                IsModerator = false,
                IsBroadcaster = false,
            },
            CancellationToken.None
        );

    /// <summary>Twenty strangers qualify the campaign; ten seconds later the action delay has passed.</summary>
    private async Task QualifyCampaignAsync()
    {
        for (int i = 0; i < 20; i++)
            await ChatAsync($"stranger{i}");
        _time.Advance(TimeSpan.FromSeconds(10));
    }

    private SpamCampaignRecord Stored()
    {
        using AppDbContext db = NewDbContext();
        return db.SpamCampaigns.Single();
    }

    [Fact]
    public async Task ADryRunChannel_NeverListsAnAccountAsActioned()
    {
        await QualifyCampaignAsync();
        _decision = new SpamDecision(
            SpamOutcome.None,
            SpamOutcome.DeleteAndEscalate,
            true,
            "would have timed out"
        );

        await ChatAsync("stranger0");

        SpamCampaignRecord record = Stored();
        record.ActionedAccountIds.Should().BeEmpty();
        record.ActionedCount.Should().Be(0);
    }

    [Fact]
    public async Task AFailedTimeout_NeverListsTheAccountAsActioned()
    {
        await QualifyCampaignAsync();
        _decision = new SpamDecision(
            SpamOutcome.DeleteAndEscalate,
            SpamOutcome.DeleteAndEscalate,
            false,
            "campaign member"
        );
        _timeoutSucceeds = false;

        await ChatAsync("stranger0");

        await _moderation
            .Received(1)
            .TimeoutAsync(
                Channel.ToString(),
                Owner,
                "stranger0",
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        Stored().ActionedAccountIds.Should().BeEmpty("the platform refused the timeout");
    }

    [Fact]
    public async Task ASuccessfulTimeout_IsTheOnlyThingTheClearedCampaignUndoes()
    {
        await QualifyCampaignAsync();
        _decision = new SpamDecision(
            SpamOutcome.DeleteAndEscalate,
            SpamOutcome.DeleteAndEscalate,
            false,
            "campaign member"
        );
        await ChatAsync("stranger0");

        // stranger1 is banned by a human moderator; the bot's enforcement does nothing to them.
        _decision = new SpamDecision(
            SpamOutcome.None,
            SpamOutcome.None,
            false,
            "moderator got there"
        );
        await ChatAsync("stranger1");

        Stored().ActionedAccountIds.Should().Be("stranger0");

        // Fifteen regulars join: the campaign clears and restores what the bot did, nothing more.
        for (int i = 0; i < 15; i++)
            await ChatAsyncAs($"regular{i}", SpamTrustTier.Established);

        await _moderation
            .Received(1)
            .UnbanAsync(
                Channel.ToString(),
                Owner,
                "stranger0",
                SpamCampaignReversalExecutor.SystemActorId,
                Arg.Any<CancellationToken>()
            );
        await _moderation
            .DidNotReceive()
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                "stranger1",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    private Task ChatAsyncAs(string userId, SpamTrustTier tier)
    {
        _spamDefense
            .EvaluateAsync(
                Arg.Is<SpamEvaluationRequest>(r => r.PlatformUserId == userId),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromResult<SpamEvaluationResult?>(
                    new SpamEvaluationResult(
                        new SpamDecision(SpamOutcome.None, SpamOutcome.None, false, "regular"),
                        SpamConfidence.Zero,
                        tier,
                        [],
                        null,
                        Skeleton,
                        new SpamDefenseSettings()
                    )
                )
            );
        return ChatAsync(userId);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }
}
