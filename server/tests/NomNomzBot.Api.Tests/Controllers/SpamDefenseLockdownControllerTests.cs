// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the lockdown endpoints (spam-defense.md §L5.1) hand the operator's choices to
/// <see cref="ILockdownService"/> unchanged, refuse a request that names no reason or no control before
/// anything on the platform is touched, and sit behind their own action keys.
/// </summary>
public sealed class SpamDefenseLockdownControllerTests
{
    private readonly ILockdownService _lockdown = Substitute.For<ILockdownService>();
    private readonly Guid _channel = Guid.CreateVersion7();

    private SpamDefenseController Build() =>
        new(
            Substitute.For<ISpamDefenseService>(),
            Substitute.For<ICurrentUserService>(),
            _lockdown
        );

    private static LockdownWindowStatus Window(string platform, params LockdownControl[] engaged) =>
        new(
            Guid.CreateVersion7(),
            platform,
            "manual: raid",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(15),
            null,
            null,
            engaged,
            [],
            [],
            []
        );

    private async Task AssertEngageNeverCalledAsync() =>
        await _lockdown
            .DidNotReceiveWithAnyArgs()
            .EngageAsync(default, default!, default!, default!, default);

    [Fact]
    public async Task Engage_passes_platform_manual_trigger_and_controls_to_the_service()
    {
        LockdownControl[] controls = [LockdownControl.SlowMode, LockdownControl.FollowersOnly];
        LockdownWindowStatus window = Window("twitch", controls);
        _lockdown
            .EngageAsync(
                _channel,
                "twitch",
                "manual: hate raid",
                Arg.Any<IReadOnlyCollection<LockdownControl>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(window));

        IActionResult response = await Build()
            .EngageLockdown(
                _channel.ToString(),
                new("twitch", "hate raid", controls),
                CancellationToken.None
            );

        response
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<LockdownWindowStatus>>()
            .Which.Data.Should()
            .Be(window);
        await _lockdown
            .Received(1)
            .EngageAsync(
                _channel,
                "twitch",
                "manual: hate raid",
                Arg.Is<IReadOnlyCollection<LockdownControl>>(c =>
                    c.Count == 2
                    && c.Contains(LockdownControl.SlowMode)
                    && c.Contains(LockdownControl.FollowersOnly)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Engage_with_no_reason_is_refused_and_the_room_is_not_touched(string? reason)
    {
        IActionResult response = await Build()
            .EngageLockdown(
                _channel.ToString(),
                new("twitch", reason!, [LockdownControl.SlowMode]),
                CancellationToken.None
            );

        response.Should().BeOfType<BadRequestObjectResult>();
        await AssertEngageNeverCalledAsync();
    }

    [Fact]
    public async Task Engage_with_no_controls_is_refused_and_the_room_is_not_touched()
    {
        IActionResult response = await Build()
            .EngageLockdown(
                _channel.ToString(),
                new("twitch", "hate raid", []),
                CancellationToken.None
            );

        response.Should().BeOfType<BadRequestObjectResult>();
        await AssertEngageNeverCalledAsync();
    }

    [Fact]
    public async Task Engage_with_a_bad_channel_id_is_refused_and_the_room_is_not_touched()
    {
        IActionResult response = await Build()
            .EngageLockdown(
                "not-a-guid",
                new("twitch", "hate raid", [LockdownControl.SlowMode]),
                CancellationToken.None
            );

        response.Should().BeOfType<BadRequestObjectResult>();
        await AssertEngageNeverCalledAsync();
    }

    [Fact]
    public async Task Engage_surfaces_a_service_failure_as_an_error_response()
    {
        _lockdown
            .EngageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<LockdownControl>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<LockdownWindowStatus>("Platform not supported", "VALIDATION_ERROR")
            );

        IActionResult response = await Build()
            .EngageLockdown(
                _channel.ToString(),
                new("kick", "hate raid", [LockdownControl.SlowMode]),
                CancellationToken.None
            );

        response
            .Should()
            .BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should()
            .BeGreaterThanOrEqualTo(400);
    }

    [Fact]
    public async Task End_passes_the_platform_to_the_service_and_returns_the_ended_window()
    {
        LockdownWindowStatus window = Window("twitch");
        _lockdown
            .EndAsync(_channel, "twitch", Arg.Any<CancellationToken>())
            .Returns(Result.Success(window));

        IActionResult response = await Build()
            .EndLockdown(_channel.ToString(), new("twitch"), CancellationToken.None);

        response
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<LockdownWindowStatus>>()
            .Which.Data.Should()
            .Be(window);
        await _lockdown.Received(1).EndAsync(_channel, "twitch", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_returns_the_windows_the_service_reports()
    {
        IReadOnlyList<LockdownWindowStatus> windows =
        [
            Window("twitch", LockdownControl.SlowMode),
            Window("kick", LockdownControl.UniqueChat),
        ];
        _lockdown.GetActiveAsync(_channel, Arg.Any<CancellationToken>()).Returns(windows);

        IActionResult response = await Build()
            .GetLockdown(_channel.ToString(), CancellationToken.None);

        response
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<NomNomzBot.Application.DTOs.StatusResponseDto<
                IReadOnlyList<LockdownWindowStatus>
            >>()
            .Which.Data.Should()
            .Equal(windows);
    }

    [Theory]
    [InlineData(nameof(SpamDefenseController.GetLockdown), "spam:lockdown:read")]
    [InlineData(nameof(SpamDefenseController.EngageLockdown), "spam:lockdown:manage")]
    [InlineData(nameof(SpamDefenseController.EndLockdown), "spam:lockdown:manage")]
    public void Each_lockdown_action_sits_behind_its_own_key(string method, string key)
    {
        string? actual = typeof(SpamDefenseController)
            .GetMethod(method)!
            .GetCustomAttribute<RequireActionAttribute>()
            ?.ActionKey;

        actual.Should().Be(key);
    }
}
