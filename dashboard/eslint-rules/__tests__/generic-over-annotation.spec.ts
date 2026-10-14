// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { RuleTester } from '@typescript-eslint/rule-tester';
import { afterAll, describe, it } from 'vitest';

import { genericOverAnnotation } from '../generic-over-annotation';

RuleTester.afterAll = afterAll;
RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.itOnly = it.only;

const tester = new RuleTester({
  languageOptions: {
    parserOptions: {
      projectService: { allowDefaultProject: ['*.ts'] },
      tsconfigRootDir: import.meta.dirname,
    },
  },
});

// Vue declares ref and computed with conditional returns; these stand-ins keep that shape.
const vue = [
  'interface Ref<T> { value: T }',
  'interface ComputedRef<T> { readonly value: T }',
  'declare function ref<T>(value: T): [T] extends [Ref<unknown>] ? T : Ref<T>;',
  'declare function computed<T>(getter: () => T): T extends never ? never : ComputedRef<T>;',
  'declare function load<T>(key: string): Promise<T>;',
  'declare function makeCount(): ComputedRef<number>;',
  'declare function pick<T, K>(value: T, key: K): Promise<T>;',
].join('\n');

function withVue(code: string): string {
  return `${vue}\n${code}`;
}

tester.run('generic-over-annotation', genericOverAnnotation, {
  valid: [
    withVue('const route = computed<{ name: string } | null>(() => null);'),
    withVue('const count = ref<number>(0);'),
    // The annotation names another wrapper than the call returns.
    withVue('const count: Ref<number> = computed(() => 1);'),
    // A call that is not generic has nowhere to take the type.
    withVue('const count: ComputedRef<number> = makeCount();'),
    // The annotation's arguments do not line up with the call's type parameters.
    withVue('const value: Promise<string> = pick("a", 1);'),
    // A call that already passes other type arguments is left alone.
    withVue('const set: Set<string> = new Set<"a">();'),
    withVue('let count: Ref<number>;'),
    // The call resolves to a non-generic overload, so a generic would not compile.
    'let gate: Promise<void> = Promise.resolve();',
  ],
  invalid: [
    {
      code: withVue(
        'const route: ComputedRef<{ name: string; label: string } | null> = computed(() => null);',
      ),
      output: withVue('const route = computed<{ name: string; label: string } | null>(() => null);'),
      errors: [{ messageId: 'generic' }],
    },
    {
      code: withVue('const count: Ref<number | null> = ref(null);'),
      output: withVue('const count = ref<number | null>(null);'),
      errors: [{ messageId: 'generic' }],
    },
    {
      code: withVue('const known: Set<string> = new Set(["a"]);'),
      output: withVue('const known = new Set<string>(["a"]);'),
      errors: [{ messageId: 'generic' }],
    },
    {
      code: withVue('const settings: Promise<number> = load("key");'),
      output: withVue('const settings = load<number>("key");'),
      errors: [{ messageId: 'generic' }],
    },
    // The call already carries the type: only the annotation goes.
    {
      code: withVue(
        'const map: ComputedRef<Record<string, string>> = computed<Record<string, string>>(() => ({}));',
      ),
      output: withVue('const map = computed<Record<string, string>>(() => ({}));'),
      errors: [{ messageId: 'generic' }],
    },
  ],
});
