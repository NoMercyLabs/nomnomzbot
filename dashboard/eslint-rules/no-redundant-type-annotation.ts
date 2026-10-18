// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { ESLintUtils, type TSESLint, type TSESTree } from '@typescript-eslint/utils';
import ts from 'typescript';

// Owner 2026-10-13: "don't type variables if the return type of the value matches and does not
// change for things like this: const queries: QueryClient = useQueryClient()", and a typed
// constant table reads `{ ... } as const satisfies Record<ReadabilityPreset, Readability>`.
// So a `const` loses an annotation its value already has, a fixed-key table moves its annotation
// to `as const satisfies`, and an annotation that widens or narrows a value stays.

const looseFlags = ts.TypeFlags.Any | ts.TypeFlags.Unknown | ts.TypeFlags.Never;
const primitiveFlags =
  ts.TypeFlags.String | ts.TypeFlags.Number | ts.TypeFlags.Boolean | ts.TypeFlags.BigInt;

function isLoose(type: ts.Type): boolean {
  if ((type.flags & looseFlags) !== 0)
    return true;

  return type.isUnion() && type.types.some(isLoose);
}

function isNullish(type: ts.Type): boolean {
  return (type.flags & (ts.TypeFlags.Null | ts.TypeFlags.Undefined)) !== 0;
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
  );
}

function unwrap(node: ts.Expression): ts.Expression {
  if (ts.isParenthesizedExpression(node) || ts.isAwaitExpression(node))
    return unwrap(node.expression);

  if (ts.isNonNullExpression(node))
    return unwrap(node.expression);

  return node;
}

// `(locale) => ...` gets its parameter types from the annotation.
function isContextTypedFunction(node: ts.Expression): boolean {
  const value = unwrap(node);

  if (!ts.isArrowFunction(value) && !ts.isFunctionExpression(value))
    return false;

  return value.parameters.some((parameter) => parameter.type === undefined);
}

function mentions(typeNode: ts.Node, name: string): boolean {
  if (ts.isTypeReferenceNode(typeNode) && ts.isIdentifier(typeNode.typeName)) {
    if (typeNode.typeName.text === name)
      return true;
  }

  return ts.forEachChild(typeNode, (child) =>
    mentions(child, name)) === true;
}

// A generic call infers from the annotation when a type parameter shows up only in its return
// type (`querySelector<E>()`), and a generic `new` without type arguments does the same
// (`new Promise(...)`, `new Map()`).
function infersFromAnnotation(
  node: ts.CallExpression | ts.NewExpression,
  checker: ts.TypeChecker,
): boolean {
  if (node.typeArguments !== undefined)
    return false;

  const declaration = checker.getResolvedSignature(node)?.declaration;

  if (declaration === undefined || ts.isJSDocSignature(declaration))
    return false;

  const typeParameters = declaration.typeParameters ?? [];

  if (typeParameters.length === 0)
    return false;

  if (ts.isNewExpression(node))
    return true;

  return typeParameters.some(
    (parameter) =>
      !declaration.parameters.some(
        (input) => input.type !== undefined && mentions(input.type, parameter.name.text),
      ),
  );
}

// A literal, generic call or `new` in the value may take its type from the annotation:
// `ref('a')` is Ref<Mode> only because of `: Ref<Mode>`. Nested functions that name their own
// return type, and `as` / `satisfies` expressions, stand on their own.
function leansOnAnnotation(node: ts.Node, checker: ts.TypeChecker): boolean {
  if (ts.isFunctionLike(node) && node.type !== undefined)
    return false;

  if (ts.isAsExpression(node) || ts.isSatisfiesExpression(node))
    return false;

  if (
    (ts.isCallExpression(node) || ts.isNewExpression(node)) &&
    infersFromAnnotation(node, checker)
  ) {
    return true;
  }

  if (isLiteralNode(node) && ts.isExpression(node)) {
    const context = checker.getContextualType(node);

    if (context !== undefined && !isLoose(context) && (context.flags & primitiveFlags) === 0) {
      return true;
    }
  }

  return ts.forEachChild(node, (child) =>
    leansOnAnnotation(child, checker)) === true;
}

