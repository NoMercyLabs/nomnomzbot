// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The "AutoMod violations count as ladder offenses" setting (S-ESCALATION-DEAD-SETTINGS). With the
/// setting and the ladder both on, the ladder's decision REPLACES the rule's own timeout or ban; with
/// either off nothing is recorded and nothing is punished here, so the caller keeps its own action.
/// </summary>
public sealed class ViolationEscalationServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000f1");
    private static readonly Guid Owner = Guid.Parse("0199c000-0000-7000-8000-0000000000f2");
    private static readonly Guid Viewer = Guid.Parse("0199c000-0000-7000-8000-0000000000f3");

    private readonly SqliteConnection _connection;
    private readonly IModerationService _moderation = Substitute.For<IModerationService>();
    private readonly IUserService _users = Substitute.For<IUserService>();

    public ViolationEscalationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
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

        _users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromResult(
                    Result.Success(
                        new UserDto(
                            Viewer.ToString(),
                            "viewer",
                            "Viewer",
                            null,
                            null,
                            DateTime.UtcNow,
                            DateTime.UtcNow
                        )
                    )
                )
            );
        _moderation
            .WarnUserAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Result.Success(new ModerationActionResult(true, null))));
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
            .Returns(_ => Task.FromResult(Result.Success(new ModerationActionResult(true, null))));
        _moderation
            .BanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Task.FromResult(Result.Success(new ModerationActionResult(true, null))));
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private void SeedPolicy(
        bool enabled,
        bool countAutoMod,
        List<EscalationLadderStep> ladder,
        int defaultTimeoutSeconds = 600
    )
    {
        using AppDbContext db = NewDbContext();
        db.ModerationEscalationPolicies.Add(
            new ModerationEscalationPolicy
            {
                BroadcasterId = Channel,
                IsEnabled = enabled,
                CountAutoModViolations = countAutoMod,
                LadderJson = JsonSerializer.Serialize(ladder),
                DefaultTimeoutSeconds = defaultTimeoutSeconds,
            }
        );
        db.SaveChanges();
    }

    private static List<EscalationLadderStep> Ladder() =>
        [new(1, "warn", null), new(2, "timeout", 60), new(3, "ban", null)];

    private async Task<ViolationEscalationOutcome> EscalateAsync(string reason = "Link posted")
    {
        using AppDbContext db = NewDbContext();
        ModerationEscalationService escalation = new(
            db,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero))
        );
        ViolationEscalationService sut = new(
            db,
            escalation,
            _users,
            _moderation,
            NullLogger<ViolationEscalationService>.Instance
        );
        return await sut.TryEscalateAsync(
            Channel,
            "viewer-1",
            "viewer",
            "Viewer",
            reason,
            CancellationToken.None
        );
    }

    private int OffenseRows()
    {
        using AppDbContext db = NewDbContext();
        return db.ModerationEscalationStates.Count();
    }

    private int StoredOffenseCount()
    {
        using AppDbContext db = NewDbContext();
        return db
            .ModerationEscalationStates.Where(s =>
                s.BroadcasterId == Channel && s.SubjectUserId == Viewer
            )
            .Select(s => s.OffenseCount)
            .Single();
    }

    [Fact]
    public async Task SettingOn_FirstOffenseWarns_SecondOffenseTimesOutForTheLadderSeconds()
    {
        SeedPolicy(enabled: true, countAutoMod: true, Ladder());

        ViolationEscalationOutcome first = await EscalateAsync("Link posted");

        first.Handled.Should().BeTrue();
        first.Action.Should().Be("warn");
        first.Applied.Should().BeTrue();
        StoredOffenseCount().Should().Be(1);
        await _moderation
            .Received(1)
            .WarnUserAsync(
                Channel.ToString(),
                Owner,
                "viewer-1",
                "Link posted",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );

        ViolationEscalationOutcome second = await EscalateAsync("Link posted");

        second.Handled.Should().BeTrue();
        second.Action.Should().Be("timeout");
        StoredOffenseCount().Should().Be(2);
        await _moderation
            .Received(1)
            .TimeoutAsync(
                Channel.ToString(),
                Owner,
                "viewer-1",
                60,
                "Link posted",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SettingOn_ThirdOffenseBans()
    {
        SeedPolicy(enabled: true, countAutoMod: true, Ladder());

        await EscalateAsync();
        await EscalateAsync();
        ViolationEscalationOutcome third = await EscalateAsync("Spam");

        third.Action.Should().Be("ban");
        await _moderation
            .Received(1)
            .BanAsync(
                Channel.ToString(),
                Owner,
                "viewer-1",
                "Spam",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SettingOff_RecordsNothingAndPunishesNothing()
    {
        SeedPolicy(enabled: true, countAutoMod: false, Ladder());

        ViolationEscalationOutcome outcome = await EscalateAsync();

        outcome.Handled.Should().BeFalse();
        OffenseRows().Should().Be(0);
        _moderation.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task LadderDisabled_WithSettingOn_RecordsNothingAndPunishesNothing()
    {
        SeedPolicy(enabled: false, countAutoMod: true, Ladder());

        ViolationEscalationOutcome outcome = await EscalateAsync();

        outcome.Handled.Should().BeFalse();
        OffenseRows().Should().Be(0);
        _moderation.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NoPolicyRow_IsNotHandled()
    {
        ViolationEscalationOutcome outcome = await EscalateAsync();

        outcome.Handled.Should().BeFalse();
        OffenseRows().Should().Be(0);
    }

    [Fact]
    public async Task TimeoutStepWithoutOwnDuration_UsesTheDefaultTimeoutSeconds()
    {
        SeedPolicy(
            enabled: true,
            countAutoMod: true,
            [new(1, "timeout", null)],
            defaultTimeoutSeconds: 900
        );

        ViolationEscalationOutcome outcome = await EscalateAsync("Caps");

        outcome.Action.Should().Be("timeout");
        await _moderation
            .Received(1)
            .TimeoutAsync(
                Channel.ToString(),
                Owner,
                "viewer-1",
                900,
                "Caps",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task APlatformFailure_IsStillHandled_SoNoSecondPunishmentFollows()
    {
        SeedPolicy(enabled: true, countAutoMod: true, [new(1, "timeout", 60)]);
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
                    Result.Failure<ModerationActionResult>("Twitch said no", "TWITCH_ERROR")
                )
            );

        ViolationEscalationOutcome outcome = await EscalateAsync();

        outcome.Handled.Should().BeTrue();
        outcome.Applied.Should().BeFalse();
        StoredOffenseCount().Should().Be(1);
    }
}
