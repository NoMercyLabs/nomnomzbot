// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The log level for a failed snapshot read. A <c>missing_scope</c> failure is a known state the grant-gap inbox
/// item already surfaces, so it logs at Debug on every pass; any other failure stays a Warning.
/// </summary>
internal static class TwitchSnapshotLogLevel
{
    public static LogLevel For(string? errorCode) =>
        errorCode == TwitchErrorCodes.MissingScope ? LogLevel.Debug : LogLevel.Warning;
}