// `const current: number = page` inside `while (page !== null)` names the type the loop's own
// narrowing needs; without it TypeScript reports a circular initializer.
function readsLoopNarrowedLet(
  node: ts.Node,
  value: ts.Expression,
  checker: ts.TypeChecker,
): boolean {
  const loop = ts.findAncestor(node, (ancestor) =>
    ts.isIterationStatement(ancestor, false));

  if (loop === undefined)
    return false;

  const visit = (child: ts.Node): boolean => {
    if (ts.isIdentifier(child)) {
      const declaration = checker.getSymbolAtLocation(child)?.valueDeclaration;

      if (
        declaration !== undefined &&
        ts.isVariableDeclaration(declaration) &&
        (ts.getCombinedNodeFlags(declaration) & ts.NodeFlags.Let) !== 0
      ) {
        return true;
      }
    }

    return ts.forEachChild(child, visit) === true;
  };

  return visit(value);
}

// `: Record<string, unknown>` over a `{}` value lets the code read any key.
function addsIndex(declared: ts.Type, value: ts.Type, checker: ts.TypeChecker): boolean {
  return checker.getIndexInfosOfType(declared).length > checker.getIndexInfosOfType(value).length;
}

const shapeDepth = 4;

// `as const` drops what the literal leaves out and freezes every list. A shape with an optional
// field the code may read, or a mutable list the code may push to, keeps its annotation.
function hasLooseShape(type: ts.Type, checker: ts.TypeChecker, depth: number): boolean {
  if (depth === 0)
    return false;

  if (type.isUnion())
    return type.types.some((member) => hasLooseShape(member, checker, depth));

  if (type.symbol?.name === 'Array')
    return true;

  if (type.getCallSignatures().length > 0)
    return false;

  const fields = type.getProperties();

  if (fields.some((field) => (field.flags & ts.SymbolFlags.Optional) !== 0))
    return true;

  const inner = [
    ...fields.map((field) => checker.getTypeOfSymbol(field)),
    ...checker.getIndexInfosOfType(type).map((info) => info.type),
  ];

  return inner.some((member) => hasLooseShape(member, checker, depth - 1));
}

// `t.x = 1`, `t.x++`, `delete t.x`: a frozen object would reject them.
function isWrite(identifier: TSESTree.Node): boolean {
  const member = identifier.parent;

  if (member?.type !== 'MemberExpression' || member.object !== identifier)
    return false;

  const parent = member.parent;

  return (
    (parent?.type === 'AssignmentExpression' && parent.left === member) ||
    parent?.type === 'UpdateExpression' ||
    (parent?.type === 'UnaryExpression' && parent.operator === 'delete')
  );
}

// A constant object states its type as `as const satisfies T`. It keeps a plain annotation when
// the code writes to it, or when `as const` would drop an optional field or freeze a list. A
// string-keyed table moves too: a read by a computed key narrows the key (oneOf), the table stays exact.
function isConstantObject(
  node: TSESTree.VariableDeclarator,
  init: TSESTree.Expression,
  declared: ts.Type,
  checker: ts.TypeChecker,
  scope: Readonly<TSESLint.Scope.ScopeManager>,
): boolean {
  if (init.type !== 'ObjectExpression' || init.properties.length === 0)
    return false;

  if (isLoose(declared) || hasLooseShape(declared, checker, shapeDepth))
    return false;

  const references = (scope.getDeclaredVariables(node)[0]?.references ?? []).filter(
    (reference) => reference.identifier !== node.id,
  );

  if (checker.getIndexInfosOfType(declared).length > 0 && references.some(isHandedOn))
    return false;

  return !references.some((reference) => isWrite(reference.identifier));
}

// An open table handed on inside an options object (`createI18n({ messages })`) lets that call
// infer its keys from the annotation; `as const` would narrow them to the literal's keys.
function isHandedOn(reference: TSESLint.Scope.Reference): boolean {
  const parent = reference.identifier.parent;

  return parent?.type === 'Property' && parent.value === reference.identifier;
}

