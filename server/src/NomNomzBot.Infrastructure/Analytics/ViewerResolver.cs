// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// The shared viewer-identity resolution every per-viewer projection (M.1/M.4/M.7) needs ([[viewer-identity-is-user]]):
/// a viewer IS a non-setup User, so it get-or-creates the Users row, then get-or-creates the per-viewer profile
/// anchor (identity only — the M.1 aggregates are owned by <c>ViewerProfileProjection</c>). Returns the tracked
/// <see cref="ViewerProfile"/> (its v7 Id is set on construction, so callers get the FK without a save), or null
/// when the viewer identity cannot be resolved. The caller saves.
/// </summary>
public sealed class ViewerResolver(IApplicationDbContext db, IUserService userService)
{
    public async Task<ViewerProfile?> ResolveAsync(
        Guid broadcasterId,
        string provider,
        string externalUserId,
        string login,
        string display,
        CancellationToken ct = default
    )
    {
        Result<UserDto> user = await userService.GetOrCreateAsync(
            externalUserId,
            login,
            display,
            provider,
            ct
        );
        if (user.IsFailure)
            return null;
        if (!Guid.TryParse(user.Value.Id, out Guid viewerUserId))
            return null;

        ViewerProfile? profile = await db.ViewerProfiles.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcasterId && p.ViewerUserId == viewerUserId,
            ct
        );
        if (profile is null)
        {
            profile = new ViewerProfile
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = viewerUserId,
                // Holds the viewer's platform-native id under the tenant channel's provider (the column
                // name predates multi-platform; for a YouTube tenant this is the YouTube channel id).
                ViewerTwitchUserId = externalUserId,
            };
            db.ViewerProfiles.Add(profile);
        }
        return profile;
    }

    /// <summary>
    /// Extracts a viewer's identity from an activity event payload, tolerating each event's field naming (chat:
    /// UserLogin/UserDisplayName; command: Username; reward: UserDisplayName). Username is the normalized display
    /// name (the Twitch login IS that); when no login field is present it is derived from the display, never the
    /// platform-native id. <c>Provider</c> is read from the payload when the event carries one (the canonical
    /// chat fact does) and defaults to Twitch for the single-platform events. Returns null when the payload is
    /// unparseable or carries no user id.
    /// </summary>
    public static (
        string Provider,
        string ExternalUserId,
        string Login,
        string Display
    )? ParseIdentity(string payloadJson)
    {
        JObject? payload = TryParse(payloadJson);
        return payload is null ? null : ParseIdentity(payload);
    }

    public static (
        string Provider,
        string ExternalUserId,
        string Login,
        string Display
    )? ParseIdentity(JObject payload)
    {
        string? externalUserId = payload["UserId"]?.Value<string>();
        if (string.IsNullOrEmpty(externalUserId))
            return null;

        string provider = payload["Provider"]?.Value<string>() ?? AuthEnums.Platform.Twitch;
        string display =
            payload["UserDisplayName"]?.Value<string>()
            ?? payload["UserLogin"]?.Value<string>()
            ?? payload["Username"]?.Value<string>()
            ?? externalUserId;
        string login =
            payload["UserLogin"]?.Value<string>()
            ?? payload["Username"]?.Value<string>()
            ?? display.ToLowerInvariant();
        return (provider, externalUserId, login, display);
    }

    public static JObject? TryParse(string payloadJson)
    {
        try
        {
            return JObject.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
