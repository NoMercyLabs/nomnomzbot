// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { AST_NODE_TYPES, ESLintUtils, type TSESTree } from '@typescript-eslint/utils';
import ts from 'typescript';

// Owner 2026-10-13: "prefer const primaryRoute = computed<{ name: string; label: string } | null>(...)
// over const primaryRoute: ComputedRef<{ name: string; label: string } | null> = computed(...)",
// then "this is basically a use generics wherever supported". When a generic call returns the
// annotated type, the type arguments move onto the call and the annotation goes.

// Vue declares these returns as conditional types, so the signature check cannot see the wrapper.
const vueWrappers: Record<string, readonly string[]> = {
  ref: ['Ref'],
  shallowRef: ['ShallowRef'],
  computed: ['ComputedRef', 'WritableComputedRef'],
};

type Created = TSESTree.CallExpression | TSESTree.NewExpression;

// Only the overload the call resolves to counts: `Promise.resolve()` picks a non-generic one even
// though `Promise.resolve<T>(value)` exists.
function returnsWrapper(declaration: ts.Declaration | undefined, wrapper: string, count: number) {
  if (declaration === undefined || !ts.isFunctionLike(declaration))
    return false;

  const returned = declaration.type;

  return (
    declaration.typeParameters?.length === count &&
    returned !== undefined &&
    ts.isTypeReferenceNode(returned) &&
    returned.typeName.getText() === wrapper
  );
}

function isVueWrapper(created: Created, wrapper: string): boolean {
  return (
    created.callee.type === AST_NODE_TYPES.Identifier &&
    (vueWrappers[created.callee.name]?.includes(wrapper) ?? false)
  );
}

export const genericOverAnnotation = ESLintUtils.RuleCreator.withoutDocs({
  meta: {
    type: 'suggestion',
    fixable: 'code',
    schema: [],
    messages: {
      generic: 'Pass the type to the call as a generic instead of annotating the variable.',
    },
  },
  defaultOptions: [],
  create(context) {
    const source = context.sourceCode;
    const services = ESLintUtils.getParserServices(context);
    const checker = services.program.getTypeChecker();

    function takesGeneric(created: Created, wrapper: string, count: number): boolean {
      if (isVueWrapper(created, wrapper))
        return true;

      const node = services.esTreeNodeToTSNodeMap.get(created);
      const declaration = checker.getResolvedSignature(node)?.getDeclaration();

      return returnsWrapper(declaration, wrapper, count);
    }

    return {
      VariableDeclarator(declarator) {
        const annotation = declarator.id.typeAnnotation?.typeAnnotation;
        const created = declarator.init;

        if (annotation?.type !== AST_NODE_TYPES.TSTypeReference) return;

        if (
          created?.type !== AST_NODE_TYPES.CallExpression &&
          created?.type !== AST_NODE_TYPES.NewExpression
        ) {
          return;
        }

        const typeArguments = annotation.typeArguments;

        if (typeArguments === undefined) return;

        const wrapper = source.getText(annotation.typeName);
        const passed = source.getText(typeArguments);
        const existing = created.typeArguments;

        if (existing !== undefined && source.getText(existing) !== passed) return;

        if (!takesGeneric(created, wrapper, typeArguments.params.length)) return;

        const removed = declarator.id.typeAnnotation!;

        context.report({
          node: removed,
          messageId: 'generic',
          fix: (fixer) => [
            fixer.remove(removed),
            ...existing === undefined
              ? [fixer.insertTextAfter(created.callee, passed)]
              : [],
          ],
        });
      },
    };
  },
});
