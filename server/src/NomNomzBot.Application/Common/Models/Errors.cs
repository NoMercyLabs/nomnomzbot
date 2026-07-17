// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models;

public static class Errors
{
    public static Result NotAuthenticated() =>
        Result.Failure("You must be logged in to perform this action.", "AUTH_REQUIRED");

    public static Result InsufficientPermission(string action) =>
        Result.Failure($"You do not have permission to {action}.", "FORBIDDEN");

    public static Result FeatureNotEnabled(string featureName) =>
        Result.Failure($"The {featureName} feature is not enabled.", "FEATURE_DISABLED");

    public static Result ScopeMissing(string scope, string feature) =>
        Result.Failure(
            $"The {feature} feature requires the '{scope}' permission.",
            "SCOPE_MISSING"
        );

    public static Result<T> NotFound<T>(string entityName, string identifier) =>
        Result.Failure<T>($"{entityName} '{identifier}' was not found.", "NOT_FOUND");

    public static Result ValidationFailed(string message) =>
        Result.Failure(message, "VALIDATION_FAILED");

    public static Result ValidationFailed(IDictionary<string, string[]> errors)
    {
        string message = string.Join(
            "; ",
            errors.SelectMany(e => e.Value.Select(v => $"{e.Key}: {v}"))
        );
        return Result.Failure(message, "VALIDATION_FAILED");
    }

    public static Result ExternalServiceUnavailable(string serviceName) =>
        Result.Failure(
            $"{serviceName} is temporarily unavailable.",
            "SERVICE_UNAVAILABLE",
            errorDetail: $"{serviceName} API returned an error or timed out"
        );

    public static Result TokenExpired(string serviceName) =>
        Result.Failure(
            $"Your {serviceName} connection has expired. Please reconnect.",
            "TOKEN_EXPIRED"
        );

    public static Result RateLimited(string action, TimeSpan retryAfter) =>
        Result.Failure(
            $"Too many requests. Please wait {retryAfter.TotalSeconds:F0} seconds.",
            "RATE_LIMITED"
        );

    public static Result AlreadyExists(string entityName, string identifier) =>
        Result.Failure($"A {entityName} named '{identifier}' already exists.", "ALREADY_EXISTS");

    /// <summary>A billing-tier quota refused the write (monetization-billing.md §3.3) — the plan caps this
    /// resource at <paramref name="limit"/>. Matches the metered path's <c>UsageQuotaExceededEvent</c> naming.</summary>
    public static Result QuotaExceeded(string what, long limit) =>
        Result.Failure(
            $"Your plan allows up to {limit} {what}. Upgrade your tier or remove one first.",
            "QUOTA_EXCEEDED"
        );

    public static Result ChannelNotFound(string channelId) =>
        Result.Failure(
            "Channel not found.",
            "CHANNEL_NOT_FOUND",
            errorDetail: $"No channel with ID {channelId}"
        );

    public static Result<T> ChannelNotFound<T>(string channelId) =>
        Result.Failure<T>(
            "Channel not found.",
            "CHANNEL_NOT_FOUND",
            errorDetail: $"No channel with ID {channelId}"
        );

    public static Result ChannelNotOnboarded(string channelId) =>
        Result.Failure("This channel has not completed setup.", "CHANNEL_NOT_ONBOARDED");
}
