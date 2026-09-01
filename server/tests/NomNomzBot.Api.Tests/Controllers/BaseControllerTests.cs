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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Concrete subclass that exposes BaseController protected helpers for testing.
/// </summary>
internal sealed class TestController : BaseController
{
    public IActionResult TestUnauthenticated(string? msg = null) => UnauthenticatedResponse(msg);

    public IActionResult TestUnauthorized(string? msg = null) => UnauthorizedResponse(msg);

    public IActionResult TestBadRequest(string? msg = null) => BadRequestResponse(msg);

    public IActionResult TestNotFound(string? msg = null) => NotFoundResponse(msg);

    public IActionResult TestConflict(string? msg = null) => ConflictResponse(msg);

    public IActionResult TestTooManyRequests(string? msg = null) => TooManyRequestsResponse(msg);

    public IActionResult TestInternalServerError(string? msg = null) =>
        InternalServerErrorResponse(msg);

    public IActionResult TestServiceUnavailable(string? msg = null) =>
        ServiceUnavailableResponse(msg);

    public IActionResult TestResultResponse<T>(Result<T> result) => ResultResponse(result);

    public IActionResult TestResultResponse(Result result) => ResultResponse(result);
}

public class BaseControllerTests
{
    private static TestController CreateController()
    {
        TestController ctrl = new();
        ctrl.ControllerContext = new();
        return ctrl;
    }

    // ─── Status code helpers ──────────────────────────────────────────────────

