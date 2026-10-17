// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

const src = join(__dirname, '..');
const designerChevron = /from '~icons\/nnz\/chevron-(down|up|left|right)'/;
const openCloseTrigger = /<(CollapsibleTrigger|AccordionTrigger|PopoverTrigger|DropdownMenuTrigger)\b|aria-expanded/;

// Triggers that are an icon only (a bell, a kebab, a clock) or a text box that
// suggests as you type. A chevron next to a lone icon would be noise.
const iconOnlyOrTextBox = new Set([
  'components/layout/AssetCard.vue',
  'components/layout/TemplateField.vue',
  'components/layout/profile/ProfileNotes.vue',
  'layouts/dashboard/ErrorInbox.vue',
  'views/dashboard/chat/components/ChatComposer.vue',
  'views/dashboard/chat/components/ChatDockHeader.vue',
  'views/dashboard/chat/components/ChatLine.vue',
  'views/dashboard/pipelines/components/PipelineMenu.vue',
]);

function vueFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory()) {
      return entry.name === '__tests__'
        ? []
        : vueFiles(path);
    }

    return path.endsWith('.vue')
      ? [path]
      : [];
  });
}

function relativePath(path: string): string {
  return relative(src, path).replace(/\\/g, '/');
}

function hasChevron(name: string): boolean {
  const text = readFileSync(join(src, name), 'utf8');

  return designerChevron.test(text);
}

// Stoney 2026-10-17: "menu's that open and close MUST contain an indication like a chevron".
describe('menus that open and close show a chevron', () => {
  const owned = vueFiles(src).filter((path) => {
    const name = relativePath(path);

    return !name.startsWith('components/ui/') &&
      !name.startsWith('views/dashboard/channel-points/');
  });

  it('gives every file with an open/close trigger a designer-pack chevron', () => {
    const missing = owned
      .filter((path) => openCloseTrigger.test(readFileSync(path, 'utf8')))
      .map(relativePath)
      .filter((name) => !iconOnlyOrTextBox.has(name))
      .filter((name) => !hasChevron(name));

    expect(missing).toEqual([]);
  });

  it.each([
    'components/ui/select/SelectTrigger.vue',
    'components/ui/dropdown-menu/DropdownMenuSubTrigger.vue',
  ])('draws the chevron of %s from the designer pack', (name) => {
    const text = readFileSync(join(src, name), 'utf8');

    expect(text).toMatch(designerChevron);
    expect(text).not.toContain('@lucide/vue');
  });

  it('turns the chevron of the shared select trigger on open', () => {
    const text = readFileSync(join(src, 'components/ui/select/SelectTrigger.vue'), 'utf8');

    expect(text).toContain('group-data-[state=open]:rotate-180');
  });
});
