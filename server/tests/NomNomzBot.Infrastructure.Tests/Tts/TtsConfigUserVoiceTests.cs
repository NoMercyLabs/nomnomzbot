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
                new TtsVoice
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

        return new Harness { Service = new TtsConfigService(db, tts, bus), Db = db };
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
            new UserTtsVoice
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
            new UserTtsVoice
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
            new UserTtsVoice
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
}
