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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The review queue's correction paths take the id exactly as the API wrote it: every Guid leaves the API
/// as a 26-character ULID (<see cref="UlidGuidJsonConverter"/>), so the overturn and batch-restore routes
/// must decode that form, and still accept a raw Guid. Run against the real service on SQLite so the
/// assertion is the stored state, not a 200.
/// </summary>
public sealed class SpamDefenseControllerIdWireTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000d1");
    private static readonly Guid Operator = Guid.Parse("0199c000-0000-7000-8000-0000000000ee");

    private readonly SqliteConnection _connection;
    private readonly ITwitchUsersApi _twitchUsers = Substitute.For<ITwitchUsersApi>();
    private readonly IModerationService _moderation = Substitute.For<IModerationService>();

    public SpamDefenseControllerIdWireTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private SpamDefenseController BuildController(AppDbContext db)
    {
        SpamDefenseService service = new(
            db,
            new FakeTimeProvider(new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)),
            _moderation,
            new CurrentTenantService(),
            _twitchUsers,
            Substitute.For<IFollowStateService>()
        );
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Operator.ToString());
        return new(service, user, Substitute.For<ILockdownService>());
    }

    private SpamDetection SeedDetection()
    {
        SpamDetection detection = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            SubjectPlatformUserId = "bot-9",
            SubjectDisplayName = "Bot9",
            Provider = AuthEnums.Platform.Twitch,
            MessageId = Guid.NewGuid().ToString(),
            MessageText = "free f0ll0ws check bio",
            Skeleton = "free follows check bio",
            Signals = nameof(SpamConfidence.High),
            Confidence = SpamConfidence.High,
            Tier = SpamTrustTier.Untrusted,
            Outcome = SpamOutcome.DeleteAndEscalate,
            WouldHaveBeen = SpamOutcome.DeleteAndEscalate,
            WasDryRun = false,
            Reason = "High confidence.",
            DetectedAt = new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc),
        };
        using AppDbContext db = NewDbContext();
        db.SpamDetections.Add(detection);
        db.SaveChanges();
        return detection;
    }

    [Fact]
    public async Task Overturn_with_the_ulid_the_api_sent_marks_the_detection_overturned()
    {
        SpamDetection detection = SeedDetection();
        _moderation
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, "unbanned")));
        string wireId = GuidUlidCodec.Encode(detection.Id);
        wireId.Should().HaveLength(26);

        using AppDbContext db = NewDbContext();
        IActionResult response = await BuildController(db)
            .OverturnDetection(Channel.ToString(), wireId, CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        using AppDbContext readBack = NewDbContext();
        SpamDetection stored = await readBack.SpamDetections.SingleAsync(d => d.Id == detection.Id);
        stored.OverturnedAt.Should().NotBeNull();
        stored.OverturnedByUserId.Should().Be(Operator);
    }

    [Fact]
    public async Task Overturn_with_a_raw_guid_still_marks_the_detection_overturned()
    {
        SpamDetection detection = SeedDetection();
        _moderation
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, "unbanned")));

        using AppDbContext db = NewDbContext();
        IActionResult response = await BuildController(db)
            .OverturnDetection(Channel.ToString(), detection.Id.ToString(), CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        using AppDbContext readBack = NewDbContext();
        (await readBack.SpamDetections.SingleAsync(d => d.Id == detection.Id))
            .OverturnedAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task Overturn_with_garbage_is_refused_and_the_detection_is_untouched()
    {
        SpamDetection detection = SeedDetection();

        using AppDbContext db = NewDbContext();
        IActionResult response = await BuildController(db)
            .OverturnDetection(Channel.ToString(), "not-an-id", CancellationToken.None);

        response.Should().BeOfType<BadRequestObjectResult>();
        using AppDbContext readBack = NewDbContext();
        (await readBack.SpamDetections.SingleAsync(d => d.Id == detection.Id))
            .OverturnedAt.Should()
            .BeNull();
    }

    [Fact]
    public async Task Restoring_a_batch_by_the_ulid_the_api_sent_stamps_every_block_restored()
    {
        Guid batch = Guid.CreateVersion7();
        using (AppDbContext seed = NewDbContext())
        {
            for (int i = 0; i < 3; i++)
                seed.FollowBotBlocks.Add(
                    new FollowBotBlock
                    {
                        BroadcasterId = Channel,
                        BatchId = batch,
                        SubjectPlatformUserId = $"bot-{i}",
                        SubjectUsername = $"viewer{i}8042193",
                        Indicators = nameof(FollowBotIndicator.GeneratedHandlePattern),
                        BatchExamined = 8,
                        BlockedAt = new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc),
                    }
                );
            seed.SaveChanges();
        }
        _twitchUsers
            .UnblockUserAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        using AppDbContext db = NewDbContext();
        IActionResult response = await BuildController(db)
            .RestoreFollowBotBatch(
                Channel.ToString(),
                GuidUlidCodec.Encode(batch),
                CancellationToken.None
            );

        response.Should().BeOfType<OkObjectResult>();
        using AppDbContext readBack = NewDbContext();
        (await readBack.FollowBotBlocks.ToListAsync())
            .Should()
            .HaveCount(3)
            .And.OnlyContain(b => b.RestoredAt != null);
    }
}
