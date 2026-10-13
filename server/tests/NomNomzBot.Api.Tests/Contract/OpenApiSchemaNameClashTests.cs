// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Contract;

/// <summary>
/// Microsoft.AspNetCore.OpenApi names a schema by the short type name. Two different C# types that reach
/// the document under one name collapse into one schema, so a client reads the wrong shape. The committed
/// snapshot cannot show such a clash (it already holds the merged schema), so this test walks the controller
/// surface by reflection: action return types, <c>[ProducesResponseType]</c> types and parameters, then
/// every public property, generic argument and collection element below them.
/// </summary>
public sealed class OpenApiSchemaNameClashTests
{
    [Fact]
    public void No_two_types_reaching_the_OpenAPI_document_share_a_schema_name()
    {
        Dictionary<string, HashSet<Type>> byName = CollectSchemaTypes();

        List<string> clashes =
        [
            .. byName
                .Where(entry => entry.Value.Count > 1)
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry =>
                    $"{entry.Key}: "
                    + string.Join(
                        " vs ",
                        entry
                            .Value.Select(type => type.FullName)
                            .OrderBy(n => n, StringComparer.Ordinal)
                    )
                ),
        ];

        clashes
            .Should()
            .BeEmpty(
                "{0}",
                $"{clashes.Count} schema name(s) are shared by different types: "
                    + string.Join(" | ", clashes)
                    + ". Rename one side so the name says what it is"
            );
    }

    private static Dictionary<string, HashSet<Type>> CollectSchemaTypes()
    {
        Dictionary<string, HashSet<Type>> byName = new(StringComparer.Ordinal);
        HashSet<Type> visited = [];

        foreach (
            Type controller in typeof(BaseController)
                .Assembly.GetTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false }
                    && typeof(ControllerBase).IsAssignableFrom(type)
                )
        )
        {
            foreach (
                MethodInfo action in controller.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                if (action.IsSpecialName || action.GetCustomAttributes<NonActionAttribute>().Any())
                    continue;

                Visit(action.ReturnType, byName, visited);
                foreach (
                    ProducesResponseTypeAttribute produces in action.GetCustomAttributes<ProducesResponseTypeAttribute>()
                )
                    Visit(produces.Type, byName, visited);

                foreach (ParameterInfo parameter in action.GetParameters())
                    Visit(parameter.ParameterType, byName, visited);
            }
        }

        return byName;
    }

    private static void Visit(
        Type type,
        Dictionary<string, HashSet<Type>> byName,
        HashSet<Type> visited
    )
    {
        Type? inner = Unwrap(type);
        if (inner is null)
            return;
        if (inner != type)
        {
            Visit(inner, byName, visited);
            return;
        }

        if (type.IsGenericType && !IsFrameworkType(type))
        {
            foreach (Type argument in type.GetGenericArguments())
                Visit(argument, byName, visited);
        }

        if (IsFrameworkType(type) || type.IsGenericParameter || !visited.Add(type))
            return;

        string name = SchemaName(type);
        if (!byName.TryGetValue(name, out HashSet<Type>? types))
            byName[name] = types = [];
        types.Add(type);

        if (type.IsEnum)
            return;

        foreach (
            PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        )
        {
            if (property.GetIndexParameters().Length == 0)
                Visit(property.PropertyType, byName, visited);
        }
    }

    /// <summary>
    /// Peels Task, ActionResult, Nullable, arrays and collections down to the element type. Returns null when
    /// nothing nameable is left, the same type when it is itself a schema candidate.
    /// </summary>
    private static Type? Unwrap(Type type)
    {
        if (type == typeof(void) || type == typeof(string) || type.IsPrimitive)
            return null;
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            Type[] arguments = type.GetGenericArguments();
            if (
                definition == typeof(Task<>)
                || definition == typeof(ValueTask<>)
                || definition == typeof(ActionResult<>)
                || definition == typeof(Nullable<>)
            )
                return arguments[0];

            Type? dictionary = type.GetInterfaces()
                .Append(type)
                .FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                );
            if (dictionary is not null)
                return dictionary.GetGenericArguments()[1];

            if (
                typeof(IEnumerable).IsAssignableFrom(type)
                && type.GetInterfaces()
                    .Append(type)
                    .FirstOrDefault(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    )
                    is { } enumerable
            )
                return enumerable.GetGenericArguments()[0];
        }

        return type;
    }

    private static bool IsFrameworkType(Type type)
    {
        string? ns = type.Namespace;
        return ns is not null
            && (
                ns == "System"
                || ns.StartsWith("System.", StringComparison.Ordinal)
                || ns.StartsWith("Microsoft.", StringComparison.Ordinal)
                || ns.StartsWith("Newtonsoft.", StringComparison.Ordinal)
            );
    }

    private static string SchemaName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return name + "Of" + string.Concat(type.GetGenericArguments().Select(SchemaName));
    }
}
