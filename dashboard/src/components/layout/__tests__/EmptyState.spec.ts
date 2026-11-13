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

import { mount } from '@vue/test-utils';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

import EmptyState from '@/components/layout/EmptyState.vue';

const src = join(__dirname, '..', '..', '..');

function vueFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory())
      return vueFiles(path);

    return path.endsWith('.vue')
      ? [path]
      : [];
  });
}

// Aaoa's layout todo P1: an empty block is <EmptyState>, never a hand-written data-slot="panel-empty".
describe('hand-built empty blocks', () => {
  it('stay out of every file except EmptyState itself', () => {
    const offenders = vueFiles(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => path !== 'components/layout/EmptyState.vue')
      .filter((path) => readFileSync(join(src, path), 'utf8').includes('data-slot="panel-empty"'));

    expect(offenders).toEqual([]);
  });
});

describe('the empty state', () => {
  it('marks its kind and shows the title and description', () => {
    const wrapper = mount(EmptyState, {
      props: {
        kind: 'noMatch',
        title: 'No results',
        description: 'Try another word.',
      },
    });
    const root = wrapper.find('[data-slot="panel-empty"]');

    expect(root.attributes('data-empty-kind')).toBe('noMatch');
    expect(root.text()).toContain('No results');
    expect(root.text()).toContain('Try another word.');
    expect(root.find('svg').exists()).toBe(true);
  });

  it('defaults to the none kind', () => {
    const wrapper = mount(EmptyState, { props: { description: 'Nothing here.' } });

    expect(wrapper.attributes('data-empty-kind')).toBe('none');
  });

  it('renders the action slot as the one primary action', () => {
    const wrapper = mount(EmptyState, {
      props: { description: 'Nothing here.' },
      slots: { action: '<button type="button" data-make>Make the first</button>' },
    });
    const action = wrapper.find('[data-slot="panel-empty-action"]');

    expect(action.find('button[data-make]').text()).toBe('Make the first');
  });

  it('shows no icon when icon is false and none when bare', () => {
    const plain = mount(EmptyState, {
      props: {
        description: 'Plain.',
        icon: false,
      },
    });
    const bare = mount(EmptyState, {
      props: {
        description: 'Bare.',
        bare: true,
      },
    });

    expect(plain.find('svg').exists()).toBe(false);
    expect(bare.find('svg').exists()).toBe(false);
    expect(bare.classes()).not.toContain('items-center');
  });
});
