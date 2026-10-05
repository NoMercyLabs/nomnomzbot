// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation.Builtins;

/// <summary>
/// <c>!allow massban</c> — the channel lets a moderator's held mass ban run now instead of after the stream
/// (chat-client.md §3.5). Lead moderator and up: the broadcaster, an editor or a lead moderator decides what is
/// banned in their channel while it is live, not the moderator who asked.
/// </summary>
public sealed class AllowBuiltin : IBuiltinCommand
{
    private readonly IMassBanConsentService _consent;
    private readonly IBuiltinResponseComposer _composer;

    public AllowBuiltin(IMassBanConsentService consent, IBuiltinResponseComposer composer)
    {
        _consent = consent;
        _composer = composer;
    }

    public string BuiltinKey => BuiltinResponseSlots.Allow.Key;
    public int DefaultCooldownSeconds => 3;

    // LeadModerator on the unified ladder (Moderator 10, LeadModerator 20, Editor 30, Broadcaster 40).
    public int DefaultMinPermissionLevel => 20;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (!MassBanArgs.IsMassBan(context.Args))
            return Result.Success(
                await ReplyAsync(context, BuiltinResponseSlots.Allow.Usage, 0, ct)
            );

        MassBanDecision decision = await _consent.ApproveAsync(
            context.BroadcasterId,
            context.TriggeringUserDisplayName,
            ct
        );
        string slot =
            decision.Status == MassBanDecisionStatus.NothingPending
                ? BuiltinResponseSlots.Allow.NothingPending
                : BuiltinResponseSlots.Allow.Approved;
        return Result.Success(await ReplyAsync(context, slot, decision.Accounts, ct));
    }

    private Task<string> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        int accounts,
        CancellationToken ct
    ) =>
        _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.Allow.Key,
                Slot = slot,
                NeutralFallback =
                    ToneTemplateCatalog.ShippedTemplate(BuiltinResponseSlots.Allow.Key, slot)
                    ?? string.Empty,
                Variables = new Dictionary<string, string>
                {
                    ["prefix"] = context.CommandPrefix,
                    ["massban.count"] = accounts.ToString(CultureInfo.InvariantCulture),
                },
            },
            ct
        );
}
