// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
