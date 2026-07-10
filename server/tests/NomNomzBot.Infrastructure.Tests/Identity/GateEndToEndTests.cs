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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// End-to-end proof of the Gate 1 + Gate 2 combination against the REAL seeded ActionDefinitions catalogue
/// (roles-permissions §7.1) for the three routes fixed in this slice (custom-events.md §5, sound-system.md §5,
/// pronouns.md §5): Gate 1 (<see cref="ChannelAccessService"/>) now admits any authenticated caller into a
/// channel's tenant scope, and Gate 2 (<see cref="ActionAuthorizationService"/> via <see cref="RoleResolver"/>)
/// is what actually separates a community-plane viewer (Everyone(0) floor — let through) from a Moderator
/// trying to clear an Editor(30) floor (still denied).
/// </summary>
public sealed class GateEndToEndTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e0");
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid Moderator = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid Editor = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");
    private static readonly Guid PlainViewer = Guid.Parse("0192a000-0000-7000-8000-0000000000e4");
    private static readonly Guid VipViewer = Guid.Parse("0192a000-0000-7000-8000-0000000000e5");
    private static readonly DateTime Now = new(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(
        ChannelAccessService Gate1,
        ActionAuthorizationService Gate2,
        AuthDbContext Db
    );

    private static async Task<Fixture> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new(Now);
        RoleResolver roleResolver = new(db, clock);
        ActionAuthorizationService gate2 = new(db, roleResolver, new RecordingEventBus(), clock);
        ChannelAccessService gate1 = new(db);

        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Owner,
                TwitchChannelId = "1",
                Name = "ch",
                NameNormalized = "ch",
            }
        );
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = Channel,
                UserId = Moderator,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = ManagementRole.Moderator.ToLevel(),
                Source = MembershipSource.HelixEditors,
                GrantedAt = Now,
            }
        );
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = Channel,
                UserId = Editor,
                ManagementRole = ManagementRole.Editor,
                LevelValue = ManagementRole.Editor.ToLevel(),
                Source = MembershipSource.HelixEditors,
                GrantedAt = Now,
            }
        );
        // A community-plane VIP with NO management membership: effective level = MAX(4, 0, 0) = Vip(4).
        db.ChannelCommunityStandings.Add(
            new ChannelCommunityStanding
            {
                BroadcasterId = Channel,
                UserId = VipViewer,
                Standing = CommunityStanding.Vip,
                LevelValue = CommunityStanding.Vip.ToLevel(),
                Source = StandingSource.EventSubBadge,
            }
        );
        await new ActionDefinitionSeeder(db).SeedAsync();
        await db.SaveChangesAsync();

        return new Fixture(gate1, gate2, db);
    }

    [Theory]
    [InlineData("customdata:write")]
    [InlineData("sounds:write")]
    public async Task Gate1_admits_a_moderator_but_gate2_still_denies_the_editor_floored_write(
        string writeActionKey
    )
    {
        Fixture f = await BuildAsync();

        // Gate 1: entry succeeds for the moderator (a real management relationship on this channel).
        (await f.Gate1.CanResolveTenantAsync(Moderator.ToString(), Channel.ToString()))
            .Should()
            .BeTrue();

        // Gate 2: the moderator's resolved level (10) is below the write action's Editor floor (30) — denied.
        Result<bool> result = await f.Gate2.AuthorizeActionAsync(
            Moderator,
            Channel,
            writeActionKey
        );
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("a Moderator must not clear an Editor floor");
    }

    [Theory]
    [InlineData("customdata:write")]
    [InlineData("sounds:write")]
    public async Task Gate2_allows_the_editor_floored_write_for_an_editor(string writeActionKey)
    {
        Fixture f = await BuildAsync();

        Result<bool> result = await f.Gate2.AuthorizeActionAsync(Editor, Channel, writeActionKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("customdata:read")]
    [InlineData("sounds:read")]
    public async Task Gate2_allows_the_moderator_floored_read_for_a_moderator(string readActionKey)
    {
        Fixture f = await BuildAsync();

        Result<bool> result = await f.Gate2.AuthorizeActionAsync(Moderator, Channel, readActionKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Gate1_admits_a_plain_viewer_with_zero_relationship_and_gate2_allows_the_everyone_floored_pronoun_write()
    {
        Fixture f = await BuildAsync();

        // Gate 1 (the fix under test): a viewer with no owner/moderator/membership/permit row on this channel
        // at all still resolves the tenant — entry is not gated on a management relationship.
        (await f.Gate1.CanResolveTenantAsync(PlainViewer.ToString(), Channel.ToString()))
            .Should()
            .BeTrue();

        // Gate 2: pronouns:self:write floors at Everyone(0); the viewer's resolved level is 0, which clears it.
        Result<bool> result = await f.Gate2.AuthorizeActionAsync(
            PlainViewer,
            Channel,
            "pronouns:self:write"
        );
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("chat:send")]
    [InlineData("commands:builtin:read")]
    [InlineData("moderation:read")]
    public async Task Gate2_allows_the_moderator_floored_keys_for_a_moderator_but_not_a_viewer(
        string moderatorFlooredKey
    )
    {
        Fixture f = await BuildAsync();

        Result<bool> moderatorResult = await f.Gate2.AuthorizeActionAsync(
            Moderator,
            Channel,
            moderatorFlooredKey
        );
        Result<bool> viewerResult = await f.Gate2.AuthorizeActionAsync(
            PlainViewer,
            Channel,
            moderatorFlooredKey
        );

        moderatorResult.Value.Should().BeTrue("the key is seeded at the Moderator(10) floor");
        viewerResult.Value.Should().BeFalse("a plain viewer resolves to level 0");
    }

    [Fact]
    public async Task Gate2_allows_a_vip_a_retiered_read_but_denies_a_destructive_action()
    {
        Fixture f = await BuildAsync();

        // The required proof: a caller whose ONLY standing is community-plane VIP (no management role)
        // clears a re-tiered non-sensitive read but is still denied a Twitch-mutating moderation action.
        Result<bool> allowedRead = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            "commands:read"
        );
        Result<bool> deniedBan = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            "moderation:ban"
        );

        allowedRead.IsSuccess.Should().BeTrue();
        allowedRead
            .Value.Should()
            .BeTrue(
                "commands:read was re-tiered to the Vip(4) floor and the caller's standing is Vip"
            );
        deniedBan.IsSuccess.Should().BeTrue();
        deniedBan
            .Value.Should()
            .BeFalse(
                "moderation:ban stayed at the Moderator(10) floor — a VIP(4) must not clear it"
            );
    }

    [Theory]
    [InlineData("commands:read")]
    [InlineData("pipelines:read")]
    [InlineData("pipelines:validate")]
    [InlineData("eventresponses:read")]
    [InlineData("timers:read")]
    [InlineData("quotes:read")]
    [InlineData("sounds:read")]
    [InlineData("reward:read")]
    [InlineData("music:config:read")]
    [InlineData("tts:voice:read")]
    [InlineData("stream:read")]
    [InlineData("widget:read")]
    [InlineData("chat:read")]
    [InlineData("dashboard:read")]
    public async Task Gate2_allows_every_retiered_read_for_a_vip_but_still_denies_a_plain_viewer(
        string vipReadKey
    )
    {
        Fixture f = await BuildAsync();

        Result<bool> vipResult = await f.Gate2.AuthorizeActionAsync(VipViewer, Channel, vipReadKey);
        Result<bool> viewerResult = await f.Gate2.AuthorizeActionAsync(
            PlainViewer,
            Channel,
            vipReadKey
        );

        vipResult.Value.Should().BeTrue("the read was re-tiered to the Vip(4) floor");
        viewerResult
            .Value.Should()
            .BeFalse("a plain viewer resolves to level 0, still below the Vip(4) floor");
    }

    [Theory]
    [InlineData("moderation:ban")]
    [InlineData("moderation:timeout")]
    [InlineData("moderation:delete_message")]
    [InlineData("chat:send")]
    [InlineData("commands:write")]
    [InlineData("quotes:write")] // bundles DELETE — deliberately NOT lowered
    [InlineData("timers:write")] // bundles DELETE — deliberately NOT lowered
    [InlineData("economy:account:adjust")]
    [InlineData("reward:manage")]
    [InlineData("roles:manage")]
    public async Task Gate2_denies_a_vip_every_action_that_stayed_above_the_vip_floor(
        string protectedKey
    )
    {
        Fixture f = await BuildAsync();

        // Proves the re-tiering lowered ONLY the enumerated non-sensitive reads: every mutating/destructive
        // or write action stayed above Vip(4), so the VIP caller (level 4) is denied each one.
        Result<bool> result = await f.Gate2.AuthorizeActionAsync(VipViewer, Channel, protectedKey);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse($"'{protectedKey}' must stay above the Vip(4) floor");
    }

    [Theory]
    [InlineData("customdata:write")]
    [InlineData("customdata:read")]
    [InlineData("sounds:write")]
    [InlineData("sounds:read")]
    [InlineData("chat:send")]
    [InlineData("stream:read")]
    [InlineData("music:config:read")]
    public async Task Gate1_loosening_does_not_escalate_a_plain_viewer_into_any_management_action(
        string managementActionKey
    )
    {
        Fixture f = await BuildAsync();

        // Gate 1 now lets the viewer in (see above), but Gate 2 must still deny every management-plane action —
        // proving the Gate 1 fix widened ENTRY only, never per-action authorization.
        Result<bool> result = await f.Gate2.AuthorizeActionAsync(
            PlainViewer,
            Channel,
            managementActionKey
        );
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }
}
