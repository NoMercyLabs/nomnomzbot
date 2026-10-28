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

using System.Text.Json.Serialization;

namespace NomNomzBot.Domain.Moderation.Enums;

/// <summary>
/// What a matched <c>ChatFilter</c> (moderation.md J.6, §2) does. <see cref="Delete"/>/<see cref="Timeout"/>
/// apply a fixed action; <see cref="Hold"/>/<see cref="Flag"/> route the message to review; and
/// <see cref="Escalate"/> defers the punishment to the per-channel escalation ladder (§3.11), which decides
/// warn/timeout/ban by the subject's running offense count. A filter never bans directly — a ban only ever
/// arrives through the ladder.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatFilterAction
{
    Delete,
    Timeout,
    Hold,
    Flag,
    Escalate,
}
