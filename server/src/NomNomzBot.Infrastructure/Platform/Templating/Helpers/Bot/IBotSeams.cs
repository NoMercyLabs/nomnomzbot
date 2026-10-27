// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
