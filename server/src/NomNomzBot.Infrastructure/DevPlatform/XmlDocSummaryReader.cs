// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Reads the compiler's XML documentation file of an assembly and returns the <c>&lt;summary&gt;</c> of a type or
/// property as one plain line that is safe inside a JSDoc comment. The generated SDK types use it so that hovering
/// an event or field in the editor shows what it is. A missing XML file is not an error: the editor then shows
/// types without help text.
/// </summary>
internal sealed partial class XmlDocSummaryReader
{
    // One reader per assembly, loaded once. A null value records that the file is missing, so the warning
    // is logged once.
    private static readonly ConcurrentDictionary<Assembly, XmlDocSummaryReader?> Readers = new();

    private readonly Dictionary<string, string> _summaries;

    private XmlDocSummaryReader(Dictionary<string, string> summaries)
    {
        _summaries = summaries;
    }

    public static XmlDocSummaryReader? ForAssembly(Assembly assembly, ILogger? logger) =>
        Readers.GetOrAdd(assembly, a => Load(a, logger));

    public static XmlDocSummaryReader Parse(string xml)
    {
        Dictionary<string, string> summaries = new(StringComparer.Ordinal);
        XDocument document = XDocument.Parse(xml);
        foreach (XElement member in document.Descendants("member"))
        {
            string? id = member.Attribute("name")?.Value;
            XElement? summary = member.Element("summary");
            if (id is null || summary is null)
                continue;

            string text = PlainText(summary);
            if (text.Length > 0)
                summaries[id] = text;
        }
        return new XmlDocSummaryReader(summaries);
    }

    /// <summary>The plain summary of a documentation member id such as <c>T:Ns.Type</c> or <c>P:Ns.Type.Prop</c>.</summary>
    public string? Summary(string memberId) => _summaries.GetValueOrDefault(memberId);

    public string? TypeSummary(Type type) => Summary($"T:{DocName(type)}");

    public string? PropertySummary(PropertyInfo property) =>
        property.DeclaringType is null
            ? null
            : Summary($"P:{DocName(property.DeclaringType)}.{property.Name}");

    private static string DocName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static XmlDocSummaryReader? Load(Assembly assembly, ILogger? logger)
    {
        string path = Path.ChangeExtension(assembly.Location, ".xml");
        if (assembly.Location.Length == 0 || !File.Exists(path))
        {
            logger?.LogWarning(
                "XML documentation file of {Assembly} not found at {Path}; SDK types carry no help text.",
                assembly.GetName().Name,
                path
            );
            return null;
        }
        return Parse(File.ReadAllText(path));
    }

    private static string PlainText(XElement element)
    {
        StringBuilder text = new();
        Append(element, text);
        string collapsed = Whitespace().Replace(text.ToString(), " ").Trim();
        return collapsed.Replace("*/", "*\\/", StringComparison.Ordinal);
    }

    private static void Append(XElement element, StringBuilder text)
    {
        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XText plain:
                    text.Append(plain.Value);
                    break;
                case XElement child:
                    AppendElement(child, text);
                    break;
            }
        }
    }

    private static void AppendElement(XElement child, StringBuilder text)
    {
        switch (child.Name.LocalName)
        {
            case "see" or "seealso":
                text.Append(ReferenceText(child));
                break;
            case "paramref" or "typeparamref":
                text.Append(child.Attribute("name")?.Value);
                break;
            default:
                Append(child, text);
                break;
        }
    }

    // <see cref="T:A.B.C"/> -> C, <see langword="null"/> -> null; a reference with inner text keeps that text.
    private static string ReferenceText(XElement see)
    {
        if (see.Nodes().Any())
            return PlainText(see);

        string? langword = see.Attribute("langword")?.Value;
        if (langword is not null)
            return langword;

        string cref = see.Attribute("cref")?.Value ?? see.Attribute("href")?.Value ?? string.Empty;
        int paren = cref.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
            cref = cref[..paren];
        int dot = cref.LastIndexOf('.');
        string name = dot >= 0 ? cref[(dot + 1)..] : cref;
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 ? name[(colon + 1)..] : name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
