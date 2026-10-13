// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { createI18n } from 'vue-i18n';
import { describe, expect, it } from 'vitest';

import en from '@/i18n/locales/en.json';
import nl from '@/i18n/locales/nl.json';
import { dashboardRoutes } from '@/router/dashboard';

import type { ActionRequiredItem } from '../actionRequired';
import { attentionPages, attentionWords } from '../actionRequiredText';

function translator(locale: 'en' | 'nl') {
  const i18n = createI18n({
    legacy: false,
    locale,
    messages: { en, nl },
  });

  return { t: i18n.global.t, te: i18n.global.te };
}

function item(overrides: Partial<ActionRequiredItem>): ActionRequiredItem {
  return {
    id: 'a1',
    kind: 'test',
    severity: 'warning',
    titleKey: 'attention_unknown_title',
    messageKey: 'attention_unknown_message',
    parameters: {},
    detectedAt: '2026-10-13T10:00:00Z',
    deepLinkRoute: '',
    sourceUserId: null,
    sourceUserName: null,
    count: 1,
    queueItemIds: [],
    ...overrides,
  };
}

// The item the live server sent on 2026-10-13.
const ownerActionFailed = item({
  kind: 'owner_action_failed',
  titleKey: 'attention_owner_action_failed_title',
  messageKey: 'attention_owner_action_failed_message',
  parameters: { action: 'redemption_refund', error: 'Redemption \'…\' was not found.' },
  deepLinkRoute: 'overlays',
});

describe('attentionWords', () => {
  it('writes the live owner_action_failed item with its action and error text', () => {
    const words = attentionWords(ownerActionFailed, translator('en'));

    expect(words.title).toBe('Action redemption_refund failed');
    expect(words.message).toContain('it did not work: Redemption \'…\' was not found.');
  });

  it('ends the message with one period when the server error already has one', () => {
    const words = attentionWords(ownerActionFailed, translator('en'));

    expect(words.message).not.toContain('..');
  });

  it('writes the same item in Dutch', () => {
    const words = attentionWords(ownerActionFailed, translator('nl'));

    expect(words.title).toBe('Actie redemption_refund is mislukt');
    expect(words.message).toContain('het lukte niet: Redemption \'…\' was not found.');
  });

  it('picks the one or many title by count and fills the count', () => {
    const held = { titleKey: 'attention_held_title', messageKey: 'attention_held_many_message' };

    const one = attentionWords(item({ ...held, count: 1 }), translator('en'));
    const many = attentionWords(
      item({
        ...held,
        count: 3,
        parameters: { count: '3' },
      }),
      translator('en'),
    );

    expect(one.title).toBe('A chat message is held for review');
    expect(many.title).toBe('3 chat messages are held for review');
    expect(many.message).toBe('AutoMod held 3 messages.');
  });

  it('reads a count the server sent as text', () => {
    const lost = item({ titleKey: 'attention_song_lost_title', count: '2' });

    expect(attentionWords(lost, translator('en')).title).toBe('2 song requests were lost');
  });

  it('names a held message category, or says flagged when the server sent none', () => {
    const base = { titleKey: 'attention_held_title', messageKey: 'attention_held_single_message' };

    const named = item({ ...base, parameters: { category: 'harassment' } });
    const unnamed = item({ ...base, parameters: { category: '' } });

    expect(attentionWords(named, translator('en')).message).toBe(
      'AutoMod held a message (harassment).',
    );

    expect(attentionWords(unnamed, translator('en')).message).toBe(
      'AutoMod held a message (flagged).',
    );
  });

  it('uses the no-detail words when Twitch gave no reason for a shared ban', () => {
    const failed = {
      messageKey: 'attention_shared_ban_twitch_ban_failed_message',
      parameters: {
        count: '4',
        targetName: 'badguy',
        detail: '',
      },
    };

    const withDetail = item({
      ...failed,
      parameters: { ...failed.parameters, detail: 'rate limit' },
    });
    const without = item(failed);

    expect(attentionWords(withDetail, translator('en')).message).toContain(
      'for badguy: rate limit.',
    );

    expect(attentionWords(without, translator('en')).message).toContain(
      'for badguy. They are not banned',
    );
  });

  it('writes the provider and source names the way a reader says them', () => {
    const reauth = item({
      titleKey: 'attention_integration_reauth_title',
      parameters: { provider: 'youtube' },
    });
    const source = item({
      titleKey: 'attention_source_unavailable_title',
      parameters: { source: 'unban_requests' },
    });
    const other = item({
      titleKey: 'attention_source_unavailable_title',
      parameters: { source: 'whatever' },
    });

    expect(attentionWords(reauth, translator('en')).title).toBe('YouTube needs to be reconnected');
    expect(attentionWords(source, translator('en')).title).toBe('Could not check unban requests');

    expect(attentionWords(other, translator('en')).title).toBe(
      'Could not check one of your checks',
    );
  });

  it('writes the Spotify pause as a clock time, with the date when it is another day', () => {
    const now = new Date(2026, 9, 13, 20, 0);
    const sameDay = new Date(2026, 9, 13, 22, 47);
    const nextDay = new Date(2026, 9, 14, 1, 5);
    const spotify = (until: Date) =>
      item({
        titleKey: 'attention_spotify_blocked_title',
        parameters: { until: until.toISOString() },
      });

    expect(attentionWords(spotify(sameDay), translator('en'), now).title).toBe(
      'Spotify paused your music until 22:47',
    );

    expect(attentionWords(spotify(nextDay), translator('en'), now).title).toBe(
      'Spotify paused your music until 2026-10-14 01:05',
    );
  });

  it('shows the plain fallback title and no message for a key it has no words for', () => {
    const words = attentionWords(item({ titleKey: 'attention_brand_new_title' }), translator('en'));

    expect(words.title).toBe('Something needs your attention');
    expect(words.message).toBeNull();
  });

  it('leaves a missing parameter empty, as the app does', () => {
    const report = item({ titleKey: 'attention_report_title', parameters: {} });

    expect(attentionWords(report, translator('en')).title).toBe(' was reported by a viewer');
  });
});

