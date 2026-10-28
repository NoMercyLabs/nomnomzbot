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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { TemplateHelper } from '@/api/templates';
import HelperPanel from '@/components/layout/HelperPanel.vue';
import { i18n } from '@/i18n';

const rows = vi.hoisted(() => {
  const fixed: TemplateHelper[] = [
    {
      key: 'user',
      descriptionKey: 'template.helper.user',
      sample: 'DukaSoft',
    },
    {
      key: 'user.name',
      descriptionKey: 'template.helper.user_name',
      sample: 'DukaSoft',
    },
    {
      key: 'channel',
      descriptionKey: 'template.helper.channel',
      sample: 'stoney_eagle',
    },
  ];

  for (let index = 1; index <= 70; index++) {
    fixed.push({
      key: `text.fn${index}`,
      descriptionKey: `template.helper.none${index}`,
      sample: `out${index}`,
    });
  }

  return fixed;
});

vi.mock('@/api/templates', () => ({
  useTemplateHelpers: () => ({ data: ref(rows) }),
}));

let mounted: VueWrapper | null = null;

function panel(insert: (text: string) => void = () => undefined): VueWrapper {
  mounted = mount(HelperPanel, {
    attachTo: document.body,
    props: {
      target: { insert },
      context: 'command',
    },
    global: { plugins: [i18n] },
  });

  return mounted;
}

function rowKeys(): string[] {
  return [...document.body.querySelectorAll('[data-helper-row]')]
    .map((row) => row.getAttribute('data-helper-row') ?? '');
}

function groupNames(): string[] {
  return [...document.body.querySelectorAll('[data-helper-group]')]
    .map((group) => group.getAttribute('data-helper-group') ?? '');
}

async function search(wrapper: VueWrapper, value: string): Promise<void> {
  await wrapper.find('input').setValue(value);
  await flushPromises();
}

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('the helper panel at 1000+ rows', () => {
  it('shows every group collapsed with no row rendered until a query or a press', () => {
    panel();

    expect(groupNames()).toEqual(['user', 'channel', 'text']);
    expect(rowKeys()).toEqual([]);
  });

  it('says how many helpers each group holds', () => {
    panel();

    const counts: string[] = [...document.body.querySelectorAll('[data-helper-count]')]
      .map((badge) => badge.getAttribute('aria-label') ?? '');

    expect(counts).toEqual(['2 helpers', '1 helper', '70 helpers']);
  });

  it('opens a group when its header is pressed and closes it on a second press', async () => {
    const wrapper = panel();
    const header = () => wrapper.find('[data-helper-group="user"] [data-helper-group-trigger]');

    await header().trigger('click');

    expect(rowKeys()).toEqual(['user', 'user.name']);

    await header().trigger('click');

    expect(rowKeys()).toEqual([]);
  });

  it('opens only the groups a query matches and hides the others', async () => {
    const wrapper = panel();

    await search(wrapper, 'stoney_eagle');

    expect(groupNames()).toEqual(['channel']);
    expect(rowKeys()).toEqual(['channel']);
  });

  it('matches the description text and the sample, not only the key', async () => {
    const wrapper = panel();

    await search(wrapper, i18n.global.t('template.helper.user_name'));

    expect(rowKeys()).toEqual(['user.name']);

    await search(wrapper, 'out7');

    expect(rowKeys()).toContain('text.fn7');
    expect(groupNames()).toEqual(['text']);
  });

  it('marks the part of the key that matched', async () => {
    const wrapper = panel();

    await search(wrapper, 'nam');

    const marked: string[] = [...document.body.querySelectorAll('[data-helper-row="user.name"] [data-helper-key] mark')]
      .map((mark) => mark.textContent ?? '');

    expect(marked).toEqual(['nam']);
  });

  it('shows a bounded slice of a big group and reveals the rest on Show more', async () => {
    const wrapper = panel();

    await wrapper.find('[data-helper-group="text"] [data-helper-group-trigger]').trigger('click');

    expect(rowKeys()).toHaveLength(60);

    const more = wrapper.find('[data-helper-more]');

    expect(more.text()).toBe('Show 10 more');

    await more.trigger('click');

    expect(rowKeys()).toHaveLength(70);
    expect(wrapper.find('[data-helper-more]').exists()).toBe(false);
  });

  it('says so when nothing matches', async () => {
    const wrapper = panel();

    await search(wrapper, 'zzzzzz');

    expect(groupNames()).toEqual([]);
    expect(wrapper.text()).toContain(i18n.global.t('template.panel.empty'));
  });

  it('inserts the exact braced key on a press, without taking focus', async () => {
    const inserted: string[] = [];
    const wrapper = panel((text) => inserted.push(text));

    await search(wrapper, 'user.name');

    const row = wrapper.find('[data-helper-row="user.name"]');
    const down = new MouseEvent('mousedown', {
      bubbles: true,
      cancelable: true,
    });

    row.element.dispatchEvent(down);
    await row.trigger('click');

    expect(down.defaultPrevented).toBe(true);
    expect(inserted).toEqual(['{user.name}']);
  });

  it('gives each group header at least 36px of height', () => {
    panel();

    const trigger = document.body.querySelector<HTMLElement>('[data-helper-group-trigger]')!;

    expect(trigger.className).toContain('min-h-9');
  });
});
