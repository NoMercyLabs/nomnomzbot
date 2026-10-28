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

import type { LocationQueryValue, Router } from 'vue-router';

// A page that sends the person to another page for one job puts the way back in the address as ?back=<path>.
// Only a dashboard page of this app is followed: a crafted link cannot send the person to another site or
// to a path the router does not know. Anything else gives undefined, so the page shows no way back.
export function dashboardReturn(
  router: Router,
  asked: LocationQueryValue | LocationQueryValue[] | undefined,
): string | undefined {
  if (typeof asked !== 'string' || !asked.startsWith('/app/') || asked.startsWith('//'))
    return undefined;

  const target = router.resolve(asked);

  return target.matched.length > 0 && target.name !== undefined
    ? target.fullPath
    : undefined;
}
