// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Rewards.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Enumerates <see cref="PipelineActionFieldKind"/> FROM THE ENUM (never a hand-written list of kinds), then
/// discovers every <see cref="IPipelineOptionProvider"/> concrete type in the Infrastructure assembly by
/// reflection — the exact same scan <c>AddImplementationsOf&lt;IPipelineOptionProvider&gt;</c> performs at
/// startup — and reads each one's declared <see cref="IPipelineOptionProvider.Kind"/> off a bare-constructed
/// instance (every provider's <c>Kind</c> getter returns a literal and touches no injected dependency, so a
/// null-arg construction is safe purely for this read). Fails if any resource-picker kind has no discovered
/// provider (S-RICH-PICKERS). Only <c>Text</c>/<c>Number</c>/<c>Boolean</c>/<c>Enum</c>/<c>ResourceId</c> are
/// excluded — the enum's own doc comment names <c>ResourceId</c> as the fallback for "no dedicated picker kind
/// yet"; the other three render a plain control, never a lookup. <c>KeyValueMap</c> (S-PIPE-TREE-d2b(a)) is
/// likewise a plain control — a labelled name→value editor, never a backend lookup — so it is excluded here
/// too. No provider type is named by hand here, so adding a new picker kind without a provider — or deleting
/// a provider file — both fail this test.
/// </summary>
public sealed class PipelineOptionProviderGuardTests
{
    private static readonly HashSet<PipelineActionFieldKind> NonPickerKinds =
    [
        PipelineActionFieldKind.Text,
        PipelineActionFieldKind.Number,
        PipelineActionFieldKind.Boolean,
        PipelineActionFieldKind.Enum,
        PipelineActionFieldKind.ResourceId,
        PipelineActionFieldKind.KeyValueMap,
    ];

    private static IEnumerable<Type> DiscoverProviderTypes(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(t =>
                t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                && typeof(IPipelineOptionProvider).IsAssignableFrom(t)
            );

    private static PipelineActionFieldKind KindOf(Type providerType)
    {
        ConstructorInfo ctor = providerType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        object?[] args = [.. ctor.GetParameters().Select(_ => (object?)null)];
        IPipelineOptionProvider instance = (IPipelineOptionProvider)ctor.Invoke(args);
        return instance.Kind;
    }

    [Fact]
    public void Every_resource_picker_kind_enumerated_from_the_enum_has_a_discovered_provider()
    {
        HashSet<PipelineActionFieldKind> pickerKinds =
        [
            .. Enum.GetValues<PipelineActionFieldKind>().Where(k => !NonPickerKinds.Contains(k)),
        ];

        List<Type> discovered = [.. DiscoverProviderTypes(typeof(RewardOptionProvider).Assembly)];
        HashSet<PipelineActionFieldKind> registeredKinds = [.. discovered.Select(KindOf)];

        IReadOnlyList<PipelineActionFieldKind> missing =
        [
            .. pickerKinds.Where(k => !registeredKinds.Contains(k)),
        ];

        Assert.True(
            missing.Count == 0,
            "Picker kind(s) with no registered IPipelineOptionProvider: "
                + string.Join(", ", missing.Select(k => k.ToWireName()))
        );
    }

    [Fact]
    public void Fixture_a_deliberately_missing_provider_is_caught_by_the_same_check()
    {
        // Proves the guard above actually catches a gap: simulate a new PipelineActionFieldKind value that no
        // provider covers by discovering the real set and then dropping one — the check must then report it.
        List<Type> discovered = [.. DiscoverProviderTypes(typeof(RewardOptionProvider).Assembly)];
        List<Type> withOneMissing = [.. discovered.Where(t => t != typeof(RewardOptionProvider))];

        HashSet<PipelineActionFieldKind> pickerKinds =
        [
            .. Enum.GetValues<PipelineActionFieldKind>().Where(k => !NonPickerKinds.Contains(k)),
        ];
        HashSet<PipelineActionFieldKind> registeredKinds = [.. withOneMissing.Select(KindOf)];

        IReadOnlyList<PipelineActionFieldKind> missing =
        [
            .. pickerKinds.Where(k => !registeredKinds.Contains(k)),
        ];

        Assert.Contains(PipelineActionFieldKind.Reward, missing);
    }

    [Fact]
    public void Registry_throws_on_two_providers_claiming_the_same_kind()
    {
        FakeOptionProvider first = new(PipelineActionFieldKind.Reward);
        FakeOptionProvider second = new(PipelineActionFieldKind.Reward);

        Assert.Throws<InvalidOperationException>(() =>
            new NomNomzBot.Infrastructure.Platform.Pipeline.PipelineOptionRegistry([first, second])
        );
    }

    [Fact]
    public async Task Registry_reports_a_failure_for_an_unregistered_kind()
    {
        NomNomzBot.Infrastructure.Platform.Pipeline.PipelineOptionRegistry registry = new([]);

        Result<PipelineOptionListResult> result = await registry.GetOptionsAsync(
            PipelineActionFieldKind.Reward,
            Guid.NewGuid(),
            search: null,
            new PaginationParams(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("UNSUPPORTED_KIND", result.ErrorCode);
    }

    private sealed class FakeOptionProvider : IPipelineOptionProvider
    {
        public FakeOptionProvider(PipelineActionFieldKind kind) => Kind = kind;

        public PipelineActionFieldKind Kind { get; }

        public Task<Result<PipelineOptionListResult>> GetOptionsAsync(
            Guid broadcasterId,
            string? search,
            PaginationParams pagination,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(PipelineOptionListResult.Of([], 0)));
    }
}
