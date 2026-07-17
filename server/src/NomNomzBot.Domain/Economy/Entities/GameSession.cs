// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// One round of a stateful, multi-participant overlay game (live-games.md K.9a) — the session layer the
/// instant-resolve <c>GamePlay</c> flow does not cover. <c>StateJson</c> snapshots the engine/game state on
/// every transition (crash recovery + overlay frame); <c>OutcomeJson</c> holds the resolved summary. At most
/// one non-terminal (<c>Lobby</c>/<c>Running</c>/<c>Resolving</c>) row exists per channel — service-enforced
/// (D7), not a DB unique, since terminal rows accumulate as history.
/// </summary>
public class GameSession : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid GameConfigId { get; set; }
    public string GameType { get; set; } = null!;
    public GameSessionStatus Status { get; set; } = GameSessionStatus.Lobby;
    public Guid? StartedByUserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? JoinClosesAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int ParticipantCount { get; set; }
    public string? StateJson { get; set; }
    public string? OutcomeJson { get; set; }
    public string? CancelReason { get; set; }
}
