// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { ESLintUtils, type TSESLint, type TSESTree } from '@typescript-eslint/utils'

// Owner 2026-10-13 asked to wrap `type PanelQuery<T> = { data: ...; isPending: ...; isError: ... }`
// into a formatted object. Prettier keeps such a type on one line while it fits, so a type
// literal with this many members or more puts every member on its own line.

const minMembers = 3
const indentUnit = '  '

function lineIndent(source: TSESLint.SourceCode, line: number): string {
  return /^\s*/.exec(source.lines[line - 1] ?? '')?.[0] ?? ''
}

function memberText(source: TSESLint.SourceCode, member: TSESTree.TypeElement): string {
  return source.getText(member).replace(/[;,]$/, '')
}

function isOneMemberPerLine(literal: TSESTree.TSTypeLiteral): boolean {
  const lines = literal.members.map((member) => member.loc.start.line)

  return (
    literal.loc.start.line < lines[0]! &&
    new Set(lines).size === lines.length &&
    literal.loc.end.line > literal.members.at(-1)!.loc.end.line
  )
}

export const typeMembersPerLine = ESLintUtils.RuleCreator.withoutDocs({
  meta: {
    type: 'layout',
    fixable: 'whitespace',
    schema: [],
    messages: {
      perLine: `A type with ${minMembers} or more members puts each member on its own line.`,
    },
  },
  defaultOptions: [],
  create(context) {
    const source = context.sourceCode

    return {
      TSTypeLiteral(literal) {
        if (literal.members.length < minMembers || isOneMemberPerLine(literal)) return

        const indent = lineIndent(source, literal.loc.start.line)
        const members = literal.members
          .map((member) => indent + indentUnit + memberText(source, member))
          .join('\n')

        context.report({
          node: literal,
          messageId: 'perLine',
          fix: (fixer) => fixer.replaceText(literal, `{\n${members}\n${indent}}`),
        })
      },
    }
  },
})
