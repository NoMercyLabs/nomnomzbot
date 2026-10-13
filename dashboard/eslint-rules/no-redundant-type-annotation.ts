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
import ts from 'typescript'

// Owner 2026-10-13: "don't type variables if the return type of the value matches and does not
// change for things like this: const queries: QueryClient = useQueryClient()", and a typed
// constant table reads `{ ... } as const satisfies Record<ReadabilityPreset, Readability>`.
// So a `const` loses an annotation its value already has, a fixed-key table moves its annotation
// to `as const satisfies`, and an annotation that widens or narrows a value stays.

const looseFlags = ts.TypeFlags.Any | ts.TypeFlags.Unknown | ts.TypeFlags.Never
const primitiveFlags =
  ts.TypeFlags.String | ts.TypeFlags.Number | ts.TypeFlags.Boolean | ts.TypeFlags.BigInt

function isLoose(type: ts.Type): boolean {
  if ((type.flags & looseFlags) !== 0) return true

  return type.isUnion() && type.types.some(isLoose)
}

function isLiteralNode(node: ts.Node): boolean {
  return (
    ts.isObjectLiteralExpression(node) ||
    ts.isArrayLiteralExpression(node) ||
    ts.isStringLiteral(node) ||
    ts.isNoSubstitutionTemplateLiteral(node) ||
    ts.isNumericLiteral(node) ||
    node.kind === ts.SyntaxKind.TrueKeyword ||
    node.kind === ts.SyntaxKind.FalseKeyword ||
    node.kind === ts.SyntaxKind.NullKeyword
  )
}

function unwrap(node: ts.Expression): ts.Expression {
  if (ts.isParenthesizedExpression(node) || ts.isAwaitExpression(node))
    return unwrap(node.expression)
  if (ts.isNonNullExpression(node)) return unwrap(node.expression)

  return node
}

// `(locale) => ...` gets its parameter types from the annotation.
function isContextTypedFunction(node: ts.Expression): boolean {
  const value = unwrap(node)

  if (!ts.isArrowFunction(value) && !ts.isFunctionExpression(value)) return false

  return value.parameters.some((parameter) => parameter.type === undefined)
}

function mentions(typeNode: ts.Node, name: string): boolean {
  if (ts.isTypeReferenceNode(typeNode) && ts.isIdentifier(typeNode.typeName)) {
    if (typeNode.typeName.text === name) return true
  }

  return ts.forEachChild(typeNode, (child) => mentions(child, name)) === true
}

// A generic call infers from the annotation when a type parameter shows up only in its return
// type (`querySelector<E>()`), and a generic `new` without type arguments does the same
// (`new Promise(...)`, `new Map()`).
function infersFromAnnotation(
  node: ts.CallExpression | ts.NewExpression,
  checker: ts.TypeChecker,
): boolean {
  if (node.typeArguments !== undefined) return false
  const declaration = checker.getResolvedSignature(node)?.declaration

  if (declaration === undefined || ts.isJSDocSignature(declaration)) return false
  const typeParameters = declaration.typeParameters ?? []

  if (typeParameters.length === 0) return false
  if (ts.isNewExpression(node)) return true

  return typeParameters.some(
    (parameter) =>
      !declaration.parameters.some(
        (input) => input.type !== undefined && mentions(input.type, parameter.name.text),
      ),
  )
}

// A literal, generic call or `new` in the value may take its type from the annotation:
// `ref('a')` is Ref<Mode> only because of `: Ref<Mode>`. Nested functions that name their own
// return type, and `as` / `satisfies` expressions, stand on their own.
function leansOnAnnotation(node: ts.Node, checker: ts.TypeChecker): boolean {
  if (ts.isFunctionLike(node) && node.type !== undefined) return false
  if (ts.isAsExpression(node) || ts.isSatisfiesExpression(node)) return false

  if (
    (ts.isCallExpression(node) || ts.isNewExpression(node)) &&
    infersFromAnnotation(node, checker)
  ) {
    return true
  }

  if (isLiteralNode(node) && ts.isExpression(node)) {
    const context = checker.getContextualType(node)

    if (context !== undefined && !isLoose(context) && (context.flags & primitiveFlags) === 0) {
      return true
    }
  }

  return ts.forEachChild(node, (child) => leansOnAnnotation(child, checker)) === true
}

// `const current: number = page` inside `while (page !== null)` names the type the loop's own
// narrowing needs; without it TypeScript reports a circular initializer.
function readsLoopNarrowedLet(
  node: ts.Node,
  value: ts.Expression,
  checker: ts.TypeChecker,
): boolean {
  const loop = ts.findAncestor(node, (ancestor) => ts.isIterationStatement(ancestor, false))

  if (loop === undefined) return false

  const visit = (child: ts.Node): boolean => {
    if (ts.isIdentifier(child)) {
      const declaration = checker.getSymbolAtLocation(child)?.valueDeclaration

      if (
        declaration !== undefined &&
        ts.isVariableDeclaration(declaration) &&
        (ts.getCombinedNodeFlags(declaration) & ts.NodeFlags.Let) !== 0
      ) {
        return true
      }
    }

    return ts.forEachChild(child, visit) === true
  }

  return visit(value)
}

// `: Record<string, unknown>` over a `{}` value lets the code read any key.
function addsIndex(declared: ts.Type, value: ts.Type, checker: ts.TypeChecker): boolean {
  return checker.getIndexInfosOfType(declared).length > checker.getIndexInfosOfType(value).length
}

const entryReaders = new Set(['entries', 'keys', 'values'])
const shapeDepth = 4

// `as const` drops what the literal leaves out and freezes every list. A shape with an optional
// field the code may read, or a mutable list the code may push to, keeps its annotation.
function hasLooseShape(type: ts.Type, checker: ts.TypeChecker, depth: number): boolean {
  if (depth === 0) return false
  if (type.isUnion()) return type.types.some((member) => hasLooseShape(member, checker, depth))
  if (type.symbol?.name === 'Array') return true
  if (type.getCallSignatures().length > 0) return false

  const fields = type.getProperties()

  if (fields.some((field) => (field.flags & ts.SymbolFlags.Optional) !== 0)) return true

  const inner = [
    ...fields.map((field) => checker.getTypeOfSymbol(field)),
    ...checker.getIndexInfosOfType(type).map((info) => info.type),
  ]

  return inner.some((member) => hasLooseShape(member, checker, depth - 1))
}

function isLiteralKey(
  node: TSESTree.Expression,
  toTsType: (node: TSESTree.Node) => ts.Type,
): boolean {
  if (node.type === 'Literal') return true

  const key = toTsType(node)

  return (key.isUnion() ? key.types : [key]).every((member) => member.isLiteral())
}

// A read that looks a key up by name: `t.name`, `t['name']`, `t[preset]` with a literal-typed
// `preset`, `Object.entries(t)`, `for (k in t)`, `{ ...t }`.
function isNamedRead(
  identifier: TSESTree.Node,
  toTsType: (node: TSESTree.Node) => ts.Type,
): boolean {
  const parent = identifier.parent

  if (parent === undefined) return false

  if (parent.type === 'MemberExpression' && parent.object === identifier) {
    return !parent.computed || isLiteralKey(parent.property as TSESTree.Expression, toTsType)
  }

  if (parent.type === 'CallExpression' && parent.arguments[0] === identifier) {
    const callee = parent.callee

    return (
      callee.type === 'MemberExpression' &&
      callee.object.type === 'Identifier' &&
      callee.object.name === 'Object' &&
      callee.property.type === 'Identifier' &&
      entryReaders.has(callee.property.name)
    )
  }

  if (parent.type === 'ForInStatement') return parent.right === identifier

  return parent.type === 'SpreadElement'
}

