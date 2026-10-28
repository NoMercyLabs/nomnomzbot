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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>Where the bot's display name comes from. A host (a test) registers its own.</summary>
public interface IBotIdentityDirectory
{
    Task<string> NameAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>What the running process says about itself: when it started and which build it is.</summary>
public interface IBotProcessInfo
{
    DateTimeOffset StartedAt { get; }

    /// <summary>The build version without the commit suffix: <c>1.42.0</c>.</summary>
    string Version { get; }
}
