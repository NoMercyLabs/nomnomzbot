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

import { membersPerLine } from '../members-per-line';

RuleTester.afterAll = afterAll;
RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.itOnly = it.only;

const tester = new RuleTester();

tester.run('members-per-line', membersPerLine, {
  valid: [
    // Three type members, each on its own line.
    'type Query = {\n  data: string;\n  isPending: boolean;\n  isError: boolean;\n}',
    // Two entries fit on one line.
    'type Link = { name: string; label: string }',
    'const link = { name: 1, label: 2 }',
    // Three object entries, each on its own line.
    'const all = {\n  ...labels,\n  ...words,\n  roles: 1,\n}',
  ],
  invalid: [
    {
      code: 'type Query<T> = { data: T; isPending: boolean; isError: boolean }',
      output: 'type Query<T> = {\n  data: T;\n  isPending: boolean;\n  isError: boolean;\n}',
      errors: [{ messageId: 'perLine' }],
    },
    {
      code: 'function f() {\n  const q: { a: 1, b: 2, c: 3 } = x\n}',
      output: 'function f() {\n  const q: {\n    a: 1;\n    b: 2;\n    c: 3;\n  } = x\n}',
      errors: [{ messageId: 'perLine' }],
    },
    {
      code: 'type Half = {\n  a: 1; b: 2\n  c: 3\n}',
      output: 'type Half = {\n  a: 1;\n  b: 2;\n  c: 3;\n}',
      errors: [{ messageId: 'perLine' }],
    },
    {
      code: 'function f() {\n  return { ...labels, ...words(\'x\'), roles: words(\'y\') }\n}',
      output:
        'function f() {\n  return {\n    ...labels,\n    ...words(\'x\'),\n    roles: words(\'y\'),\n  }\n}',
      errors: [{ messageId: 'perLine' }],
    },
  ],
});
