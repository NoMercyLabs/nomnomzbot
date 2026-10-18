// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
