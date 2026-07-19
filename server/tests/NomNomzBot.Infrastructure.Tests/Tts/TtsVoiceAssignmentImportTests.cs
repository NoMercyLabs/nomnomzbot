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
/// the catalogue come back as skipped rows with a reason (and create nothing — no User rows ever); the
/// request is capped at 500 rows; and rows land only on the target tenant.
/// </summary>
public sealed class TtsVoiceAssignmentImportTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-4444-7000-8000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("019f2a00-4444-7000-8000-000000000002");

    private sealed class Harness
    {
        public required TtsConfigService Service { get; init; }
        public required TtsTestDbContext Db { get; init; }
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

        return new Harness
        {
            Service = new TtsConfigService(
                db,
                tts,
                Substitute.For<IEventBus>(),
                Substitute.For<Application.Services.ISubjectKeyService>()
            ),
            Db = db,
        };
    }

    private static TtsVoiceAssignmentRowDto Row(string userId, string voiceId) =>
        new() { TwitchUserId = userId, VoiceId = voiceId };

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
}
