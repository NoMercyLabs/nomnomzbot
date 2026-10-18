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
using System.Text.Json;
using System.Text.RegularExpressions;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>How a failed refresh-grant response must be treated.</summary>
internal enum RefreshRejectionKind
{
    /// <summary>Says nothing about the grant (5xx, 429, an odd 400, a timeout): back off and try again.</summary>
    Transient,

    /// <summary>A 401 with no proof either way: counts toward <c>needs_reauth</c>, which repeats confirm.</summary>
    Counted,

    /// <summary>The provider refused the grant or the client for good: the owner must reconnect now.</summary>
    Refused,
}

/// <summary>A classified refusal plus the provider's OAuth <c>error</c> code (safe to log; never a body).</summary>
internal readonly record struct RefreshRejection(RefreshRejectionKind Kind, string? ErrorCode);

/// <summary>
/// Reads a failed OAuth refresh-grant response and says what it proves. A refusal for good is the RFC 6749
/// §5.2 <c>invalid_grant</c>, <c>invalid_client</c> or <c>unauthorized_client</c> error (Spotify, Google,
/// Kick) or Twitch's own 400 <c>"Invalid refresh token"</c>: the owner must reconnect. Any other 401 counts
/// toward <c>needs_reauth</c> so repeats confirm it. A 5xx, a 429, a timeout or any other 400 says nothing
/// about the grant, so the caller backs off and tries again later.
/// </summary>
internal static class OAuthRefreshRejection
{
    private const string TwitchInvalidRefreshToken = "invalid refresh token";

    private static readonly HashSet<string> RefusedForGoodCodes =
    [
        "invalid_grant",
        "invalid_client",
        "unauthorized_client",
    ];

    // An OAuth error code is a short snake_case token. Anything else in that field is not logged.
    private static readonly Regex SafeErrorCode = new(
        "^[a-z_]{1,40}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50)
    );

    /// <summary>
    /// Records a rejected refresh on the vault by its kind and returns the classification so the caller can
    /// log the provider's error code.
    /// </summary>
    public static async Task<RefreshRejection> RecordAsync(
        IIntegrationTokenVault vault,
        Guid connectionId,
        HttpResponseMessage response,
        string error,
        CancellationToken cancellationToken
    )
    {
        RefreshRejection rejection = await ClassifyAsync(response, cancellationToken);
        switch (rejection.Kind)
        {
            case RefreshRejectionKind.Refused:
                await vault.MarkRefreshRefusedAsync(connectionId, error, cancellationToken);
                break;
            case RefreshRejectionKind.Counted:
                await vault.MarkRefreshFailureAsync(connectionId, error, cancellationToken);
                break;
            default:
                await vault.MarkTransientRefreshFailureAsync(
                    connectionId,
                    error,
                    cancellationToken
                );
                break;
        }

        return rejection;
    }

    public static async Task<RefreshRejection> ClassifyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized))
            return new(RefreshRejectionKind.Transient, null);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        (string? code, string? message) = ReadErrorFields(body);

        string? safeCode = code is not null && SafeErrorCode.IsMatch(code) ? code : null;
        bool refusedForGood =
            (safeCode is not null && RefusedForGoodCodes.Contains(safeCode))
            || message?.Contains(TwitchInvalidRefreshToken, StringComparison.OrdinalIgnoreCase)
                == true;

        if (refusedForGood)
            return new(RefreshRejectionKind.Refused, safeCode);

        RefreshRejectionKind kind =
            response.StatusCode == HttpStatusCode.Unauthorized
                ? RefreshRejectionKind.Counted
                : RefreshRejectionKind.Transient;
        return new(kind, safeCode);
    }

    private static (string? Code, string? Message) ReadErrorFields(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);

            return (
                StringProperty(document.RootElement, "error"),
                StringProperty(document.RootElement, "message")
            );
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
