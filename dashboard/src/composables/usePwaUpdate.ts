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

// The app is installable and its build files stay cached for good (vite.config.ts). A new deploy loads by
// itself on the next page change, so nobody has to remember to reload. The open page never reloads under
// the user (Stoney, 2026-11-25: "don't do this for edit pages"); the toast offers it for whoever stays put.
import { useRegisterSW } from 'virtual:pwa-register/vue';
import { onScopeDispose, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { Router } from 'vue-router';
import { toast } from 'vue-sonner';

// How often an open tab asks the server whether a new build exists.
const checkEveryMs = 2 * 60 * 1000;

export function usePwaUpdate(router: Router): void {
  const { t } = useI18n();
  const { needRefresh, updateServiceWorker } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      if (!registration) return;

      setInterval(() => void registration.update(), checkEveryMs);

      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
          void registration.update();
        }
      });
    },
  });

  // Global guards run after the pages' own leave guards, so an unsaved-changes prompt still decides first.
  const removeGuard = router.beforeEach(async (to) => {
    if (!needRefresh.value)
      return true;

    await updateServiceWorker(false);
    window.location.assign(to.fullPath);

    return false;
  });

  onScopeDispose(removeGuard);

  watch(needRefresh, (waiting: boolean) => {
    if (!waiting) return;

    toast(t('pwa.update.title'), {
      description: t('pwa.update.body'),
      duration: Infinity,
      action: {
        label: t('pwa.update.reload'),
        onClick: () => void updateServiceWorker(true),
      },
    });
  });
}
