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

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>Every problem a rejected project save found, returned as the failure response's <c>data</c>.</summary>
/// <param name="Errors">One entry per problem, in the order the build reported them.</param>
public sealed record ProjectBuildFailure(IReadOnlyList<ProjectBuildError> Errors)
    : IResponseErrorData;
