// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';

import { i18n } from '@/i18n';

import ComingSoonView from '../ComingSoonView.vue';

const held = vi.hoisted(() => ({ label: 'games' }));

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: { comingSoon: held.label } }),
}));

function render() {
  return mount(ComingSoonView, { global: { plugins: [i18n] } });
}

describe('the coming-soon page', () => {
  it('says in one line what a planned page will do', () => {
    held.label = 'games';
    const text = render().text();

    expect(text).toContain('Coming soon');
    expect(text).toContain('Run chat games for your viewers');
  });

  it('falls back to the plain line for a page with no description', () => {
    held.label = 'home';
    const text = render().text();

    expect(text).toContain('is planned and not built yet');
  });

  it('centers the state in the page body under the header', () => {
    held.label = 'gallery';
    const body = render().get('[data-slot=page-body]');

    expect(body.classes()).toContain('items-center');
    expect(body.classes()).toContain('justify-center');
    expect(body.text()).toContain('widgets');
  });
});
