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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// Shared plumbing for <c>!lurk</c>/<c>!unlurk</c> (legacy parity, S068a): get-or-create the caller's
/// viewer row, flip <see cref="User.IsLurking"/>, persist it, and confirm. There is no separate "session"
/// table — the flag lives directly on the existing per-viewer <see cref="User"/> row, the same place
/// <see cref="UpdateUserInfoBuiltin"/> writes its refreshed profile fields.
/// </summary>
public abstract class LurkBuiltinBase : IBuiltinCommand
{
    private readonly IUserService _users;
    private readonly IApplicationDbContext _db;
    private readonly bool _lurking;

    protected LurkBuiltinBase(IUserService users, IApplicationDbContext db, bool lurking)
    {
        _users = users;
        _db = db;
        _lurking = lurking;
    }

    public abstract string BuiltinKey { get; }
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        Result<UserDto> caller = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (caller.IsFailure)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} your account could not be resolved."
            );

        User? row = await _db.Users.FirstOrDefaultAsync(
            u => u.TwitchUserId == context.TriggeringUserId,
            ct
        );
        if (row is not null)
        {
            row.IsLurking = _lurking;
            await _db.SaveChangesAsync(ct);
        }

        return Result.Success(
            _lurking
                ? $"@{context.TriggeringUserDisplayName} is now lurking. Enjoy the stream!"
                : $"@{context.TriggeringUserDisplayName} is no longer lurking. Welcome back!"
        );
    }
}

/// <summary>Chat builtin <c>!lurk</c> — marks the caller as lurking.</summary>
public sealed class LurkBuiltin : LurkBuiltinBase
{
    public LurkBuiltin(IUserService users, IApplicationDbContext db)
        : base(users, db, lurking: true) { }

    public override string BuiltinKey => "lurk";
}

/// <summary>Chat builtin <c>!unlurk</c> — clears the caller's lurking flag.</summary>
public sealed class UnlurkBuiltin : LurkBuiltinBase
{
    public UnlurkBuiltin(IUserService users, IApplicationDbContext db)
        : base(users, db, lurking: false) { }

    public override string BuiltinKey => "unlurk";
}
