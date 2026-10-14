// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Assets.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Assets.Pipeline;
using NomNomzBot.Infrastructure.Discord.Pipeline;
using NomNomzBot.Infrastructure.Identity.Pipeline;
using NomNomzBot.Infrastructure.Rewards.Pipeline;
using NomNomzBot.Infrastructure.Sound.Pipeline;
using NomNomzBot.Infrastructure.Tts.Pipeline;
using NomNomzBot.Infrastructure.Widgets.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Field-content tests for the S-RICH-PICKERS option providers: each asserts the actual <c>Value</c>,
/// <c>Label</c>, <c>SecondaryText</c> and <c>State</c> a real source row maps to — never just list length —
/// plus the tenant-scoping and unavailable-vs-empty behaviour every provider must honor.
/// </summary>
public sealed class PipelineOptionProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private static readonly Guid OtherBroadcaster = Guid.NewGuid();

    private static readonly PaginationParams DefaultPage = new(Page: 1, PageSize: 25);

    // ── reward ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reward_option_carries_cost_and_pause_state()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Reward reward = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Title = "Hydrate!",
            Cost = 500,
            IsPaused = true,
            IsEnabled = true,
        };
        db.Rewards.Add(reward);
        await db.SaveChangesAsync();

        RewardOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.SourceAvailable);
        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal(reward.Id.ToString(), option.Value);
        Assert.Equal("Hydrate!", option.Label);
        Assert.Equal("500 points · paused", option.SecondaryText);
        Assert.Equal(PipelineOptionState.Selectable, option.State);
    }

    [Fact]
    public async Task Disabled_reward_is_unavailable_with_a_reason()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.Rewards.Add(
            new Reward
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Title = "Retired reward",
                IsEnabled = false,
            }
        );
        await db.SaveChangesAsync();

        RewardOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal(PipelineOptionState.Unavailable, option.State);
        Assert.Equal("Reward is disabled.", option.Reason);
    }

    [Fact]
    public async Task Rewards_are_tenant_scoped()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.Rewards.Add(
            new Reward
            {
                Id = Guid.NewGuid(),
                BroadcasterId = OtherBroadcaster,
                Title = "Someone else's reward",
            }
        );
        await db.SaveChangesAsync();

        RewardOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        Assert.True(result.Value.SourceAvailable);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    // ── voice ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Voice_option_carries_locale_gender_and_provider()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.TtsVoices.Add(
            new TtsVoice
            {
                Id = "en-US-JennyNeural",
                Name = "Jenny",
                DisplayName = "Jenny (US)",
                Locale = "en-US",
                Gender = "Female",
                Provider = "azure",
            }
        );
        await db.SaveChangesAsync();

        VoiceOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("en-US-JennyNeural", option.Value);
        Assert.Equal("Jenny (US)", option.Label);
        Assert.Equal("en-US · Female · azure", option.SecondaryText);
    }

    // ── sound_clip ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SoundClip_option_carries_duration()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.SoundClips.Add(
            new SoundClip
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "airhorn",
                DisplayName = "Airhorn",
                StorageKey = "clips/airhorn.mp3",
                MimeType = "audio/mpeg",
                DurationMs = 2500,
                SizeBytes = 4096,
                IsEnabled = true,
                CreatedByUserId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();

        SoundClipOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("Airhorn", option.Label);
        Assert.Equal("2.5s", option.SecondaryText);
    }

    // ── widget ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Widget_option_carries_framework_and_source()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.Widgets.Add(
            new Widget
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "Alert Box",
                Framework = "vue",
                Source = "custom",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        WidgetOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("Alert Box", option.Label);
        Assert.Equal("vue · custom", option.SecondaryText);
    }

    // ── asset ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Asset_option_carries_kind_and_size()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.ChannelAssets.Add(
            new ChannelAsset
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "boot-chime",
                DisplayName = "Boot Chime",
                Kind = "audio",
                MimeType = "audio/wav",
                StorageKey = "assets/boot-chime.wav",
                SizeBytes = 2048,
                CreatedByUserId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();

        AssetOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("Boot Chime", option.Label);
        Assert.Equal("audio · 2.0 KB", option.SecondaryText);
    }

    // ── twitch_user ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TwitchUser_option_carries_display_name_and_differing_login()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Guid viewerUserId = Guid.NewGuid();
        db.Users.Add(
            new User
            {
                Id = viewerUserId,
                TwitchUserId = "123456",
                Username = "grimlock_plays",
                UsernameNormalized = "grimlock_plays",
                DisplayName = "GrimlockPlays",
                ProfileImageUrl = "https://example.test/avatar.png",
            }
        );
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                ViewerUserId = viewerUserId,
                ViewerTwitchUserId = "123456",
                UsernameSnapshot = "grimlock_plays",
                DisplayNameSnapshot = "GrimlockPlays",
                LastSeenAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        TwitchUserOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("123456", option.Value);
        Assert.Equal("GrimlockPlays", option.Label);
        Assert.Equal("@grimlock_plays", option.SecondaryText);
        Assert.Equal("https://example.test/avatar.png", option.ImageUrl);
    }

    [Fact]
    public async Task TwitchUser_options_are_tenant_scoped()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Guid viewerUserId = Guid.NewGuid();
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                Id = Guid.NewGuid(),
                BroadcasterId = OtherBroadcaster,
                ViewerUserId = viewerUserId,
                ViewerTwitchUserId = "999",
                UsernameSnapshot = "someone_else_chatter",
            }
        );
        await db.SaveChangesAsync();

        TwitchUserOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        Assert.Empty(result.Value.Items);
    }

    // ── discord_channel / discord_role: unavailable vs empty ────────────────────

    [Fact]
    public async Task DiscordChannel_reports_unavailable_when_no_active_guild_link()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        DiscordChannelOptionProvider provider = new(db, new ThrowingDirectory());

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceAvailable);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.UnavailableReason));
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task DiscordChannel_reports_unavailable_distinct_from_empty_when_gateway_call_fails()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                GuildId = "guild-1",
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        DiscordChannelOptionProvider provider = new(
            db,
            new FailingDirectory("Discord bot token expired — reconnect the integration.")
        );

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceAvailable);
        Assert.Equal(
            "Discord bot token expired — reconnect the integration.",
            result.Value.UnavailableReason
        );
    }

    [Fact]
    public async Task DiscordChannel_option_carries_type_and_parent_category()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                GuildId = "guild-1",
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        FakeDirectory directory = new(
            channels:
            [
                new DiscordGuildChannelDto("cat-1", "Announcements", 4, null, 0),
                new DiscordGuildChannelDto("chan-1", "go-live", 0, "cat-1", 1),
            ]
        );
        DiscordChannelOptionProvider provider = new(db, directory);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption channelOption = Assert.Single(result.Value.Items, o => o.Value == "chan-1");
        Assert.Equal("go-live", channelOption.Label);
        Assert.Equal("Text · Announcements", channelOption.SecondaryText);
    }

    [Fact]
    public async Task DiscordRole_option_carries_colour_and_mentionable()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                GuildId = "guild-1",
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        FakeDirectory directory = new(
            roles:
            [
                new DiscordGuildRoleDto(
                    "role-1",
                    "VIP",
                    0xFF00FF,
                    1,
                    Managed: false,
                    Mentionable: true
                ),
            ]
        );
        DiscordRoleOptionProvider provider = new(db, directory);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("VIP", option.Label);
        Assert.Equal("#FF00FF · mentionable", option.SecondaryText);
        Assert.Equal(PipelineOptionState.Selectable, option.State);
    }

    [Fact]
    public async Task Managed_discord_role_is_unavailable()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                GuildId = "guild-1",
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        FakeDirectory directory = new(
            roles: [new DiscordGuildRoleDto("role-bot", "MEE6", 0, 0, Managed: true)]
        );
        DiscordRoleOptionProvider provider = new(db, directory);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: null, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal(PipelineOptionState.Unavailable, option.State);
        Assert.NotNull(option.Reason);
    }

    // ── value lookup: the one option a saved value points at ─────────────────────
    // PageSize 1 and a Search that matches nothing prove the lookup ignores both: the wanted option is
    // the second row by sort order, so it would be missing from the first page.

    private static readonly PaginationParams OneRowPage = new(Page: 1, PageSize: 1);

    private static PipelineOptionQuery Lookup(string value) =>
        new(Search: "no-label-contains-this", Value: value, OneRowPage);

    [Fact]
    public async Task Reward_value_lookup_returns_exactly_the_matching_option()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Reward first = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Title = "Alpha",
        };
        Reward wanted = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Title = "Zulu Reward",
            Cost = 100,
            IsEnabled = true,
        };
        db.Rewards.AddRange(first, wanted);
        await db.SaveChangesAsync();
        RewardOptionProvider provider = new(db);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(wanted.Id.ToString())
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(Guid.NewGuid().ToString())
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal(wanted.Id.ToString(), option.Value);
        Assert.Equal("Zulu Reward", option.Label);
        Assert.Equal(1, found.Value.TotalCount);
        Assert.Empty(unknown.Value.Items);
        Assert.True(unknown.Value.SourceAvailable);
    }

    [Fact]
    public async Task Reward_value_lookup_does_not_cross_tenants_or_accept_non_guid_text()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Reward other = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = OtherBroadcaster,
            Title = "Not yours",
        };
        db.Rewards.Add(other);
        await db.SaveChangesAsync();
        RewardOptionProvider provider = new(db);

        Result<PipelineOptionListResult> otherTenant = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(other.Id.ToString())
        );
        Result<PipelineOptionListResult> garbage = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("not-a-guid")
        );

        Assert.Empty(otherTenant.Value.Items);
        Assert.Empty(garbage.Value.Items);
    }

    [Fact]
    public async Task Voice_value_lookup_returns_exactly_the_matching_option()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.TtsVoices.AddRange(
            new TtsVoice
            {
                Id = "de-DE-KatjaNeural",
                Name = "Katja",
                DisplayName = "Aaa Katja",
                Locale = "de-DE",
                Gender = "Female",
                Provider = "azure",
            },
            new TtsVoice
            {
                Id = "en-US-GuyNeural",
                Name = "Guy",
                DisplayName = "Guy (US)",
                Locale = "en-US",
                Gender = "Male",
                Provider = "azure",
            }
        );
        await db.SaveChangesAsync();
        VoiceOptionProvider provider = new(db);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("en-US-GuyNeural")
        );
        Result<PipelineOptionListResult> wrongCase = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("EN-US-GUYNEURAL")
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("xx-XX-Nobody")
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal("en-US-GuyNeural", option.Value);
        Assert.Equal("Guy (US)", option.Label);
        Assert.Equal("en-US · Male · azure", option.SecondaryText);
        Assert.Empty(wrongCase.Value.Items);
        Assert.Empty(unknown.Value.Items);
    }

    [Fact]
    public async Task SoundClip_value_lookup_returns_exactly_the_matching_option()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        SoundClip first = NewClip("Alpha Clip");
        SoundClip wanted = NewClip("Zulu Clip");
        db.SoundClips.AddRange(first, wanted);
        await db.SaveChangesAsync();
        SoundClipOptionProvider provider = new(db);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(wanted.Id.ToString())
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(Guid.NewGuid().ToString())
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal(wanted.Id.ToString(), option.Value);
        Assert.Equal("Zulu Clip", option.Label);
        Assert.Empty(unknown.Value.Items);
    }

    private static SoundClip NewClip(string displayName) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = displayName.ToLowerInvariant().Replace(' ', '-'),
            DisplayName = displayName,
            StorageKey = "clips/x.mp3",
            MimeType = "audio/mpeg",
            DurationMs = 1000,
            SizeBytes = 1,
            IsEnabled = true,
            CreatedByUserId = Guid.NewGuid(),
        };

    [Fact]
    public async Task Widget_value_lookup_returns_exactly_the_matching_option()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Widget first = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "Alpha Widget",
            Framework = "vue",
            Source = "custom",
        };
        Widget wanted = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "Zulu Widget",
            Framework = "react",
            Source = "first_party",
            IsEnabled = true,
        };
        db.Widgets.AddRange(first, wanted);
        await db.SaveChangesAsync();
        WidgetOptionProvider provider = new(db);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(wanted.Id.ToString())
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(Guid.NewGuid().ToString())
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal(wanted.Id.ToString(), option.Value);
        Assert.Equal("Zulu Widget", option.Label);
        Assert.Equal("react · first_party", option.SecondaryText);
        Assert.Empty(unknown.Value.Items);
    }

    [Fact]
    public async Task Asset_value_lookup_returns_exactly_the_matching_option()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ChannelAsset first = NewAsset("Alpha Asset");
        ChannelAsset wanted = NewAsset("Zulu Asset");
        db.ChannelAssets.AddRange(first, wanted);
        await db.SaveChangesAsync();
        AssetOptionProvider provider = new(db);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(wanted.Id.ToString())
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup(Guid.NewGuid().ToString())
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal(wanted.Id.ToString(), option.Value);
        Assert.Equal("Zulu Asset", option.Label);
        Assert.Equal("audio · 2.0 KB", option.SecondaryText);
        Assert.Empty(unknown.Value.Items);
    }

    private static ChannelAsset NewAsset(string displayName) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = displayName.ToLowerInvariant().Replace(' ', '-'),
            DisplayName = displayName,
            Kind = "audio",
            MimeType = "audio/wav",
            StorageKey = "assets/x.wav",
            SizeBytes = 2048,
            CreatedByUserId = Guid.NewGuid(),
        };

    [Fact]
    public async Task TwitchUser_value_lookup_returns_exactly_the_matching_viewer_by_user_id()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.ViewerProfiles.AddRange(
            NewProfile("111", "recent_viewer", "RecentViewer", DateTime.UtcNow),
            NewProfile("222", "old_viewer", "OldViewer", DateTime.UtcNow.AddDays(-30))
        );
        await db.SaveChangesAsync();
        TwitchUserOptionProvider provider = new(db);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("222")
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("333")
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal("222", option.Value);
        Assert.Equal("OldViewer", option.Label);
        Assert.Equal("@old_viewer", option.SecondaryText);
        Assert.Empty(unknown.Value.Items);
    }

    private static ViewerProfile NewProfile(
        string twitchUserId,
        string login,
        string displayName,
        DateTime lastSeenAt
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            ViewerUserId = Guid.NewGuid(),
            ViewerTwitchUserId = twitchUserId,
            UsernameSnapshot = login,
            DisplayNameSnapshot = displayName,
            LastSeenAt = lastSeenAt,
        };

    [Fact]
    public async Task DiscordChannel_value_lookup_returns_exactly_the_matching_option()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        await AddActiveGuildAsync(db);
        FakeDirectory directory = new(
            channels:
            [
                new DiscordGuildChannelDto("chan-1", "general", 0, null, 0),
                new DiscordGuildChannelDto("chan-2", "go-live", 0, null, 1),
            ]
        );
        DiscordChannelOptionProvider provider = new(db, directory);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("chan-2")
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("chan-9")
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal("chan-2", option.Value);
        Assert.Equal("go-live", option.Label);
        Assert.Equal(1, found.Value.TotalCount);
        Assert.Empty(unknown.Value.Items);
        Assert.True(unknown.Value.SourceAvailable);
    }

    [Fact]
    public async Task DiscordRole_value_lookup_returns_exactly_the_matching_option()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        await AddActiveGuildAsync(db);
        FakeDirectory directory = new(
            roles:
            [
                new DiscordGuildRoleDto("role-1", "Moderator", 0x00FF00, 5, Managed: false),
                new DiscordGuildRoleDto(
                    "role-2",
                    "VIP",
                    0xFF00FF,
                    1,
                    Managed: false,
                    Mentionable: true
                ),
            ]
        );
        DiscordRoleOptionProvider provider = new(db, directory);

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("role-2")
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            Lookup("role-9")
        );

        PipelineOption option = Assert.Single(found.Value.Items);
        Assert.Equal("role-2", option.Value);
        Assert.Equal("VIP", option.Label);
        Assert.Equal("#FF00FF · mentionable", option.SecondaryText);
        Assert.Empty(unknown.Value.Items);
    }

    private static async Task AddActiveGuildAsync(PipelineOptionsTestDbContext db)
    {
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                GuildId = "guild-1",
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
            }
        );
        await db.SaveChangesAsync();
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class ThrowingDirectory : IDiscordGuildDirectoryService
    {
        public Task<Result<DiscordGuildInfoDto>> GetGuildAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Should not be reached — no active connection.");

        public Task<Result<IReadOnlyList<DiscordGuildRoleDto>>> GetGuildRolesAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Should not be reached — no active connection.");

        public Task<Result<IReadOnlyList<DiscordGuildChannelDto>>> GetGuildChannelsAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Should not be reached — no active connection.");

        public Task<Result<IReadOnlyList<DiscordAssignableRoleDto>>> GetAssignableGuildRolesAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Should not be reached — no active connection.");

        public Task<Result<IReadOnlyList<DiscordPostableChannelDto>>> GetPostableGuildChannelsAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Should not be reached — no active connection.");
    }

    private sealed class FailingDirectory : IDiscordGuildDirectoryService
    {
        private readonly string _message;

        public FailingDirectory(string message) => _message = message;

        public Task<Result<DiscordGuildInfoDto>> GetGuildAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<DiscordGuildInfoDto>(_message));

        public Task<Result<IReadOnlyList<DiscordGuildRoleDto>>> GetGuildRolesAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<IReadOnlyList<DiscordGuildRoleDto>>(_message));

        public Task<Result<IReadOnlyList<DiscordGuildChannelDto>>> GetGuildChannelsAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<IReadOnlyList<DiscordGuildChannelDto>>(_message));

        public Task<Result<IReadOnlyList<DiscordAssignableRoleDto>>> GetAssignableGuildRolesAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<IReadOnlyList<DiscordAssignableRoleDto>>(_message));

        public Task<Result<IReadOnlyList<DiscordPostableChannelDto>>> GetPostableGuildChannelsAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<IReadOnlyList<DiscordPostableChannelDto>>(_message));
    }

    private sealed class FakeDirectory : IDiscordGuildDirectoryService
    {
        private readonly IReadOnlyList<DiscordGuildRoleDto> _roles;
        private readonly IReadOnlyList<DiscordGuildChannelDto> _channels;

        public FakeDirectory(
            IReadOnlyList<DiscordGuildRoleDto>? roles = null,
            IReadOnlyList<DiscordGuildChannelDto>? channels = null
        )
        {
            _roles = roles ?? [];
            _channels = channels ?? [];
        }

        public Task<Result<DiscordGuildInfoDto>> GetGuildAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success(new DiscordGuildInfoDto("guild-1", "Test Guild", null, null))
            );

        public Task<Result<IReadOnlyList<DiscordGuildRoleDto>>> GetGuildRolesAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(_roles));

        public Task<Result<IReadOnlyList<DiscordGuildChannelDto>>> GetGuildChannelsAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(_channels));

        public Task<Result<IReadOnlyList<DiscordAssignableRoleDto>>> GetAssignableGuildRolesAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success<IReadOnlyList<DiscordAssignableRoleDto>>([
                    .. _roles.Select(r => new DiscordAssignableRoleDto(
                        r.Id,
                        r.Name,
                        r.Color,
                        r.Position,
                        r.Managed,
                        r.Mentionable,
                        r.Permissions,
                        CanAssign: true,
                        UnavailableReasonCode: null,
                        UnavailableReason: null
                    )),
                ])
            );

        public Task<Result<IReadOnlyList<DiscordPostableChannelDto>>> GetPostableGuildChannelsAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success<IReadOnlyList<DiscordPostableChannelDto>>([
                    .. _channels.Select(c => new DiscordPostableChannelDto(
                        c.Id,
                        c.Name,
                        c.Type,
                        c.ParentId,
                        c.Position,
                        CanPost: true,
                        UnavailableReasonCode: null,
                        UnavailableReason: null
                    )),
                ])
            );
    }
}
