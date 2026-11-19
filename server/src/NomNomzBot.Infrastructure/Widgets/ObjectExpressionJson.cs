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

using System.Text.Json;
using Acornima;
using Acornima.Ast;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Turns a parsed JavaScript object literal into JSON without running it. Only literals, objects, arrays and
/// templates without expressions convert; any other node is a <see cref="Problem"/> that names the node.
/// </summary>
internal static class ObjectExpressionJson
{
    /// <summary>The node that is not a plain value, and why.</summary>
    public readonly record struct Problem(Node Node, string Message);

    public static (string? Json, Problem? Problem) Convert(ObjectExpression literal)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            Problem? problem = WriteValue(writer, literal);
            if (problem is not null)
                return (null, problem);
        }

        return (System.Text.Encoding.UTF8.GetString(stream.ToArray()), null);
    }

    private static Problem? WriteValue(Utf8JsonWriter writer, Node node)
    {
        switch (node)
        {
            case ObjectExpression obj:
                return WriteObject(writer, obj);
            case ArrayExpression array:
                return WriteArray(writer, array);
            case TemplateLiteral { Expressions.Count: 0 } template:
                writer.WriteStringValue(template.Quasis[0].Value.Cooked);
                return null;
            case UnaryExpression
            {
                Operator: Operator.UnaryNegation or Operator.UnaryPlus,
                Argument: NumericLiteral number,
            } unary:
                writer.WriteNumberValue(
                    unary.Operator == Operator.UnaryNegation ? -number.Value : number.Value
                );
                return null;
            case Literal literal:
                return WriteLiteral(writer, literal);
            default:
                return NotLiteral(node);
        }
    }

    private static Problem? WriteLiteral(Utf8JsonWriter writer, Literal literal)
    {
        switch (literal)
        {
            case StringLiteral text:
                writer.WriteStringValue(text.Value);
                return null;
            case NumericLiteral number:
                writer.WriteNumberValue(number.Value);
                return null;
            case BooleanLiteral flag:
                writer.WriteBooleanValue(flag.Value);
                return null;
            case NullLiteral:
                writer.WriteNullValue();
                return null;
            default:
                return NotLiteral(literal);
        }
    }

    private static Problem? WriteObject(Utf8JsonWriter writer, ObjectExpression obj)
    {
        writer.WriteStartObject();
        foreach (Node member in obj.Properties)
        {
            if (
                member
                is not Property { Computed: false, Method: false, Kind: PropertyKind.Init } property
            )
                return NotLiteral(member);

            string? key = KeyOf(property.Key);
            if (key is null)
                return NotLiteral(property.Key);

            writer.WritePropertyName(key);
            Problem? problem = WriteValue(writer, property.Value);
            if (problem is not null)
                return problem;
        }

        writer.WriteEndObject();
        return null;
    }

    private static Problem? WriteArray(Utf8JsonWriter writer, ArrayExpression array)
    {
        writer.WriteStartArray();
        foreach (Node? element in array.Elements)
        {
            if (element is null)
                return NotLiteral(array);

            Problem? problem = WriteValue(writer, element);
            if (problem is not null)
                return problem;
        }

        writer.WriteEndArray();
        return null;
    }

    private static string? KeyOf(Node key) =>
        key switch
        {
            Identifier identifier => identifier.Name,
            StringLiteral text => text.Value,
            NumericLiteral number => number.Raw,
            _ => null,
        };

    private static Problem NotLiteral(Node node)
    {
        string what = node is Identifier identifier ? $"'{identifier.Name}'" : node.Type.ToString();
        return new(
            node,
            $"{what} is not a literal value; defineSettings needs plain values the server can read without running your code."
        );
    }
}
