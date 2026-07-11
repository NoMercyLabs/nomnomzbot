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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <see cref="ChannelBotController.GetScopes"/> is the white-label BOT account's chat-permission page,
/// not the streamer's — the signal behind the dashboard's bot "action required" prompt.
/// <list type="bullet">
///   <item>A completed bot grant (the <c>twitch_bot</c> connection holding the bot chat scopes) reads
///     100% so the prompt clears.</item>
///   <item>A bot connection missing a genuinely-required Helix chat scope reads incomplete.</item>
///   <item>The self-host fallback — only the streamer's own <c>twitch</c> connection, holding the Helix chat
///     scopes but not the legacy IRC scopes — still reads complete, because the IRC scopes are de-required.</item>
///   <item>The dedicated <c>twitch_bot</c> connection is preferred over the streamer's <c>twitch</c> one.</item>
/// </list>
/// </summary>
public sealed class ChannelBotControllerScopesTests
{
    private const string ChannelId = "0192a000-0000-7000-8000-00000000b07a";

    // The scopes the bot OAuth flow actually grants (AuthService.BotScopes).
    private const string HelixRead = "user:read:chat";
    private const string HelixWrite = "user:write:chat";
    private const string LegacyIrcRead = "chat:read";
    private const string LegacyIrcEdit = "chat:edit";
    private const string WhisperRead = "user:read:whispers";

    [Fact]
    public async Task GetScopes_CompletedBotGrant_ReadsHundredPercentAndClearsPrompt()
    {
        // A completed bot OAuth: the twitch_bot connection holds every scope the flow requests.
        ChannelBotController controller = Build(
            Conn("twitch_bot", HelixRead, HelixWrite, LegacyIrcRead, LegacyIrcEdit)
        );

        ChannelBotController.ScopesResponseDto data = await GetScopesData(controller);

        // The prompt fires while granted < total; a completed grant must read granted == total.
        data.GrantedCount.Should().Be(data.TotalCount);
        // Only the two required Helix chat scopes gate the prompt.
        data.TotalCount.Should().Be(2);
        data.GrantedCount.Should().Be(2);

        // The required Helix scopes are reported granted...
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == HelixRead)
            .Which.Should()
            .BeEquivalentTo(new { Granted = true, Required = true });
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == HelixWrite)
            .Which.Should()
            .BeEquivalentTo(new { Granted = true, Required = true });

        // ...and the legacy IRC scopes are listed for transparency but de-required (never gate the prompt).
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == LegacyIrcRead)
            .Which.Required.Should()
            .BeFalse();
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == LegacyIrcEdit)
            .Which.Required.Should()
            .BeFalse();
    }

    [Fact]
    public async Task GetScopes_BotGrantMissingRequiredScope_ReadsIncomplete()
    {
        // The bot connection is missing the Helix send scope — a genuine "action required".
        ChannelBotController controller = Build(
            Conn("twitch_bot", HelixRead, LegacyIrcRead, LegacyIrcEdit)
        );

        ChannelBotController.ScopesResponseDto data = await GetScopesData(controller);

        data.GrantedCount.Should().BeLessThan(data.TotalCount);
        data.TotalCount.Should().Be(2);
        data.GrantedCount.Should().Be(1);
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == HelixWrite)
            .Which.Granted.Should()
            .BeFalse();
    }

    [Fact]
    public async Task GetScopes_SelfHostFallback_StreamerTokenWithoutIrcScopes_ReadsComplete()
    {
        // Self-host: no dedicated bot connection. The bot chats as the streamer's OWN account, whose twitch
        // connection carries the Helix chat scopes (RequiredScopes) but NOT the legacy IRC scopes.
        ChannelBotController controller = Build(Conn("twitch", HelixRead, HelixWrite));

        ChannelBotController.ScopesResponseDto data = await GetScopesData(controller);

        // De-requiring the IRC scopes is exactly what lets this read complete.
        data.GrantedCount.Should().Be(data.TotalCount);
        data.TotalCount.Should().Be(2);
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == LegacyIrcRead)
            .Which.Should()
            .BeEquivalentTo(new { Granted = false, Required = false });
    }

    [Fact]
    public async Task GetScopes_WhisperScope_IsListedButNeverGatesThePrompt()
    {
        // The bot's whisper inbox (user:read:whispers → the platform-plane user.whisper.message topic) is
        // surfaced on the permission page so its absence is visible, but it must not flip the bot to
        // "action required": a pre-whisper bot grant (the four chat scopes only) still reads complete, and
        // the scope reads granted once a re-auth carries it.
        ChannelBotController controller = Build(
            Conn("twitch_bot", HelixRead, HelixWrite, LegacyIrcRead, LegacyIrcEdit)
        );

        ChannelBotController.ScopesResponseDto data = await GetScopesData(controller);

        data.GrantedCount.Should()
            .Be(data.TotalCount, "a missing whisper scope must not reopen the prompt");
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == WhisperRead)
            .Which.Should()
            .BeEquivalentTo(new { Granted = false, Required = false });

        ChannelBotController regranted = Build(
            Conn("twitch_bot", HelixRead, HelixWrite, LegacyIrcRead, LegacyIrcEdit, WhisperRead)
        );
        ChannelBotController.ScopesResponseDto after = await GetScopesData(regranted);
        after
            .Permissions.Should()
            .ContainSingle(p => p.Scope == WhisperRead)
            .Which.Granted.Should()
            .BeTrue();
    }

    [Fact]
    public async Task GetScopes_PrefersDedicatedBotConnectionOverStreamer()
    {
        // Both connections present: the streamer's twitch token is complete, but the dedicated bot connection
        // is missing the send scope. The page must read the BOT connection (incomplete), not the streamer's.
        ChannelBotController controller = Build(
            Conn("twitch", HelixRead, HelixWrite),
            Conn("twitch_bot", HelixRead)
        );

        ChannelBotController.ScopesResponseDto data = await GetScopesData(controller);

        data.GrantedCount.Should().Be(1);
        data.TotalCount.Should().Be(2);
        data.Permissions.Should()
            .ContainSingle(p => p.Scope == HelixWrite)
            .Which.Granted.Should()
            .BeFalse();
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static async Task<ChannelBotController.ScopesResponseDto> GetScopesData(
        ChannelBotController controller
    )
    {
        IActionResult result = await controller.GetScopes(ChannelId, default);
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ChannelBotController.ScopesResponseDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ChannelBotController.ScopesResponseDto>>()
            .Subject;
        return body.Data!;
    }

    private static IntegrationConnectionDto Conn(string provider, params string[] scopes) =>
        new(
            Id: Guid.NewGuid(),
            BroadcasterId: Guid.Parse(ChannelId),
            Provider: provider,
            ProviderAccountId: "acct",
            ProviderAccountName: "acct-name",
            Status: "connected",
            Scopes: scopes,
            IsByok: false,
            ConnectedAt: DateTime.UtcNow,
            LastRefreshedAt: null,
            ConsecutiveFailureCount: 0
        );

    private static ChannelBotController Build(params IntegrationConnectionDto[] connections)
    {
        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .ListConnectionsAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<IntegrationConnectionDto>>(connections.ToList()));

        IConfiguration config = new ConfigurationBuilder().Build();

        ChannelBotController controller = new(
            Substitute.For<IAuthService>(),
            vault,
            config,
            Substitute.For<ITwitchOAuthStateService>()
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return controller;
    }
}
