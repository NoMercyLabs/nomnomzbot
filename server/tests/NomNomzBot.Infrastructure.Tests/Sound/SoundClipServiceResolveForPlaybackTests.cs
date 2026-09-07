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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// <c>ResolveForPlaybackAsync</c> resolves a <c>play_sound</c> reference "by id or name" — before this fix it
/// tried a bare <see cref="Guid.TryParse"/> first and fell back to a name match, so a clip reference sent in
/// the dashboard picker's ULID wire form (26-char Crockford, UlidGuidJsonConverter) fell all the way through
/// to the NAME lookup and missed (no clip is named a 26-char ULID) instead of being resolved by id. Same bug
/// class as run_code's code_script_id, surfacing differently because of the name-lookup fallback.
/// </summary>
public sealed class SoundClipServiceResolveForPlaybackTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192c000-0000-7000-8000-00000000d001");

    private static (SoundClipService Service, PipelineOptionsTestDbContext Db) Build()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ISoundClipStore store = Substitute.For<ISoundClipStore>();
        store
            .GetPlaybackUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result<string>.Success($"/api/v1/sound-clips/stream/{call.Arg<string>()}")
            );
        SoundClipService service = new(
            db,
            store,
            Substitute.For<ISoundClipOverlayNotifier>(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IResourceQuotaService>(),
            Substitute.For<IPipelineStepReferenceScanner>()
        );
        return (service, db);
    }

    private static SoundClip NewClip(Guid id, string name) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            Name = name,
            DisplayName = name,
            StorageKey = $"{Broadcaster:N}/{name}.mp3",
            MimeType = "audio/mpeg",
            DurationMs = 1000,
            SizeBytes = 2048,
            DefaultVolume = 65,
            IsEnabled = true,
            CreatedByUserId = Guid.NewGuid(),
        };

    [Fact]
    public async Task Resolves_by_raw_guid()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db) = Build();
        Guid clipId = Guid.NewGuid();
        db.SoundClips.Add(NewClip(clipId, "airhorn"));
        await db.SaveChangesAsync();

        Result<SoundPlaybackDto> result = await service.ResolveForPlaybackAsync(
            Broadcaster,
            clipId.ToString(),
            volumeOverride: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ClipId.Should().Be(clipId);
    }

    [Fact]
    public async Task Resolves_by_name_when_the_reference_is_not_an_id()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db) = Build();
        Guid clipId = Guid.NewGuid();
        db.SoundClips.Add(NewClip(clipId, "airhorn"));
        await db.SaveChangesAsync();

        Result<SoundPlaybackDto> result = await service.ResolveForPlaybackAsync(
            Broadcaster,
            "airhorn",
            volumeOverride: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ClipId.Should().Be(clipId);
    }

    [Fact]
    public async Task A_ulid_form_clip_reference_resolves_by_id_instead_of_missing_a_name_lookup()
    {
        (SoundClipService service, PipelineOptionsTestDbContext db) = Build();
        Guid clipId = Guid.NewGuid();
        db.SoundClips.Add(NewClip(clipId, "airhorn"));
        await db.SaveChangesAsync();
        string ulid = OwnedIdCodec.Encode(clipId);

        Result<SoundPlaybackDto> result = await service.ResolveForPlaybackAsync(
            Broadcaster,
            ulid,
            volumeOverride: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ClipId.Should().Be(clipId);
    }
}
