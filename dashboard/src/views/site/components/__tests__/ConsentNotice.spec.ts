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
import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { privacyRoute, termsRoute } from '@/views/site/legal/route';
import login from '@/views/site/login/i18n/en.json';

import ConsentNotice from '../ConsentNotice.vue';

describe('the consent notice', () => {
  it('names the terms and the privacy policy as links to the real legal pages', async () => {
    i18n.global.mergeLocaleMessage('en', login);

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/',
          component: { template: '<div />' },
          children: [termsRoute, privacyRoute],
        },
      ],
    });

    await router.push('/');

    const wrapper = mount(ConsentNotice, { global: { plugins: [i18n, router] } });
    const links = wrapper.findAll('a');

    expect(wrapper.text()).toBe(
      'By continuing, you agree to the terms of service and the privacy policy.',
    );

    expect(links.map((link) => link.text())).toEqual(['terms of service', 'privacy policy']);

    expect(links.map((link) => link.attributes('href'))).toEqual([
      '/terms-of-service',
      '/privacy-policy',
    ]);

    expect(router.resolve('/terms-of-service').name).toBe('legal-terms');
    expect(router.resolve('/privacy-policy').name).toBe('legal-privacy');
  });
});
