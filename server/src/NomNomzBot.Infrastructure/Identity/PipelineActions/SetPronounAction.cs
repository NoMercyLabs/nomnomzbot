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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using Pronoun = NomNomzBot.Domain.Identity.Entities.Pronoun;

namespace NomNomzBot.Infrastructure.Identity.PipelineActions;

/// <summary>
/// Pipeline action <c>set_pronoun</c> — a moderator overriding ANOTHER viewer's pronoun (the legacy
/// <c>!setpronoun &lt;username&gt; &lt;pronoun&gt;</c> flow). Distinct from the self-service
/// <c>PUT /pronouns/me</c> surface (<see cref="IPronounSelfService"/> only ever acts on the caller): this
/// action resolves a Twitch login to a platform user, then calls the same service on the TARGET's behalf,
/// with <c>ManualOverride: true</c> so the pinned choice sticks until cleared.
///
/// Parameters:
///   username — Twitch login/display name to set the pronoun for (required; a leading @ is tolerated).
///              Supports {variable} substitution — e.g. "{args.0}".
///   pronoun  — pronoun catalog name (e.g. "he/him", "she/her", "they/them"), or "clear"/"reset" to remove
///              the override and return to automatic resolution. Supports {variable} substitution.
///
/// Usage example:
///   { "type": "set_pronoun", "username": "{args.0}", "pronoun": "{args.1}" }
/// </summary>
public sealed class SetPronounAction : ICommandAction
{
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly IUserService _users;
    private readonly IPronounSelfService _pronouns;
    private readonly IApplicationDbContext _db;

    public string ActionType => "set_pronoun";

    public SetPronounAction(
        ITwitchUsersApi twitchUsers,
        IUserService users,
        IPronounSelfService pronouns,
        IApplicationDbContext db
    )
    {
        _twitchUsers = twitchUsers;
        _users = users;
        _pronouns = pronouns;
        _db = db;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string username = ResolveVariable(
                action.GetString("username") ?? string.Empty,
                ctx.Variables
            )
            .Trim()
            .TrimStart('@');
        string pronounArg = ResolveVariable(
                action.GetString("pronoun") ?? string.Empty,
                ctx.Variables
            )
            .Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(pronounArg))
            return ActionResult.Failure(
                "set_pronoun: usage — !setpronoun <username> <pronoun|clear>"
            );

        Result<IReadOnlyList<TwitchUser>> lookup = await _twitchUsers.GetUsersByLoginsAsync(
            [username.ToLowerInvariant()],
            ctx.CancellationToken
        );
        TwitchUser? target = lookup.IsSuccess ? lookup.Value.FirstOrDefault() : null;
        if (target is null)
            return ActionResult.Failure($"set_pronoun: could not find user '{username}' on Twitch");

        Result<UserDto> user = await _users.GetOrCreateAsync(
            target.Id,
            target.Login,
            target.DisplayName,
            cancellationToken: ctx.CancellationToken
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid targetUserId))
            return ActionResult.Failure(
                user.ErrorMessage ?? "set_pronoun: could not resolve the target user"
            );

        if (
            pronounArg.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || pronounArg.Equals("reset", StringComparison.OrdinalIgnoreCase)
        )
        {
            await _pronouns.SetAsync(
                targetUserId,
                new()
                {
                    PronounId = 0,
                    AltPronounId = 0,
                    ManualOverride = false,
                },
                ctx.CancellationToken
            );
            return ActionResult.Success($"cleared pronoun override for {target.DisplayName}");
        }

        Pronoun? pronoun = await _db.Pronouns.FirstOrDefaultAsync(
            p => p.Name.ToLower() == pronounArg.ToLower(),
            ctx.CancellationToken
        );
        if (pronoun is null)
        {
            string available = string.Join(", ", _db.Pronouns.Select(p => p.Name));
            return ActionResult.Failure(
                $"set_pronoun: unknown pronoun '{pronounArg}'. Available: {available}"
            );
        }

        UserPronounDto? result = await _pronouns.SetAsync(
            targetUserId,
            new() { PronounId = pronoun.Id, ManualOverride = true },
            ctx.CancellationToken
        );
        if (result is null)
            return ActionResult.Failure("set_pronoun: failed to set pronoun");

        return ActionResult.Success($"set {target.DisplayName}'s pronoun to {pronoun.Name}");
    }

    private static string ResolveVariable(string value, IDictionary<string, string> variables)
    {
        if (!value.StartsWith('{') || !value.EndsWith('}'))
            return value;
        variables.TryGetValue(value[1..^1], out string? resolved);
        return resolved ?? string.Empty;
    }
}
