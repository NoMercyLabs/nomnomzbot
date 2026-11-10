// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The member count a page shows beside the Twitch subscriber count. Members that cannot be read are a normal
/// state: the answer is a success without a number, so the page leaves the tile out instead of showing a 0.
/// </summary>
public sealed class YouTubeMembersControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000e3be6");

    private static async Task<(int? Status, YouTubeMemberCountDto? Data)> CountAsync(
        Result<int> read
    )
    {
        IYouTubeMembersService members = Substitute.For<IYouTubeMembersService>();
        members.GetMemberCountAsync(Channel, Arg.Any<CancellationToken>()).Returns(read);

        IActionResult answer = await new YouTubeMembersController(members).GetMemberCount(
            Channel,
            CancellationToken.None
        );

        ObjectResult result = answer.Should().BeAssignableTo<ObjectResult>().Subject;
        StatusResponseDto<YouTubeMemberCountDto> body = result
            .Value.Should()
            .BeOfType<StatusResponseDto<YouTubeMemberCountDto>>()
            .Subject;
        return (result.StatusCode, body.Data);
    }

    [Fact]
    public async Task The_count_YouTube_gave_is_handed_on()
    {
        (int? status, YouTubeMemberCountDto? data) = await CountAsync(Result.Success(17));

        status.Should().Be(200);
        data.Should().Be(new YouTubeMemberCountDto(17));
    }

    [Fact]
    public async Task Members_that_cannot_be_read_answer_200_without_a_number()
    {
        (int? status, YouTubeMemberCountDto? data) = await CountAsync(
            Result.Failure<int>("The YouTube members cannot be read.", "NOT_AVAILABLE")
        );

        status.Should().Be(200);
        data.Should().Be(new YouTubeMemberCountDto(null));
    }
}
