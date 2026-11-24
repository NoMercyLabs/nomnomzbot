<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one notice after a pipeline save that needs a browser source which is not open in OBS.
// It only informs: the save already worked, and a step that finds its source missing is skipped when it runs.
// The frame mounts it once; the save API fills useMissingSources, so no page carries its own copy.
// One primary action ("Set it up", the OBS page); "Later" only closes it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { useMissingSources } from '@/composables/useMissingSources';

const { t, te } = useI18n();
const router = useRouter();
const store = useMissingSources();
const { items } = store;

const open = computed(() => items.value.length > 0);

const title = computed(() =>
  items.value.length === 1
    ? t('common.missingSources.titleOne')
    : t('common.missingSources.titleMany'),
);

const lines = computed(() =>
  items.value.map((item) => {
    const key = `common.missingSources.surfaces.${item.surfaceKey}`;
    const surface = te(key)
      ? t(key)
      : item.surfaceKey;

    return {
      key: item.surfaceKey,
      text: t('common.missingSources.line', { surface }),
    };
  }),
);

function setOpen(next: boolean): void {
  if (!next) {
    store.dismiss();
  }
}

function setUp(): void {
  store.dismiss();

  void router.push({
    name: 'dashboard-obs',
    query: { view: 'overlays' },
    hash: '#required-overlays',
  });
}
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent
      data-missing-sources
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-115"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
        <DialogDescription class="sr-only">{{ title }}</DialogDescription>
      </div>
      <ul class="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto px-6 pb-4 text-sm">
        <li v-for="line in lines" :key="line.key" data-missing-source>
          {{ line.text }}
        </li>
      </ul>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button data-missing-later type="button" variant="ghost" @click="store.dismiss()">
          {{ $t('common.missingSources.later') }}
        </Button>
        <Button data-missing-set-up type="button" @click="setUp()">
          {{ $t('common.missingSources.setUp') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
