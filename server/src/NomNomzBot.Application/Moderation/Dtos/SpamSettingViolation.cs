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
/// One refused spam-defense setting as data the dashboard translates itself: <see cref="Key"/> is the i18n
/// key of the message, <see cref="Field"/> the request field to mark (camelCase, as in the JSON body).
/// <see cref="Minimum"/> and <see cref="Maximum"/> carry the accepted range of an out-of-range value.
/// </summary>
public sealed record SpamSettingViolation(
    string Key,
    string Field,
    double? Minimum = null,
    double? Maximum = null
)
{
    public const string OutOfRange = "spam.policy.error.outOfRange";
    public const string DequalifyNotBelowQualify = "spam.policy.error.dequalifyNotBelowQualify";
    public const string MaxWindowBelowWindow = "spam.policy.error.maxWindowBelowWindow";
    public const string LockdownMaxBelowLockdown = "spam.policy.error.lockdownMaxBelowLockdown";
}

/// <summary>
/// The failure data of a refused spam-defense policy save: every failing field. The API writes it into the
/// 400 response <c>data</c> (code <c>VALIDATION_FAILED</c>).
/// </summary>
public sealed record SpamSettingViolations(IReadOnlyList<SpamSettingViolation> Violations)
    : IResponseErrorData;
