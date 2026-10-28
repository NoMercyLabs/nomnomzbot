// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { RuleTester } from '@typescript-eslint/rule-tester';
import { afterAll, describe, it } from 'vitest';

import { maxCallNesting } from '../max-call-nesting';

RuleTester.afterAll = afterAll;
RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.itOnly = it.only;

const tester = new RuleTester();

tester.run('max-call-nesting', maxCallNesting, {
  valid: [
    // Two levels read fine.
    'const clamped = Math.min(1, Math.max(0, v))',
    // A chain reads left to right; it is not nesting.
    'const text = wrapper.find(\'#a\').find(\'#b\').text().trim()',
    // A function argument starts over.
    'const bytes = color.map((v) => Math.round(v * 255)).join(" ")',
    // A named property starts over: its name names the step.
    'const line = t(\'timedOut\', { minutes: Math.max(1, Math.round(seconds / 60)) })',
    // A test's expect() does not count.
    'expect(queries.getQueryData(widgetsKey(\'c1\'))).toEqual(before)',
  ],
  invalid: [
    {
      // The owner's example, 2026-10-13.
      code: 'const css = `rgb(${color.map((v) => Math.round(Math.min(1, Math.max(0, v)) * 255)).join(\' \')})`',
      errors: [{ messageId: 'tooDeep', data: { depth: 3 } }],
    },
    {
      code: 'const accent = css(tint(rgb(scheme.muted), accent, 0.2, theme))',
      errors: [{ messageId: 'tooDeep', data: { depth: 3 } }],
    },
    {
      // Inside a property, the count starts over and still applies.
      code: 'const line = t(\'k\', { a: f(g(h(1))) })',
      errors: [{ messageId: 'tooDeep', data: { depth: 3 } }],
    },
    {
      // Reported once, on the outer call.
      code: 'f(g(h(i(1))))',
      errors: [{ messageId: 'tooDeep', data: { depth: 4 } }],
    },
  ],
});
