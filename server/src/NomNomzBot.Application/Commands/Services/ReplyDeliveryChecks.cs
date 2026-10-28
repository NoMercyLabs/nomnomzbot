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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The one check every surface runs before it stores a <see cref="ReplyDelivery"/> (commands, timers, chat
/// triggers, event responses), so a value the overlay cannot honor is refused at the door, never clamped.
/// </summary>
public static class ReplyDeliveryChecks
{
    /// <summary>Success for a missing or storable delivery; a validation failure naming the problem otherwise.</summary>
    public static Result Check(this ReplyDelivery? delivery) =>
        delivery?.Problem() is { } problem ? Errors.ValidationFailed(problem) : Result.Success();
}
