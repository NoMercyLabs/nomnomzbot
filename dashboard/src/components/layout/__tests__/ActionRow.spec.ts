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

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { h } from 'vue';

import ActionRow from '@/components/layout/ActionRow.vue';

const src = join(__dirname, '..', '..', '..');

// The seven places that drew an option row with the old settings-only ReadabilityRow.
const importers = [
  'views/dashboard/overlays/components/AudioMixPanel.vue',
  'views/dashboard/settings/BasicsView.vue',
  'views/dashboard/settings/EngagementView.vue',
  'views/dashboard/settings/ReadabilityView.vue',
  'views/dashboard/settings/white-label-bot/WhiteLabelBotView.vue',
  'views/dashboard/tts/RulesPanel.vue',
  'views/dashboard/tts/SpeakingPanel.vue',
] as const;

function textOf(path: string): string {
  return readFileSync(join(src, path), 'utf8');
}

describe('an action row', () => {
  it('shows the title, the help line and the hint, then the control after them', () => {
    const wrapper = mount(ActionRow, {
      props: {
        title: 'Command prefix',
        description: 'The sign before every command.',
        hint: 'Up to 5 signs.',
      },
      slots: { default: () => h('button', { 'data-control': '' }, 'Reset') },
    });
    const row = wrapper.get('[data-slot="action-row"]');
    const text = row.get('[data-slot="action-row-text"]');
    const action = row.get('[data-slot="action-row-action"]');

    expect(text.get('[data-row-title]').text()).toBe('Command prefix');
    expect(text.get('[data-row-description]').text()).toBe('The sign before every command.');
    expect(text.get('[data-row-hint]').text()).toBe('Up to 5 signs.');
    expect(action.find('[data-control]').exists()).toBe(true);
    const order = text.element.compareDocumentPosition(action.element);

    expect(order).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });

  it('draws no help line when the option has none', () => {
    const wrapper = mount(ActionRow, { props: { title: 'Timezone' } });

    expect(wrapper.find('[data-row-description]').exists()).toBe(false);
    expect(wrapper.find('[data-row-hint]').exists()).toBe(false);
  });

  it('lets the action drop under the text on a narrow screen instead of squeezing the title', () => {
    const wrapper = mount(ActionRow, { props: { title: 'Volume' } });
    const row = wrapper.get('[data-slot="action-row"]');

    expect(row.classes()).toContain('flex-wrap');
    expect(row.get('[data-slot="action-row-text"]').classes()).toContain('basis-56');
  });
});

describe('the action row importers', () => {
  it('lives with the shared rows, and the settings-only copy is gone', () => {
    expect(existsSync(join(src, 'views/dashboard/settings/components/ReadabilityRow.vue'))).toBe(false);
  });

  it.each(importers)('%s draws its option rows with ActionRow', (path) => {
    const text = textOf(path);

    expect(text).toContain('import ActionRow from \'@/components/layout/ActionRow.vue\';');
    expect(text).toMatch(/<ActionRow\b/);
    expect(text).not.toContain('ReadabilityRow');
  });
});
