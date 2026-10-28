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

using FluentAssertions;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// A template key must hold the kind of value its name promises, on every trigger: <c>user</c> the name as the
/// platform shows it, <c>user.name</c> the login, <c>user.id</c> the platform id, and <c>target</c> the login of the
/// person an event is about. Every alert handler's sample goes through the live builder, so one data source covers
/// every event: a handler that leaves a key out, or fills one with another kind of value, fails here by name.
/// </summary>
public sealed class TriggerIdentityKindTests
{
    private static List<TriggerSample> SamplesWithAViewer() =>
        [
            .. TriggerSampleTests
                .AllSamples()
                .Where(sample =>
                    sample.Id.EndsWith("Event", StringComparison.Ordinal)
                    && sample.UserId is not null
                    && sample.Variables.ContainsKey("user")
                ),
        ];

    private static string Failures(Func<TriggerSample, IEnumerable<string>> check) =>
        string.Join(
            ", ",
            SamplesWithAViewer()
                .SelectMany(sample => check(sample).Select(f => $"{sample.Id}: {f}"))
        );

    [Fact]
    public void Every_trigger_with_a_viewer_seeds_user_user_name_and_user_id()
    {
        string missing = Failures(sample =>
            new[] { "user", "user.name", "user.id" }.Where(key =>
                !sample.Variables.TryGetValue(key, out string? value)
                || string.IsNullOrWhiteSpace(value)
            )
        );

        missing.Should().BeEmpty();
    }

    [Fact]
    public void Each_identity_key_holds_the_kind_of_value_its_name_promises()
    {
        string wrong = Failures(sample =>
        {
            IReadOnlyDictionary<string, string> v = sample.Variables;
            List<string> problems = [];
            if (v.GetValueOrDefault("user.id") != sample.UserId)
                problems.Add($"user.id is '{v.GetValueOrDefault("user.id")}', not the platform id");
            if (v.GetValueOrDefault("user") != sample.UserDisplayName)
                problems.Add($"user is '{v.GetValueOrDefault("user")}', not the display name");
            string login = v.GetValueOrDefault("user.name") ?? string.Empty;
            if (login != login.ToLowerInvariant() || login.Any(char.IsWhiteSpace))
                problems.Add($"user.name is '{login}', not a login");
            if (login == v.GetValueOrDefault("user.id"))
                problems.Add("user.name holds the id");
            if (v.GetValueOrDefault("target") != login)
                problems.Add($"target is '{v.GetValueOrDefault("target")}', not the login");
            if (v.GetValueOrDefault("target.displayname") != sample.UserDisplayName)
                problems.Add("target.displayname is not the display name");
            return problems;
        });

        wrong.Should().BeEmpty();
    }
}
