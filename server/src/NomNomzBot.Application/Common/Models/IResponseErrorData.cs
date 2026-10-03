// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models;

/// <summary>
/// Marks a <see cref="Result.ErrorData"/> payload the API may write into the failure response's <c>data</c>.
/// Any other error data stays server side, so no failure body changes unless its service opts in.
/// </summary>
public interface IResponseErrorData;
