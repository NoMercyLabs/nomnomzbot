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
