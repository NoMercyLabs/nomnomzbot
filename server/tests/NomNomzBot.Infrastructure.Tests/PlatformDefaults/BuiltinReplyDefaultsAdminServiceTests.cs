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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.PlatformDefaults;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PlatformDefaults;

/// <summary>
/// Plan item A4, family 3: built-in reply texts. Proves an admin text changes what the composer actually says
/// for a channel without its own override (whatever its personality tone), a channel's own override keeps
/// winning on a slot that takes one, clearing restores the shipped wording, the blast radius counts only the
/// channels the override really protects, a stale count is refused, and the save is audited and read back.
/// </summary>
public sealed class BuiltinReplyDefaultsAdminServiceTests
{
    private const string Key = BuiltinResponseSlots.Uptime.Key;
    private const string Live = BuiltinResponseSlots.Uptime.Live;
    private const string Offline = BuiltinResponseSlots.Uptime.Offline;
    private static readonly Guid Follower = Guid.Parse("0199f300-0000-7000-8000-00000000c001");
    private static readonly Guid OwnChannel = Guid.Parse("0199f300-0000-7000-8000-00000000c002");
    private static readonly Guid Admin = Guid.Parse("0199f300-0000-7000-8000-00000000c003");

    private sealed record Harness(
        AuthDbContext Db,
        BuiltinReplyDefaultsAdminService Sut,
        ITemplateResolver Templates,
        PlatformBuiltinReplyDefaultsReader Reader
    );

    private static async Task<Harness> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.AddRange(NewChannel(Follower, "alpha"), NewChannel(OwnChannel, "bravo"));
        db.ChannelBuiltinCommands.Add(
            new()
            {
                BroadcasterId = OwnChannel,
                BuiltinKey = "!uptime",
                OverridesJson = """{ "responseTemplate": "bravo has been live {uptime}" }""",
            }
        );
        await db.SaveChangesAsync();

