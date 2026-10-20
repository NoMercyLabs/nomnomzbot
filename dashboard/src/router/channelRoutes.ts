// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type {
  RouteLocationNormalized,
  RouteLocationNormalizedLoaded,
  RouteLocationRaw,
  RouteMeta,
  RouteRecordRaw,
} from 'vue-router';

import { takeOAuthLanding } from '@/api/auth';
import { currentChannelLogin } from '@/composables/useCurrentChannel';

// Every dashboard page is reachable twice: /app/<page> for the channel being managed, and
// /app/u/<channelName>/<page> for one named channel, so a shared link needs no manual switch.
export const channelParam = 'channelName';
export const channelPrefix = '/app/u/';
const variantMark = 'u:';

const viewerHome = {
  path: '/me',
  replace: true,
} as const satisfies RouteLocationRaw;

export type ChannelParams = { channelName: string };

// The channel-prefixed twin of each page: same page, name marked, path unchanged below the prefix.
// A platform page (the admin) and a per-person page (the "You" settings) belong to no channel: they have no twin.
export function isChannelPage(meta: RouteMeta | undefined): boolean {
  return meta?.navMode !== 'admin' && meta?.userLevel !== true;
}

export function channelVariants(pages: readonly RouteRecordRaw[]): RouteRecordRaw[] {
  return pages
    .filter((page) => isChannelPage(page.meta))
    .map((page) => ({
      ...page,
      name: `${variantMark}${String(page.name)}`,
    }) as RouteRecordRaw);
}

// The channel a route names, or null on a short route.
export function routeChannel(route: RouteLocationNormalizedLoaded): string | null {
  const named = route.params[channelParam];

  return typeof named === 'string'
    ? named
    : null;
}

// A page's own route name, without the mark of its channel twin.
export function pageName(name: string): string {
  return name.startsWith(variantMark)
    ? name.slice(variantMark.length)
    : name;
}

// Where a link to a page goes: the twin under the channel prefix, so no click bounces through a redirect.
// Without a channel (a viewer, or the list not loaded yet) the link keeps the short page.
export function pageLocation(name: string, login: string | null) {
  if (login === null)
    return { name } as const satisfies RouteLocationRaw;

  return {
    name: `${variantMark}${name}`,
    params: { [channelParam]: login },
  } as const satisfies RouteLocationRaw;
}

function baseName(route: RouteLocationNormalizedLoaded): string {
  return pageName(String(route.name ?? 'dashboard-home'));
}

// Where the same page of another channel lives. The own channel has its prefixed address too.
// A page that belongs to one item of the old channel (an id param) falls back to the channel home.
export function switchLocation(
  route: RouteLocationNormalizedLoaded,
  login: string,
) {
  const itemParams = Object.keys(route.params).filter((key) => key !== channelParam);
  const name = itemParams.length > 0
    ? 'dashboard-home'
    : baseName(route);
  const keep = itemParams.length > 0
    ? {}
    : { query: route.query, hash: route.hash };

  return {
    name: `${variantMark}${name}`,
    params: { [channelParam]: login },
    ...keep,
  } as const satisfies RouteLocationRaw;
}

// A page of the unprefixed dashboard: /app or /app/<page>.
function isShortPath(path: string): boolean {
  const inApp = path === '/app' || path.startsWith('/app/');

  return inApp && !path.startsWith(channelPrefix);
}

// A short dashboard address of a page that has a channel twin. Platform and per-person pages stay short.
function isShortChannelPage(to: RouteLocationNormalized): boolean {
  return isShortPath(to.path) && isChannelPage(to.meta);
}

// The same address under a channel prefix, keeping query and hash. It replaces the entry: no extra history.
function prefixed(to: RouteLocationNormalized, login: string) {
  const rest = to.path.slice('/app'.length);

  return {
    path: `${channelPrefix}${encodeURIComponent(login)}${rest}`,
    query: to.query,
    hash: to.hash,
    replace: true,
  } as const satisfies RouteLocationRaw;
}

// Moving inside a channel route keeps the prefix, whether the link is a name or a path.
export function channelPrefixGuard(
  to: RouteLocationNormalized,
  from: RouteLocationNormalized,
): true | RouteLocationRaw {
  const channel = from.params[channelParam];

  if (typeof channel !== 'string' || to.params[channelParam] !== undefined || !isShortChannelPage(to))
    return true;

  return prefixed(to, channel);
}

// An old /app/u/<name>/admin/... (or /settings) address has no twin; it opens the platform page at /app/....
export function platformPathGuard(to: RouteLocationNormalized) {
  if (!to.path.startsWith(channelPrefix))
    return true;

  const [, ...rest] = to.path.slice(channelPrefix.length).split('/');
  const [first] = rest;

  if (first !== 'admin' && !(first === 'settings' && rest.length === 1))
    return true;

  return {
    path: `/app/${rest.join('/')}`,
    query: to.query,
    hash: to.hash,
    replace: true,
  } as const satisfies true | RouteLocationRaw;
}

// Opening /app/<page> from outside a channel route lands on /app/u/<current channel>/<page>.
// It runs after the sign-in guard, so a signed-out visitor never gets here and the login flow stays as it is.
// A person with no channel (a viewer) keeps the short address of a page, and lands on My channels from the bare /app.
export async function ownChannelGuard(
  to: RouteLocationNormalized,
): Promise<true | RouteLocationRaw> {
  if (!isShortChannelPage(to) || takeOAuthLanding())
    return true;

  const login = await currentChannelLogin().catch(() => null);

  if (login === null)
    return to.path === '/app'
      ? viewerHome
      : true;

  return prefixed(to, login);
}
