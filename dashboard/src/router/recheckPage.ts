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

import type {
  NavigationGuardWithThis,
  RouteLocationNormalizedLoaded,
  RouteLocationRaw,
  Router,
} from 'vue-router';

type PageGuard = NavigationGuardWithThis<undefined>;

// A guard of the page answers with its return value, so the callback stays unused.
const unused: Parameters<PageGuard>[2] = () => undefined;

// The entry guards (beforeEnter) of every record the open page sits in.
function entryGuards(open: RouteLocationNormalizedLoaded): readonly PageGuard[] {
  return open.matched.flatMap((record) => {
    const own = record.beforeEnter;

    if (own === undefined)
      return [];

    return Array.isArray(own)
      ? own
      : [own];
  });
}

// The first redirect an entry guard of the open page answers with, else undefined.
async function entryRefusal(
  open: RouteLocationNormalizedLoaded,
): Promise<RouteLocationRaw | undefined> {
  for (const guard of entryGuards(open)) {
    const answer = await guard.call(undefined, open, open, unused);
    const redirects = typeof answer === 'string' || (typeof answer === 'object' && !(answer instanceof Error));

    if (redirects)
      return answer;
  }

  return undefined;
}

// The page checks run when a page opens. A person's role can change while the page stays open (view as),
// so this runs them again on the open page: its own entry guards first, then the router's global guards,
// which a navigation to the same page skips unless it is forced. A page the role may not open lands on the
// first page it may; a page it may open stays, and the history keeps its length.
export async function recheckOpenPage(router: Router): Promise<void> {
  const open = router.currentRoute.value;
  const refusal = await entryRefusal(open);

  if (refusal !== undefined) {
    await router.replace(refusal);

    return;
  }

  if (open.name === undefined) return;

  await router.replace({
    name: open.name,
    params: open.params,
    query: open.query,
    hash: open.hash,
    force: true,
  });
}
