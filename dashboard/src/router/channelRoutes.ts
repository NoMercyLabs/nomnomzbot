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
  RouteRecordRaw,
} from 'vue-router';

// Every dashboard page is reachable twice: /app/<page> for the channel being managed, and
// /app/u/<channelName>/<page> for one named channel, so a shared link needs no manual switch.
export const channelParam = 'channelName';
export const channelPrefix = '/app/u/';
const variantMark = 'u:';

export type ChannelParams = { channelName: string };

// The channel-prefixed twin of each page: same page, name marked, path unchanged below the prefix.
export function channelVariants(pages: readonly RouteRecordRaw[]): RouteRecordRaw[] {
  return pages.map((page) => ({
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

function baseName(route: RouteLocationNormalizedLoaded): string {
  const name = String(route.name ?? 'dashboard-home');

  return name.startsWith(variantMark)
    ? name.slice(variantMark.length)
    : name;
}

// Where the same page of another channel lives. A null login is the own channel: the short form.
// A page that belongs to one item of the old channel (an id param) falls back to the channel home.
export function switchLocation(
  route: RouteLocationNormalizedLoaded,
  login: string | null,
): RouteLocationRaw {
  const itemParams = Object.keys(route.params).filter((key) => key !== channelParam);
  const name = itemParams.length > 0
    ? 'dashboard-home'
    : baseName(route);
  const keep = itemParams.length > 0
    ? {}
    : { query: route.query, hash: route.hash };

  if (login === null) {
    leaveOnce = true;

    return { name, ...keep };
  }

  return {
    name: `${variantMark}${name}`,
    params: { [channelParam]: login },
    ...keep,
  };
}

// The short form is asked for on purpose when the own channel is chosen; the guard lets it stay.
let leaveOnce = false;

function underChannelPrefix(path: string): boolean {
  return path.startsWith(channelPrefix);
}

// Moving inside a channel route keeps the prefix, whether the link is a name or a path.
export function channelPrefixGuard(
  to: RouteLocationNormalized,
  from: RouteLocationNormalized,
): true | RouteLocationRaw {
  const asked = leaveOnce;

  leaveOnce = false;

  const channel = from.params[channelParam];

  if (asked || typeof channel !== 'string' || to.params[channelParam] !== undefined)
    return true;

  const inApp = to.path === '/app' || to.path.startsWith('/app/');

  if (!inApp || underChannelPrefix(to.path))
    return true;

  const rest = to.path.slice('/app'.length);

  return {
    path: `${channelPrefix}${encodeURIComponent(channel)}${rest}`,
    query: to.query,
    hash: to.hash,
    replace: true,
  };
}
