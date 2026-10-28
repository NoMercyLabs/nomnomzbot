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
