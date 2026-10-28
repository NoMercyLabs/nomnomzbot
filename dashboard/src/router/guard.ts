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

import type { LocationQueryValue, RouteLocationNormalized, RouteLocationRaw } from 'vue-router';

import { activeBot } from '@/api/activeBot';
import { restoreSession } from '@/api/auth';
import { probeBot } from '@/api/system';
import type { NavMode } from '@/layouts/dashboard/navigation';
import { loginRoute } from '@/views/site/login/route';
import { setupRoute } from '@/views/site/setup/route';

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean;
    // A page only a signed-out visitor needs, such as the login page.
    guestOnly?: boolean;
    // The first-run setup wizard: open to anyone until the bot reports onboarding done.
    setup?: boolean;
    // The sidebar the page sits in; without it the page sits in the streamer dashboard.
    navMode?: NavMode;
    // A page about the person, not a channel (the "You" settings): it has no /app/u/<name> twin.
    userLevel?: boolean;
    // Words key under frame.nav of a page that is planned and not built yet.
    comingSoon?: string;
    // The chat page as a viewer sees it: read and send in your own voice, no moderation (chat-client.md 9a).
    viewerChat?: boolean;
    // The channel action key a person must hold to open the page; anyone else lands on the first page they may open.
    action?: string;
    // The page is open to every signed-in person on purpose (the You settings, a coming-soon page): the explicit opt-out from naming an action.
    open?: true;
  }
}

// Only a path on this site is followed, so a crafted link cannot send a signed-in visitor elsewhere.
export function returnPath(asked: LocationQueryValue | LocationQueryValue[] | undefined): string {
  return typeof asked === 'string' && asked.startsWith('/') && !asked.startsWith('//')
    ? asked
    : '/app';
}

// Onboarding never reverts once the bot reports it done, so the answer is asked for only until then.
let onboardingDone = false;

export function forgetOnboardingAnswer(): void {
  onboardingDone = false;
}

// A fresh install has no sign-in yet, so the first-run wizard comes before the login and the dashboard.
// The setup page itself sends a finished bot on to the dashboard.
export async function setupGuard(to: RouteLocationNormalized): Promise<true | RouteLocationRaw> {
  const onSetupPage = to.matched.some((record) => record.meta.setup);

  const gated = to.matched.some((record) => record.meta.requiresAuth || record.meta.guestOnly);

  if (!onSetupPage && (onboardingDone || !gated))
    return true;

  const probe = await probeBot(activeBot.value);

  if (probe.state !== 'ready')
    return true;

  onboardingDone = probe.onboardingComplete !== false;

  // Onboarding is done once the Twitch app is chosen, which is before the last wizard steps. So the wizard
  // stays open for a visitor who is not signed in yet, and only a signed-in owner is sent on.
  if (onSetupPage)
    return onboardingDone && (await restoreSession())
      ? { path: '/app', replace: true }
      : true;

  return onboardingDone
    ? true
    : { name: setupRoute.name, replace: true };
}

export async function signInGuard(to: RouteLocationNormalized) {
  if (to.matched.some((record) => record.meta.guestOnly))
    return guestGuard(to);

  if (!to.matched.some((record) => record.meta.requiresAuth))
    return true;

  if (await restoreSession())
    return withoutToken(to);

  return {
    name: loginRoute.name,
    query: { return_to: to.fullPath },
  } as const satisfies RouteLocationRaw;
}

// A visitor with a valid session skips the login page and goes where the login was asked for.
async function guestGuard(to: RouteLocationNormalized): Promise<true | RouteLocationRaw> {
  if (!(await restoreSession()))
    return true;

  return { path: returnPath(to.query.return_to), replace: true };
}

// The router writes the address it navigates to, so an OAuth fragment that is kept here would
// put the access token back in the address bar after restoreSession stripped it.
function withoutToken(to: RouteLocationNormalized): true | RouteLocationRaw {
  if (!to.hash.includes('access_token='))
    return true;

  return {
    path: to.path,
    query: to.query,
    hash: '',
    replace: true,
  };
}
