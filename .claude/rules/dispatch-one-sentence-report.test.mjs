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
// The dispatch-one-sentence-report rule in scoped-rules.json against the NoMercy agent-dispatch-guard: an agent type
// without a Write tool (Explore) cannot write a details file, so the rule must name a report line for it that both
// guards accept (2026-11-12: a read-only review brief could pass neither guard, whatever it said).
// Run: node .claude/rules/dispatch-one-sentence-report.test.mjs (exit 0 = both guards accept the line the rule names).
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { dispatchFailures } from 'file:///C:/Projects/NoMercy/.claude/scripts/rules-gate.mjs';

const registry = JSON.parse(readFileSync(new URL('./scoped-rules.json', import.meta.url), 'utf8'));
const rule = registry.dispatch.find((entry) => entry.id === 'dispatch-one-sentence-report');
const noWriteLine = /without a Write tool[^']*'([^']+)'/.exec(rule?.text ?? '')?.[1];
let failures = 0;
const check = (name, ok, detail = '') => {
  if (!ok) failures++;
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? `: ${detail}` : ''}`);
};

check('the rule names a report line for an agent type without a Write tool', Boolean(noWriteLine), noWriteLine ?? 'none');

if (noWriteLine) {
  const brief = `You are one researcher worker. Model: sonnet.\nTask (one item): read src/a.ts and say what it exports.\nTime budget: 10 minutes.\n${noWriteLine}`;
  const rulesGate = dispatchFailures(registry, { prompt: brief, subagent_type: 'Explore', model: 'sonnet' }).some((entry) => entry.id === 'dispatch-one-sentence-report');

  check('rules-gate accepts that line in an Explore brief', !rulesGate);

  const dir = mkdtempSync(join(tmpdir(), 'one-sentence-report-'));
  const draft = join(dir, 'brief.md');

  writeFileSync(draft, brief);

  let output = '';

  try {
    output = execFileSync('node', ['C:/Projects/NoMercy/.claude/scripts/brief-check.mjs', draft, '--type', 'Explore', '--model', 'sonnet'], { encoding: 'utf8' });
  } catch (error) {
    output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }

  check('brief-check does not refuse that line as a file an Explore agent cannot write', !/has no Write tool/.test(output));
}

process.exit(failures ? 1 : 0);