        PlatformBuiltinReplyDefaultsReader reader = new(
            new SingleContextScopeFactory(db),
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );
        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.ArgAt<string>(0));
        return new(
            db,
            new(db, reader, new FakeTimeProvider(DateTimeOffset.UtcNow)),
            templates,
            reader
        );
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

    private static Task<string> ComposeAsync(Harness h, string slot, string? channelOverride)
    {
        FakeChannelBuiltinReplies own = channelOverride is null
            ? FakeChannelBuiltinReplies.None
            : new FakeChannelBuiltinReplies().Set(Follower, Key, slot, channelOverride);
        return new BuiltinResponseComposer(h.Templates, h.Reader, own).ComposeAsync(
            new()
            {
                BroadcasterId = Follower,
                Personality = PersonalityTone.Sassy,
                BuiltinKey = Key,
                Slot = slot,
                NeutralFallback = "neutral",
            }
        );
    }

    [Fact]
    public async Task An_admin_text_replaces_the_tone_lines_but_a_channel_override_still_wins()
    {
        Harness h = await BuildAsync();
        (await ComposeAsync(h, Live, null))
            .Should()
            .BeOneOf(ToneTemplateCatalog.Get(PersonalityTone.Sassy, Key, Live));

        Result<BuiltinReplyDefaultDto> saved = await h.Sut.SetAsync(
            Key,
            Live,
            new("On air for {uptime}.", ConfirmedChannelsAffected: 1),
            Admin
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        (await ComposeAsync(h, Live, null)).Should().Be("On air for {uptime}.");
        (await ComposeAsync(h, Live, "bravo has been live {uptime}"))
            .Should()
            .Be("bravo has been live {uptime}");
    }

    [Fact]
    public async Task Clearing_restores_the_shipped_wording_and_the_save_is_audited_and_read_back()
    {
        Harness h = await BuildAsync();
        BuiltinReplyDefaultDto set = (
            await h.Sut.SetAsync(Key, Live, new("On air {uptime}", 1), Admin)
        ).Value;

        BuiltinReplyDefaultDto cleared = (
            await h.Sut.SetAsync(Key, Live, new(null, 1), Admin)
        ).Value;

        set.PlatformTemplate.Should().Be("On air {uptime}");
        set.ShippedTemplate.Should().Be(ToneTemplateCatalog.ShippedTemplate(Key, Live));
        set.ChannelsWithOwnReply.Should().Be(1);
        cleared.PlatformTemplate.Should().BeNull();
        (await h.Db.PlatformBuiltinReplyDefaults.CountAsync()).Should().Be(0);
        (await ComposeAsync(h, Live, null))
            .Should()
            .BeOneOf(ToneTemplateCatalog.Get(PersonalityTone.Sassy, Key, Live));
        List<IamAuditLog> audit = await h.Db.IamAuditLogs.OrderBy(a => a.Id).ToListAsync();
        audit
            .Select(a => a.Justification)
            .Should()
            .Equal("old=(shipped);new=On air {uptime}", "old=On air {uptime};new=(shipped)");
        audit.Should().OnlyContain(a => a.Permission == "platform_default:builtin_reply");
    }

    [Fact]
    public async Task The_override_protects_only_the_slots_that_receive_it()
    {
        Harness h = await BuildAsync();

        PlatformDefaultBlastRadiusDto live = (
            await h.Sut.PreviewAsync(Key, Live, new("x {uptime}"))
        ).Value;
        PlatformDefaultBlastRadiusDto offline = (
            await h.Sut.PreviewAsync(Key, Offline, new("offline now"))
        ).Value;

        live.ChannelsAffected.Should().Be(1, "bravo's own override keeps the live reply");
        live.ChannelsKeepingOwnSetting.Should().Be(1);
        live.SampleChannelNames.Should().Equal("alpha");
        offline.ChannelsAffected.Should().Be(2, "no channel override reaches the offline reply");
        offline.ChannelsKeepingOwnSetting.Should().Be(0);
    }

    [Fact]
    public async Task A_stale_count_or_an_unknown_slot_is_refused_and_nothing_is_written()
    {
        Harness h = await BuildAsync();

        Result<BuiltinReplyDefaultDto> stale = await h.Sut.SetAsync(
            Key,
            Offline,
            new("offline now", ConfirmedChannelsAffected: 1),
            Admin
        );
        Result<BuiltinReplyDefaultDto> unknown = await h.Sut.SetAsync(
            Key,
            "nonsense",
            new("x", 0),
            Admin
        );

        stale.ErrorCode.Should().Be("PREVIEW_STALE");
        unknown.ErrorCode.Should().Be("NOT_FOUND");
        (await h.Db.PlatformBuiltinReplyDefaults.CountAsync()).Should().Be(0);
        (await h.Db.IamAuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_list_offers_every_catalogued_slot_with_its_shipped_wording()
    {
        Harness h = await BuildAsync();

        IReadOnlyList<BuiltinReplyDefaultDto> rows = (await h.Sut.ListAsync()).Value;

        rows.Select(r => (r.BuiltinKey, r.Slot)).Should().Equal(ToneTemplateCatalog.AllSlots());
        BuiltinReplyDefaultDto live = rows.Single(r => r.BuiltinKey == Key && r.Slot == Live);
        live.ShippedTemplate.Should().NotBeNullOrWhiteSpace();
        live.PlatformTemplate.Should().BeNull();
        live.ChannelsWithOwnReply.Should()
            .Be(1, "bravo's legacy single override still counts for the live slot it fed");
        rows.Single(r => r.BuiltinKey == Key && r.Slot == Offline)
            .ChannelsWithOwnReply.Should()
            .Be(0);
    }

    private sealed class SingleContextScopeFactory(IApplicationDbContext db) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(db);

        private sealed class Scope(IApplicationDbContext db) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(IApplicationDbContext) ? db : null;

            public void Dispose() { }
        }
    }
}
