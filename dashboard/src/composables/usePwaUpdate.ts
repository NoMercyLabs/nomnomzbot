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

// The app is installable and its build files stay cached for good (vite.config.ts). A cached build never
// updates itself, so a new deploy shows one toast: the user reloads when it suits them, not mid-edit.
import { useRegisterSW } from 'virtual:pwa-register/vue';
import { watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

// How often an open tab asks the server whether a new build exists.
const checkEveryMs = 60 * 60 * 1000;

export function usePwaUpdate(): void {
  const { t } = useI18n();
  const { needRefresh, updateServiceWorker } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      if (!registration) return;

      setInterval(() => void registration.update(), checkEveryMs);
    },
  });

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
