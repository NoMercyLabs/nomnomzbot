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

using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// A hand-built <see cref="IEventCatalog"/> — feeds the emitter a controlled descriptor set so the PII /
/// NotExposed / Internal-tier / enum behaviour can be asserted precisely, without scanning the whole Domain
/// assembly.
/// </summary>
internal sealed class FakeEventCatalog : IEventCatalog
{
    public FakeEventCatalog(params EventDescriptor[] descriptors) => Descriptors = descriptors;

    public IReadOnlyList<EventDescriptor> Descriptors { get; }
}

/// <summary>Enum fixture — proves an enum property becomes a string-literal union.</summary>
public enum SdkFixtureColor
{
    Red,
    Green,
    Blue,
}

/// <summary>Nested value-object fixture — proves a nested record becomes its own interface / object schema.</summary>
public sealed class SdkFixtureNested
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// The main emitter fixture — a normal field, an enum, a nullable, a collection of a nested record, a
/// <c>[Pii]</c> field, and a <c>[NotExposed]</c> field. Public tier so it also appears in the widget context.
/// </summary>
[Event("nnztest.pii.sample", EventVisibility.Public)]
public sealed class PiiSampleEvent : DomainEventBase
{
    public required string Normal { get; init; }
    public SdkFixtureColor Color { get; init; }
    public string? OptionalNote { get; init; }
    public IReadOnlyList<SdkFixtureNested> Items { get; init; } = [];

    [Pii]
    public string? SecretEmail { get; init; }

    [NotExposed]
    public Guid InternalId { get; init; }
}

/// <summary>Internal-tier fixture — must never appear in any generated context.</summary>
[Event("nnztest.internal.sample", EventVisibility.Internal)]
public sealed class InternalSampleEvent : DomainEventBase
{
    public required string Whatever { get; init; }
}

/// <summary>Duplicate-wire-name fixture pair — proves the catalog fails fast on a collision.</summary>
[Event("nnztest.dup")]
public sealed class DupOneEvent : DomainEventBase
{
    public int A { get; init; }
}

[Event("nnztest.dup")]
public sealed class DupTwoEvent : DomainEventBase
{
    public int B { get; init; }
}
