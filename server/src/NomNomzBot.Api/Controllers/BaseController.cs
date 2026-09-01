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
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Api.Controllers;

/// <summary>Shared controller base: rate limiting, JSON output, common error responses, and Result-to-HTTP helpers.
/// The default tier is "write-cheap" (generous, per user); <see cref="RateLimitReadTierConvention"/> splits
/// GET/HEAD actions onto the separate "read" tier so a caller's background polling never contends with
/// their own writes (S114). A controller that needs a different tier (anonymous public surface,
/// platform-admin, auth/device-poll) declares its own <see cref="EnableRateLimitingAttribute"/> and opts
/// out of both the inherited default and the convention.</summary>
[ApiController]
[EnableRateLimiting(RateLimitPolicyNames.WriteCheap)]
[Produces("application/json")]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status500InternalServerError)]
public abstract class BaseController : ControllerBase
{
    protected IActionResult UnauthenticatedResponse(string? message = null, string? code = null) =>
        Unauthorized(
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Unauthorized",
                Code = code,
            }
        );

    protected IActionResult UnauthorizedResponse(string? message = null, string? code = null) =>
        StatusCode(
            403,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Forbidden",
                Code = code,
            }
        );

    protected IActionResult BadRequestResponse(string? message = null, string? code = null) =>
        BadRequest(
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Bad request",
                Code = code,
            }
        );

    protected IActionResult NotFoundResponse(string? message = null, string? code = null) =>
        NotFound(
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Not found",
                Code = code,
            }
        );

    protected IActionResult ConflictResponse(string? message = null, string? code = null) =>
        Conflict(
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Conflict",
                Code = code,
            }
        );

    protected IActionResult TooManyRequestsResponse(string? message = null, string? code = null) =>
        StatusCode(
            429,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Too many requests",
                Code = code,
            }
        );

    protected IActionResult InternalServerErrorResponse(
        string? message = null,
        string? code = null
    ) =>
        StatusCode(
            500,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Internal server error",
                Code = code,
            }
        );

    protected IActionResult ServiceUnavailableResponse(
        string? message = null,
        string? code = null
    ) =>
        StatusCode(
            503,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = message ?? "Service unavailable",
                Code = code,
            }
        );

    protected IActionResult GetPaginatedResponse<T>(IEnumerable<T> data, PageRequestDto request)
    {
        List<T> items = [.. data];
        bool hasMore = items.Count >= request.Take;
        items = [.. items.Take(request.Take)];

        return Ok(
            new PaginatedResponse<T>
            {
                Data = items,
                NextPage = hasMore ? request.Page + 1 : null,
                HasMore = hasMore,
            }
        );
    }

    /// <summary>
    /// Appends a failure's <c>ErrorDetail</c> — a downstream provider's own rejection reason (e.g. Twitch's
    /// "The reward title is not unique") — onto its <c>ErrorMessage</c> when present, so the client sees WHY,
    /// not just a generic "(400)". <c>ErrorDetail</c> was previously read only for retry-after headers and
    /// silently dropped everywhere a message reaches the user (consequences-must-be-visible.md).
    /// </summary>
    private static string? WithDetail(string? message, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? message : $"{message} — {detail}";

    protected IActionResult ResultResponse<T>(NomNomzBot.Application.Common.Models.Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(new StatusResponseDto<T> { Data = result.Value });

        return result.ErrorCode switch
        {
            "AUTH_REQUIRED"
            or "TOKEN_EXPIRED"
            or "INVALID_TOKEN"
            or "TOKEN_REVOKED"
            or "SESSION_INVALID"
            or "SESSION_NOT_ACTIVE"
            or "UNAUTHENTICATED"
            or TwitchErrorCodes.Unauthorized => UnauthenticatedResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            "FORBIDDEN"
            or "FEATURE_DISABLED"
            or "NOT_ENTITLED"
            or "SCOPE_MISSING"
            or "BILLING_LIMIT"
            or "JAR_MEMBERSHIP_REQUIRED"
            or "AGE_CONSENT_REQUIRED"
            or "GAMBLING_DISABLED"
            or "QUOTA_EXCEEDED"
            or "tier_limit_reached"
            or "EGRESS_NOT_ALLOWED"
            or "MISSING_SCOPE"
            or TwitchErrorCodes.MissingScope
            or "PREMIUM_REQUIRED"
            or "NOT_ELIGIBLE"
            or "MUSIC_FORBIDDEN"
            or "VTS_DENIED"
            or "VTS_UNAUTHORIZED"
            or "CANNOT_MODERATE_BROADCASTER"
            or "SOURCE_NOT_ALLOWED"
            or "PROJECT_DEPENDENCY_NOT_ALLOWED"
            or "WIDGET_DEPENDENCY_NOT_ALLOWED"
            or "TENANT_MISMATCH" => UnauthorizedResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            "NOT_FOUND"
            or TwitchErrorCodes.NotFound
            or "CHANNEL_NOT_FOUND"
            or "CHANNEL_NOT_ONBOARDED"
            or "QUOTES_EMPTY"
            or "PICKLIST_EMPTY"
            or "IDENTITY_NOT_FOUND"
            or "KEY_NOT_FOUND"
            or "SOURCE_NOT_FOUND"
            or "PROJECTION_NOT_FOUND"
            or "NO_CAMPAIGN"
            or "NO_ACTIVE_DEVICE"
            or "UNKNOWN_GAME"
            or "UNKNOWN_PROVIDER"
            or "UNKNOWN_SCOPE_SET"
            or "LEGACY_DB_NOT_FOUND"
            or "WIDGET_NO_SOURCE"
            or "WIDGET_NO_SETTINGS_SCHEMA"
            or "PROJECT_ENTRY_MISSING"
            or "WIDGET_PROJECT_ENTRY_MISSING" => NotFoundResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            "VALIDATION_FAILED"
            or "BET_OUT_OF_RANGE"
            or "TWITCH_NOT_CONFIGURED"
            or "INVALID_ID"
            or "INVALID_KEY"
            or "INVALID_SEQUENCE_BLOCK"
            or "INVALID_CALLBACK"
            or "INVALID_CHANNEL_ID"
            or "INVALID_GRAPH"
            or "INVALID_PATH"
            or "INVALID_STATE"
            or "PROJECT_PATH_INVALID"
            or "WIDGET_PROJECT_PATH_INVALID"
            or "WIDGET_CLONE_SOURCE_INVALID"
            or "BUNDLE_INVALID"
            or "ENVELOPE_INVALID"
            or "IMPORT_INVALID_ENVELOPE"
            or "IMPORT_MALFORMED_LINE"
            or "CONFIRMATION_REQUIRED"
            or "DURATION_EXCEEDED"
            or "BUNDLE_TOO_LARGE"
            or "WIDGET_FRAMEWORK_UNSUPPORTED"
            or "NO_MISSING_SCOPES"
            or "SHOP_REQUIRED"
            or "SUBJECT_KEY_MISSING"
            or "GAME_NOT_CONFIGURED"
            or "NO_SCOPES"
            or "NO_TENANT" => BadRequestResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            "ALREADY_EXISTS"
            or "MIGRATION_PENDING_EXTERNAL_REMOVAL"
            or TwitchErrorCodes.NoToken
            or "INSUFFICIENT_FUNDS"
            or "ACCOUNT_FROZEN"
            or "CURRENCY_DISABLED"
            or "MAX_BALANCE_EXCEEDED"
            or "OUT_OF_STOCK"
            or "ON_COOLDOWN"
            or "PER_STREAM_LIMIT"
            or "JAR_NOT_OPEN"
            or "JAR_CAP_EXCEEDED"
            or "CAPABILITY_UNSUPPORTED"
            or "TRACK_BLOCKED"
            or "MARKETPLACE_NO_PUBLISHER_TOKEN"
            or "MARKETPLACE_AUTH_FAILED"
            // A refused act-as: the request is well-formed but the caller has no open support session
            // (SESSION_REQUIRED) or the platform build doesn't offer impersonation at all (NOT_SUPPORTED) —
            // both are an actionable "you can't do this right now", the same class as the other conflicts
            // above, never a server fault.
            or "SESSION_REQUIRED"
            or "NOT_SUPPORTED"
            or "ALREADY_ENTERED"
            or "DUPLICATE_ASSIGNMENT"
            or "DUPLICATE_TRACK"
            or "GIVEAWAY_ALREADY_ACTIVE"
            or "GIVEAWAY_NOT_OPEN"
            or "SESSION_ALREADY_ACTIVE"
            or "IDENTITY_ALREADY_LINKED"
            or "PROVIDER_ALREADY_LINKED"
            or "LAST_IDENTITY"
            or "PRIMARY_IDENTITY"
            or "LAST_MANAGER"
            or "CONCURRENCY_CONFLICT"
            or "ESCALATION_STATE_RACE"
            or "USAGE_RECORD_RACE"
            or "TARGET_INACTIVE"
            or "COOLDOWN"
            or "LIMIT_EXCEEDED"
            or "QUEUE_FULL"
            or "CODE_POOL_EXHAUSTED"
            or "WIDGET_VERSION_NOT_SUCCESSFUL"
            or "WIDGET_GALLERY_ITEM_NOT_VERIFIED"
            or "KEY_NOT_ACTIVE"
            or "KEY_DESTROYED"
            or "PROJECTION_RUN_IN_PROGRESS"
            or "FIRST_PARTY_IMMUTABLE" => ConflictResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            // Discord upstream results are never our fault (500). An invalid/expired bot token or a missing
            // connection is an actionable "reconnect the Discord bot" state → 409, so the client shows a
            // reconnect prompt instead of a generic failure; other upstream conditions map to their true class.
            "DISCORD_UNAUTHORIZED" or "DISCORD_NOT_CONNECTED" => ConflictResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            "DISCORD_NOT_FOUND" => NotFoundResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            "RATE_LIMITED" or "DISCORD_RATE_LIMITED" or TwitchErrorCodes.RateLimited =>
                TooManyRequestsResponse(
                    WithDetail(result.ErrorMessage, result.ErrorDetail),
                    result.ErrorCode
                ),
            "SERVICE_UNAVAILABLE"
            or "MARKETPLACE_UNAVAILABLE"
            or "DISCORD_ERROR"
            or "DISCORD_TRANSPORT"
            or "PROVIDER_UNAVAILABLE"
            or "PROVIDER_NOT_CONFIGURED"
            or "PROVIDER_NOT_CONNECTED"
            or "MUSIC_AUTH_FAILED"
            or "TWITCH_ERROR"
            or TwitchErrorCodes.TwitchError
            or TwitchErrorCodes.Transport
            or "VTS_ERROR"
            or "VTS_BRIDGE_OFFLINE"
            or "VTS_NOT_CONNECTED"
            or "VTS_TIMEOUT"
            or "VTS_WRONG_MODE"
            or "VTS_DISABLED"
            or "OBS_ERROR"
            or "OBS_BRIDGE_OFFLINE"
            or "OBS_DISABLED"
            or "OBS_NOT_CONNECTED"
            or "OBS_TIMEOUT"
            or "OBS_WRONG_MODE"
            or "EMOTE_PROVIDER_ERROR"
            or "WIDGET_BUILD_TOOL_UNAVAILABLE" => ServiceUnavailableResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            // Genuinely internal — our own machinery (journal append, projections, crypto, token exchange,
            // import/export, provisioning) faulted; there is no client action that avoids this, so 500 is the
            // correct, intentional class rather than an omission.
            "JOURNAL_APPEND_FAILED"
            or "JOURNAL_APPEND_BATCH_FAILED"
            or "PROJECTION_FAULTED"
            or "WIDGET_BUILD_FAILED"
            or "EXPORT_FAILED"
            or "IMPORT_FAILED"
            or "IMPORT_UPCAST_FAILED"
            or "ERASURE_FAILED"
            or "PROVISIONING_FAILED"
            or "UNWRAP_FAILED"
            or "DECRYPT_FAILED"
            or "TOKEN_EXCHANGE_FAILED"
            or "TOKEN_STORE_FAILED"
            or "USER_FETCH_FAILED"
            or "DEVICE_TRANSFER_FAILED"
            or "UPCASTER_CHAIN_BROKEN"
            or "INTERNAL_ERROR" => InternalServerErrorResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
            _ => InternalServerErrorResponse(
                WithDetail(result.ErrorMessage, result.ErrorDetail),
                result.ErrorCode
            ),
        };
    }

    protected IActionResult ResultResponse(NomNomzBot.Application.Common.Models.Result result) =>
        ResultResponse(result.WithValue<object?>(null));

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
            : MapTwitchError(result.ErrorCode, WithDetail(result.ErrorMessage, result.ErrorDetail));

    protected IActionResult TwitchResultResponse<T>(
        NomNomzBot.Application.Common.Models.Result<T> result
    ) =>
        result.IsSuccess
            ? Ok(new StatusResponseDto<T> { Data = result.Value })
            : MapTwitchError(result.ErrorCode, WithDetail(result.ErrorMessage, result.ErrorDetail));

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
