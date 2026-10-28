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

import createClient from 'openapi-fetch';

import { isMixedContent } from '@/lib/botAddress';
import type { paths } from './schema';

export type BotProbe =
  | { state: 'ready'; onboardingComplete: boolean } |
  { state: 'reachable' } |
  { state: 'unreachable' } |
  { state: 'mixedContent' };

const statusPath = '/api/v{version}/system/status';
const probeTimeoutMs: number = 5000;

// A browser hides the answer of a bot that does not allow this page, so a no-cors call only shows it is there.
async function answers(origin: string): Promise<boolean> {
  try {
    await fetch(`${origin}/api/v1/system/status`, {
      mode: 'no-cors',
      signal: AbortSignal.timeout(probeTimeoutMs),
    });

    return true;
  } catch {
    return false;
  }
}

// Asks a bot whether it is there. A fresh client, so the token of the serving bot never goes to another bot.
export async function probeBot(
  origin: string,
  pageOrigin: string = window.location.origin,
): Promise<BotProbe> {
  if (isMixedContent(pageOrigin, origin))
    return { state: 'mixedContent' };

  try {
    const { data } = await createClient<paths>({ baseUrl: origin }).GET(statusPath, {
      params: { path: { version: '1' } },
      signal: AbortSignal.timeout(probeTimeoutMs),
    });

    if (data?.data)
      return { state: 'ready', onboardingComplete: data.data.onboardingComplete };
  } catch (error) {
    if (error instanceof DOMException && error.name === 'TimeoutError')
      return { state: 'unreachable' };
    // Unreachable, or reachable but not allowing this page; the no-cors call below tells the two apart.
  }

  return (await answers(origin))
    ? { state: 'reachable' }
    : { state: 'unreachable' };
}
