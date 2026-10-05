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
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A streamer sets how TTS says their channel name over <c>/channels/{id}/tts/name-pronunciation</c>: PUT stores
/// the trimmed value on the channel row and GET returns it, an empty PUT clears it, a value over 100 characters
/// is a 400 with the limit in the message and nothing written, and the route is gated by
/// <c>tts:config:read</c> / <c>tts:config:write</c> so a caller below the write floor is denied.
/// </summary>
public sealed class TtsNamePronunciationControllerTests
{
    private static readonly Guid ChannelId = Guid.CreateVersion7();

    private static TtsNamePronunciationController Build(
        TtsConfigControllerOwnVoiceTestDbContext db
    ) => new(new ChannelNamePronunciationService(db));

    private static async Task<TtsConfigControllerOwnVoiceTestDbContext> SeedAsync()
    {
        TtsConfigControllerOwnVoiceTestDbContext db =
            TtsConfigControllerOwnVoiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "xX_JD_Xx",
                NameNormalized = "xx_jd_xx",
                Provider = AuthEnums.Platform.Twitch,
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static ChannelNamePronunciationDto Body(IActionResult result)
    {
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ((StatusResponseDto<ChannelNamePronunciationDto>)ok.Value!).Data!;
    }

    [Fact]
    public async Task Put_then_Get_returns_the_stored_trimmed_value_with_the_channel_name()
    {
        using TtsConfigControllerOwnVoiceTestDbContext db = await SeedAsync();
        TtsNamePronunciationController controller = Build(db);

        IActionResult put = await controller.Set(
            ChannelId.ToString(),
            new() { Pronunciation = "  Jaydee " },
            CancellationToken.None
        );
        IActionResult get = await controller.Get(ChannelId.ToString(), CancellationToken.None);

        Body(put).Should().Be(new ChannelNamePronunciationDto("xX_JD_Xx", "Jaydee"));
        Body(get).Should().Be(new ChannelNamePronunciationDto("xX_JD_Xx", "Jaydee"));
        (await db.Channels.AsNoTracking().SingleAsync())
            .UsernamePronunciation.Should()
            .Be("Jaydee");
    }

    [Fact]
    public async Task An_empty_put_clears_the_value()
    {
        using TtsConfigControllerOwnVoiceTestDbContext db = await SeedAsync();
        TtsNamePronunciationController controller = Build(db);
        await controller.Set(
            ChannelId.ToString(),
            new() { Pronunciation = "Jaydee" },
            CancellationToken.None
        );

        IActionResult put = await controller.Set(
            ChannelId.ToString(),
            new() { Pronunciation = "" },
            CancellationToken.None
        );
        IActionResult get = await controller.Get(ChannelId.ToString(), CancellationToken.None);

        Body(put).Pronunciation.Should().BeNull();
        Body(get).Pronunciation.Should().BeNull();
        (await db.Channels.AsNoTracking().SingleAsync()).UsernamePronunciation.Should().BeNull();
    }

    [Fact]
    public async Task A_value_over_100_characters_is_a_400_naming_the_limit_and_writes_nothing()
    {
        using TtsConfigControllerOwnVoiceTestDbContext db = await SeedAsync();
        TtsNamePronunciationController controller = Build(db);

        IActionResult put = await controller.Set(
            ChannelId.ToString(),
            new() { Pronunciation = new string('x', 101) },
            CancellationToken.None
        );

        BadRequestObjectResult bad = put.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Which.Message.Should()
            .Contain("100");
        (await db.Channels.AsNoTracking().SingleAsync()).UsernamePronunciation.Should().BeNull();
    }

    [Fact]
    public async Task An_invalid_channel_id_is_a_400()
    {
        using TtsConfigControllerOwnVoiceTestDbContext db = await SeedAsync();
        TtsNamePronunciationController controller = Build(db);

        IActionResult get = await controller.Get("not-a-guid", CancellationToken.None);

        get.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(nameof(TtsNamePronunciationController.Get), "tts:config:read")]
    [InlineData(nameof(TtsNamePronunciationController.Set), "tts:config:write")]
    public async Task A_caller_without_the_action_is_denied_and_one_with_it_is_allowed(
        string method,
        string actionKey
    )
    {
        RequireActionAttribute? gate = typeof(TtsNamePronunciationController)
            .GetMethod(method)!
            .GetCustomAttribute<RequireActionAttribute>();
        gate.Should().NotBeNull($"{method} must carry a RequireAction gate");
        gate.ActionKey.Should().Be(actionKey);

        ActionAuthorizationPolicyProvider provider = new(
            Options.Create(new AuthorizationOptions())
        );
        AuthorizationPolicy policy = (await provider.GetPolicyAsync(gate.Policy!))!;
        ActionAuthorizationRequirement requirement = policy
            .Requirements.OfType<ActionAuthorizationRequirement>()
            .Single();

        (await Run(requirement, allowedKey: "some:other:action"))
            .Should()
            .BeFalse("a caller lacking the action is refused (403)");
        (await Run(requirement, allowedKey: actionKey)).Should().BeTrue();
    }

    private static async Task<bool> Run(
        ActionAuthorizationRequirement requirement,
        string allowedKey
    )
    {
        IActionAuthorizationService gate2 = Substitute.For<IActionAuthorizationService>();
        gate2
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Result.Success(call.ArgAt<string>(2) == allowedKey));
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.CreateVersion7().ToString());
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(ChannelId);

        AuthorizationHandlerContext context = new(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity("test")),
            resource: null
        );
        await new ActionAuthorizationHandler(gate2, user, tenant).HandleAsync(context);
        return context.HasSucceeded;
    }
}
