// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

// The sibling settings tabs sit in one page container. Each tab's root panel
// must take the container width: a per-tab max-w-* makes one tab narrower
// than the tab beside it. ReadabilityView is left out on purpose: SettingsView.spec
// pins its max-w-4xl cap (label-to-control distance on wide screens).
const tabs = ['BasicsView', 'PersonalityView', 'EngagementView'] as const;

function rootPanelClass(name: string): string {
  const source = readFileSync(resolve(__dirname, '..', `${name}.vue`), 'utf8');
  const template = source.slice(source.indexOf('<template>'));
  const match = /<PanelCard\b[^>]*?\bclass="([^"]*)"/s.exec(template);

  if (!match)
    throw new Error(`${name} has no root PanelCard with a class`);

  return match[1] ?? '';
}

describe('settings tabs share one panel width', () => {
  it.each(tabs)('%s root panel has no max-width of its own', (name) => {
    expect(rootPanelClass(name)).not.toMatch(/(^|\s)max-w-/);
  });
});
