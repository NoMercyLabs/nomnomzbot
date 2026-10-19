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

namespace NomNomzBot.Application.Trust.Dtos;

/// <summary>
/// Why a trust-tuning save was refused, as data the dashboard translates itself: <see cref="Key"/> is
/// the i18n key of the message, <see cref="Field"/> the request field to mark.
/// </summary>
public sealed record TrustPolicyViolation(string Key, string Field) : IResponseErrorData
{
    public const string WeightsMustSumToOne = "trust.policy.error.weightsMustSumToOne";
    public const string NotFinite = "trust.policy.error.notFinite";
    public const string WeightOutOfRange = "trust.policy.error.weightOutOfRange";
    public const string DecayOutOfRange = "trust.policy.error.decayOutOfRange";
    public const string FactorOutOfRange = "trust.policy.error.factorOutOfRange";
    public const string BoostOutOfRange = "trust.policy.error.boostOutOfRange";
    public const string PenaltyOutOfRange = "trust.policy.error.penaltyOutOfRange";
    public const string TiersMustAscend = "trust.policy.error.tiersMustAscend";
    public const string TierOutOfRange = "trust.policy.error.tierOutOfRange";
    public const string HeatHalfLifeOutOfRange = "trust.policy.error.heatHalfLifeOutOfRange";
    public const string HeatDeltaOutOfRange = "trust.policy.error.heatDeltaOutOfRange";
}
