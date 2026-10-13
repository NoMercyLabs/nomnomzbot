// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { AST_NODE_TYPES, ESLintUtils, type TSESTree } from '@typescript-eslint/utils'

// Owner 2026-10-13: "lets also reduce the complexity for things like this, its a challenge to
// parse what this does" about `Math.round(Math.min(1, Math.max(0, v)) * 255)`. A call inside a
// call inside a call is three levels; the inner steps get a name of their own instead. A chain
// (`a.find(x).text()`) reads left to right and is not nesting; a function argument and a named
// object property start over; a test's `expect(...)` does not count.

const maxDepth = 2

type Call = TSESTree.CallExpression | TSESTree.NewExpression

function isCall(node: TSESTree.Node): node is Call {
  return node.type === AST_NODE_TYPES.CallExpression || node.type === AST_NODE_TYPES.NewExpression
}

// A function argument and a named object property each start the count over; the property name
// already names its step.
function startsOver(node: TSESTree.Node): boolean {
  return (
    node.type === AST_NODE_TYPES.ArrowFunctionExpression ||
    node.type === AST_NODE_TYPES.FunctionExpression ||
    node.type === AST_NODE_TYPES.Property
  )
}

// `expect(value)` only marks what a test checks; it adds no step to read.
function isAssertion(call: Call): boolean {
  return call.callee.type === AST_NODE_TYPES.Identifier && call.callee.name === 'expect'
}

// The deepest call nesting inside an expression, without entering functions or properties.
function depthOf(node: TSESTree.Node): number {
  if (startsOver(node))
    return 0

  if (isCall(node)) {
    const fromArguments = Math.max(0, ...node.arguments.map(depthOf))
    const fromChain = depthOf(node.callee)
    const own = isAssertion(node)
      ? 0
      : 1

    return Math.max(own + fromArguments, fromChain)
  }

  let deepest = 0

  for (const [key, value] of Object.entries(node)) {
    if (key === 'parent') continue

    const children: unknown[] = Array.isArray(value)
      ? value
      : [value]

    for (const child of children) {
      if (child !== null && typeof child === 'object' && 'type' in child) {
        deepest = Math.max(deepest, depthOf(child as TSESTree.Node))
      }
    }
  }

  return deepest
}

// True when the call sits inside another call's arguments, so the outer call reports instead.
function isNestedInCall(call: Call): boolean {
  let child: TSESTree.Node = call
  // The Program node's parent is null at run time, whatever the types say.
  let parent: TSESTree.Node | null | undefined = call.parent

  while (parent && !startsOver(parent)) {
    if (isCall(parent) && parent.callee !== child)
      return true

    child = parent
    parent = parent.parent
  }

  return false
}

export const maxCallNesting = ESLintUtils.RuleCreator.withoutDocs({
  meta: {
    type: 'suggestion',
    schema: [],
    messages: {
      tooDeep: 'Calls nest {{depth}} deep; give the inner steps a name of their own (at most 2).',
    },
  },
  defaultOptions: [],
  create(context) {
    function check(call: Call): void {
      if (isNestedInCall(call)) return

      const depth = depthOf(call)

      if (depth > maxDepth) {
        context.report({ node: call, messageId: 'tooDeep', data: { depth } })
      }
    }

    return {
      CallExpression: check,
      NewExpression: check,
    }
  },
})
