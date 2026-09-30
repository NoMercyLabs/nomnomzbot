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
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reads a failed OAuth refresh-grant response and says whether it proves the refresh token is dead. Only
/// that proof may count toward <c>needs_reauth</c>: the RFC 6749 §5.2 <c>invalid_grant</c> error (Spotify,
/// Google, Kick) or Twitch's own 400 <c>"Invalid refresh token"</c>. A 5xx, a 429, a timeout or any other
/// answer says nothing about the grant, so the caller backs off and tries again later.
/// </summary>
internal static class OAuthRefreshRejection
{
    private const string InvalidGrant = "invalid_grant";
    private const string TwitchInvalidRefreshToken = "invalid refresh token";

    /// <summary>
    /// Records a rejected refresh on the vault: a dead grant counts toward needs_reauth, anything else only
    /// stamps the error so the refresher backs off.
    /// </summary>
    public static async Task RecordAsync(
        IIntegrationTokenVault vault,
        Guid connectionId,
        HttpResponseMessage response,
        string error,
        CancellationToken cancellationToken
    )
    {
        if (await IsDeadGrantAsync(response, cancellationToken))
            await vault.MarkRefreshFailureAsync(connectionId, error, cancellationToken);
        else
            await vault.MarkTransientRefreshFailureAsync(connectionId, error, cancellationToken);
    }

    public static async Task<bool> IsDeadGrantAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized))
            return false;

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return IsDeadGrantBody(body);
    }

    private static bool IsDeadGrantBody(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            return StringProperty(document.RootElement, "error") == InvalidGrant
                || StringProperty(document.RootElement, "message")
                    ?.Contains(TwitchInvalidRefreshToken, StringComparison.OrdinalIgnoreCase)
                    == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
