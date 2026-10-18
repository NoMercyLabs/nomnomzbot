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
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""", "invalid_grant")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}""", "invalid_grant")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""", "invalid_client")]
    [InlineData(
        HttpStatusCode.BadRequest,
        """{"error":"unauthorized_client"}""",
        "unauthorized_client"
    )]
    [InlineData(
        HttpStatusCode.BadRequest,
        """{"status":400,"message":"Invalid refresh token"}""",
        null
    )]
    public async Task A_refusal_for_good_is_recognised_and_carries_the_oauth_error_code(
        HttpStatusCode status,
        string body,
        string? code
    )
    {
        RefreshRejection rejection = await OAuthRefreshRejection.ClassifyAsync(
            Response(status, body),
            CancellationToken.None
        );

        rejection.Kind.Should().Be(RefreshRejectionKind.Refused);
        rejection.ErrorCode.Should().Be(code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.Unauthorized, "<html>denied</html>")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"access_denied"}""")]
    public async Task Any_other_401_counts_toward_needs_reauth(HttpStatusCode status, string body)
    {
        RefreshRejection rejection = await OAuthRefreshRejection.ClassifyAsync(
            Response(status, body),
            CancellationToken.None
        );

        rejection.Kind.Should().Be(RefreshRejectionKind.Counted);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"invalid_grant"}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":"invalid_grant"}""")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>bad gateway</html>")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_request"}""")]
    [InlineData(HttpStatusCode.BadRequest, """["invalid_grant"]""")]
    public async Task Server_errors_and_odd_400s_are_transient(HttpStatusCode status, string body)
    {
        RefreshRejection rejection = await OAuthRefreshRejection.ClassifyAsync(
            Response(status, body),
            CancellationToken.None
        );

        rejection.Kind.Should().Be(RefreshRejectionKind.Transient);
    }

    [Fact]
    public async Task An_error_field_that_is_not_an_oauth_code_is_never_returned_for_logging()
    {
        RefreshRejection rejection = await OAuthRefreshRejection.ClassifyAsync(
            Response(
                HttpStatusCode.Unauthorized,
                """{"error":"ya29.A0ARrdaM-secret-looking value"}"""
            ),
            CancellationToken.None
        );

        rejection.ErrorCode.Should().BeNull();
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
