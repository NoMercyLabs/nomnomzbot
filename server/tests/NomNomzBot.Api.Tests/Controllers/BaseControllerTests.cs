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
        result!.StatusCode.Should().Be(200);
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
        result!.StatusCode.Should().Be(200);
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
}
