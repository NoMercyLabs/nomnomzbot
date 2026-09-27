// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
