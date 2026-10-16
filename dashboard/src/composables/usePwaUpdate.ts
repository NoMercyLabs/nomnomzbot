// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
