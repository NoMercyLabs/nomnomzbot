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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Sound;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// The <c>sound_clip</c> platform-template kind, end to end (plan T1b-A3, option A): an admin uploads an audio
/// file to the platform library, a template names it by id, a channel install copies the bytes through the real
/// <see cref="SoundClipService.UploadAsync"/> (so format, size, duration, clip count and the storage quota run
/// again), and retiring the last template that names a file removes the file.
/// </summary>
public sealed class PlatformTemplateSoundClipTests : IAsyncDisposable
{
    private readonly PlatformTemplateHarness _h = new();
    private readonly InMemorySoundClipStore _store = new();
    private readonly SoundClipTemplateInstaller _installer;
    private readonly PlatformAudioAssetService _assets;
    private long _storageLimitBytes = long.MaxValue;

    public PlatformTemplateSoundClipTests()
    {
        IResourceQuotaService quota = Substitute.For<IResourceQuotaService>();
        quota
            .GetCurrentCountAsync(
                Arg.Any<Guid>(),
                "sound_clip_storage_bytes",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<long>.Success(0));
        quota
            .CheckAsync(
                Arg.Any<Guid>(),
                "sound_clip_storage_bytes",
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                long requested = call.ArgAt<long>(2);
                return Result<QuotaCheckDto>.Success(
                    new(
                        requested <= _storageLimitBytes,
                        "sound_clip_storage_bytes",
                        requested,
                        _storageLimitBytes,
                        Math.Max(0, _storageLimitBytes - requested)
                    )
                );
            });

