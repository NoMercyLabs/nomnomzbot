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
/// Supplies <see cref="UlidGuidModelBinder"/> for <see cref="Guid"/> and <c>Guid?</c> models. Inserted at the front
/// of the binder-provider list so it precedes the built-in simple-type binder; it therefore explicitly declines the
/// sources whose dedicated providers normally run first (body, services, header, form files) so a <c>[FromBody]</c>
/// / <c>[FromHeader]</c> Guid is still bound by them, not stolen by the value-provider path.
/// </summary>
public sealed class UlidGuidModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Type modelType = context.Metadata.ModelType;
        if (modelType != typeof(Guid) && modelType != typeof(Guid?))
            return null;

        BindingSource? source =
            context.BindingInfo?.BindingSource ?? context.Metadata.BindingSource;
        if (
            source == BindingSource.Body
            || source == BindingSource.Services
            || source == BindingSource.Header
            || source == BindingSource.FormFile
            || source == BindingSource.Special
        )
            return null;

        return new UlidGuidModelBinder();
    }
}
