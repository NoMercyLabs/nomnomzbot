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

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>Every problem a rejected project save found, returned as the failure response's <c>data</c>.</summary>
/// <param name="Errors">One entry per problem, in the order the build reported them.</param>
public sealed record ProjectBuildFailure(IReadOnlyList<ProjectBuildError> Errors)
    : IResponseErrorData;
