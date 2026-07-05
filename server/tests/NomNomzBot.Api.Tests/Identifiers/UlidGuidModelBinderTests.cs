// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Primitives;
using NomNomzBot.Api.Identifiers;

namespace NomNomzBot.Api.Tests.Identifiers;

/// <summary>
/// The route/query binding path. A <see cref="Guid"/> action parameter binds from a ULID string OR a raw Guid
/// string; a missing/empty value is null for <c>Guid?</c> and a binding error for a non-nullable Guid; a malformed
/// value is a binding error (a 400, never a 500 or a silent <see cref="Guid.Empty"/>).
/// </summary>
public sealed class UlidGuidModelBinderTests
{
    private static readonly Guid KnownId = Guid.Parse("0192a000-0000-7000-8000-000000000d01");

    private sealed class FixedValueProvider(string key, ValueProviderResult result) : IValueProvider
    {
        public bool ContainsPrefix(string prefix) =>
            string.Equals(prefix, key, StringComparison.OrdinalIgnoreCase);

        public ValueProviderResult GetValue(string requested) =>
            string.Equals(requested, key, StringComparison.OrdinalIgnoreCase)
                ? result
                : ValueProviderResult.None;
    }

    private static DefaultModelBindingContext ContextFor(Type modelType, ValueProviderResult value)
    {
        return new DefaultModelBindingContext
        {
            ModelName = "id",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new FixedValueProvider("id", value),
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
        };
    }

    private static async Task<DefaultModelBindingContext> BindAsync(Type modelType, string? value)
    {
        ValueProviderResult result = value is null
            ? ValueProviderResult.None
            : new ValueProviderResult(new StringValues(value));
        DefaultModelBindingContext context = ContextFor(modelType, result);
        await new UlidGuidModelBinder().BindModelAsync(context);
        return context;
    }

    [Fact]
    public async Task Binds_a_ulid_route_value_to_the_stored_guid()
    {
        DefaultModelBindingContext context = await BindAsync(
            typeof(Guid),
            GuidUlidCodec.Encode(KnownId)
        );

        context.Result.IsModelSet.Should().BeTrue();
        context.Result.Model.Should().Be(KnownId);
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task Binds_a_raw_guid_route_value_too()
    {
        DefaultModelBindingContext context = await BindAsync(typeof(Guid), KnownId.ToString());

        context.Result.IsModelSet.Should().BeTrue();
        context.Result.Model.Should().Be(KnownId);
    }

    [Fact]
    public async Task Malformed_value_is_a_binding_error_not_a_bound_empty_guid()
    {
        DefaultModelBindingContext context = await BindAsync(typeof(Guid), "not-an-id");

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["id"]!.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Empty_value_binds_nullable_guid_to_null()
    {
        DefaultModelBindingContext context = await BindAsync(typeof(Guid?), "");

        context.Result.IsModelSet.Should().BeTrue();
        context.Result.Model.Should().BeNull();
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task Empty_value_is_a_binding_error_for_a_required_guid()
    {
        DefaultModelBindingContext context = await BindAsync(typeof(Guid), "");

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_value_leaves_the_binding_unset()
    {
        DefaultModelBindingContext context = await BindAsync(typeof(Guid?), null);

        context.Result.IsModelSet.Should().BeFalse();
    }

    [Fact]
    public void Provider_declines_body_sourced_guids_but_binds_route_guids()
    {
        UlidGuidModelBinderProvider provider = new();

        provider.GetBinder(ProviderContext(typeof(Guid), BindingSource.Body)).Should().BeNull();
        provider.GetBinder(ProviderContext(typeof(Guid), BindingSource.Header)).Should().BeNull();
        provider
            .GetBinder(ProviderContext(typeof(Guid), BindingSource.Path))
            .Should()
            .BeOfType<UlidGuidModelBinder>();
        provider
            .GetBinder(ProviderContext(typeof(Guid?), BindingSource.Query))
            .Should()
            .BeOfType<UlidGuidModelBinder>();
        provider.GetBinder(ProviderContext(typeof(string), BindingSource.Path)).Should().BeNull();
    }

    private static TestBinderProviderContext ProviderContext(
        Type modelType,
        BindingSource source
    ) => new(new EmptyModelMetadataProvider().GetMetadataForType(modelType), source);

    private sealed class TestBinderProviderContext(ModelMetadata metadata, BindingSource source)
        : ModelBinderProviderContext
    {
        public override BindingInfo BindingInfo { get; } = new() { BindingSource = source };
        public override ModelMetadata Metadata { get; } = metadata;
        public override IModelMetadataProvider MetadataProvider { get; } =
            new EmptyModelMetadataProvider();

        public override IModelBinder CreateBinder(ModelMetadata metadata) =>
            throw new NotSupportedException();
    }
}
