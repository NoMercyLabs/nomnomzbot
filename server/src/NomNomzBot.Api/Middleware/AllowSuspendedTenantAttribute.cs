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

namespace NomNomzBot.Api.Middleware;

/// <summary>
/// Marks an endpoint the people of a suspended channel still reach: the channel list, which carries the
/// suspension so the dashboard can show why. <see cref="TenantResolutionMiddleware"/> skips its suspension
/// refusal for it; every other channel endpoint stays dark while the suspension lasts.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowSuspendedTenantAttribute : Attribute;
