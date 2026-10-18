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
/// Marks an endpoint the people of a suspended channel still reach: the channel list, which carries the
/// suspension so the dashboard can show why. <see cref="TenantResolutionMiddleware"/> skips its suspension
/// refusal for it; every other channel endpoint stays dark while the suspension lasts.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowSuspendedTenantAttribute : Attribute;
