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
using NomNomzBot.Application.Contracts.Discord;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Providers;

/// <summary>
/// REQUIREMENT: the Discord integration must expose what the Discord bot API allows a streaming bot to manage —
/// guilds, channels, roles, member-role assignment, and message delivery. HARD project rule
/// (external-api-full-management-coverage): whatever the Discord API lets the bot do, the bot must let the
/// operator manage. These tests enumerate the Discord service surface that EXISTS (reflection) and compare it
/// to the capability set the Discord REST API supports. A red is a Discord capability still to wire.
/// </summary>
public sealed class DiscordCoverageTests
{
    private static readonly Type[] DiscordServices =
    [
        typeof(IDiscordBotGateway),
        typeof(IDiscordGuildService),
        typeof(IDiscordGuildDirectoryService),
        typeof(IDiscordNotificationRoleService),
        typeof(IDiscordNotificationConfigService),
        typeof(IDiscordNotificationDispatcher),
    ];

    [Fact]
    public void Discord_services_cover_the_core_guild_channel_role_message_surface()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(DiscordServices);

        (string Capability, string[] Keywords)[] expected =
        [
            ("Get guild", ["GetGuild"]),
            ("List guild roles", ["GetGuildRoles"]),
            ("List guild channels", ["GetGuildChannels"]),
            ("Post channel message", ["PostMessage"]),
            ("Post interactive/button message", ["PostButtonMessage"]),
            ("Open DM channel", ["OpenDmChannel"]),
            ("Add member role", ["AddMemberRole"]),
            ("Remove member role", ["RemoveMemberRole"]),
            ("Create managed role", ["CreateRole"]),
            ("Update managed role", ["UpdateRole"]),
            ("Delete managed role", ["DeleteRole"]),
        ];

        List<string> missing = expected
            .Where(capability => !methods.Covers(capability.Keywords))
            .Select(capability => capability.Capability)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the bot must expose the core Discord guild/channel/role/message management surface"
            );
    }

    [Fact]
    public void Discord_services_cover_the_full_documented_management_surface()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(DiscordServices);

        // Discord bot-API capabilities a streaming bot is expected to manage but the seam does not yet expose.
        // These reds are the concrete Discord coverage backlog.
        (string Capability, string[] Keywords)[] expected =
        [
            (
                "Edit a posted message (PATCH /channels/{id}/messages/{id})",
                ["EditMessage", "UpdateMessage"]
            ),
            ("Delete a posted message (DELETE /channels/{id}/messages/{id})", ["DeleteMessage"]),
            ("Manage guild/channel webhooks (create/list/execute)", ["Webhook"]),
            (
                "List guild members (GET /guilds/{id}/members)",
                ["GetGuildMembers", "ListGuildMembers"]
            ),
            (
                "Create a guild channel (POST /guilds/{id}/channels)",
                ["CreateGuildChannel", "CreateChannel"]
            ),
        ];

        List<string> missing = expected
            .Where(capability => !methods.Covers(capability.Keywords))
            .Select(capability => capability.Capability)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the Discord API lets the bot do these; full-management-coverage requires the bot to expose them — "
                    + $"missing: [{string.Join(", ", missing)}]"
            );
    }
}
