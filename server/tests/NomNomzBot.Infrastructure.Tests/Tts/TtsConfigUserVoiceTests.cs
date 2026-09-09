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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves per-viewer TTS voice management (tts.md §3.3): a voice is only stored when the channel can actually
/// synthesize it (validated against the voice catalogue the dispatch resolver uses), assignment is an idempotent
/// upsert (no duplicate rows), clearing removes the row so the viewer falls back to the channel default, and both
/// get/clear on a missing assignment report NOT_FOUND. Assertions are on the persisted rows, not the call surface.
/// </summary>
public sealed class TtsConfigUserVoiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-2222-7000-8000-000000000001");
    private const string Viewer = "viewer-77";

    private sealed class Harness
    {
        public required TtsConfigService Service { get; init; }
        public required TtsTestDbContext Db { get; init; }
    }

    private static Harness Build(params string[] catalogueVoiceIds)
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        foreach (string id in catalogueVoiceIds)
        {
            db.TtsVoices.Add(
                new()
                {
                    Id = id,
                    Name = id,
                    DisplayName = id,
                    Locale = "en-US",
                    Gender = "Female",
                    Provider = "edge",
                    IsDefault = false,
                }
            );
        }
        db.SaveChanges();

        ITtsService tts = Substitute.For<ITtsService>();
        tts.GetAvailableVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TtsVoiceInfo>>([]));

        IEventBus bus = Substitute.For<IEventBus>();
        Application.Services.ISubjectKeyService subjectKeys =
            Substitute.For<Application.Services.ISubjectKeyService>();

        return new()
        {
            Service = new(
                db,
                tts,
                bus,
                subjectKeys,
                Substitute.For<Application.Identity.Services.IUserService>()
            ),
            Db = db,
        };
    }

    private static SetUserVoiceDto Set(string voiceId) => new() { VoiceId = voiceId };

    [Fact]
    public async Task SetUserVoiceAsync_UnknownVoice_RejectsWithoutStoring()
    {
        Harness h = Build(); // empty catalogue, provider returns nothing

        Result<UserTtsVoiceDto> result = await h.Service.SetUserVoiceAsync(
            Tenant,
            Viewer,
            Set("voice-that-does-not-exist")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        (await h.Db.UserTtsVoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetUserVoiceAsync_KnownVoice_PersistsAssignment()
    {
        Harness h = Build("voice-a", "voice-b");

        Result<UserTtsVoiceDto> result = await h.Service.SetUserVoiceAsync(
            Tenant,
            Viewer,
            Set("voice-a")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(Viewer);
        result.Value.VoiceId.Should().Be("voice-a");

        UserTtsVoice row = await h.Db.UserTtsVoices.SingleAsync();
        row.BroadcasterId.Should().Be(Tenant);
        row.UserId.Should().Be(Viewer);
        row.VoiceId.Should().Be("voice-a");
    }

    [Fact]
    public async Task SetUserVoiceAsync_ExistingAssignment_UpdatesInPlace()
    {
        Harness h = Build("voice-a", "voice-b");
        h.Db.UserTtsVoices.Add(
            new()
            {
                BroadcasterId = Tenant,
                UserId = Viewer,
                VoiceId = "voice-a",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<UserTtsVoiceDto> result = await h.Service.SetUserVoiceAsync(
            Tenant,
            Viewer,
            Set("voice-b")
        );

        result.IsSuccess.Should().BeTrue();
        // Upsert, not insert: still one row, now pointing at the new voice.
        UserTtsVoice row = await h.Db.UserTtsVoices.SingleAsync();
        row.VoiceId.Should().Be("voice-b");
    }

    [Fact]
    public async Task GetUserVoiceAsync_NoAssignment_ReturnsNotFound()
    {
        Harness h = Build("voice-a");

        Result<UserTtsVoiceDto> result = await h.Service.GetUserVoiceAsync(Tenant, Viewer);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GetUserVoiceAsync_WithAssignment_ReturnsIt()
    {
        Harness h = Build("voice-a");
        h.Db.UserTtsVoices.Add(
            new()
            {
                BroadcasterId = Tenant,
                UserId = Viewer,
                VoiceId = "voice-a",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<UserTtsVoiceDto> result = await h.Service.GetUserVoiceAsync(Tenant, Viewer);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(Viewer);
        result.Value.VoiceId.Should().Be("voice-a");
    }

    [Fact]
    public async Task ClearUserVoiceAsync_RemovesAssignment()
    {
        Harness h = Build("voice-a");
        h.Db.UserTtsVoices.Add(
            new()
            {
                BroadcasterId = Tenant,
                UserId = Viewer,
                VoiceId = "voice-a",
            }
        );
        await h.Db.SaveChangesAsync();

        Result result = await h.Service.ClearUserVoiceAsync(Tenant, Viewer);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.UserTtsVoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ClearUserVoiceAsync_NoAssignment_ReturnsNotFound()
    {
        Harness h = Build("voice-a");

        Result result = await h.Service.ClearUserVoiceAsync(Tenant, Viewer);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ── Id type fix (owner punch list 2026-09-08 §3 — schema P.3: "Id guid PK (was int -> surrogate)") ──

    /// <summary>
    /// <c>UserTtsVoice.Id</c> used to be a bare <c>int</c> surrogate, drifted from its own locked schema and
    /// from every other per-user entity's PK convention. Proves the fixed column round-trips a real,
    /// distinct <see cref="Guid"/> per row — not merely that the type declaration changed.
    /// </summary>
    [Fact]
    public async Task Id_IsAGuid_AssignedAppSide_AndDistinctPerRow()
    {
        Harness h = Build("voice-a", "voice-b");

        Result<UserTtsVoiceDto> first = await h.Service.SetUserVoiceAsync(
            Tenant,
            "viewer-1",
            Set("voice-a")
        );
        Result<UserTtsVoiceDto> second = await h.Service.SetUserVoiceAsync(
            Tenant,
            "viewer-2",
            Set("voice-b")
        );
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        List<UserTtsVoice> rows = await h.Db.UserTtsVoices.OrderBy(v => v.UserId).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].Id.Should().NotBe(Guid.Empty);
        rows[1].Id.Should().NotBe(Guid.Empty);
        rows[0].Id.Should().NotBe(rows[1].Id, "each row gets its own distinct surrogate key");
    }

    /// <summary>The (BroadcasterId, UserId) uniqueness the surrogate-key change must not have weakened.</summary>
    [Fact]
    public async Task TheUniqueBroadcasterUserIndex_StillRejectsATrueDuplicate()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.UserTtsVoices.Add(
            new()
            {
                BroadcasterId = Tenant,
                UserId = Viewer,
                VoiceId = "voice-a",
            }
        );
        await db.SaveChangesAsync();

        db.UserTtsVoices.Add(
            new()
            {
                BroadcasterId = Tenant,
                UserId = Viewer,
                VoiceId = "voice-b",
            }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