function isModuleScope(node: TSESTree.VariableDeclarator): boolean {
  const statement = node.parent.parent;

  if (statement?.type === 'ExportNamedDeclaration')
    return statement.parent.type === 'Program';

  return statement?.type === 'Program';
}

// A constant list (`const likelyNext: readonly Route[] = [ ... ]`) states its type as
// `[ ... ] as const satisfies readonly Route[]` (owner 2026-10-13). A mutable `T[]` stays: `as const`
// freezes the list, so code that pushes to it would break.
function isConstantList(
  init: TSESTree.Expression,
  declared: ts.Type,
  checker: ts.TypeChecker,
  node: TSESTree.VariableDeclarator,
): boolean {
  if (init.type !== 'ArrayExpression' || init.elements.length === 0 || !isModuleScope(node))
    return false;

  if (!checker.isArrayType(declared) || declared.symbol.name !== 'ReadonlyArray')
    return false;

  const element = checker.getIndexTypeOfType(declared, ts.IndexKind.Number);

  return element !== undefined && !isLoose(element) && !holdsList(element, checker);
}

// A row that holds a list (`entries: NavEntry[]`) turns into a union of different tuples that
// `.flatMap()` cannot read.
function holdsList(element: ts.Type, checker: ts.TypeChecker): boolean {
  if (element.isUnion())
    return element.types.some((member) => holdsList(member, checker));

  return element
    .getProperties()
    .some((field) => checker.isArrayType(checker.getTypeOfSymbol(field)));
}

// Owner 2026-10-18: "when mapping i also want you to type the return as const instead of the
// argument". Inside an array-method callback the list types the arguments, so an argument
// annotation goes, and the returned literal states its type as `as const satisfies T`.
const arrayMethods = new Set([
  'every',
  'filter',
  'find',
  'findIndex',
  'findLast',
  'findLastIndex',
  'flatMap',
  'forEach',
  'map',
  'reduce',
  'reduceRight',
  'some',
  'sort',
  'toSorted',
]);

type Callback = TSESTree.ArrowFunctionExpression | TSESTree.FunctionExpression;

type Fn = Callback | TSESTree.FunctionDeclaration;

type Services = ReturnType<typeof ESLintUtils.getParserServices>;

type CallbackTarget = {
  callback: Callback;
  literals: TSESTree.Expression[];
  typeNode: TSESTree.TypeNode;
  removal: TSESTree.Node;
};

// The name a function goes by: `function a`, `const a = () => ...`, `a() {}` in a class or object.
function nameOf(node: Fn): string | null {
  if (node.id?.name !== undefined)
    return node.id.name;

  const parent = node.parent;

  if (parent?.type === 'VariableDeclarator' && parent.id.type === 'Identifier')
    return parent.id.name;

  const keyed = parent?.type === 'MethodDefinition' || parent?.type === 'Property';

  return keyed && !parent.computed && parent.key.type === 'Identifier'
    ? parent.key.name
    : null;
}

// TS cannot infer the return type of a function that calls itself, so that annotation stays.
function callsItself(node: Fn, sourceCode: Readonly<TSESLint.SourceCode>): boolean {
  const name = nameOf(node);

  if (name === null)
    return false;

  const escaped = name.replaceAll('$', '\\$');

  return new RegExp(`\\b${escaped}\\b`).test(sourceCode.getText(node.body));
}

// An overload list states the signatures; the implementation return type stays with them.
function isOverloaded(node: Fn, services: Services, checker: ts.TypeChecker): boolean {
  const tsNode = services.esTreeNodeToTSNodeMap.get(node);
  const named = ts.isFunctionDeclaration(tsNode) || ts.isMethodDeclaration(tsNode);

  if (!named || tsNode.name === undefined)
    return false;

  const declarations = checker.getSymbolAtLocation(tsNode.name)?.declarations ?? [];

  return (
    declarations.filter((item) => ts.isFunctionDeclaration(item) || ts.isMethodDeclaration(item))
      .length > 1
  );
}

// The array method a callback is passed to, or null when it is anything else.
function arrayMethodOf(node: Callback, checker: ts.TypeChecker, services: Services): string | null {
  const call = node.parent;

  if (call?.type !== 'CallExpression' || !call.arguments.includes(node))
    return null;

  const callee = call.callee;

  if (
    callee.type !== 'MemberExpression' ||
    callee.computed ||
    callee.property.type !== 'Identifier'
  )
    return null;

  if (!arrayMethods.has(callee.property.name))
    return null;

  const receiver = checker.getTypeAtLocation(services.esTreeNodeToTSNodeMap.get(callee.object));

  return checker.isArrayType(receiver) || checker.isTupleType(receiver)
    ? callee.property.name
    : null;
}

function propertyName(property: TSESTree.ObjectLiteralElement): string | null {
  if (property.type !== 'Property' || property.computed)
    return null;

  if (property.key.type === 'Identifier')
    return property.key.name;

  return property.key.type === 'Literal'
    ? String(property.key.value)
    : null;
}

// `as const` freezes the lists the literal writes out. It loosens a literal when such a list is
// declared mutable; a list that arrives through a spread or a variable is not frozen. An optional
// field the literal leaves out is fine: the owner types the return, and the type check names a
// read of the dropped field.
function loosensLiteral(
  literal: TSESTree.Node,
  declared: ts.Type,
  checker: ts.TypeChecker,
  services: Services,
  depth: number = shapeDepth,
): boolean {
  if (depth === 0)
    return false;

  if (declared.isUnion())
    return declared.types.some((member) =>
      loosensLiteral(literal, member, checker, services, depth),
    );

  if (literal.type === 'ArrayExpression')
    return loosensList(literal, declared, checker, services, depth);

  if (literal.type !== 'ObjectExpression')
    return false;

  return declared.getProperties().some((field) => {
    const stated = literal.properties.find((property) => propertyName(property) === field.name);

    return (
      stated?.type === 'Property' &&
      loosensLiteral(stated.value, checker.getTypeOfSymbol(field), checker, services, depth - 1)
    );
  });
}

function isWidePrimitive(type: ts.Type): boolean {
  if (type.isUnion())
    return type.types.some(isWidePrimitive);

  return (
    (type.flags & primitiveFlags) !== 0 &&
    !type.isLiteral() &&
    (type.flags & ts.TypeFlags.BooleanLiteral) === 0
  );
}

function isTokenLiteral(node: TSESTree.Node): boolean {
  return (
    (node.type === 'Literal' && node.value !== null && typeof node.value !== 'object') ||
    (node.type === 'TemplateLiteral' && node.expressions.length === 0)
  );
}

// `as const` turns `'x'` into the type `'x'`: a field declared `string` (a form's blank state)
// would then reject every later write, so such a literal keeps its annotation.
function narrowsLiteral(
  literal: TSESTree.Node,
  declared: ts.Type,
  checker: ts.TypeChecker,
): boolean {
  if (isTokenLiteral(literal))
    return isWidePrimitive(declared) || (declared.flags & ts.TypeFlags.Boolean) !== 0;

  if (declared.isUnion())
    return declared.types.some((member) => narrowsLiteral(literal, member, checker));

  if (literal.type === 'ArrayExpression') {
    const row = checker.getIndexTypeOfType(declared, ts.IndexKind.Number);

    return (
      row !== undefined &&
      literal.elements.some(
        (element) =>
          element !== null &&
          element.type !== 'SpreadElement' &&
          !isTokenLiteral(element) &&
          narrowsLiteral(element, row, checker),
      )
    );
  }

  if (literal.type !== 'ObjectExpression')
    return false;

  return declared.getProperties().some((field) => {
    const stated = literal.properties.find((property) => propertyName(property) === field.name);

    return (
      stated?.type === 'Property' &&
      narrowsLiteral(stated.value, checker.getTypeOfSymbol(field), checker)
    );
  });
}

function loosensList(
  literal: TSESTree.ArrayExpression,
  declared: ts.Type,
  checker: ts.TypeChecker,
  services: Services,
  depth: number,
  mutableOk: boolean = false,
): boolean {
  if (checker.isTupleType(declared) || !checker.isArrayType(declared))
    return false;

  if (declared.symbol.name !== 'ReadonlyArray' && !mutableOk)
    return true;

  const row = checker.getIndexTypeOfType(declared, ts.IndexKind.Number);

  if (row === undefined)
    return true;

  return literal.elements.some(
    (element) =>
      element !== null &&
      element.type !== 'SpreadElement' &&
      loosensLiteral(element, row, checker, services, depth - 1),
  );
}

// `as const` makes a tuple readonly, so the satisfies type says so too. `flatMap` reads a
// readonly list as well, so its mutable list return turns readonly the same way.
function isMutableFlatList(declared: ts.Type, method: string, checker: ts.TypeChecker): boolean {
  return method === 'flatMap' && checker.isArrayType(declared) && declared.symbol.name === 'Array';
}

function isTuple(type: ts.Type, checker: ts.TypeChecker): type is ts.TupleTypeReference {
  return checker.isTupleType(type);
}

// An object or array literal is never null or undefined: it satisfies only the other members.
function withoutEmptyMembers(
  typeNode: TSESTree.TypeNode,
  sourceCode: Readonly<TSESLint.SourceCode>,
): string {
  if (typeNode.type !== 'TSUnionType')
    return sourceCode.getText(typeNode);

  const kept = typeNode.types.filter(
    (member) => member.type !== 'TSNullKeyword' && member.type !== 'TSUndefinedKeyword',
  );

  return kept.length === 0 || kept.length === typeNode.types.length
    ? sourceCode.getText(typeNode)
    : kept.map((member) => sourceCode.getText(member)).join(' | ');
}

function frozenTypeText(
  declared: ts.Type,
  text: string,
  method: string,
  checker: ts.TypeChecker,
): string {
  const mutableTuple = isTuple(declared, checker) && !declared.target.readonly;

  return mutableTuple || isMutableFlatList(declared, method, checker)
    ? `readonly ${text}`
    : text;
}

function isLiteral(node: TSESTree.Node | null): node is TSESTree.Expression {
  return node?.type === 'ObjectExpression' || node?.type === 'ArrayExpression';
}

// A returned `cond ? { ... } : null` has a literal in each branch that is one.
function branchLiterals(node: TSESTree.Node | null): TSESTree.Expression[] {
  if (node?.type === 'ConditionalExpression')
    return [...branchLiterals(node.consequent), ...branchLiterals(node.alternate)];

  return isLiteral(node)
    ? [node]
    : [];
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
      list: 'A constant list states its type as `[ ... ] as const satisfies readonly T[]`.',
      callbackParam: 'The list already types this callback parameter. Drop the annotation.',
      bareSatisfies:
        'A literal states its type as `as const satisfies T`, never a bare `satisfies T`.',
      callbackTarget:
        'The generic or annotation names what the callback returns. State it on the literal: `computed(() => ({ ... }) as const satisfies T)`.',
      literalNeverNull:
        'A literal is never null or undefined: `as const satisfies T`, without the empty members of the union.',
      callbackReturn:
        'Type the returned literal, not the callback: `(x) => ({ ... }) as const satisfies T`.',
    },
  },
  defaultOptions: [],
  create(context) {
    const services = ESLintUtils.getParserServices(context);
    const checker = services.program.getTypeChecker();
    const toType = (node: TSESTree.TypeNode): ts.Type =>
      checker.getTypeFromTypeNode(services.esTreeNodeToTSNodeMap.get(node) as ts.TypeNode);

    const functions: TSESTree.ReturnStatement[][] = [];

    // The literal's closing parenthesis comes first when the arrow body is `({ ... })`.
    const literalEnd = (
      literal: TSESTree.Expression,
      callback: Fn,
    ): TSESTree.Node | TSESTree.Token => {
      const before = context.sourceCode.getTokenBefore(literal);
      const arrow = before === null
        ? null
        : context.sourceCode.getTokenBefore(before);

      if (callback.body === literal && before?.value === '(' && arrow?.value === '=>')
        return context.sourceCode.getTokenAfter(literal) ?? literal;

      return literal;
    };

    const checkParameters = (node: Callback, method: string): void => {
      const tsNode = services.esTreeNodeToTSNodeMap.get(node) as ts.Expression;
      const signature = checker.getContextualType(tsNode)?.getCallSignatures()[0];

      if (signature === undefined) return;

      for (const [index, parameter] of node.params.entries()) {
        const symbol = signature.parameters[index];
        const annotation = 'typeAnnotation' in parameter
          ? parameter.typeAnnotation
          : undefined;

        // The first `reduce` parameter is the accumulator; its annotation picks the result type.
        if (annotation === undefined || symbol === undefined) continue;

        if (parameter.type === 'AssignmentPattern' || (method.startsWith('reduce') && index === 0)) continue;

        const given = checker.getTypeOfSymbolAtLocation(symbol, tsNode);
        const declared = toType(annotation.typeAnnotation);

        if (isLoose(declared) || isLoose(given)) continue;

        if (!checker.isTypeAssignableTo(declared, given)) continue;

        if (!checker.isTypeAssignableTo(given, declared)) continue;

        context.report({
          node: annotation,
          messageId: 'callbackParam',
          fix: (fixer) => fixer.remove(annotation),
        });
      }
    };

    const checkReturn = (
      node: Fn,
      returns: TSESTree.ReturnStatement[],
      method: string | null,
    ): void => {
      const annotation = node.returnType;

      if (annotation === undefined || node.async || node.generator) return;

      if (annotation.typeAnnotation.type === 'TSTypePredicate') return;

      const returned =
        node.body.type === 'BlockStatement'
          ? returns.map((item) => item.argument)
          : [node.body];
      const literals = returned.flatMap(branchLiterals);
      const declared = toType(annotation.typeAnnotation);

      // A named type that resolves to `any` (an import from a .vue file) cannot be judged: report only.
      const unresolved = isLoose(declared) && annotation.typeAnnotation.type === 'TSTypeReference';

      if (literals.length === 0 || (isLoose(declared) && !unresolved)) return;

      // Outside an array callback, a function that calls itself or has overloads keeps its
      // annotation, and an unresolved type is no reason to fail the run.
      const general = method === null;
      const keeps =
        unresolved ||
        callsItself(node, context.sourceCode) ||
        isOverloaded(node, services, checker);

      // A declared union (`T | null`) is what a type predicate or a narrowing consumer reads: the
      // inferred literal type would no longer match it, so the annotation stays.
      // A string-keyed record keeps it too: the literals of two returns infer two different shapes.
      const keyed = checker.getIndexTypeOfType(declared, ts.IndexKind.String) !== undefined;

      const nullable = declared.isUnion() && declared.types.some(isNullish);

      if (general && (keeps || keyed || nullable)) return;

      // `as const` must not freeze a list the code mutates; a return that is no literal
      // (`return reward`) already has its type and stays as it is.
      const kind = method ?? '';
      const flat = isMutableFlatList(declared, kind, checker);
      const loosens = (literal: TSESTree.Expression): boolean =>
        flat && literal.type === 'ArrayExpression'
          ? loosensList(literal, declared, checker, services, shapeDepth, true)
          : loosensLiteral(literal, declared, checker, services);
      const safe = !unresolved && !literals.some(loosens);
      const typeText = frozenTypeText(
        declared,
        withoutEmptyMembers(annotation.typeAnnotation, context.sourceCode),
        kind,
        checker,
      );

      // A literal that `as const` would loosen or narrow (a mutable list, `''` for `string`) keeps
      // the annotation its consumers need; the owner never accepts a bare `satisfies`.
      if (
        general &&
        (!safe || literals.some((literal) => narrowsLiteral(literal, declared, checker)))
      ) return;

      context.report({
        node: annotation,
        messageId: 'callbackReturn',
        fix: safe
          ? (fixer) => [
              fixer.remove(annotation),
              ...literals.map((literal) =>
                fixer.insertTextAfter(literalEnd(literal, node), ` as const satisfies ${typeText}`),
              ),
            ]
          : null,
      });
    };

    const returnedLiterals = (callback: Callback): TSESTree.Expression[] => {
      if (callback.body.type !== 'BlockStatement')
        return branchLiterals(callback.body);

      // One return that is no literal (`return accepted`) makes the getter's type a union the
      // generic keeps whole: all or none.
      const returns = returnsWithin(callback.body);

      return returns.every((item) => branchLiterals(item.argument).length > 0)
        ? returns.flatMap((item) => branchLiterals(item.argument))
        : [];
    };

    // Every `return` of this function, nested ifs and loops included, nested functions excluded.
    const returnsWithin = (node: TSESTree.Node): TSESTree.ReturnStatement[] => {
      if (node.type === 'ReturnStatement')
        return [node];

      if (
        node.type === 'ArrowFunctionExpression' ||
        node.type === 'FunctionExpression' ||
        node.type === 'FunctionDeclaration'
      )
        return [];

      const keys = context.sourceCode.visitorKeys[node.type] ?? [];

      return keys.flatMap((key) => {
        const child = (node as unknown as Record<string, unknown>)[key];
        const children = Array.isArray(child)
          ? child
          : [child];

        return children
          .filter(
            (entry): entry is TSESTree.Node =>
              typeof entry === 'object' && entry !== null && 'type' in entry,
          )
          .flatMap(returnsWithin);
      });
    };

    // `computed<T>(() => ({ ... }))` and `const x: Ref<T> = computed(() => ({ ... }))` name the
    // callback's result type on the call; the owner wants it on the literal, as `as const satisfies T`.
    const callbackTargetOf = (call: TSESTree.CallExpression): CallbackTarget | null => {
      const parent = call.parent;
      const generic = call.typeArguments?.params.length === 1
        ? call.typeArguments
        : undefined;
      const declarator =
        parent.type === 'VariableDeclarator' &&
        parent.init === call &&
        parent.id.type === 'Identifier'
          ? parent.id.typeAnnotation
          : undefined;
      const reference = declarator?.typeAnnotation;
      const typeNode =
        generic?.params[0] ??
        (reference?.type === 'TSTypeReference' && reference.typeArguments?.params.length === 1
          ? reference.typeArguments.params[0]
          : undefined);
      const callback = call.arguments.find(
        (argument) =>
          argument.type === 'ArrowFunctionExpression' || argument.type === 'FunctionExpression',
      );

      if (typeNode === undefined || callback === undefined)
        return null;

      if (callback.type !== 'ArrowFunctionExpression' && callback.type !== 'FunctionExpression')
        return null;

      if (callback.async || callback.generator || callback.returnType !== undefined)
        return null;

      const literals = returnedLiterals(callback);
      const declared = toType(typeNode);
      const tsCallback = services.esTreeNodeToTSNodeMap.get(callback) as ts.Expression;
      const signature = checker.getContextualType(tsCallback)?.getCallSignatures()[0];
      const result =
        signature === undefined
          ? undefined
          : checker.getReturnTypeOfSignature(signature);

      if (literals.length === 0 || result === undefined || isLoose(declared) || isLoose(result))
        return null;

      if (
        !checker.isTypeAssignableTo(declared, result) ||
        !checker.isTypeAssignableTo(result, declared)
      )
        return null;

      if (literals.some((literal) => loosensLiteral(literal, declared, checker, services)))
        return null;

      if (literals.some((literal) => narrowsLiteral(literal, declared, checker)))
        return null;

      const removal = generic ?? declarator;

      return removal === undefined
        ? null
        : {
            callback,
            literals,
            typeNode,
            removal,
          };
    };

    const leave = (node: Fn): void => {
      const returns = functions.pop() ?? [];
      const method =
        node.type === 'FunctionDeclaration'
          ? null
          : arrayMethodOf(node, checker, services);

      if (method !== null && node.type !== 'FunctionDeclaration') {
        checkParameters(node, method);
      }

      checkReturn(node, returns, method);
    };

    return {
      ArrowFunctionExpression: (): void => {
        functions.push([]);
      },
      'ArrowFunctionExpression:exit': leave,
      FunctionExpression: (): void => {
        functions.push([]);
      },
      'FunctionExpression:exit': leave,
      FunctionDeclaration: (): void => {
        functions.push([]);
      },
      'FunctionDeclaration:exit': leave,
      TSSatisfiesExpression(node): void {
        const inner =
          node.expression.type === 'TSAsExpression'
            ? node.expression.expression
            : node.expression;

        if (!isLiteral(inner)) return;

        const keyword = context.sourceCode.getTokenBefore(node.typeAnnotation);
        const last = keyword === null
          ? null
          : context.sourceCode.getTokenBefore(keyword);
        const stated = context.sourceCode.getText(node.typeAnnotation);
        const literalOnly = withoutEmptyMembers(node.typeAnnotation, context.sourceCode);

        if (last === null) return;

        if (last.value === 'const' && literalOnly !== stated) {
          context.report({
            node,
            messageId: 'literalNeverNull',
            fix: (fixer) => fixer.replaceText(node.typeAnnotation, literalOnly),
          });

          return;
        }

        if (node.expression.type === 'TSAsExpression') return;

        context.report({
          node,
          messageId: 'bareSatisfies',
          fix: (fixer) => fixer.insertTextAfter(last, ' as const'),
        });
      },
      CallExpression(node): void {
        const target = callbackTargetOf(node);

        if (target === null) return;

        const typeText = context.sourceCode.getText(target.typeNode);

        context.report({
          node: target.removal,
          messageId: 'callbackTarget',
          fix: (fixer) => [
            fixer.remove(target.removal),
            ...target.literals.map((literal) =>
              fixer.insertTextAfter(
                literalEnd(literal, target.callback),
                ` as const satisfies ${typeText}`,
              ),
            ),
          ],
        });
      },
      ReturnStatement(node): void {
        functions.at(-1)?.push(node);
      },
      VariableDeclarator(node): void {
        const annotation = node.id.typeAnnotation;
        const init = node.init;

        if (annotation === undefined || init === null) return;

        if (init.type === 'CallExpression' && callbackTargetOf(init) !== null) return;

        if (node.id.type !== 'Identifier' || node.parent.kind !== 'const') return;

        const scope = context.sourceCode.scopeManager;
        const declared = toType(annotation.typeAnnotation);

        if (scope !== null && isConstantObject(node, init, declared, checker, scope)) {
          const typeText = context.sourceCode.getText(annotation.typeAnnotation);

          context.report({
            node: annotation,
            messageId: 'table',
            fix: (fixer) => [
              fixer.remove(annotation),
              fixer.insertTextAfter(init, ` as const satisfies ${typeText}`),
            ],
          });

          return;
        }

        if (isConstantList(init, declared, checker, node)) {
          const typeText = context.sourceCode.getText(annotation.typeAnnotation);

          context.report({
            node: annotation,
            messageId: 'list',
            fix: (fixer) => [
              fixer.remove(annotation),
              fixer.insertTextAfter(init, ` as const satisfies ${typeText}`),
            ],
          });

          return;
        }

        const declarator = services.esTreeNodeToTSNodeMap.get(node);
        const valueNode = services.esTreeNodeToTSNodeMap.get(init) as ts.Expression;
        const value = checker.getTypeAtLocation(valueNode);

        if (isLoose(declared) || isLoose(value)) return;

        if (addsIndex(declared, value, checker)) return;

        if (!checker.isTypeAssignableTo(declared, value)) return;

        if (!checker.isTypeAssignableTo(value, declared)) return;

        if (isContextTypedFunction(valueNode)) return;

        if (leansOnAnnotation(valueNode, checker)) return;

        if (readsLoopNarrowedLet(declarator, valueNode, checker)) return;

        context.report({
          node: annotation,
          messageId: 'redundant',
          fix: (fixer) => fixer.remove(annotation),
        });
      },
    };
  },
});
