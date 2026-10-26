// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { commitOf } from '@/api/updates';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import UpdateCard from '../components/UpdateCard.vue';

const RELEASE_SHA: string = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';

// A fake GitHub: the release list, and a compare answer per base commit.
const github = vi.hoisted(() => ({
  releases: [] as {
    tag_name: string;
    target_commitish: string;
    html_url: string;
    published_at: string;
  }[],
  compare: 'ahead' as string,
  asked: [] as string[],
  down: false,
}));

function answer(url: string): Response {
  github.asked.push(url.replace('https://api.github.com/repos/NoMercyLabs/nomnomzbot', ''));

  if (github.down)
    return new Response('{}', { status: 403 });

  const body = url.includes('/releases')
    ? github.releases
    : { status: github.compare };

  return new Response(JSON.stringify(body), { status: 200 });
}

let wrapper: VueWrapper | undefined;

async function open(botVersion: string): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(UpdateCard, {
    props: { botVersion },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);

  github.releases = [
    {
      tag_name: 'beta-2026.10.27-5',
      target_commitish: 'c'.repeat(40),
      html_url: 'https://x/beta',
      published_at: '2026-10-27T04:00:00Z',
    },
    {
      tag_name: 'stable-2026.10.26-4',
      target_commitish: RELEASE_SHA,
      html_url: 'https://x/stable',
      published_at: '2026-10-26T12:00:00Z',
    },
  ];

  github.compare = 'ahead';
  github.asked = [];
  github.down = false;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => answer(url)));
});

afterEach(() => {
  wrapper?.unmount();
  vi.unstubAllGlobals();
});

describe('commitOf', () => {
  it('takes the commit from the bot version and refuses a build without one', () => {
    expect(commitOf('0.1.0+2a8544cf01011d91fbf4d6b2741f4a426d8faca7')).toBe('2a8544cf01011d91fbf4d6b2741f4a426d8faca7');
    expect(commitOf('0.1.0')).toBeNull();
    expect(commitOf('0.1.0+dev')).toBeNull();
  });
});

describe('UpdateCard', () => {
  it('names the newest stable release, skipping beta, when it is ahead of this bot', async () => {
    const page = await open('0.1.0+aaaaaaa');

    expect(github.asked).toEqual(['/releases?per_page=30', `/compare/aaaaaaa...${RELEASE_SHA}`]);
    expect(page.get('[data-update-available]').text()).toContain('stable-2026.10.26-4');
    expect(page.get('a').attributes('href')).toBe('https://x/stable');
  });

  it('says the bot is current when the release is not ahead', async () => {
    github.compare = 'identical';
    const page = await open('0.1.0+aaaaaaa');

    expect(page.find('[data-update-available]').exists()).toBe(false);
    expect(page.get('[data-update-current]').text()).toBe(en.dashboard.admin.system.update.current);
  });

  it('never asks GitHub for a build without a commit', async () => {
    const page = await open('0.1.0');

    expect(github.asked).toEqual([]);
    expect(page.find('[data-update-unknown]').exists()).toBe(true);
  });

  it('has nothing to compare before the first stable release', async () => {
    github.releases = github.releases.filter((release) => !release.tag_name.startsWith('stable-'));
    const page = await open('0.1.0+aaaaaaa');

    expect(github.asked).toEqual(['/releases?per_page=30']);
    expect(page.find('[data-update-unknown]').exists()).toBe(true);
  });

  it('shows the failure text when GitHub refuses', async () => {
    github.down = true;
    const page = await open('0.1.0+aaaaaaa');

    expect(page.text()).toContain(en.dashboard.admin.system.update.failed);
  });
});
