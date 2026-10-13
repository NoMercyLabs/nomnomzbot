// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { AST_NODE_TYPES, ESLintUtils, type TSESLint } from '@typescript-eslint/utils'

// Owner 2026-10-13 called `(id) => (te(key) ? t(key) : id)` messy. So an arrow whose body is a
// ternary starts that ternary on the next line, one level in, without the wrapping brackets;
// @stylistic/multiline-ternary then puts each branch on its own line.

const indentUnit = '  '

function lineIndent(source: TSESLint.SourceCode, line: number): string {
  return /^\s*/.exec(source.lines[line - 1] ?? '')?.[0] ?? ''
}

export const arrowTernaryBody = ESLintUtils.RuleCreator.withoutDocs({
  meta: {
    type: 'layout',
    fixable: 'whitespace',
    schema: [],
    messages: {
      nextLine: 'A ternary arrow body starts on the next line, without brackets.',
    },
  },
  defaultOptions: [],
  create(context) {
    const source = context.sourceCode

    return {
      ArrowFunctionExpression(arrow) {
        const body = arrow.body

        if (body.type !== AST_NODE_TYPES.ConditionalExpression) return

        const open = source.getTokenBefore(body)
        const close = source.getTokenAfter(body)
        const wrapped = open?.value === '(' && close?.value === ')'

        if (open === null || close === null) return

        const arrowToken = wrapped
          ? source.getTokenBefore(open)
          : open

        if (arrowToken?.value !== '=>') return

        if (!wrapped && arrowToken.loc.end.line !== body.loc.start.line) return

        const indent = lineIndent(source, arrowToken.loc.end.line) + indentUnit

        context.report({
          node: body,
          messageId: 'nextLine',
          fix: (fixer) => {
            const start = fixer.replaceTextRange(
              [arrowToken.range[1], body.range[0]],
              `\n${indent}`,
            )

            return wrapped
              ? [start, fixer.removeRange([body.range[1], close.range[1]])]
              : start
          },
        })
      },
    }
  },
})
