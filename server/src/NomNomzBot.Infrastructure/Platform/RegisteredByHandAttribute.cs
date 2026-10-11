// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// Marks a hosted worker that <c>AddInfrastructure</c> wires by hand, so the
/// <see cref="AssemblyScanExtensions.AddHostedWorkers"/> scan skips it. A worker is either scanned or
/// wired by hand, never both: both gives it two hosted entries, and the host then runs it twice.
/// Wire by hand only for a real reason — a start or stop order, a deployment-profile gate, or one
/// instance shared with a service interface.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RegisteredByHandAttribute : Attribute;