        SoundClipService clips = new(
            _h.Db,
            _store,
            Substitute.For<ISoundClipOverlayNotifier>(),
            Substitute.For<IChannelRegistry>(),
            quota,
            Substitute.For<IPipelineStepReferenceScanner>(),
            Substitute.For<IOverlayPresenceRegistry>()
        );
        _installer = new(_h.Db, clips, _store);
        _assets = new(_h.Db, _h.Iam, _store);
    }

    [Fact]
    public async Task Upload_runs_the_clip_file_rules_and_stores_the_file_in_the_platform_area()
    {
        byte[] wav = TestAudio.Wav(seconds: 2);

        Result<PlatformAudioAssetDto> uploaded = await UploadAsync("Airhorn", "airhorn.wav", wav);

        uploaded.IsSuccess.Should().BeTrue(uploaded.ErrorMessage);
        PlatformAudioAsset row = await _h.Db.PlatformAudioAssets.SingleAsync();
        row.Id.Should().Be(uploaded.Value.Id);
        row.DisplayName.Should().Be("Airhorn");
        row.FileName.Should().Be("airhorn.wav");
        row.ContentType.Should().Be("audio/wav", "the type is sniffed from the bytes");
        row.DurationMs.Should().Be(2000);
        row.SizeBytes.Should().Be(wav.Length);
        row.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        row.UploadedByPrincipalId.Should().Be(_h.ActingPrincipalId);
        row.StorageKey.Should().StartWith("platform/");
        _store.Blobs[row.StorageKey].Should().Equal(wav);
    }

    [Fact]
    public async Task Upload_of_a_file_that_is_not_audio_is_refused_and_nothing_is_stored()
    {
        Result<PlatformAudioAssetDto> uploaded = await UploadAsync(
            "Fake",
            "fake.mp3",
            TestAudio.NotAudio()
        );

        uploaded.IsFailure.Should().BeTrue();
        uploaded.ErrorCode.Should().Be("INVALID_FORMAT");
        (await _h.Db.PlatformAudioAssets.CountAsync()).Should().Be(0);
        _store.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task A_template_naming_a_file_that_is_not_in_the_library_cannot_be_authored()
    {
        Result<PlatformContentDefinitionDto> created = await _h.AdminService(_installer)
            .CreateDefinitionAsync(
                _h.ActingPrincipalId,
                new(
                    PlatformContentKinds.SoundClip,
                    "airhorn",
                    "airhorn",
                    null,
                    Payload("airhorn", Guid.NewGuid())
                )
            );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await _h.Db.PlatformContentDefinitions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Install_copies_the_audio_into_the_channels_own_storage_with_the_template_settings()
    {
        byte[] wav = TestAudio.Wav(seconds: 3);
        Guid assetId = (await UploadAsync("Airhorn", "airhorn.wav", wav)).Value.Id;
        string platformKey = (await _h.Db.PlatformAudioAssets.SingleAsync()).StorageKey;
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "airhorn",
            Payload("airhorn", assetId)
        );
        Channel channel = await _h.AddChannelAsync("streamer-b");

        Result<InstalledPlatformTemplateDto> installed = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        SoundClip clip = await _h.Db.SoundClips.SingleAsync();
        clip.Id.Should().Be(installed.Value.EntityId);
        clip.BroadcasterId.Should().Be(channel.Id);
        clip.Name.Should().Be("airhorn");
        clip.DisplayName.Should().Be("Air Horn");
        clip.DefaultVolume.Should().Be(65);
        clip.CooldownSeconds.Should().Be(30);
        clip.MinPermissionLevel.Should().Be(2, "Subscriber is rung 2 on the ladder");
        clip.TriggerWord.Should().Be("horn");
        clip.CreatedByUserId.Should().Be(_h.CallerUserId);
        clip.MimeType.Should().Be("audio/wav");
        clip.DurationMs.Should().Be(3000);
        clip.SizeBytes.Should().Be(wav.Length);
        clip.PlatformSourceDefinitionId.Should().Be(definitionId);
        clip.PlatformSourceVersion.Should().Be(1);
        clip.PlatformSourceHash.Should()
            .Be(SoundClipTemplatePayload.FromEntity(clip).ComputeHash());

        clip.StorageKey.Should()
            .StartWith($"{channel.Id:N}/", "the channel owns its copy of the bytes")
            .And.NotBe(platformKey);
        _store.Blobs[clip.StorageKey].Should().Equal(wav);
        _store
            .Blobs[platformKey]
            .Should()
            .Equal(wav, "install reads the platform file, never moves it");
    }

    [Fact]
    public async Task Install_over_the_channels_storage_quota_is_refused_and_leaves_no_clip_or_bytes()
    {
        byte[] wav = TestAudio.Wav(seconds: 3);
        Guid assetId = (await UploadAsync("Airhorn", "airhorn.wav", wav)).Value.Id;
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "airhorn",
            Payload("airhorn", assetId)
        );
        Channel channel = await _h.AddChannelAsync("streamer-b");
        _storageLimitBytes = wav.Length - 1;

        Result<InstalledPlatformTemplateDto> installed = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        installed.IsFailure.Should().BeTrue();
        installed.ErrorCode.Should().Be("CHANNEL_BUDGET_EXCEEDED");
        (await _h.Db.SoundClips.CountAsync()).Should().Be(0);
        _store.Blobs.Keys.Should().OnlyContain(k => k.StartsWith("platform/"));
    }

    [Fact]
    public async Task A_second_install_into_the_same_channel_gets_the_next_free_slug()
    {
        Guid assetId = (await UploadAsync("Airhorn", "airhorn.wav", TestAudio.Wav(1))).Value.Id;
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "airhorn",
            Payload("airhorn", assetId, trigger: null)
        );
        Channel channel = await _h.AddChannelAsync("streamer-b");
        PlatformTemplateCatalogService catalog = _h.Catalog(_installer);

        await catalog.InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));
        Result<InstalledPlatformTemplateDto> second = await catalog.InstallAsync(
            _h.CallerUserId,
            channel.Id,
            definitionId,
            new(null)
        );

        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        List<string> names = await _h
            .Db.SoundClips.Select(c => c.Name)
            .OrderBy(n => n)
            .ToListAsync();
        names.Should().Equal("airhorn", "airhorn-2");
    }

    [Fact]
    public async Task Deleting_a_file_a_live_template_names_is_refused_and_the_file_stays()
    {
        Guid assetId = (await UploadAsync("Airhorn", "airhorn.wav", TestAudio.Wav(1))).Value.Id;
        await _h.PublishTemplateAsync(_installer, "airhorn", Payload("airhorn", assetId));

        Result deleted = await _assets.DeleteAsync(_h.ActingPrincipalId, assetId);

        deleted.IsFailure.Should().BeTrue();
        deleted.ErrorCode.Should().Be("ASSET_IN_USE");
        deleted.ErrorMessage.Should().Contain("airhorn");
        PlatformAudioAsset row = await _h.Db.PlatformAudioAssets.SingleAsync();
        _store.Blobs.Should().ContainKey(row.StorageKey);
    }

    [Fact]
    public async Task Retiring_the_last_template_that_names_a_file_removes_the_file_but_not_installed_copies()
    {
        byte[] wav = TestAudio.Wav(2);
        Guid assetId = (await UploadAsync("Airhorn", "airhorn.wav", wav)).Value.Id;
        string platformKey = (await _h.Db.PlatformAudioAssets.SingleAsync()).StorageKey;
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "airhorn",
            Payload("airhorn", assetId)
        );
        Channel channel = await _h.AddChannelAsync("streamer-b");
        await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        Result retired = await _h.AdminService(_installer)
            .RetireDefinitionAsync(_h.ActingPrincipalId, definitionId);

        retired.IsSuccess.Should().BeTrue(retired.ErrorMessage);
        (await _h.Db.PlatformAudioAssets.CountAsync()).Should().Be(0);
        PlatformAudioAsset tombstone = await _h
            .Db.PlatformAudioAssets.IgnoreQueryFilters()
            .SingleAsync();
        tombstone.DeletedAt.Should().NotBeNull();
        _store.Blobs.Should().NotContainKey(platformKey);

        SoundClip clip = await _h.Db.SoundClips.SingleAsync();
        _store
            .Blobs[clip.StorageKey]
            .Should()
            .Equal(wav, "the channel's copy outlives the template");
    }

    [Fact]
    public async Task Retiring_one_of_two_templates_that_name_a_file_keeps_the_file()
    {
        Guid shared = (await UploadAsync("Airhorn", "airhorn.wav", TestAudio.Wav(1))).Value.Id;
        Guid onlyA = (await UploadAsync("Bell", "bell.wav", TestAudio.Wav(1, fill: 0x70))).Value.Id;
        Guid templateA = await _h.PublishTemplateAsync(
            _installer,
            "airhorn-a",
            Payload("airhorn", shared)
        );
        // A later draft of A switches to the bell: every version A ever had counts as naming a file.
        await _h.DraftVersionAsync(_installer, templateA, Payload("bell", onlyA));
        await _h.PublishTemplateAsync(_installer, "airhorn-b", Payload("airhorn", shared));

        Result retired = await _h.AdminService(_installer)
            .RetireDefinitionAsync(_h.ActingPrincipalId, templateA);

        retired.IsSuccess.Should().BeTrue(retired.ErrorMessage);
        List<Guid> live = await _h.Db.PlatformAudioAssets.Select(a => a.Id).ToListAsync();
        live.Should()
            .Equal([shared], "airhorn-b still names the shared file; only A named the bell");
        string sharedKey = (await _h.Db.PlatformAudioAssets.SingleAsync()).StorageKey;
        _store.Blobs.Keys.Should().Equal([sharedKey]);
    }

    private Task<Result<PlatformAudioAssetDto>> UploadAsync(
        string displayName,
        string fileName,
        byte[] bytes
    ) =>
        _assets.UploadAsync(
            _h.ActingPrincipalId,
            new(displayName, fileName, new MemoryStream(bytes))
        );

    private static string Payload(string name, Guid assetId, string? trigger = "horn") =>
        Newtonsoft.Json.JsonConvert.SerializeObject(
            new SoundClipTemplatePayload
            {
                Name = name,
                DisplayName = "Air Horn",
                AssetId = assetId,
                DefaultVolume = 65,
                CooldownSeconds = 30,
                MinPermissionLevel = "Subscriber",
                TriggerWord = trigger,
            }
        );

    public ValueTask DisposeAsync() => _h.DisposeAsync();
}
