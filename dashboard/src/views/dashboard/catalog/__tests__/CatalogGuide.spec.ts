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
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';

import FilterBar from '@/components/layout/FilterBar.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import CatalogGuide from '../components/CatalogGuide.vue';
import en from '../i18n/en.json';

let mounted: VueWrapper | null = null;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<VueWrapper> {
  const host = defineComponent(() => () => h(TooltipProvider, () =>
    h(CatalogGuide)));

  mounted = mount(host, {
    attachTo: document.body,
    global: { plugins: [i18n] },
  });

  await flushPromises();

  return mounted;
}

describe('the PanelCard tiers guide', () => {
  it('shows one live card per tier, each naming its rule', async () => {
    const wrapper = await open();
    const cards = wrapper
      .findAllComponents(PanelCard)
      .filter((card) => card.element.closest('[data-catalog="guide-tiers"]') !== null);

    expect(cards.map((card) => card.props('tier'))).toEqual(['lead', 'default', 'quiet']);
    expect(cards.at(0)?.text()).toContain(en.dashboard.catalog.guide.tiers.lead.rule);
    expect(cards.at(2)?.text()).toContain(en.dashboard.catalog.guide.tiers.quiet.rule);
  });
});

describe('the stable action placement guide', () => {
  it('names the four placements and keeps one primary button in the header example', async () => {
    const wrapper = await open();
    const section = wrapper.find('[data-catalog="guide-actions"]');

    expect(section.findAll('li')).toHaveLength(4);

    const header = wrapper
      .findAllComponents(PanelCard)
      .find((card) => card.element.closest('[data-catalog="guide-actions"]') !== null);
    const buttons = header?.findAllComponents(Button) ?? [];
    const variants = buttons.map((button) => button.props('variant'));

    // The primary button leaves the variant at its default, so only the outline one is named.
    expect(variants).toEqual(['outline', undefined]);
  });

  it('reports the row action the person pressed through RowEditDelete', async () => {
    const wrapper = await open();

    await wrapper.find('[data-catalog="guide-actions"] [data-row-delete]').trigger('click');

    expect(wrapper.find('[data-catalog="guide-actions"]').text()).toContain(
      `Last delete: ${en.dashboard.catalog.rows.delete}`,
    );
  });
});

describe('the advanced filter recipe', () => {
  it('lists the three steps in order and keeps the advanced filters folded at first', async () => {
    const wrapper = await open();
    const steps = wrapper.find('[data-catalog="guide-filters"]').findAll('ol li');

    expect(steps.map((step) => step.text().slice(0, 2))).toEqual(['1.', '2.', '3.']);
    expect(wrapper.find('[data-catalog-advanced]').attributes('data-state')).toBe('closed');
    expect(wrapper.find('[data-catalog-advanced-trigger]').text()).toContain(en.dashboard.catalog.guide.filters.advanced);
  });

  it('narrows the rows by the compact filter and by the search', async () => {
    const wrapper = await open();
    const demo = wrapper.find('[data-catalog-filter-demo]');

    expect(demo.text()).toContain('!so');
    expect(demo.text()).toContain('!hydrate');

    wrapper.findComponent(FilterBar).vm.$emit('update:modelValue', 'timer');
    await flushPromises();

    expect(demo.text()).not.toContain('!lurk');
    expect(demo.text()).toContain('!hydrate');
    expect(demo.text()).toContain('!socials');

    await demo.find('input[type="search"], input').setValue('hyd');

    expect(demo.text()).toContain('!hydrate');
    expect(demo.text()).not.toContain('!socials');
  });

  it('counts a set advanced filter in the trigger badge, filters by it and shows the empty text when nothing matches', async () => {
    const wrapper = await open();

    await wrapper.find('[data-catalog-advanced-trigger]').trigger('click');
    await flushPromises();

    const content = wrapper.find('[data-catalog-advanced]');

    expect(content.attributes('data-state')).toBe('open');

    await content.find('[role="switch"]').trigger('click');
    await flushPromises();

    const demo = wrapper.find('[data-catalog-filter-demo]');

    expect(wrapper.find('[data-catalog-advanced-trigger]').text()).toContain('1');
    expect(demo.text()).not.toContain('!hug');
    expect(demo.text()).toContain('!lurk');

    await demo.find('input[type="search"], input').setValue('nothing-matches');

    expect(demo.text()).toContain(en.dashboard.catalog.guide.filters.noMatch);
    expect(demo.find('[data-slot="panel-empty"]').exists()).toBe(true);
  });
});

describe('the settings authoring contract and the sidebar review', () => {
  it('lists the five contract rules, naming the frame, the route title and the slots', async () => {
    const wrapper = await open();
    const rules = wrapper.findAll('[data-catalog="guide-settings"] [data-catalog-contract] li');
    const words = en.dashboard.catalog.guide.settings.rule;

    expect(rules).toHaveLength(5);
    const texts = rules.map((rule) => rule.text());

    expect(texts).toEqual([words.frame, words.title, words.crumb, words.slots, words.body]);
    expect(rules[3]?.text()).toContain('tabs');
    expect(rules[3]?.text()).toContain('panel');
  });

  it('reviews every settings sidebar group with its entry count and a reason', async () => {
    const wrapper = await open();
    const rows = wrapper.findAll('[data-catalog="guide-settings"] [data-catalog-review] li');
    const groups = settingsNav.filter((group) => group.key !== 'back');

    expect(rows).toHaveLength(groups.length);

    for (const [index, group] of groups.entries()) {
      expect(rows[index]?.text()).toContain(String(group.entries.length));
      expect(rows[index]?.text()).toContain(i18n.global.t(`frame.group.${group.key}`));
    }

    const counted = rows.reduce((sum, row) => sum + Number(row.attributes('data-count')), 0);

    expect(counted).toBe(28);
  });
});
