// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
// The dispatch-one-item rule in scoped-rules.json: one work item per builder, inline in the prompt.
// Run: node .claude/rules/dispatch-one-item.test.mjs (exit 0 = every case decided as expected).
import { readFileSync } from 'node:fs';
import { dispatchFailures } from 'file:///C:/Projects/NoMercy/.claude/scripts/rules-gate.mjs';

const registry = JSON.parse(readFileSync(new URL('./scoped-rules.json', import.meta.url), 'utf8'));
const budget = 'Time budget: 20 minutes. Context budget 100k: at 100k, stop, write a handoff and report its path.';
const cases = [
  ['numbered items', `## Work\n1. Volume 0 stays muted.\n2. TTS listeners attach once.\n${budget}`, true],
  ['numbered items with a heading', `## Work\n### 1. Permit leak\n### 2. Timeout\n${budget}`, true],
  ['lettered items', `## Tests\na. App test.\nb. Server test.\n${budget}`, true],
  ['item words', `Item 1: fix B.\nItem 2: fix C.\n${budget}`, true],
  ['brief by file reference', `Your full brief is in C:/x/brief.md. Read it now. ${budget}`, true],
  ['one item, steps as bullets', `## Work\n1. Fix the permit leak.\n- write the test\n- run it\n- fix\n${budget}`, false],
  ['a manager listing lanes', `You are a manager. Lanes:\n1. overlay\n2. sdk\nLanes 1 and 2 run in parallel. ${budget}`, false],
  ['a version number mid-line', `Use .NET 2.0 style? No: target net10.0. ${budget}`, false],
];

let failures = 0;
for (const [name, prompt, rejected] of cases) {
  const got = dispatchFailures(registry, { prompt, subagent_type: 'general-purpose', model: 'sonnet' })
    .some((d) => d.id === 'dispatch-one-item');
  if (got !== rejected) failures++;
  console.log(`${got === rejected ? 'ok  ' : 'FAIL'} ${name}: rejected=${got}, expected ${rejected}`);
}
process.exit(failures ? 1 : 0);
