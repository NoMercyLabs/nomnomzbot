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

namespace NomNomzBot.Api.Controllers;

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
            "FORBIDDEN" or "FEATURE_DISABLED" or "SCOPE_MISSING" or "BILLING_LIMIT" =>
                UnauthorizedResponse(result.ErrorMessage),
            "NOT_FOUND" or "CHANNEL_NOT_FOUND" or "CHANNEL_NOT_ONBOARDED" => NotFoundResponse(
                result.ErrorMessage
            ),
            "VALIDATION_FAILED" => BadRequestResponse(result.ErrorMessage),
            "ALREADY_EXISTS" => ConflictResponse(result.ErrorMessage),
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
            "FORBIDDEN" or "FEATURE_DISABLED" or "SCOPE_MISSING" or "BILLING_LIMIT" =>
                UnauthorizedResponse(result.ErrorMessage),
            "NOT_FOUND" or "CHANNEL_NOT_FOUND" or "CHANNEL_NOT_ONBOARDED" => NotFoundResponse(
                result.ErrorMessage
            ),
            "VALIDATION_FAILED" => BadRequestResponse(result.ErrorMessage),
            "ALREADY_EXISTS" => ConflictResponse(result.ErrorMessage),
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
}
