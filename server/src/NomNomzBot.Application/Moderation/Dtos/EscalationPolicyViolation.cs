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

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// Why an escalation policy save was refused, as data the dashboard translates itself:
/// <see cref="Key"/> is the i18n key of the message, <see cref="Field"/> the request field to mark
/// (<c>offenseWindowHours</c>, <c>defaultTimeoutSeconds</c>, <c>ladder</c>, or a rung such as
/// <c>ladder[2].timeoutSeconds</c>).
/// </summary>
public sealed record EscalationPolicyViolation(string Key, string Field) : IResponseErrorData
{
    public const string WindowOutOfRange = "moderation.escalation.error.windowOutOfRange";
    public const string TimeoutOutOfRange = "moderation.escalation.error.timeoutOutOfRange";
    public const string LadderEmpty = "moderation.escalation.error.ladderEmpty";
    public const string LadderTooLong = "moderation.escalation.error.ladderTooLong";
    public const string StepsMustAscend = "moderation.escalation.error.stepsMustAscend";
    public const string UnknownAction = "moderation.escalation.error.unknownAction";
    public const string TimeoutStepOutOfRange = "moderation.escalation.error.timeoutStepOutOfRange";
}
