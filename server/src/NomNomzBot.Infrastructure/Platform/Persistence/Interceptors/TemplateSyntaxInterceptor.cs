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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

/// <summary>
/// Rewrites the unsupported <c>${variable}</c> placeholder form to <c>{variable}</c> on every
/// <see cref="TemplatedUserContentAttribute"/>-marked property as it is saved.
/// <para>
/// The owner's live <c>!lurk</c> reply rendered as <c>$Astro rolls up…</c>: the resolver substitutes
/// <c>{user}</c> and leaves the preceding <c>$</c> alone, so a template carried over from a
/// StreamElements-style setup keeps a stray dollar in front of every value. The engine is deliberately
/// NOT taught to swallow a leading <c>$</c> — this bot has an economy and <c>${points}</c> may mean a
/// literal dollar — so the syntax is corrected at rest instead. Owner's call: "templates should not
/// use ${} but just {}".
/// </para>
/// <para>
/// Done here rather than in each service for the same reason
/// <c>TemplatedUserContentSavePathGuardTests</c> reflects over that attribute instead of hand-listing
/// services: there are seven marked properties across six entities today, and a per-service sweep
/// covers only the ones someone remembered. A new templated field is normalised the moment it carries
/// the attribute, with nothing else to update.
/// </para>
/// </summary>
public sealed class TemplateSyntaxInterceptor : SaveChangesInterceptor
{
    // Reflection per entity CLR type, resolved once. The change tracker hands back the entity type on
    // every save, so recomputing the marked-property set each time would be pure waste.
    private static readonly Dictionary<Type, PropertyInfo[]> MarkedProperties = [];
    private static readonly Lock CacheLock = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not null)
            NormalizeTemplates(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is not null)
            NormalizeTemplates(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private static void NormalizeTemplates(DbContext context)
    {
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            foreach (PropertyInfo property in GetMarkedProperties(entry.Entity.GetType()))
            {
                object? value = property.GetValue(entry.Entity);

                switch (value)
                {
                    case string text:
                    {
                        string? normalized = TemplateSyntaxNormalizer.Normalize(text);
                        if (!string.Equals(normalized, text, StringComparison.Ordinal))
                            property.SetValue(entry.Entity, normalized);
                        break;
                    }
                    // Command.TemplateResponses and Timer.Messages are lists of templates, not one.
                    case List<string> texts:
                    {
                        for (int i = 0; i < texts.Count; i++)
                        {
                            string? normalized = TemplateSyntaxNormalizer.Normalize(texts[i]);
                            if (
                                normalized is not null
                                && !string.Equals(normalized, texts[i], StringComparison.Ordinal)
                            )
                                texts[i] = normalized;
                        }

                        break;
                    }
                }
            }
        }
    }

    private static PropertyInfo[] GetMarkedProperties(Type entityType)
    {
        lock (CacheLock)
        {
            if (MarkedProperties.TryGetValue(entityType, out PropertyInfo[]? cached))
                return cached;

            PropertyInfo[] marked =
            [
                .. entityType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p =>
                        p.CanRead
                        && p.CanWrite
                        && p.IsDefined(typeof(TemplatedUserContentAttribute), inherit: true)
                    ),
            ];

            MarkedProperties[entityType] = marked;
            return marked;
        }
    }
}
