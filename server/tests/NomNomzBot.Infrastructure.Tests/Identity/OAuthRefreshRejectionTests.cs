// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The one decision every refresher shares: which token-endpoint answers prove the refresh token is dead.
/// Each provider's real dead-grant body counts; a server error, a rate limit, a client-configuration error
/// or an unreadable body never does.
/// </summary>
public sealed class OAuthRefreshRejectionTests
{
    [Theory]
    [InlineData(
        HttpStatusCode.BadRequest,
        """{"error":"invalid_grant","error_description":"Refresh token revoked"}"""
    )]
    [InlineData(
        HttpStatusCode.BadRequest,
        """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}"""
    )]
    [InlineData(HttpStatusCode.BadRequest, """{"status":400,"message":"Invalid refresh token"}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}""")]
    public async Task A_dead_grant_answer_is_recognised(HttpStatusCode status, string body)
    {
        bool dead = await OAuthRefreshRejection.IsDeadGrantAsync(
            Response(status, body),
            CancellationToken.None
        );

        dead.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"invalid_grant"}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":"invalid_grant"}""")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>bad gateway</html>")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_client"}""")]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.BadRequest, """["invalid_grant"]""")]
    public async Task Any_other_answer_is_not_a_dead_grant(HttpStatusCode status, string body)
    {
        bool dead = await OAuthRefreshRejection.IsDeadGrantAsync(
            Response(status, body),
            CancellationToken.None
        );

        dead.Should().BeFalse();
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
