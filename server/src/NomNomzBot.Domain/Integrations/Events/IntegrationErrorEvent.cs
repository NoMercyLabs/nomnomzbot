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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Integrations.Events;

/// <summary>Published when an external integration encounters an error.</summary>
public sealed class IntegrationErrorEvent : DomainEventBase
{
    /// <summary>The name of the integration that failed.</summary>
    public required string IntegrationName { get; init; }

    /// <summary>A short description of what went wrong.</summary>
    public required string ErrorMessage { get; init; }
}
