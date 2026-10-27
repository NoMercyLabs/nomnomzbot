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
import { ref } from 'vue';
import BellRing from '~icons/nnz/bell-ring';
import ChatMessages from '~icons/nnz/chat-messages';
import NoteEighthPair from '~icons/nnz/note-eighth-pair';
import WaveForm from '~icons/nnz/wave-form';

import type { Widget } from '@/api/widgets';
import { i18n } from '@/i18n';

import OverlayRow from '../components/OverlayRow.vue';
import { overlayKind } from '../components/overlayKind';
import en from '../i18n/en.json';

// The row's setup button asks whether the bot controls OBS; that read needs a channel and a query client.
vi.mock('@/composables/useObsRepairsWidgets', () => ({
  useObsRepairsWidgets: () => ref<boolean>(false),
}));

describe('overlay icon', () => {
  it('comes from the events the overlay listens to', () => {
    expect(overlayKind(['tts_speak', 'now_playing'])).toMatchObject({
      key: 'tts',
      icon: WaveForm,
    });

    expect(overlayKind(['ChatMessage'])).toMatchObject({ key: 'chat', icon: ChatMessages });

    expect(overlayKind(['spotify.track_changed'])).toMatchObject({
      key: 'nowPlaying',
      icon: NoteEighthPair,
    });

    expect(overlayKind(['follow', 'cheer'])).toMatchObject({ key: 'alerts', icon: BellRing });
  });

  it('is empty when no event gives the overlay a clear kind', () => {
    expect(overlayKind([])).toEqual({ key: 'overlay', icon: null });
    expect(overlayKind(['unknown.event']).icon).toBeNull();
  });

  it('gives every overlay of the live channel a picture from its events', () => {
    const live: Record<string, string[]> = {
      reward: ['reward_redeemed'],
      game: ['game.lobby', 'game.running', 'game.resolved'],
      feather: ['feather.steal', 'feather.show', 'feather.event'],
      hypeTrain: ['hype_train_begin', 'hype_train_progress', 'hype_train_end'],
      leaderboard: ['leaderboard.show'],
      poll: ['poll_begin', 'prediction_begin'],
      raid: ['raid_recap.show'],
      songQueue: ['sr_queue'],
    };

    for (const [key, events] of Object.entries(live)) {
      const kind = overlayKind(events);

      expect(kind.key).toBe(key);
      expect(kind.icon).not.toBeNull();
      expect(en.dashboard.overlays.kinds).toHaveProperty(key);
    }
  });

  it('leaves no empty tile on a list row without a picture', () => {
    i18n.global.mergeLocaleMessage('en', en);

    const row = mount(OverlayRow, {
      props: {
        selected: false,
        widget: {
          name: 'Plain',
          isEnabled: true,
          isAttached: true,
          eventSubscriptions: ['unknown.event'],
        } as unknown as Widget,
      },
      global: { plugins: [i18n] },
    });

    expect(row.find('[data-overlay-icon]').exists()).toBe(false);
    row.unmount();
  });

  it('never reads a name or title', async () => {
    const source = (await import('../components/overlayKind?raw')).default;

    expect(overlayKind.length).toBe(1);
    expect(source).not.toMatch(/\((name|title)\b|\b(name|title):\s*string/);
  });
});
