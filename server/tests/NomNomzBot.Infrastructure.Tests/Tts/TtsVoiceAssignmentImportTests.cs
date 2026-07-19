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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the bulk voice-assignment import (the legacy-bot migration surface): valid rows create new
/// UserTtsVoice rows or upsert existing ones; a Twitch user the bot has never seen and a voice missing from
/// the catalogue come back as skipped rows with a reason (and create nothing by default); with
/// createMissing a row that carries a username creates the viewer through the SAME chat-ingest
/// get-or-create seam (IUserService.GetOrCreateAsync) before the assignment lands; the request is capped
/// at 500 rows; and rows land only on the target tenant.
/// </summary>
public sealed class TtsVoiceAssignmentImportTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-4444-7000-8000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("019f2a00-4444-7000-8000-000000000002");

    private sealed class Harness
    {
        public required TtsConfigService Service { get; init; }
        public required TtsTestDbContext Db { get; init; }
        public required Application.Identity.Services.IUserService Users { get; init; }
    }

    private static Harness Build(string[]? knownUserIds = null, string[]? catalogueVoiceIds = null)
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        foreach (string userId in knownUserIds ?? [])
        {
            db.UserIdentities.Add(
                new UserIdentity
                {
                    UserId = Guid.CreateVersion7(),
                    Provider = "twitch",
                    ProviderUserId = userId,
                    ProviderUsername = $"user-{userId}",
                }
            );
        }
        foreach (string voiceId in catalogueVoiceIds ?? [])
        {
            db.TtsVoices.Add(
                new TtsVoice
                {
                    Id = voiceId,
                    Name = voiceId,
                    DisplayName = voiceId,
                    Locale = "en-US",
                    Gender = "Female",
                    Provider = "edge",
                }
            );
        }
        db.SaveChanges();

        ITtsService tts = Substitute.For<ITtsService>();
        tts.GetAvailableVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TtsVoiceInfo>>([]));

        // The chat-ingest identity seam, faked with the seam's real consequence: a call mints the viewer's
        // User id and lands the twitch UserIdentity row exactly as the chat path would — so the tests can
        // assert the created row's shape, not just that a mock was called.
        Application.Identity.Services.IUserService users =
            Substitute.For<Application.Identity.Services.IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                string platformUserId = call.ArgAt<string>(0);
                string username = call.ArgAt<string>(1);
                string displayName = call.ArgAt<string>(2);
                Guid userId = Guid.CreateVersion7();
                db.UserIdentities.Add(
                    new UserIdentity
                    {
                        UserId = userId,
                        Provider = "twitch",
                        ProviderUserId = platformUserId,
                        ProviderUsername = username,
                        ProviderDisplayName = displayName,
                        IsPrimary = true,
                    }
                );
                await db.SaveChangesAsync(call.ArgAt<CancellationToken>(4));
                return Result.Success(
                    new Application.Identity.Dtos.UserDto(
                        userId.ToString(),
                        username,
                        displayName,
                        ProfileImageUrl: null,
                        Email: null,
                        CreatedAt: DateTime.UtcNow,
                        LastLoginAt: DateTime.UtcNow
                    )
                );
            });

        return new Harness
        {
            Service = new TtsConfigService(
                db,
                tts,
                Substitute.For<IEventBus>(),
                Substitute.For<Application.Services.ISubjectKeyService>(),
                users
            ),
            Db = db,
            Users = users,
        };
    }

    private static TtsVoiceAssignmentRowDto Row(
        string userId,
        string voiceId,
        string? username = null
    ) =>
        new()
        {
            TwitchUserId = userId,
            VoiceId = voiceId,
            Username = username,
        };

    [Fact]
    public async Task Import_CreatesNewAndUpsertsExisting_AndReportsCounts()
    {
        Harness h = Build(knownUserIds: ["100", "200"], catalogueVoiceIds: ["voice-a", "voice-b"]);
        h.Db.UserTtsVoices.Add(
            new UserTtsVoice
            {
                BroadcasterId = Tenant,
                UserId = "100",
                VoiceId = "voice-a",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("100", "voice-b"), Row("200", "voice-a")]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Imported.Should().Be(2);
        result.Value.Skipped.Should().BeEmpty();

        List<UserTtsVoice> rows = await h.Db.UserTtsVoices.OrderBy(v => v.UserId).ToListAsync();
        rows.Should().HaveCount(2); // the existing row was UPDATED, not doubled
        rows[0].UserId.Should().Be("100");
        rows[0].VoiceId.Should().Be("voice-b");
        rows[0].BroadcasterId.Should().Be(Tenant);
        rows[1].UserId.Should().Be("200");
        rows[1].VoiceId.Should().Be("voice-a");
    }

    [Fact]
    public async Task Import_SkipsUnknownUserAndUnknownVoice_WithReasons_AndCreatesNoUsers()
    {
        Harness h = Build(knownUserIds: ["100"], catalogueVoiceIds: ["voice-a"]);

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("100", "voice-a"), Row("999", "voice-a"), Row("100", "ghost-voice")]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Imported.Should().Be(1);
        result.Value.Skipped.Should().HaveCount(2);
        result
            .Value.Skipped.Should()
            .ContainEquivalentOf(new TtsVoiceImportSkipDto("999", "unknown_user"));
        result
            .Value.Skipped.Should()
            .ContainEquivalentOf(new TtsVoiceImportSkipDto("100", "unknown_voice"));

        // No identity was fabricated for the unknown user, and only the valid assignment landed.
        (await h.Db.UserIdentities.CountAsync())
            .Should()
            .Be(1);
        UserTtsVoice stored = await h.Db.UserTtsVoices.SingleAsync();
        stored.UserId.Should().Be("100");
        stored.VoiceId.Should().Be("voice-a");
    }

    [Fact]
    public async Task Import_Over500Rows_IsRejectedWholesale()
    {
        Harness h = Build(knownUserIds: ["100"], catalogueVoiceIds: ["voice-a"]);
        List<TtsVoiceAssignmentRowDto> rows = Enumerable
            .Range(0, 501)
            .Select(i => Row($"u{i}", "voice-a"))
            .ToList();

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            rows
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await h.Db.UserTtsVoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_IsTenantIsolated_RowsLandOnlyOnTheTargetChannel()
    {
        Harness h = Build(knownUserIds: ["100"], catalogueVoiceIds: ["voice-a"]);
        h.Db.UserTtsVoices.Add(
            new UserTtsVoice
            {
                BroadcasterId = OtherTenant,
                UserId = "100",
                VoiceId = "voice-a",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("100", "voice-a")]
        );

        result.Value.Imported.Should().Be(1);
        // The OTHER tenant's assignment is untouched; the target tenant got its own row.
        (
            await h.Db.UserTtsVoices.CountAsync(v =>
                v.BroadcasterId == OtherTenant && v.UserId == "100"
            )
        )
            .Should()
            .Be(1);
        (await h.Db.UserTtsVoices.CountAsync(v => v.BroadcasterId == Tenant && v.UserId == "100"))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task Import_DuplicateRowsForOneUser_UpsertOnce_LastWins()
    {
        Harness h = Build(knownUserIds: ["100"], catalogueVoiceIds: ["voice-a", "voice-b"]);

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("100", "voice-a"), Row("100", "voice-b")]
        );

        result.IsSuccess.Should().BeTrue();
        UserTtsVoice stored = await h.Db.UserTtsVoices.SingleAsync();
        stored.VoiceId.Should().Be("voice-b");
    }

    [Fact]
    public async Task Import_CreateMissing_CreatesViewerThroughChatSeam_AndAssignmentLands()
    {
        Harness h = Build(knownUserIds: [], catalogueVoiceIds: ["voice-a"]);

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("555", "voice-a", username: "legacyfan")],
            createMissing: true
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Imported.Should().Be(1);
        result.Value.Skipped.Should().BeEmpty();

        // The viewer User row was minted through the chat-ingest seam with the row's twitch id + login
        // (display name = login, the best the legacy export carries) — the same shape a first chat message
        // would create.
        await h
            .Users.Received(1)
            .GetOrCreateAsync(
                "555",
                "legacyfan",
                "legacyfan",
                "twitch",
                Arg.Any<CancellationToken>()
            );
        UserIdentity identity = await h.Db.UserIdentities.SingleAsync(i =>
            i.ProviderUserId == "555"
        );
        identity.Provider.Should().Be("twitch");
        identity.ProviderUsername.Should().Be("legacyfan");
        identity.ProviderDisplayName.Should().Be("legacyfan");
        identity.UserId.Should().NotBe(Guid.Empty);
        identity.IsPrimary.Should().BeTrue();

        UserTtsVoice assignment = await h.Db.UserTtsVoices.SingleAsync();
        assignment.BroadcasterId.Should().Be(Tenant);
        assignment.UserId.Should().Be("555");
        assignment.VoiceId.Should().Be("voice-a");
    }

    [Fact]
    public async Task Import_WithoutCreateMissing_UnknownUserWithUsername_IsStillSkipped()
    {
        Harness h = Build(knownUserIds: [], catalogueVoiceIds: ["voice-a"]);

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("555", "voice-a", username: "legacyfan")]
        );

        result.Value.Imported.Should().Be(0);
        result
            .Value.Skipped.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new TtsVoiceImportSkipDto("555", "unknown_user"));
        // Nothing was created — no identity, no assignment, and the seam was never touched.
        (await h.Db.UserIdentities.CountAsync())
            .Should()
            .Be(0);
        (await h.Db.UserTtsVoices.CountAsync()).Should().Be(0);
        await h
            .Users.DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Import_CreateMissing_WithoutUsername_UnknownUserIsSkipped()
    {
        Harness h = Build(knownUserIds: [], catalogueVoiceIds: ["voice-a"]);

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("555", "voice-a")],
            createMissing: true
        );

        result.Value.Imported.Should().Be(0);
        result
            .Value.Skipped.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new TtsVoiceImportSkipDto("555", "unknown_user"));
        (await h.Db.UserIdentities.CountAsync()).Should().Be(0);
        await h
            .Users.DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Import_CreateMissing_UnknownVoice_SkipsWithoutCreatingTheViewer()
    {
        Harness h = Build(knownUserIds: [], catalogueVoiceIds: ["voice-a"]);

        Result<TtsVoiceImportResultDto> result = await h.Service.ImportUserVoiceAssignmentsAsync(
            Tenant,
            [Row("555", "ghost-voice", username: "legacyfan")],
            createMissing: true
        );

        result.Value.Imported.Should().Be(0);
        result
            .Value.Skipped.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new TtsVoiceImportSkipDto("555", "unknown_voice"));
        // A voice that can never play must not mint a viewer User as a side effect.
        (await h.Db.UserIdentities.CountAsync())
            .Should()
            .Be(0);
        (await h.Db.UserTtsVoices.CountAsync()).Should().Be(0);
        await h
            .Users.DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync(default!, default!, default!, default!, default);
    }
}