    [Fact]
    public void UnauthenticatedResponse_Returns401()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestUnauthenticated() as ObjectResult;
        result!.StatusCode.Should().Be(401);
    }

    [Fact]
    public void UnauthorizedResponse_Returns403()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestUnauthorized() as ObjectResult;
        result!.StatusCode.Should().Be(403);
    }

    [Fact]
    public void BadRequestResponse_Returns400()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestBadRequest() as ObjectResult;
        result!.StatusCode.Should().Be(400);
    }

    [Fact]
    public void NotFoundResponse_Returns404()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestNotFound() as ObjectResult;
        result!.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ConflictResponse_Returns409()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestConflict() as ObjectResult;
        result!.StatusCode.Should().Be(409);
    }

    [Fact]
    public void TooManyRequestsResponse_Returns429()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestTooManyRequests() as ObjectResult;
        result!.StatusCode.Should().Be(429);
    }

    [Fact]
    public void InternalServerErrorResponse_Returns500()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestInternalServerError() as ObjectResult;
        result!.StatusCode.Should().Be(500);
    }

    [Fact]
    public void ServiceUnavailableResponse_Returns503()
    {
        TestController ctrl = CreateController();
        ObjectResult? result = ctrl.TestServiceUnavailable() as ObjectResult;
        result!.StatusCode.Should().Be(503);
    }

    // ─── ResultResponse<T> ────────────────────────────────────────────────────

    [Fact]
    public void ResultResponseT_Success_Returns200()
    {
        TestController ctrl = CreateController();
        OkObjectResult? result = ctrl.TestResultResponse(Result.Success("hello")) as OkObjectResult;

        result.Should().NotBeNull();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public void ResultResponseT_AuthRequired_Returns401()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.NotAuthenticated().ToTyped<string>()) as ObjectResult;

        result!.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ResultResponseT_NotFound_Returns404()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.NotFound<string>("User", "u1")) as ObjectResult;

        result!.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ResultResponseT_ValidationFailed_Returns400()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.ValidationFailed("bad").ToTyped<string>())
            as ObjectResult;

        result!.StatusCode.Should().Be(400);
    }

    [Fact]
    public void ResultResponseT_AlreadyExists_Returns409()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.AlreadyExists("User", "alice").ToTyped<string>())
            as ObjectResult;

        result!.StatusCode.Should().Be(409);
    }

    [Fact]
    public void ResultResponseT_RateLimited_Returns429()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(
                Errors.RateLimited("login", TimeSpan.FromSeconds(30)).ToTyped<string>()
            ) as ObjectResult;

        result!.StatusCode.Should().Be(429);
    }

    [Fact]
    public void ResultResponseT_ServiceUnavailable_Returns503()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.ExternalServiceUnavailable("Spotify").ToTyped<string>())
            as ObjectResult;

        result!.StatusCode.Should().Be(503);
    }

    [Fact]
    public void ResultResponseT_Forbidden_Returns403()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.InsufficientPermission("delete").ToTyped<string>())
            as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }

    // ─── ResultResponse (void) ────────────────────────────────────────────────

    [Fact]
    public void ResultResponse_Success_Returns200()
    {
        TestController ctrl = CreateController();
        OkObjectResult? result = ctrl.TestResultResponse(Result.Success()) as OkObjectResult;

        result.Should().NotBeNull();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public void ResultResponse_Failure_Returns500ForUnknownCode()
    {
        TestController ctrl = CreateController();
        Result failure = Result.Failure("internal", "UNKNOWN_CODE");
        ObjectResult? result = ctrl.TestResultResponse(failure) as ObjectResult;

        result!.StatusCode.Should().Be(500);
    }

    [Fact]
    public void ResultResponse_TokenExpired_Returns401()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.TokenExpired("Twitch")) as ObjectResult;

        result!.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ResultResponse_ChannelNotFound_Returns404()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.ChannelNotFound("ch1")) as ObjectResult;

        result!.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ResultResponse_FeatureDisabled_Returns403()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Errors.FeatureNotEnabled("SongRequests")) as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }

    // ─── Economy codes (economy.md §5) ────────────────────────────────────────

    [Theory]
    [InlineData("INSUFFICIENT_FUNDS")]
    [InlineData("ACCOUNT_FROZEN")]
    [InlineData("CURRENCY_DISABLED")]
    [InlineData("MAX_BALANCE_EXCEEDED")]
    [InlineData("OUT_OF_STOCK")]
    [InlineData("ON_COOLDOWN")]
    [InlineData("PER_STREAM_LIMIT")]
    [InlineData("JAR_NOT_OPEN")]
    [InlineData("JAR_CAP_EXCEEDED")]
    public void ResultResponse_EconomyConflictCodes_Return409(string code)
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("nope", code)) as ObjectResult;

        result!.StatusCode.Should().Be(409);
    }

    [Theory]
    [InlineData("JAR_MEMBERSHIP_REQUIRED")]
    [InlineData("AGE_CONSENT_REQUIRED")]
    [InlineData("GAMBLING_DISABLED")]
    public void ResultResponse_EconomyForbiddenCodes_Return403(string code)
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("nope", code)) as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ResultResponse_BetOutOfRange_Returns400()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("nope", "BET_OUT_OF_RANGE")) as ObjectResult;

        result!.StatusCode.Should().Be(400);
    }

    // ─── Admin act-as (tenant-access / impersonation) codes ───────────────────
    //
    // Regression coverage for the "act as owner" 500: StartImpersonationAsync refuses with
    // SESSION_REQUIRED (no open, unexpired support-access grant) or NOT_SUPPORTED (impersonation
    // disabled on self-host) — both were previously unmapped and fell through to a bare 500.

    [Fact]
    public void ResultResponse_SessionRequired_Returns409NotInternalServerError()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("no session", "SESSION_REQUIRED"))
            as ObjectResult;

        result!.StatusCode.Should().Be(409);
    }

    [Fact]
    public void ResultResponse_NotSupported_Returns409NotInternalServerError()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("self-host", "NOT_SUPPORTED")) as ObjectResult;

        result!.StatusCode.Should().Be(409);
    }

    [Fact]
    public void ResultResponseT_SessionRequired_Returns409NotInternalServerError()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure<string>("no session", "SESSION_REQUIRED"))
            as ObjectResult;

        result!.StatusCode.Should().Be(409);
    }

    // ─── Raw TwitchErrorCodes forwarded through the app-level ResultResponse (twitch-helix.md §3) ───
    //
    // Regression coverage for the moderation shield/blocked-terms/unban-requests 500s: those services
    // forward a Helix call's Result untouched (lowercase TwitchErrorCodes, a different code space than
    // the app-level UPPER_SNAKE_CASE ones above), and ResultResponse's switch only matched the latter —
    // so a real Twitch 401 fell through to the unmatched-code default and came back as a bare 500.

    [Fact]
    public void ResultResponse_TwitchUnauthorized_Returns401NotInternalServerError()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("Twitch rejected the token.", "unauthorized"))
            as ObjectResult;

        result!.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ResultResponse_TwitchMissingScope_Returns403()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("Missing scope.", "missing_scope"))
            as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ResultResponse_TwitchNoToken_Returns409NotInternalServerError()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("No linked Twitch identity.", "no_token"))
            as ObjectResult;

        result!.StatusCode.Should().Be(409);
    }

    [Fact]
    public void ResultResponse_TwitchNotFound_Returns404()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("Not found on Twitch.", "not_found"))
            as ObjectResult;

        result!.StatusCode.Should().Be(404);
    }

    [Fact]
    public void ResultResponse_TwitchRateLimited_Returns429()
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("Rate limited.", "rate_limited"))
            as ObjectResult;

        result!.StatusCode.Should().Be(429);
    }

    [Theory]
    [InlineData("twitch_error")]
    [InlineData("transport")]
    public void ResultResponse_TwitchUpstreamCodes_Return503NotInternalServerError(string code)
    {
        TestController ctrl = CreateController();
        ObjectResult? result =
            ctrl.TestResultResponse(Result.Failure("Twitch request failed.", code)) as ObjectResult;

        result!.StatusCode.Should().Be(503);
    }

    [Fact]
    public void ResultResponse_VoidSuccess_HasNoDataField()
    {
        TestController ctrl = CreateController();
        OkObjectResult result = (OkObjectResult)ctrl.TestResultResponse(Result.Success());

        // The void overload delegates to the generic one internally — the delegation must not leak a
        // "data" payload into a void success response.
        result.Value.Should().BeOfType<StatusResponseDto<object?>>();
        ((StatusResponseDto<object?>)result.Value!).Data.Should().BeNull();
        ((StatusResponseDto<object?>)result.Value!).Status.Should().Be("ok");
    }

    // ─── S-OWN07: the failure's Result.ErrorCode must reach the wire body ──────
    //
    // Before this, every ResultResponse/BadRequestResponse/etc. failure answered with
    // {"status":"error","message":"..."} and NO machine-readable code — the dashboard could only branch on
    // the bare HTTP status, so a provider connect with no BYOC client (PROVIDER_NOT_CONFIGURED) was
    // indistinguishable from any other 4xx/5xx and surfaced as a generic error toast instead of opening the
    // BYOC onboarding dialog. These prove the code actually lands in the response DTO the client parses.

    [Fact]
    public void ResultResponse_ProviderNotConfigured_CarriesTheErrorCodeInTheResponseBody()
    {
        TestController ctrl = CreateController();
        ObjectResult result = (ObjectResult)
            ctrl.TestResultResponse(
                Result.Failure<object>(
                    "spotify app credentials are not configured.",
                    "PROVIDER_NOT_CONFIGURED"
                )
            );

        StatusResponseDto<object> body = (StatusResponseDto<object>)result.Value!;
        body.Code.Should().Be("PROVIDER_NOT_CONFIGURED");
        body.Status.Should().Be("error");
    }

    [Fact]
    public void ResultResponse_Success_LeavesTheCodeFieldNull()
    {
        TestController ctrl = CreateController();
        OkObjectResult result = (OkObjectResult)ctrl.TestResultResponse(Result.Success("value"));

        ((StatusResponseDto<string>)result.Value!).Code.Should().BeNull();
    }

    [Theory]
    [InlineData("NOT_FOUND")]
    [InlineData("RATE_LIMITED")]
    [InlineData("INTERNAL_ERROR")]
    public void ResultResponse_EveryStatusClass_StillCarriesTheErrorCode(string code)
    {
        // The code must ride along regardless of which HTTP status class the ErrorCode maps to — not just
        // the one branch S-OWN07 depends on.
        TestController ctrl = CreateController();
        ObjectResult result = (ObjectResult)
            ctrl.TestResultResponse(Result.Failure<object>("failed", code));

        ((StatusResponseDto<object>)result.Value!).Code.Should().Be(code);
    }
}
