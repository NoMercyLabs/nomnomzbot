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
