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

import { watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import { navByMode } from './navModes';
import { useNavigation } from './useNavigation';

// A page whose read action the person does not hold shows failing panels. The person lands on the first
// page the sidebar offers instead, so a previewed role never meets a page it cannot read.
export function useReadableRedirect(): void {
  const router = useRouter();
  const route = useRoute();
  const navigation = useNavigation();

  watch(
    () => [route.name, navigation.entries.value.map((entry) => entry.route)] as const,
    ([name, readableRoutes]) => {
      const mode = route.meta.navMode ?? 'dashboard';
      const owner = navByMode[mode]
        .flatMap((group) => group.entries)
        .find((entry) => navigation.isCurrent(entry));
      const first = readableRoutes[0];

      if (typeof name !== 'string' || owner === undefined || first === undefined) return;

      if (!readableRoutes.includes(owner.route)) {
        void router.replace({ name: first });
      }
    },
    { immediate: true },
  );
}
