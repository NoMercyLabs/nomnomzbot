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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// The <c>if</c> namespace (catalog section 7.5, spec section 6.3.2): <c>{if:value|then|else}</c>, the
/// <c>if.not</c>, <c>if.all</c>, <c>if.any</c> and <c>if.empty</c> combinators, and the path forms
/// <c>{if.&lt;path&gt;:then|else}</c> and <c>{if.&lt;path&gt;.&lt;op&gt;:operand:then|else}</c>. Every row is lazy: the
/// branch that is not chosen never renders.
/// </summary>
public sealed class IfTemplateHelpers : LogicHelperProvider
{
    private const string PathPrefix = "if.";

    public override string Namespace => "if";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        Row("if", "thanks sub!", lazy: true),
        Row("if.not", "not a mod", lazy: true),
        Row("if.all", "both", lazy: true),
        Row("if.any", "yes", lazy: true),
        Row("if.empty", "who?", lazy: true),
        PathRow("if.<path>", "on", "template.helper.if.path"),
        .. ValuePredicate.Names.Select(op =>
            PathRow($"if.<path>.{op}", "yes", $"template.helper.if.path.{op}")
        ),
    ];

    public override async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        node.Key.ToLowerInvariant() switch
        {
            "if" => await ValueFormAsync(node, negate: false, cancellationToken),
            "if.not" => await ValueFormAsync(node, negate: true, cancellationToken),
            "if.all" => await BothFormAsync(node, all: true, cancellationToken),
            "if.any" => await BothFormAsync(node, all: false, cancellationToken),
            "if.empty" => await EmptyFormAsync(node, cancellationToken),
            _ => await PathFormAsync(scope, node, cancellationToken),
        };

    private static TemplateHelperEntry PathRow(string key, string sample, string descriptionKey) =>
        Row(key, sample, lazy: true, prefix: PathPrefix, descriptionKey: descriptionKey);

    /// <summary><c>if:value|then[|else]</c> and <c>if.not:value|then[|else]</c>.</summary>
    private static async ValueTask<string?> ValueFormAsync(
        HelperNode node,
        bool negate,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count is not (2 or 3))
            return null;

        bool condition = LogicArguments.IsTrue(
            await LogicArguments.RenderAsync(node, 0, cancellationToken)
        );
        return await BranchAsync(node, condition != negate, 1, 2, cancellationToken);
    }

    /// <summary><c>if.all:a|b|then[|else]</c> and <c>if.any:a|b|then[|else]</c>; the second value renders only when needed.</summary>
    private static async ValueTask<string?> BothFormAsync(
        HelperNode node,
        bool all,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count is not (3 or 4))
            return null;

        bool first = LogicArguments.IsTrue(
            await LogicArguments.RenderAsync(node, 0, cancellationToken)
        );
        bool decided = all ? !first : first;
        bool condition = decided
            ? first
            : LogicArguments.IsTrue(await LogicArguments.RenderAsync(node, 1, cancellationToken));
        return await BranchAsync(node, condition, 2, 3, cancellationToken);
    }

    /// <summary><c>if.empty:value|then[|else]</c>: the then branch runs when the value is blank.</summary>
    private static async ValueTask<string?> EmptyFormAsync(
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count is not (2 or 3))
            return null;

        string value = await LogicArguments.RenderAsync(node, 0, cancellationToken);
        return await BranchAsync(node, value.Length == 0, 1, 2, cancellationToken);
    }

    /// <summary>
    /// <c>if.&lt;path&gt;:then[|else]</c> tests the path's value for truth; <c>if.&lt;path&gt;.&lt;op&gt;:operand:then[|else]</c>
    /// tests it with an operator. The path is read through the render's own resolver, so a seed, a provider key
    /// and an as-built key all work; a path nothing resolves counts as empty.
    /// </summary>
    private static async ValueTask<string?> PathFormAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (
            scope.ResolveKeyAsync is null
            || !node.Key.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)
        )
            return null;

        string path = node.Key[PathPrefix.Length..];
        int dot = path.LastIndexOf('.');
        if (dot > 0 && ValuePredicate.IsOperator(path[(dot + 1)..]))
            return await OperatorFormAsync(
                scope,
                node,
                path[..dot],
                path[(dot + 1)..],
                cancellationToken
            );

        if (node.Arguments.Count is not (1 or 2))
            return null;

        string? value = await scope.ResolveKeyAsync(path, cancellationToken);
        return await BranchAsync(node, LogicArguments.IsTrue(value), 0, 1, cancellationToken);
    }

    private static async ValueTask<string?> OperatorFormAsync(
        TemplateRenderScope scope,
        HelperNode node,
        string path,
        string op,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count is not (1 or 2))
            return null;

        SplitArgument? operandAndThen = await LogicArguments.SplitAsync(
            node,
            0,
            ':',
            cancellationToken
        );
        if (operandAndThen is null)
            return null;

        string value = await scope.ResolveKeyAsync!(path, cancellationToken) ?? "";
        bool? holds = ValuePredicate.Evaluate(op, value, operandAndThen.Value.Head);
        if (holds is null)
            return null;

        return holds.Value
            ? await operandAndThen.Value.Tail()
            : await LogicArguments.RenderOrEmptyAsync(node, 1, cancellationToken);
    }

    private static async ValueTask<string?> BranchAsync(
        HelperNode node,
        bool condition,
        int thenIndex,
        int elseIndex,
        CancellationToken cancellationToken
    ) =>
        await LogicArguments.RenderOrEmptyAsync(
            node,
            condition ? thenIndex : elseIndex,
            cancellationToken
        );
}
