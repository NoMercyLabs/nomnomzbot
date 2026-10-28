// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
