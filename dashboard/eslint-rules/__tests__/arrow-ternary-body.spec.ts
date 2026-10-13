// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { RuleTester } from '@typescript-eslint/rule-tester'
import { afterAll, describe, it } from 'vitest'

import { arrowTernaryBody } from '../arrow-ternary-body'

RuleTester.afterAll = afterAll
RuleTester.describe = describe
RuleTester.it = it
RuleTester.itOnly = it.only

const tester = new RuleTester()

tester.run('arrow-ternary-body', arrowTernaryBody, {
  valid: [
    // A ternary body starts on the line after the arrow, without brackets.
    'const pick = (a: boolean) =>\n  a\n    ? 1\n    : 2',
    // Brackets that belong to a member read stay.
    "const bare = (name: string) => (name ? 'a' : 'b').toLowerCase()",
    // An object body keeps its brackets.
    'const make = () => ({ a: 1 })',
  ],
  invalid: [
    {
      code: 'const pick = (a: boolean) => (a\n  ? 1\n  : 2)',
      output: 'const pick = (a: boolean) =>\n  a\n  ? 1\n  : 2',
      errors: [{ messageId: 'nextLine' }],
    },
    {
      code: 'list.map((row) => (row.on\n  ? { ...row }\n  : row))',
      output: 'list.map((row) =>\n  row.on\n  ? { ...row }\n  : row)',
      errors: [{ messageId: 'nextLine' }],
    },
    {
      code: 'function f() {\n  return (a: boolean) => (a ? 1 : 2)\n}',
      output: 'function f() {\n  return (a: boolean) =>\n    a ? 1 : 2\n}',
      errors: [{ messageId: 'nextLine' }],
    },
  ],
})
