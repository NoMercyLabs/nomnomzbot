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
    public async Task A_vip_is_denied_a_lowerable_read_by_default_but_allowed_once_the_broadcaster_lowers_it()
    {
        Fixture f = await BuildAsync();

        // (a) Out of the box commands:read DEFAULTS to the Moderator(10) base — a caller whose only standing is
        // community-plane VIP(4) does NOT clear it. Lowering the floor granted the VIP nothing on its own.
        Result<bool> beforeOverride = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            "commands:read"
        );
        beforeOverride.IsSuccess.Should().BeTrue();
        beforeOverride
            .Value.Should()
            .BeFalse("commands:read defaults to the Moderator(10) base — a VIP(4) is below it");

        // (b) The broadcaster CHOOSES to lower commands:read to its Vip(4) floor via the override machinery.
        Result<int> lowered = await f.Gate2.SetActionOverrideAsync(
            Channel,
            "commands:read",
            4,
            Owner
        );
        lowered
            .IsSuccess.Should()
            .BeTrue("Vip(4) is at commands:read's floor, so the override is accepted");
        lowered.Value.Should().Be(4);

        // …and now the same VIP clears it.
        Result<bool> afterOverride = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            "commands:read"
        );
        afterOverride
            .Value.Should()
            .BeTrue("the broadcaster lowered commands:read to the Vip(4) floor the caller sits at");
    }

    [Fact]
    public async Task A_broadcaster_cannot_lower_a_destructive_action_below_its_moderator_floor()
    {
        Fixture f = await BuildAsync();

        // (c) moderation:ban is Twitch-mutating and irreversible-if-abused — its floor stays Moderator(10). The
        // override machinery REJECTS any attempt to drop it to Vip(4), so a VIP can never be handed the ban.
        Result<int> rejected = await f.Gate2.SetActionOverrideAsync(
            Channel,
            "moderation:ban",
            4,
            Owner
        );
        rejected.IsSuccess.Should().BeFalse("Vip(4) is below moderation:ban's Moderator(10) floor");
        rejected.ErrorMessage.Should().Contain("floor");

        // And with no override taking effect, the VIP is still denied the ban.
        Result<bool> deniedBan = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            "moderation:ban"
        );
        deniedBan
            .Value.Should()
            .BeFalse(
                "moderation:ban stayed at the Moderator(10) floor — a VIP(4) must not clear it"
            );
    }

    [Theory]
    [InlineData("commands:read")]
    [InlineData("commands:builtin:read")]
    [InlineData("pipelines:read")]
    [InlineData("pipelines:validate")]
    [InlineData("eventresponses:read")]
    [InlineData("timers:read")]
    [InlineData("quotes:read")]
    [InlineData("sounds:read")]
    [InlineData("reward:read")]
    [InlineData("music:config:read")]
    [InlineData("tts:config:read")]
    [InlineData("tts:voice:read")]
    [InlineData("stream:read")]
    [InlineData("widget:read")]
    [InlineData("chat:read")]
    [InlineData("dashboard:read")]
    [InlineData("quotes:write")] // curating a quote (add/edit) — the one non-destructive write with a VIP floor
    public async Task A_lowerable_action_defaults_to_moderator_and_the_broadcaster_can_open_it_to_a_vip(
        string lowerableKey
    )
    {
        Fixture f = await BuildAsync();

        // By default the action sits at its Moderator(10) base — the VIP(4) is denied and the broadcaster gets
        // nothing extra out of the box.
        Result<bool> vipByDefault = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            lowerableKey
        );
        vipByDefault
            .Value.Should()
            .BeFalse(
                $"'{lowerableKey}' defaults to the Moderator(10) base until the broadcaster lowers it"
            );

        // The broadcaster may lower it to the Vip(4) floor (the override is accepted because Vip is the floor).
        Result<int> lowered = await f.Gate2.SetActionOverrideAsync(Channel, lowerableKey, 4, Owner);
        lowered
            .IsSuccess.Should()
            .BeTrue($"'{lowerableKey}' has a Vip(4) floor the broadcaster may lower to");

        // Now the VIP clears it, but a plain viewer (level 0) still does not.
        Result<bool> vipAfter = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            lowerableKey
        );
        Result<bool> viewerAfter = await f.Gate2.AuthorizeActionAsync(
            PlainViewer,
            Channel,
            lowerableKey
        );
        vipAfter
            .Value.Should()
            .BeTrue($"the broadcaster lowered '{lowerableKey}' to the Vip(4) floor");
        viewerAfter
            .Value.Should()
            .BeFalse("a plain viewer resolves to level 0, still below the Vip(4) floor");
    }

    [Theory]
    [InlineData("moderation:ban")]
    [InlineData("moderation:timeout")]
    [InlineData("moderation:delete_message")]
    [InlineData("chat:send")]
    [InlineData("commands:write")]
    [InlineData("quotes:delete")] // deleting a quote stays Moderator — a VIP can add/edit, not wipe the book
    [InlineData("timers:write")] // bundles DELETE — deliberately NOT lowered
    [InlineData("economy:account:adjust")]
    [InlineData("reward:manage")]
    [InlineData("roles:manage")]
    public async Task A_protected_action_denies_a_vip_and_cannot_be_lowered_to_the_vip_floor(
        string protectedKey
    )
    {
        Fixture f = await BuildAsync();

        // The floor stayed at Moderator+ because abusing the action causes real / irreversible harm: the VIP
        // caller (level 4) is denied by default…
        Result<bool> vipByDefault = await f.Gate2.AuthorizeActionAsync(
            VipViewer,
            Channel,
            protectedKey
        );
        vipByDefault.IsSuccess.Should().BeTrue();
        vipByDefault.Value.Should().BeFalse($"'{protectedKey}' must stay above the Vip(4) floor");

        // …and the broadcaster CANNOT lower it to Vip(4): the override machinery rejects a level below the floor,
        // so there is no path by which a VIP is ever handed one of these actions.
        Result<int> rejected = await f.Gate2.SetActionOverrideAsync(
            Channel,
            protectedKey,
            4,
            Owner
        );
        rejected
            .IsSuccess.Should()
            .BeFalse($"'{protectedKey}' has a floor above Vip(4) and must reject a VIP override");
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
