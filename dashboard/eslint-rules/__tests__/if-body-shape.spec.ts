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

import { ifBodyShape } from '../if-body-shape'

RuleTester.afterAll = afterAll
RuleTester.describe = describe
RuleTester.it = it
RuleTester.itOnly = it.only

const tester = new RuleTester()

tester.run('if-body-shape', ifBodyShape, {
  valid: [
    // A bare exit stays on the if line.
    'function f(a: boolean) { if (a) return }',
    'for (const a of [1]) { if (a) continue }',
    'for (const a of [1]) { if (a) break }',
    // A returned or thrown value goes on the next line, one level in.
    'function f(a: boolean) {\n  if (a)\n    return 1\n\n  return 2\n}',
    'function f(a: boolean) {\n  if (a)\n    throw new Error()\n}',
    // Anything else sits in a block.
    'function f(a: boolean) {\n  let b = 0\n  if (a) {\n    b = 1\n  }\n}',
    'function f(a: boolean) {\n  if (a)\n    return 1\n  else\n    return 2\n}',
    'function f(a: boolean, b: boolean) {\n  if (a) {\n    g()\n  } else if (b) {\n    g()\n  }\n}',
  ],
  invalid: [
    {
      code: 'function f(a: boolean) {\n  if (a) return 1\n}',
      output: 'function f(a: boolean) {\n  if (a)\n    return 1\n}',
      errors: [{ messageId: 'nextLine' }],
    },
    {
      code: 'function f(a: boolean) {\n  if (a) throw new Error()\n}',
      output: 'function f(a: boolean) {\n  if (a)\n    throw new Error()\n}',
      errors: [{ messageId: 'nextLine' }],
    },
    {
      code: 'function f(a: boolean) {\n  let b = 0\n  if (a) b = 1\n}',
      output: 'function f(a: boolean) {\n  let b = 0\n  if (a) {\n    b = 1\n  }\n}',
      errors: [{ messageId: 'block' }],
    },
    {
      code: 'function f(a: boolean) {\n  if (a)\n    g()\n}',
      output: 'function f(a: boolean) {\n  if (a) {\n    g()\n  }\n}',
      errors: [{ messageId: 'block' }],
    },
    {
      code: 'function f(a: boolean) {\n  if (a)\n    return\n}',
      output: 'function f(a: boolean) {\n  if (a) return\n}',
      errors: [{ messageId: 'sameLine' }],
    },
    {
      code: 'function f(a: boolean) {\n  if (a) g()\n  else h()\n}',
      output: 'function f(a: boolean) {\n  if (a) {\n    g()\n  }\n  else {\n    h()\n  }\n}',
      errors: [{ messageId: 'block' }, { messageId: 'block' }],
    },
  ],
})
