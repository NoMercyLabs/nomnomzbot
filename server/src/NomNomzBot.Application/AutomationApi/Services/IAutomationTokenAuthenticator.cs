// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.AutomationApi.Services;

/// <summary>
/// Data-plane credential check (automation-api.md §3): resolves a presented secret to its
/// <see cref="AutomationPrincipal"/> by hash lookup, rejecting expired, revoked, and soft-deleted
/// tokens, and touching <c>LastUsedAt</c>.
/// </summary>
public interface IAutomationTokenAuthenticator
{
    Task<Result<AutomationPrincipal>> AuthenticateAsync(
        string presentedSecret,
        CancellationToken ct = default
    );
}