describe('attention route', () => {
  it.each([
    ['overlays', '/app/overlays'],
    ['people/overview', '/app/people'],
    ['/tts', '/app/tts'],
    ['music/song-requests', '/app/music'],
  ])('sends the server route %j to the Vue page %j', (deepLinkRoute, route) => {
    expect(attentionWords(item({ deepLinkRoute }), translator('en')).route).toBe(route);
  });

  it.each(['integrations', 'webhooks', 'channel-points', ''])(
    'shows no button for %j, which has no Vue page',
    (deepLinkRoute) => {
      expect(attentionWords(item({ deepLinkRoute }), translator('en')).route).toBeNull();
    },
  );

  it('lists only pages the dashboard router really has', () => {
    const paths = (dashboardRoutes.children ?? []).map((child) => child.path);

    for (const page of attentionPages) {
      expect(paths).toContain(page);
    }
  });
});

describe('attention words files', () => {
  it('has the same keys in English and Dutch', () => {
    const english = Object.keys(en.frame.inbox.attention).sort();
    const dutch = Object.keys(nl.frame.inbox.attention).sort();

    expect(dutch).toEqual(english);
  });

  it('has the same placeholders in English and Dutch for every key', () => {
    const names = (text = '') => [...text.matchAll(/\{(\w+)\}/g)].map((hit) => hit[1]).sort();
    const english: Record<string, string> = en.frame.inbox.attention;
    const dutch: Record<string, string> = nl.frame.inbox.attention;

    const placeholders = (words: Record<string, string>) =>
      Object.fromEntries(Object.keys(english).map((key) => [key, names(words[key])]));

    expect(placeholders(dutch)).toEqual(placeholders(english));
  });
});
