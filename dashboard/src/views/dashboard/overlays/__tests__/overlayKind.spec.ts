// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import BellRing from '~icons/nnz/bell-ring';
import ChatMessages from '~icons/nnz/chat-messages';
import NoteEighthPair from '~icons/nnz/note-eighth-pair';
import WaveForm from '~icons/nnz/wave-form';

import { overlayKind } from '../components/overlayKind';

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
    expect(overlayKind(['game.lobby']).icon).toBeNull();
  });

  it('never reads a name or title', async () => {
    const source = (await import('../components/overlayKind?raw')).default;

    expect(overlayKind.length).toBe(1);
    expect(source).not.toMatch(/\((name|title)\b|\b(name|title):\s*string/);
  });
});
