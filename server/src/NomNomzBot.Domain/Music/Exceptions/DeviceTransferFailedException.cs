// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Exceptions;

namespace NomNomzBot.Domain.Music.Exceptions;

/// <summary>
/// A provider transport call to transfer playback to another device was rejected or failed —
/// most commonly Spotify's <c>PUT /me/player</c> returning 404 because the target
/// <c>device_id</c> is stale (Spotify Connect device ids rotate whenever a client reconnects).
/// The §3.5 seam member returns plain <c>Task</c>, so this exception is the in-seam carrier; the
/// first Result-typed surface (<c>IMusicService</c>) catches it and maps it to
/// <c>Failure("DEVICE_TRANSFER_FAILED")</c> — callers above that surface never see a throw, and
/// critically never see a false <c>Result.Success()</c> for a transfer that didn't happen.
/// </summary>
public sealed class DeviceTransferFailedException : DomainException
{
    public DeviceTransferFailedException(string provider, int? statusCode)
        : base(
            statusCode is { } code
                ? $"{provider} rejected the device transfer (HTTP {code})."
                : $"{provider} device transfer request failed."
        )
    {
        Provider = provider;
        StatusCode = statusCode;
    }

    /// <summary>The provider registry key that rejected the call (e.g. "spotify").</summary>
    public string Provider { get; }

    /// <summary>The provider's HTTP status code, when the call reached the provider at all.</summary>
    public int? StatusCode { get; }
}
