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

import { trailingArrowArgument } from '../trailing-arrow-argument';

RuleTester.afterAll = afterAll;
RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.itOnly = it.only;

const tester = new RuleTester();

tester.run('trailing-arrow-argument', trailingArrowArgument, {
  valid: [
    // The arrow after another argument ends its line at `=>`; the body starts on the next line.
    'set(key, (current) =>\n  apply(current))',
    // A lone or first callback stays on one line.
    'ids.map((row) => row.id)',
    'watch(() => open, run)',
    // An arrow on its own line, as a long argument list breaks, stays.
    'set(\n  key,\n  (current) => apply(current),\n)',
    // An arrow with more arguments after it is not the trailing one, so it stays on one line.
    'watch(settings, (next) => apply(next), { deep: true })',
    // A block body stays.
    'set(key, (current) => {\n  apply(current)\n})',
    // An object or array body opens on the `=>` line, like a block body.
    'vi.mock(\'../client\', () => ({ client: http }))',
    'attach(hub, handlers, () => ({\n  queries,\n  channelId: joined,\n}))',
    'set(key, (feed) => [\n  ...feed,\n  line,\n])',
  ],
  invalid: [
    {
      code: '  set(key, (current) => apply(current, variables))',
      output: '  set(key, (current) =>\n    apply(current, variables))',
      errors: [{ messageId: 'bodyNextLine' }],
    },
    {
      // Owner 2026-10-13, on `() =>` with `({` on the next line: "this is terrible for reading".
      code: 'attach(hub, handlers, () =>\n  ({\n    queries,\n  }))',
      output: 'attach(hub, handlers, () => ({\n    queries,\n  }))',
      errors: [{ messageId: 'literalOnArrowLine' }],
    },
    {
      code: 'set(key, (feed) =>\n  [...feed, line])',
      output: 'set(key, (feed) => [...feed, line])',
      errors: [{ messageId: 'literalOnArrowLine' }],
    },
  ],
});
