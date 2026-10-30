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

import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { h } from 'vue';

import { setWidth } from '@/__tests__/viewport';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { i18n } from '@/i18n';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  document.body.innerHTML = '';
});

async function pageAt(width: number): Promise<void> {
  setWidth(width);

  mounted = mount(
    {
      render: () =>
        h(PanelBeside, null, {
          default: () => h('p', { 'data-content': '' }, 'page'),
          panel: () => h(SidePanel, { label: 'Rules', open: true }, () =>
            h('p', { 'data-options': '' }, 'options')),
        }),
    },
    { attachTo: document.body, global: { plugins: [i18n] } },
  );

  await flushPromises();
}

// Stoney 2026-10-20: "the request rules pannel proably fits just fine as a 3rd  column on this breakpont and must
// be consistent with the other pannel behaviors."
describe('the row a side panel opens into', () => {
  it('puts the panel in a third column right after the page content where the window is wide', async () => {
    await pageAt(1440);

    const column = document.body.querySelector('aside[data-slot="side-panel"]');

    expect(column?.previousElementSibling?.querySelector('[data-content]')).not.toBeNull();
    expect(column?.querySelector('[data-options]')).not.toBeNull();
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it('opens the panel as a sheet over the page where the window is narrow', async () => {
    await pageAt(900);

    expect(document.body.querySelector('aside[data-slot="side-panel"]')).toBeNull();
    expect(document.body.querySelector('[role="dialog"][data-slot="side-panel"]')).not.toBeNull();
  });
});

// The five pages that once forced a sheet at every width. A page that opens a side panel puts it in the shared row.
const pages = import.meta.glob<string>(
  [
    '/src/views/dashboard/music/MusicView.vue',
    '/src/views/dashboard/media-share/MediaShareView.vue',
    '/src/views/dashboard/overlays/OverlaysView.vue',
    '/src/views/dashboard/admin/AdminTenantsView.vue',
    '/src/views/dashboard/settings/webhooks/WebhooksView.vue',
  ],
  {
    eager: true,
    query: '?raw',
    import: 'default',
  },
);

describe('the pages whose side panel was a sheet at every width', () => {
  it('covers all five pages', () => {
    expect(Object.keys(pages)).toHaveLength(5);
  });

  it.each(Object.entries(pages))('%s opens its panel in the shared row', (_path, source) => {
    expect(source).toContain('<PanelBeside>');
    expect(source).toMatch(/<template [^>]*#panel>/);
    expect(source).not.toMatch(/drawer-?only/i);
  });
});
