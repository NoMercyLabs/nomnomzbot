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

namespace NomNomzBot.Application.Identity.Services;

public interface IGdprService
{
    Task<Result<string>> ExportUserDataAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result> DeleteUserDataAsync(string userId, CancellationToken cancellationToken = default);
}
