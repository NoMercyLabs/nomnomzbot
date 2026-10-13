// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteLocationNormalized, RouteLocationRaw } from 'vue-router'

import { restoreSession } from '@/api/auth'
import { loginRoute } from '@/views/site/login/route'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    // A page only a signed-out visitor needs, such as the login page.
    guestOnly?: boolean
  }
}

// Only a path on this site is followed, so a crafted link cannot send a signed-in visitor elsewhere.
export function returnPath(asked: unknown): string {
  return typeof asked === 'string' && asked.startsWith('/') && !asked.startsWith('//')
    ? asked
    : '/app'
}

export async function signInGuard(to: RouteLocationNormalized): Promise<true | RouteLocationRaw> {
  if (to.matched.some((record) => record.meta.guestOnly))
    return guestGuard(to)

  if (!to.matched.some((record) => record.meta.requiresAuth))
    return true

  if (await restoreSession())
    return withoutToken(to)

  return { name: loginRoute.name, query: { return_to: to.fullPath } }
}

// A visitor with a valid session skips the login page and goes where the login was asked for.
async function guestGuard(to: RouteLocationNormalized): Promise<true | RouteLocationRaw> {
  if (!(await restoreSession()))
    return true

  return { path: returnPath(to.query.return_to), replace: true }
}

// The router writes the address it navigates to, so an OAuth fragment that is kept here would
// put the access token back in the address bar after restoreSession stripped it.
function withoutToken(to: RouteLocationNormalized): true | RouteLocationRaw {
  if (!to.hash.includes('access_token='))
    return true

  return { path: to.path, query: to.query, hash: '', replace: true }
}
