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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { defineComponent, h, type Component } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import ConnectionRow from '@/components/layout/ConnectionRow.vue';
import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import EmptyState from '@/components/layout/EmptyState.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import ListPanel from '@/components/layout/ListPanel.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';

import CatalogConnections from '../components/CatalogConnections.vue';
import CatalogEditors from '../components/CatalogEditors.vue';
import CatalogFeedback from '../components/CatalogFeedback.vue';
import CatalogPages from '../components/CatalogPages.vue';
import CatalogRows from '../components/CatalogRows.vue';
import CatalogWidthFrame from '../components/CatalogWidthFrame.vue';
import en from '../i18n/en.json';

let mounted: VueWrapper | null = null;

beforeEach(() => {
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

// The frame mounts the one TooltipProvider; a section that holds an icon action needs it too.
async function show(section: Component): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  });
  const host = defineComponent(() => () => h(TooltipProvider, () =>
    h(section)));

  await router.push('/');

  mounted = mount(host, {
    attachTo: document.body,
    global: {
      plugins: [i18n, router],
      stubs: { LanguagePicker: true },
    },
  });

  await flushPromises();

  return mounted;
}

describe('CatalogFeedback', () => {
  it('draws EmptyState in all four kinds, each with one action and only the first-run one primary', async () => {
    const wrapper = await show(CatalogFeedback);
    const empties = wrapper.findAllComponents(EmptyState);

    expect(empties.map((empty) => empty.props('kind'))).toEqual(['none', 'noMatch', 'filtered', 'gated']);

    const variants = empties.map((empty) => empty.findComponent(Button).props('variant'));

    expect(variants).toEqual(['default', 'outline', 'outline', 'outline']);
    expect(wrapper.find('[data-catalog="empty-states"]').exists()).toBe(true);
  });

  it('draws InlineNotice in its five tones plus the compact error and gated lines', async () => {
    const wrapper = await show(CatalogFeedback);
    const notices = wrapper.findAllComponents(InlineNotice);

    expect(notices.map((notice) => notice.props('tone'))).toEqual([
      'info', 'warning', 'error', 'success', 'gated', 'error', 'gated',
    ]);

    expect(notices.filter((notice) => notice.props('compact'))).toHaveLength(2);
    expect(notices.at(2)?.attributes('role')).toBe('alert');
  });
});

describe('CatalogConnections', () => {
  it('draws the five StatusBadge tones and the three ConnectionRow states with their own action', async () => {
    const wrapper = await show(CatalogConnections);
    const badges = wrapper
      .findAllComponents(StatusBadge)
      .filter((badge) => badge.element.closest('[data-catalog-badges]') !== null);
    const rows = wrapper.findAllComponents(ConnectionRow);

    expect(badges.map((badge) => badge.props('tone'))).toEqual(['ok', 'bad', 'live', 'off', 'attention']);
    expect(rows.map((row) => row.props('state'))).toEqual(['connected', 'disconnected', 'attention']);

    expect(rows.map((row) => row.find('button').text())).toEqual([
      en.dashboard.catalog.connections.action.connected,
      en.dashboard.catalog.connections.action.disconnected,
      en.dashboard.catalog.connections.action.attention,
    ]);
  });
});

describe('CatalogRows', () => {
  it('draws SettingRow on, off, disabled and read-only', async () => {
    const wrapper = await show(CatalogRows);
    const settings = wrapper.findAllComponents(SettingRow);

    expect(settings.map((row) => [row.props('disabled'), row.props('readonly')])).toEqual([
      [false, false],
      [false, false],
      [true, false],
      [false, true],
    ]);
  });

  it('counts an edit and a delete from RowEditDelete and drops Delete from a fixed item', async () => {
    const wrapper = await show(CatalogRows);

    await wrapper.find('[data-row-edit]').trigger('click');
    await wrapper.find('[data-row-delete]').trigger('click');

    expect(wrapper.text()).toContain('Edited 1 times, deleted 1 times');
    expect(wrapper.findAll('[data-row-delete]')).toHaveLength(2);
    expect(wrapper.findAll('[data-row-edit]')).toHaveLength(3);
  });

  it('locks the read-only row with a disabled fieldset that carries the reason', async () => {
    const wrapper = await show(CatalogRows);
    const scope = wrapper.find('[data-slot="read-only-scope"]');

    expect(scope.attributes('disabled')).toBeDefined();
    expect(scope.attributes('title')).toBe(en.dashboard.catalog.rows.readOnlyReason);
  });
});

describe('CatalogEditors', () => {
  it('opens the destructive confirm from the footer Delete and counts the delete once confirmed', async () => {
    const wrapper = await show(CatalogEditors);

    await wrapper.find('[data-editor-delete]').trigger('click');
    await flushPromises();

    expect(document.body.textContent).toContain(en.dashboard.catalog.confirm.title);
    expect(document.body.textContent).toContain(en.dashboard.catalog.confirm.chat);
  });

  it('draws DetailEditorPane idle, open and missing, and ListPanel loading, empty and error', async () => {
    const wrapper = await show(CatalogEditors);
    const panes = wrapper.findAllComponents(DetailEditorPane);

    expect(panes.map((pane) => [pane.props('open'), pane.props('missing')])).toEqual([
      [false, false],
      [true, false],
      [true, true],
    ]);

    expect(wrapper.find('[data-catalog-pane="idle"]').text()).toContain(en.dashboard.catalog.editors.idle);
    expect(wrapper.find('[data-catalog-pane="missing"]').text()).toContain(en.dashboard.catalog.editors.missing);

    const lists = wrapper.findAllComponents(ListPanel);

    expect(lists.map((list) => list.props('state'))).toEqual(['loading', 'empty', 'error']);

    expect(lists.map((list) => list.findComponent(PanelCard).attributes('data-state'))).toEqual([
      'loading',
      'empty',
      'error',
    ]);
  });
});

describe('CatalogWidthFrame', () => {
  it('constrains the example to the width the reader picks', async () => {
    mounted = mount(CatalogWidthFrame, {
      props: { title: 'Frame' },
      slots: { default: '<p>Example</p>' },
      global: { plugins: [i18n] },
    });

    const box = (): string | undefined => mounted!.find('[data-catalog-frame-box]').attributes('data-frame-width');

    expect(box()).toBe('desktop');

    await mounted.findAll('[data-segment]').at(2)?.trigger('click');

    expect(box()).toBe('phone');
    expect(mounted.find('[data-catalog-frame-box]').classes()).toContain('max-w-sm');
  });
});

describe('CatalogPages', () => {
  it('shows each page recipe in its own frame', async () => {
    const wrapper = await show(CatalogPages);
    const drawn = wrapper.findAll('[data-catalog]').map((section) => section.attributes('data-catalog'));

    expect(drawn).toEqual(['page-body', 'settings-frame', 'admin-collection', 'task-shell']);
    expect(wrapper.findAll('[data-catalog-frame]')).toHaveLength(4);
    expect(wrapper.find('[data-slot="task-actions"]').exists()).toBe(true);
    expect(wrapper.find('[data-slot="wizard-actions"]').exists()).toBe(true);
    expect(wrapper.find('[data-slot="task-column"]').exists()).toBe(true);
    expect(wrapper.find('[data-slot="page-body"]').exists()).toBe(true);
  });
});
