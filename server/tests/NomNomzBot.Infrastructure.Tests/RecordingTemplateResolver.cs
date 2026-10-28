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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Tests;

/// <summary>
/// The real <see cref="ITemplateResolver"/> from <see cref="TestTemplateResolver"/> with one addition: it keeps
/// a copy of each seed bag it was given, so a test can assert what the caller seeded. It fills every template
/// with the real resolver and never with a copy of the substitution.
/// </summary>
internal sealed class RecordingTemplateResolver : ITemplateResolver
{
    private readonly ITemplateResolver _inner = TestTemplateResolver.Create();

    public List<Dictionary<string, string>> Seeds { get; } = [];

    public Task<string> ResolveAsync(
        string template,
        IDictionary<string, string> seedVariables,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Seeds.Add(new Dictionary<string, string>(seedVariables, StringComparer.OrdinalIgnoreCase));
        return _inner.ResolveAsync(template, seedVariables, broadcasterId, cancellationToken);
    }

    public string Resolve(string template, IDictionary<string, string> variables) =>
        _inner.Resolve(template, variables);
}
