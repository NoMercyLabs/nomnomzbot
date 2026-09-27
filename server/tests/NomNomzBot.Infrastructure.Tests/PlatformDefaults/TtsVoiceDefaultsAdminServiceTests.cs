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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Content.Tts;
using NomNomzBot.Infrastructure.PlatformDefaults;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PlatformDefaults;

/// <summary>
/// Plan item A4, family 4: the platform default TTS voice is the catalogue's flagged voice, and a channel that
/// never picked one reads (and speaks) it. Proves an admin change moves what a following channel reads, never
/// touches a channel with its own pick, survives the seeder's next run, counts the blast radius right, refuses
/// a stale count, an unknown voice and a keyed-provider voice, is read back and audited; and that a channel's
/// own pick takes it off the default while the follow flag puts it back.
/// </summary>
public sealed class TtsVoiceDefaultsAdminServiceTests
{
    private const string Aria = "en-US-AriaNeural";
    private const string Guy = "en-US-GuyNeural";
    private const string Sonia = "en-GB-SoniaNeural";
    private static readonly Guid Follower = Guid.Parse("0199f200-0000-7000-8000-00000000c001");
    private static readonly Guid OwnChannel = Guid.Parse("0199f200-0000-7000-8000-00000000c002");
    private static readonly Guid Admin = Guid.Parse("0199f200-0000-7000-8000-00000000c003");
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        AuthDbContext Db,
        TtsVoiceDefaultsAdminService Sut,
        TtsConfigService Channels
    );

    private static async Task<Harness> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.AddRange(NewChannel(Follower, "alpha"), NewChannel(OwnChannel, "bravo"));
        await db.SaveChangesAsync();
        await new TtsVoiceSeeder(db).SeedAsync();

        // bravo picked Guy for itself; alpha has no config row at all (a brand-new channel).
        db.TtsConfigs.Add(new() { BroadcasterId = OwnChannel, DefaultVoiceId = Guy });
        await db.SaveChangesAsync();

        TtsVoiceDefaultsAdminService sut = new(db, new FakeTimeProvider(Now));
        TtsConfigService channels = new(
            db,
            Substitute.For<ITtsService>(),
            Substitute.For<IEventBus>(),
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<IUserService>(),
            new PlatformTtsVoiceDefault(db)
        );
        return new(db, sut, channels);
    }

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = id,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = name + "-ext",
            Name = name,
            NameNormalized = name,
            Status = AuthEnums.ChannelStatus.Active,
        };

    [Fact]
    public async Task A_following_channel_reads_the_platform_default_and_an_own_pick_wins()
    {
        Harness h = await BuildAsync();

        TtsConfigDto alpha = (await h.Channels.GetConfigAsync(Follower)).Value;
        TtsConfigDto bravo = (await h.Channels.GetConfigAsync(OwnChannel)).Value;

        alpha.DefaultVoiceId.Should().Be(Aria, "the shipped default is the seeded flag");
        alpha.FollowsPlatformDefaultVoice.Should().BeTrue();
        bravo.DefaultVoiceId.Should().Be(Guy);
        bravo.FollowsPlatformDefaultVoice.Should().BeFalse();
    }

    [Fact]
    public async Task An_admin_change_moves_the_following_channel_and_leaves_an_own_pick_alone()
    {
        Harness h = await BuildAsync();

        Result<TtsVoiceDefaultDto> saved = await h.Sut.SetAsync(
            new(Sonia, ConfirmedChannelsAffected: 1),
            Admin
        );
        TtsConfigDto alpha = (await h.Channels.GetConfigAsync(Follower)).Value;
        TtsConfigDto bravo = (await h.Channels.GetConfigAsync(OwnChannel)).Value;

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        alpha.DefaultVoiceId.Should().Be(Sonia);
        bravo.DefaultVoiceId.Should().Be(Guy);
    }

    [Fact]
    public async Task The_saved_default_is_read_back_audited_and_survives_a_re_seed()
    {
        Harness h = await BuildAsync();

        TtsVoiceDefaultDto saved = (
            await h.Sut.SetAsync(new(Sonia, ConfirmedChannelsAffected: 1), Admin)
        ).Value;
        await new TtsVoiceSeeder(h.Db).SeedAsync();

        saved.VoiceId.Should().Be(Sonia);
        saved.DisplayName.Should().Be("Sonia (GB)");
        saved.Provider.Should().Be("edge");
        saved.ChannelsFollowing.Should().Be(1);
        saved.ChannelsWithOwnVoice.Should().Be(1);
        List<string> flagged = await h
            .Db.TtsVoices.AsNoTracking()
            .Where(v => v.IsDefault)
            .Select(v => v.Id)
            .ToListAsync();
        flagged
            .Should()
            .ContainSingle("exactly one default, and the seeder never moves it back")
            .Which.Should()
            .Be(Sonia);
        IamAuditLog audit = await h.Db.IamAuditLogs.SingleAsync();
        audit.Permission.Should().Be("platform_default:tts_voice");
        audit.TargetResource.Should().Be("default_voice");
        audit.Justification.Should().Be($"old=voice={Aria};new=voice={Sonia}");
        audit.AffectedTenantCount.Should().Be(1);
        (await h.Sut.GetAsync()).Value.VoiceId.Should().Be(Sonia);
    }

    [Fact]
    public async Task Blast_radius_counts_only_channels_that_follow_and_a_stale_count_is_refused()
    {
        Harness h = await BuildAsync();

        PlatformDefaultBlastRadiusDto radius = (await h.Sut.PreviewAsync(new(Sonia))).Value;
        Result<TtsVoiceDefaultDto> stale = await h.Sut.SetAsync(
            new(Sonia, ConfirmedChannelsAffected: 2),
            Admin
        );
        PlatformDefaultBlastRadiusDto unchanged = (await h.Sut.PreviewAsync(new(Aria))).Value;

        radius.ChannelsAffected.Should().Be(1);
        radius.ChannelsKeepingOwnSetting.Should().Be(1);
        radius.SampleChannelNames.Should().Equal("alpha");
        stale.ErrorCode.Should().Be("PREVIEW_STALE");
        (await h.Sut.GetAsync()).Value.VoiceId.Should().Be(Aria, "a refused save changes nothing");
        unchanged.ChannelsAffected.Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_voice_and_a_keyed_provider_voice_are_refused()
    {
        Harness h = await BuildAsync();
        h.Db.TtsVoices.Add(
            new()
            {
                Id = "eleven-rachel",
                Name = "Rachel",
                DisplayName = "Rachel",
                Locale = "en-US",
                Gender = "Female",
                Provider = "elevenlabs",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<PlatformDefaultBlastRadiusDto> unknown = await h.Sut.PreviewAsync(
            new("no-such-voice")
        );
        Result<TtsVoiceDefaultDto> keyed = await h.Sut.SetAsync(
            new("eleven-rachel", ConfirmedChannelsAffected: 1),
            Admin
        );

        unknown.ErrorCode.Should().Be("NOT_FOUND");
        keyed.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await h.Sut.GetAsync()).Value.VoiceId.Should().Be(Aria);
        (await h.Db.IamAuditLogs.CountAsync()).Should().Be(0, "a refused change is not audited");
    }

    [Fact]
    public async Task A_channel_pick_leaves_the_default_and_the_follow_flag_puts_it_back()
    {
        Harness h = await BuildAsync();

        TtsConfigDto own = (
            await h.Channels.UpdateConfigAsync(Follower, new() { DefaultVoiceId = Guy })
        ).Value;
        TtsConfigDto back = (
            await h.Channels.UpdateConfigAsync(
                Follower,
                new() { FollowPlatformDefaultVoice = true }
            )
        ).Value;
        TtsConfig stored = await h
            .Db.TtsConfigs.AsNoTracking()
            .SingleAsync(c => c.BroadcasterId == Follower);

        own.DefaultVoiceId.Should().Be(Guy);
        own.FollowsPlatformDefaultVoice.Should().BeFalse();
        back.DefaultVoiceId.Should().Be(Aria);
        back.FollowsPlatformDefaultVoice.Should().BeTrue();
        stored.DefaultVoiceId.Should().BeNull("following is stored as no pick of its own");
    }
}
