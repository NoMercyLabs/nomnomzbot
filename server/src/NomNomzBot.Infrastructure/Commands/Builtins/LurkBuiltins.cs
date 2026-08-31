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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// Shared plumbing for <c>!lurk</c>/<c>!unlurk</c> (legacy parity, S068a): get-or-create the caller's
/// viewer row, flip <see cref="User.IsLurking"/>, persist it, and confirm in the channel's personality tone
/// via <see cref="IBuiltinResponseComposer"/>. There is no separate "session" table — the flag lives directly
/// on the existing per-viewer <see cref="User"/> row, the same place <see cref="UpdateUserInfoBuiltin"/>
/// writes its refreshed profile fields.
/// </summary>
public abstract class LurkBuiltinBase : IBuiltinCommand
{
    private readonly IUserService _users;
    private readonly IApplicationDbContext _db;
    private readonly IBuiltinResponseComposer _composer;
    private readonly bool _lurking;

    protected LurkBuiltinBase(
        IUserService users,
        IApplicationDbContext db,
        IBuiltinResponseComposer composer,
        bool lurking
    )
    {
        _users = users;
        _db = db;
        _composer = composer;
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

        string reply = await _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.Lurk.Key,
                Slot = _lurking
                    ? BuiltinResponseSlots.Lurk.Lurking
                    : BuiltinResponseSlots.Lurk.NotLurking,
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = _lurking
                    ? $"@{context.TriggeringUserDisplayName} is now lurking. Enjoy the stream!"
                    : $"@{context.TriggeringUserDisplayName} is no longer lurking. Welcome back!",
                Variables = new Dictionary<string, string>
                {
                    ["user"] = context.TriggeringUserDisplayName,
                },
            },
            ct
        );
        return Result.Success(reply);
    }
}

/// <summary>Chat builtin <c>!lurk</c> — marks the caller as lurking.</summary>
public sealed class LurkBuiltin : LurkBuiltinBase
{
    public LurkBuiltin(
        IUserService users,
        IApplicationDbContext db,
        IBuiltinResponseComposer composer
    )
        : base(users, db, composer, lurking: true) { }

    public override string BuiltinKey => "lurk";
}

/// <summary>Chat builtin <c>!unlurk</c> — clears the caller's lurking flag.</summary>
public sealed class UnlurkBuiltin : LurkBuiltinBase
{
    public UnlurkBuiltin(
        IUserService users,
        IApplicationDbContext db,
        IBuiltinResponseComposer composer
    )
        : base(users, db, composer, lurking: false) { }

    public override string BuiltinKey => "unlurk";
}
