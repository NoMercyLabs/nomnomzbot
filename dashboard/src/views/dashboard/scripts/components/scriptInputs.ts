// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { TestTrigger } from '@/composables/useEditorBridge';
import { humanize } from '@/lib/text';

export type ScriptInputs = { variables: string[]; usesArgs: boolean };

const VARIABLE_READ = /bot\.getVar\(\s*(['"])([^'"]+)\1/g;
const ARGS_READ = /\bbot\.args\b/;

// What a script reads from whatever starts it, taken from its own code: the pipeline variables it asks
// for by name, and whether it uses the words typed after a command. The bot declares no events for a script.
export function readInputs(source: string): ScriptInputs {
  const names = Array.from(source.matchAll(VARIABLE_READ), (match) =>
    match[2] ?? '');

  return {
    variables: [...new Set(names)],
    usesArgs: ARGS_READ.test(source),
  };
}

type Say = (key: string) => string;
type Has = (key: string) => boolean;

const READS = 'dashboard.scripts.detail.reads';

// The inputs as plain lines: the typed words first, then each variable in words, or in its own words when
// the page has none for it ("followed_at" reads "Followed at").
export function inputLines(inputs: ScriptInputs, t: Say, te: Has): string[] {
  const named: string[] = inputs.variables.map((name) => {
    const path = `${READS}.names.${name.replaceAll('.', '_')}`;

    return te(path)
      ? t(path)
      : humanize(name.replaceAll('.', ' '));
  });

  return inputs.usesArgs
    ? [t(`${READS}.args`), ...named]
    : named;
}

// The sample events that hand over at least one variable the script reads: the events it can be tested with.
export function eventsFor(triggers: TestTrigger[], variables: string[]): TestTrigger[] {
  return triggers.filter((sample) => variables.some((name) => name in sample.variables));
}
