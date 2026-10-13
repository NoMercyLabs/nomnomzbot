// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { AST_NODE_TYPES, ESLintUtils, type TSESLint, type TSESTree } from '@typescript-eslint/utils'

// Owner 2026-10-13: an arrow after another call argument breaks its line, and "lets move the arrow
// to the first line". So `setQueryData(queryKey, (current) =>` ends the line and the body starts
// on the next line, one level in. Only the last argument: a lone or first callback, an arrow with
// more arguments after it, and a block body stay as they are.

const indentUnit = '  '

function lineIndent(source: TSESLint.SourceCode, line: number): string {
  return /^\s*/.exec(source.lines[line - 1] ?? '')?.[0] ?? ''
}

function isTrailingArrowArgument(arrow: TSESTree.ArrowFunctionExpression): boolean {
  if (arrow.body.type === AST_NODE_TYPES.BlockStatement)
    return false

  const call = arrow.parent

  if (call.type !== AST_NODE_TYPES.CallExpression && call.type !== AST_NODE_TYPES.NewExpression) {
    return false
  }

  const index = call.arguments.indexOf(arrow)
  const previous = call.arguments[index - 1]
  const isLast = index === call.arguments.length - 1

  return isLast && index > 0 && previous !== undefined && previous.loc.end.line === arrow.loc.start.line
}

export const trailingArrowArgument = ESLintUtils.RuleCreator.withoutDocs({
  meta: {
    type: 'layout',
    fixable: 'whitespace',
    schema: [],
    messages: {
      bodyNextLine: 'An arrow after another argument starts its body on the line after `=>`.',
    },
  },
  defaultOptions: [],
  create(context) {
    const source = context.sourceCode

    return {
      ArrowFunctionExpression(arrow) {
        if (!isTrailingArrowArgument(arrow)) return

        const arrowToken = source.getTokenBefore(arrow.body, (token) =>
          token.value === '=>')
        const first = arrowToken === null
          ? null
          : source.getTokenAfter(arrowToken)

        if (arrowToken === null || first === null) return

        if (first.loc.start.line !== arrowToken.loc.end.line) return

        const indent = lineIndent(source, arrowToken.loc.end.line) + indentUnit

        context.report({
          node: arrowToken,
          messageId: 'bodyNextLine',
          fix: (fixer) => fixer.replaceTextRange([arrowToken.range[1], first.range[0]], `\n${indent}`),
        })
      },
    }
  },
})
