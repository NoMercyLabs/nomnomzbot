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
using FluentAssertions;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The owner's live <c>!lurk</c> reply rendered as <c>$Astro rolls up…</c> — a template stored as
/// <c>${user}</c>, which the resolver renders by substituting <c>{user}</c> and leaving the <c>$</c>.
/// <see cref="Infrastructure.Platform.Persistence.Interceptors.TemplateSyntaxInterceptor"/> corrects
/// the syntax at rest, on every <see cref="TemplatedUserContentAttribute"/>-marked property.
/// </summary>
public class TemplateSyntaxInterceptorTests
{
    /// <summary>
    /// The coverage claim, enumerated from the real domain model rather than asserted. A per-service
    /// sweep of this bug would have covered the three fields someone thought of and missed the rest;
    /// this fails the moment a templated field exists that the interceptor cannot normalise.
    /// </summary>
    [Fact]
    public void Every_templated_property_in_the_domain_is_a_shape_the_interceptor_handles()
    {
        List<string> unhandled = [];

        IEnumerable<Type> entityTypes = typeof(TemplatedUserContentAttribute)
            .Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract);

        int marked = 0;
        foreach (Type entity in entityTypes)
        {
            foreach (
                PropertyInfo property in entity.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                )
            )
            {
                if (!property.IsDefined(typeof(TemplatedUserContentAttribute), inherit: true))
                    continue;

                marked++;

                bool handled =
                    property.PropertyType == typeof(string)
                    || property.PropertyType == typeof(List<string>);
                if (!handled)
                    unhandled.Add($"{entity.Name}.{property.Name} : {property.PropertyType.Name}");

                if (!property.CanWrite)
                    unhandled.Add($"{entity.Name}.{property.Name} (no setter — cannot normalise)");
            }
        }

        // Widget/Vue source is NOT templated user content and must never be marked as such: it is full
        // of JavaScript template literals (`${count} viewers`), and normalising those would corrupt the
        // code. Verified against the owner's real database, where the only two rows containing "${" are
        // WidgetGalleryItems.SourceCode — correctly outside this interceptor's reach.
        typeof(NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem)
            .GetProperty(nameof(NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem.SourceCode))!
            .IsDefined(typeof(TemplatedUserContentAttribute), inherit: true)
            .Should()
            .BeFalse(
                "widget source contains JS template literals; marking it templated would rewrite ${x} inside code"
            );

        marked
            .Should()
            .BeGreaterThan(
                0,
                "the attribute is the seam this interceptor keys on — zero marked properties would mean the guard proves nothing"
            );
        unhandled
            .Should()
            .BeEmpty(
                "every [TemplatedUserContent] property must be a shape TemplateSyntaxInterceptor can rewrite; "
                    + "add the shape to the interceptor's switch when a new one appears"
            );
    }
}
