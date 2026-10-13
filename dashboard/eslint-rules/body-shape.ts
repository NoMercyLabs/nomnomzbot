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

// Owner 2026-10-13: "if statements may only directly return void, when a value is set then i
// want a proper codeblock, and when a value is returned i want the return on the next line
// indented". So a bare `return`, `break` or `continue` stays on the if line, a returned or thrown
// value goes on the next line one level in, and every other body sits in a block. A loop body
// always sits in a block.

const indentUnit = '  '

type Shape = 'sameLine' | 'nextLine' | 'block'

function shapeOf(body: TSESTree.Statement): Shape {
  switch (body.type) {
    case AST_NODE_TYPES.BreakStatement:
    case AST_NODE_TYPES.ContinueStatement:
      return 'sameLine'
    case AST_NODE_TYPES.ReturnStatement:
      return body.argument === null
        ? 'sameLine'
        : 'nextLine'
    case AST_NODE_TYPES.ThrowStatement:
      return 'nextLine'
    default:
      return 'block'
  }
}

function lineIndent(source: TSESLint.SourceCode, node: TSESTree.Node): string {
  return /^\s*/.exec(source.lines[node.loc.start.line - 1] ?? '')?.[0] ?? ''
}

export const bodyShape =ESLintUtils.RuleCreator.withoutDocs({
  meta: {
    type: 'layout',
    fixable: 'whitespace',
    schema: [],
    messages: {
      sameLine: 'A bare return, break or continue stays on the if line.',
      nextLine: 'A returned or thrown value goes on the next line, one level in.',
      block: 'An if that does more than return sits in a block.',
      loopBlock: 'A loop body sits in a block.',
    },
  },
  defaultOptions: [],
  create(context) {
    const source = context.sourceCode

    function wrapInBlock(
      statement: TSESTree.Statement,
      body: TSESTree.Statement,
      before: TSESTree.Token,
      messageId: 'block' | 'loopBlock',
    ): void {
      const indent = lineIndent(source, statement)
      const text = source.getText(body)

      context.report({
        node: body,
        messageId,
        fix: (fixer) =>
          fixer.replaceTextRange(
            [before.range[1], body.range[1]],
            ` {\n${indent}${indentUnit}${text}\n${indent}}`,
          ),
      })
    }

    function checkLoop(
      loop:
        | TSESTree.DoWhileStatement |
        TSESTree.ForInStatement |
        TSESTree.ForOfStatement |
        TSESTree.ForStatement |
        TSESTree.WhileStatement,
    ): void {
      if (loop.body.type === AST_NODE_TYPES.BlockStatement) return

      const before = source.getTokenBefore(loop.body)

      if (before === null) return

      wrapInBlock(loop, loop.body, before, 'loopBlock')
    }

    function check(statement: TSESTree.IfStatement, body: TSESTree.Statement | null): void {
      if (body === null || body.type === AST_NODE_TYPES.BlockStatement) return

      if (body.type === AST_NODE_TYPES.IfStatement) return

      const before = source.getTokenBefore(body)

      if (before === null) return

      const indent = lineIndent(source, statement)
      const shape = shapeOf(body)
      const onSameLine = before.loc.end.line === body.loc.start.line

      if (shape === 'sameLine' && !onSameLine) {
        context.report({
          node: body,
          messageId: 'sameLine',
          fix: (fixer) => fixer.replaceTextRange([before.range[1], body.range[0]], ' '),
        })
      }

      if (shape === 'nextLine' && onSameLine) {
        context.report({
          node: body,
          messageId: 'nextLine',
          fix: (fixer) =>
            fixer.replaceTextRange([before.range[1], body.range[0]], `\n${indent}${indentUnit}`),
        })
      }

      if (shape === 'block') {
        wrapInBlock(statement, body, before, 'block')
      }
    }

    return {
      IfStatement(statement) {
        check(statement, statement.consequent)
        check(statement, statement.alternate)
      },
      DoWhileStatement: checkLoop,
      ForInStatement: checkLoop,
      ForOfStatement: checkLoop,
      ForStatement: checkLoop,
      WhileStatement: checkLoop,
    }
  },
})
