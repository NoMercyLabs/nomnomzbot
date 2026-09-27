// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>A platform with no admin reply texts: every slot keeps its shipped wording.</summary>
internal sealed class NoPlatformBuiltinReplies : IPlatformBuiltinReplyDefaults
{
    public static readonly NoPlatformBuiltinReplies Instance = new();

    public Task<string?> GetAsync(string builtinKey, string slot, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task InvalidateAsync(CancellationToken ct = default) => Task.CompletedTask;
}
