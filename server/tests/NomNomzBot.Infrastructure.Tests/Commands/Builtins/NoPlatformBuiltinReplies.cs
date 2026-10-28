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
