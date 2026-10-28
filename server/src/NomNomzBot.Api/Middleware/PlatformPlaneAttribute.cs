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
/// Marks a controller (or action) as platform-plane: it acts across every tenant, naming the channel it
/// touches explicitly, so <see cref="TenantResolutionMiddleware"/> never resolves an ambient tenant for it.
/// Without this marker every request the dashboard sends carries the operator's active channel
/// (<c>X-Channel-Id</c>, or their own channel by fallback), and the tenant query filter then narrows every
/// cross-tenant admin read — EventSub health, webhook deliveries, the job queue, usage, error budgets — to
/// that one channel while the page claims to show the whole platform.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class PlatformPlaneAttribute : Attribute;
