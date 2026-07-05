// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Api.Controllers;

/// <summary>Shared controller base: rate limiting, JSON output, common error responses, and Result-to-HTTP helpers.</summary>
[ApiController]
[EnableRateLimiting("api")]
[Produces("application/json")]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status500InternalServerError)]
public abstract class BaseController : ControllerBase
{
    protected IActionResult UnauthenticatedResponse(string? message = null) =>
        Unauthorized(
            new StatusResponseDto<object> { Status = "error", Message = message ?? "Unauthorized" }
        );

    protected IActionResult UnauthorizedResponse(string? message = null) =>
        StatusCode(
            403,
            new StatusResponseDto<object> { Status = "error", Message = message ?? "Forbidden" }
        );

    protected IActionResult BadRequestResponse(string? message = null) =>
        BadRequest(
            new StatusResponseDto<object> { Status = "error", Message = message ?? "Bad request" }
        );

    protected IActionResult NotFoundResponse(string? message = null) =>
        NotFound(
            new StatusResponseDto<object> { Status = "error", Message = message ?? "Not found" }
        );

    protected IActionResult ConflictResponse(string? message = null) =>
        Conflict(
            new StatusResponseDto<object> { Status = "error", Message = message ?? "Conflict" }
        );

    protected IActionResult TooManyRequestsResponse(string? message = null) =>
        StatusCode(
            429,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Too many requests",
            }
        );

    protected IActionResult InternalServerErrorResponse(string? message = null) =>
        StatusCode(
            500,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Internal server error",
            }
        );

    protected IActionResult ServiceUnavailableResponse(string? message = null) =>
        StatusCode(
            503,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Service unavailable",
            }
        );

    protected IActionResult GetPaginatedResponse<T>(IEnumerable<T> data, PageRequestDto request)
    {
        List<T> items = data.ToList();
        bool hasMore = items.Count >= request.Take;
        items = items.Take(request.Take).ToList();

        return Ok(
            new PaginatedResponse<T>
            {
                Data = items,
                NextPage = hasMore ? request.Page + 1 : null,
                HasMore = hasMore,
            }
        );
    }

    protected IActionResult ResultResponse<T>(NomNomzBot.Application.Common.Models.Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(new StatusResponseDto<T> { Data = result.Value });

        return result.ErrorCode switch
        {
            "AUTH_REQUIRED" or "TOKEN_EXPIRED" or "INVALID_TOKEN" => UnauthenticatedResponse(
                result.ErrorMessage
            ),
            "FORBIDDEN"
            or "FEATURE_DISABLED"
            or "SCOPE_MISSING"
            or "BILLING_LIMIT"
            or "JAR_MEMBERSHIP_REQUIRED"
            or "AGE_CONSENT_REQUIRED"
            or "GAMBLING_DISABLED"
            or "QUOTA_EXCEEDED"
            or "tier_limit_reached"
            or "EGRESS_NOT_ALLOWED"
            or "MISSING_SCOPE"
            or "PREMIUM_REQUIRED" => UnauthorizedResponse(result.ErrorMessage),
            "NOT_FOUND" or "CHANNEL_NOT_FOUND" or "CHANNEL_NOT_ONBOARDED" or "QUOTES_EMPTY" =>
                NotFoundResponse(result.ErrorMessage),
            "VALIDATION_FAILED" or "BET_OUT_OF_RANGE" or "TWITCH_NOT_CONFIGURED" =>
                BadRequestResponse(result.ErrorMessage),
            "ALREADY_EXISTS"
            or "INSUFFICIENT_FUNDS"
            or "ACCOUNT_FROZEN"
            or "CURRENCY_DISABLED"
            or "MAX_BALANCE_EXCEEDED"
            or "OUT_OF_STOCK"
            or "ON_COOLDOWN"
            or "PER_STREAM_LIMIT"
            or "JAR_NOT_OPEN"
            or "JAR_CAP_EXCEEDED"
            or "CAPABILITY_UNSUPPORTED" => ConflictResponse(result.ErrorMessage),
            "RATE_LIMITED" => TooManyRequestsResponse(result.ErrorMessage),
            "SERVICE_UNAVAILABLE" => ServiceUnavailableResponse(result.ErrorMessage),
            _ => InternalServerErrorResponse(result.ErrorMessage),
        };
    }

    protected IActionResult ResultResponse(NomNomzBot.Application.Common.Models.Result result)
    {
        if (result.IsSuccess)
            return Ok(new StatusResponseDto<object> { Status = "ok" });

        return result.ErrorCode switch
        {
            "AUTH_REQUIRED" or "TOKEN_EXPIRED" or "INVALID_TOKEN" => UnauthenticatedResponse(
                result.ErrorMessage
            ),
            "FORBIDDEN"
            or "FEATURE_DISABLED"
            or "SCOPE_MISSING"
            or "BILLING_LIMIT"
            or "JAR_MEMBERSHIP_REQUIRED"
            or "AGE_CONSENT_REQUIRED"
            or "GAMBLING_DISABLED"
            or "QUOTA_EXCEEDED"
            or "tier_limit_reached"
            or "EGRESS_NOT_ALLOWED"
            or "MISSING_SCOPE"
            or "PREMIUM_REQUIRED" => UnauthorizedResponse(result.ErrorMessage),
            "NOT_FOUND" or "CHANNEL_NOT_FOUND" or "CHANNEL_NOT_ONBOARDED" or "QUOTES_EMPTY" =>
                NotFoundResponse(result.ErrorMessage),
            "VALIDATION_FAILED" or "BET_OUT_OF_RANGE" or "TWITCH_NOT_CONFIGURED" =>
                BadRequestResponse(result.ErrorMessage),
            "ALREADY_EXISTS"
            or "INSUFFICIENT_FUNDS"
            or "ACCOUNT_FROZEN"
            or "CURRENCY_DISABLED"
            or "MAX_BALANCE_EXCEEDED"
            or "OUT_OF_STOCK"
            or "ON_COOLDOWN"
            or "PER_STREAM_LIMIT"
            or "JAR_NOT_OPEN"
            or "JAR_CAP_EXCEEDED"
            or "CAPABILITY_UNSUPPORTED" => ConflictResponse(result.ErrorMessage),
            "RATE_LIMITED" => TooManyRequestsResponse(result.ErrorMessage),
            "SERVICE_UNAVAILABLE" => ServiceUnavailableResponse(result.ErrorMessage),
            _ => InternalServerErrorResponse(result.ErrorMessage),
        };
    }

    protected IActionResult GetPaginatedResponse<T>(
        NomNomzBot.Application.Common.Models.PagedList<T> pagedList,
        PageRequestDto request
    )
    {
        return Ok(
            new PaginatedResponse<T>
            {
                Data = pagedList.Items,
                NextPage = pagedList.HasNextPage ? pagedList.Page + 1 : null,
                HasMore = pagedList.HasNextPage,
            }
        );
    }

    /// <summary>
    /// Translates a Helix <see cref="NomNomzBot.Application.Common.Models.Result"/>'s
    /// <see cref="TwitchErrorCodes"/> to problem-details status codes (twitch-helix.md §3):
    /// <c>missing_scope</c>→403, <c>unauthorized</c>→401, <c>no_token</c>→409, <c>not_found</c>→404,
    /// <c>rate_limited</c>→429, <c>twitch_error</c>/<c>transport</c>→502. Use for endpoints that call the
    /// Twitch sub-clients directly (a separate code space from the app-level <see cref="ResultResponse(NomNomzBot.Application.Common.Models.Result)"/>).
    /// </summary>
    protected IActionResult TwitchResultResponse(
        NomNomzBot.Application.Common.Models.Result result
    ) =>
        result.IsSuccess
            ? Ok(new StatusResponseDto<object> { Status = "ok" })
            : MapTwitchError(result.ErrorCode, result.ErrorMessage);

    protected IActionResult TwitchResultResponse<T>(
        NomNomzBot.Application.Common.Models.Result<T> result
    ) =>
        result.IsSuccess
            ? Ok(new StatusResponseDto<T> { Data = result.Value })
            : MapTwitchError(result.ErrorCode, result.ErrorMessage);

    private IActionResult MapTwitchError(string? code, string? message) =>
        code switch
        {
            TwitchErrorCodes.MissingScope => UnauthorizedResponse(message),
            TwitchErrorCodes.Unauthorized => UnauthenticatedResponse(message),
            TwitchErrorCodes.NoToken => ConflictResponse(message),
            TwitchErrorCodes.NotFound => NotFoundResponse(message),
            TwitchErrorCodes.RateLimited => TooManyRequestsResponse(message),
            _ => StatusCode(
                StatusCodes.Status502BadGateway,
                new StatusResponseDto<object>
                {
                    Status = "error",
                    Message = message ?? "Twitch request failed.",
                }
            ),
        };
}
