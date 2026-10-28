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

import { describe, expect, it } from 'vitest';

import type { ChatTrigger } from '@/api/chatTriggers';

import { changeOf, draftOf } from '../components/chatTriggerDraft';

const saved = {
  id: 'a',
  pattern: 'words a',
  matchType: 'contains',
  caseSensitive: false,
  isEnabled: true,
  response: 'Hello there',
  pipelineId: null,
  cooldownSeconds: 30,
  minPermissionLevel: 'Everyone',
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
} as ChatTrigger;

describe('chat trigger reply mode', () => {
  it('opens a trigger without a pipeline in Simple mode', () => {
    expect(draftOf(saved).mode).toBe('simple');
  });

  it('sends only the picked pipeline when a simple trigger switches to Full pipeline', () => {
    const draft = {
      ...draftOf(saved),
      mode: 'full' as const,
      target: { kind: 'pipeline' as const, id: 'p1' },
    };

    expect(changeOf(draft, saved)).toEqual({ target: { kind: 1, id: 'p1' } });
  });

  it('sends a command target when a command is picked in Full mode', () => {
    const draft = {
      ...draftOf(saved),
      mode: 'full' as const,
      target: { kind: 'command' as const, id: 'c1' },
    };

    expect(changeOf(draft, saved)).toEqual({ target: { kind: 0, id: 'c1' } });
  });

  it('opens a trigger bound to a command in Full mode with that command', () => {
    const bound = { ...saved, target: { kind: 0, id: 'c1' } } as ChatTrigger;

    expect(draftOf(bound)).toMatchObject({
      mode: 'full',
      target: { kind: 'command', id: 'c1' },
    });
  });

  it('clears a command binding when the trigger switches back to Simple', () => {
    const bound = { ...saved, target: { kind: 0, id: 'c1' } } as ChatTrigger;
    const draft = { ...draftOf(bound), mode: 'simple' as const };

    expect(changeOf(draft, bound)).toMatchObject({ target: { kind: 1, id: '' } });
  });

  it('sends nothing for Full pipeline until a pipeline is picked', () => {
    const draft = {
      ...draftOf(saved),
      mode: 'full' as const,
      response: 'Edited text',
    };

    expect(changeOf(draft, saved)).toEqual({});
  });
});
