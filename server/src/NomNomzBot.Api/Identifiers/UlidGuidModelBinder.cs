// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NomNomzBot.Api.Identifiers;

/// <summary>
/// Binds a route/query <see cref="Guid"/> (or <c>Guid?</c>) from a ULID string OR a raw Guid string
/// (<see cref="GuidUlidCodec"/>). Registered ahead of the framework's simple-type binder so a 26-char ULID path
/// segment — which the built-in Guid parser rejects — resolves to the stored Guid. A missing value leaves an
/// optional binding unset; a present-but-unparseable value fails binding as a 400, matching the framework's Guid
/// binder. Body-, header-, and service-sourced values are left to their dedicated binders (see the provider).
/// </summary>
public sealed class UlidGuidModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        string modelName = bindingContext.ModelName;
        ValueProviderResult valueResult = bindingContext.ValueProvider.GetValue(modelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(modelName, valueResult);

        string? value = valueResult.FirstValue;
        if (string.IsNullOrEmpty(value))
        {
            // An empty value binds to null for Guid? and is a required-field error for a non-nullable Guid —
            // the same split the framework's SimpleTypeModelBinder makes.
            if (bindingContext.ModelType == typeof(Guid?))
                bindingContext.Result = ModelBindingResult.Success(null);
            else
                bindingContext.ModelState.TryAddModelError(
                    modelName,
                    $"The value for '{modelName}' is required."
                );

            return Task.CompletedTask;
        }

        if (GuidUlidCodec.TryDecode(value, out Guid id))
            bindingContext.Result = ModelBindingResult.Success(id);
        else
            bindingContext.ModelState.TryAddModelError(
                modelName,
                $"'{value}' is not a valid identifier."
            );

        return Task.CompletedTask;
    }
}