// `t.x = 1`, `t.x++`, `delete t.x`: a frozen object would reject them.
function isWrite(identifier: TSESTree.Node): boolean {
  const member = identifier.parent

  if (member?.type !== 'MemberExpression' || member.object !== identifier) return false

  const parent = member.parent

  return (
    (parent?.type === 'AssignmentExpression' && parent.left === member) ||
    parent?.type === 'UpdateExpression' ||
    (parent?.type === 'UnaryExpression' && parent.operator === 'delete')
  )
}

// A constant object states its type as `as const satisfies T`. It keeps a plain annotation when
// the code writes to it, when `as const` would drop an optional field or freeze a list, and, for
// an open-keyed table (`Record<string, V>`), when something reads it by a computed key or from
// another file.
function isConstantObject(
  node: TSESTree.VariableDeclarator,
  init: TSESTree.Expression,
  declared: ts.Type,
  checker: ts.TypeChecker,
  scope: Readonly<TSESLint.Scope.ScopeManager>,
  toTsType: (node: TSESTree.Node) => ts.Type,
): boolean {
  if (init.type !== 'ObjectExpression' || init.properties.length === 0) return false
  if (isLoose(declared) || hasLooseShape(declared, checker, shapeDepth)) return false

  const references = (scope.getDeclaredVariables(node)[0]?.references ?? []).filter(
    (reference) => reference.identifier !== node.id,
  )

  if (references.some((reference) => isWrite(reference.identifier))) return false

  if (checker.getIndexInfosOfType(declared).length === 0) return true
  if (node.parent.parent?.type === 'ExportNamedDeclaration') return false

  return references.every((reference) => isNamedRead(reference.identifier, toTsType))
}

export const noRedundantTypeAnnotation = ESLintUtils.RuleCreator.withoutDocs({
  meta: {
    type: 'suggestion',
    fixable: 'code',
    schema: [],
    messages: {
      redundant:
        'The value already has this type. Drop the annotation; keep one only to widen or narrow, through the generic (ref<T>(), computed<T>()).',
      table: 'A constant object states its type as `{ ... } as const satisfies T`.',
    },
  },
  defaultOptions: [],
  create(context) {
    const services = ESLintUtils.getParserServices(context)
    const checker = services.program.getTypeChecker()
    const toType = (node: TSESTree.TypeNode): ts.Type =>
      checker.getTypeFromTypeNode(services.esTreeNodeToTSNodeMap.get(node) as ts.TypeNode)
    const toTsType = (node: TSESTree.Node): ts.Type =>
      checker.getTypeAtLocation(services.esTreeNodeToTSNodeMap.get(node))

    return {
      VariableDeclarator(node): void {
        const annotation = node.id.typeAnnotation
        const init = node.init

        if (annotation === undefined || init === null) return
        if (node.id.type !== 'Identifier' || node.parent.kind !== 'const') return

        const scope = context.sourceCode.scopeManager
        const declared = toType(annotation.typeAnnotation)

        if (scope !== null && isConstantObject(node, init, declared, checker, scope, toTsType)) {
          const typeText = context.sourceCode.getText(annotation.typeAnnotation)

          context.report({
            node: annotation,
            messageId: 'table',
            fix: (fixer) => [
              fixer.remove(annotation),
              fixer.insertTextAfter(init, ` as const satisfies ${typeText}`),
            ],
          })

          return
        }

        const declarator = services.esTreeNodeToTSNodeMap.get(node)
        const valueNode = services.esTreeNodeToTSNodeMap.get(init) as ts.Expression
        const value = checker.getTypeAtLocation(valueNode)

        if (isLoose(declared) || isLoose(value)) return
        if (addsIndex(declared, value, checker)) return
        if (!checker.isTypeAssignableTo(declared, value)) return
        if (!checker.isTypeAssignableTo(value, declared)) return
        if (isContextTypedFunction(valueNode)) return
        if (leansOnAnnotation(valueNode, checker)) return
        if (readsLoopNarrowedLet(declarator, valueNode, checker)) return

        context.report({
          node: annotation,
          messageId: 'redundant',
          fix: (fixer) => fixer.remove(annotation),
        })
      },
    }
  },
})
