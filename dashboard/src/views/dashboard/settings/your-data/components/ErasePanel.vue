<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The two ways to end the use of your data. Opt out stops marketing, leaderboards and analytics use and keeps the
// data. Erase destroys it and cannot be undone: the bot counts what it would destroy first, and the dialog shows
// those counts before it asks.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchErasurePreview,
  useRequestErasure,
  useRequestOptOut,
  type ErasurePreview,
} from '@/api/yourData';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { errorText } from '@/lib/errors';

const { t, te } = useI18n();
const erase = useRequestErasure();
const optOut = useRequestOptOut();

const eraseOpen = ref(false);
const optOutOpen = ref(false);
const consequences = ref<string[]>([]);
const failure = ref<string | undefined>(undefined);
const notice = ref<string | undefined>(undefined);

function categoryLabel(key: string): string {
  const path = `dashboard.settings.yourData.erase.category.${key}`;

  return te(path)
    ? t(path)
    : key.replaceAll('_', ' ');
}

function previewLines(preview: ErasurePreview): string[] {
  const lines = preview.categories
    .filter((category) => Number(category.rowCount) > 0)
    .map((category) =>
      t('dashboard.settings.yourData.erase.line', {
        count: Number(category.rowCount),
        what: categoryLabel(category.categoryKey),
      }));

  return lines.length > 0
    ? lines
    : [t('dashboard.settings.yourData.erase.nothing')];
}

// The count is read before the dialog opens: when it cannot be read, erasing is not offered.
async function askErase(): Promise<void> {
  failure.value = undefined;
  notice.value = undefined;

  try {
    consequences.value = previewLines(await fetchErasurePreview());
    eraseOpen.value = true;
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

async function confirmErase(): Promise<void> {
  try {
    await erase.mutateAsync();
    notice.value = t('dashboard.settings.yourData.erase.requested');
  } catch (error) {
    failure.value = errorText(error, t);
  }

  eraseOpen.value = false;
}

async function confirmOptOut(): Promise<void> {
  failure.value = undefined;
  notice.value = undefined;

  try {
    await optOut.mutateAsync();
    notice.value = t('dashboard.settings.yourData.optOut.requested');
  } catch (error) {
    failure.value = errorText(error, t);
  }

  optOutOpen.value = false;
}
</script>

<template>
  <PanelCard
    data-panel="erase"
    tier="quiet"
    :title="$t('dashboard.settings.yourData.erase.title')"
    :description="$t('dashboard.settings.yourData.erase.description')"
  >
    <RowGroup>
      <div class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
        <div class="min-w-0 flex-1">
          <span class="block text-sm font-medium">{{ $t('dashboard.settings.yourData.optOut.title') }}</span>
          <span class="block text-sm text-muted-foreground">
            {{ $t('dashboard.settings.yourData.optOut.description') }}
          </span>
        </div>
        <Button variant="outline" data-opt-out @click="optOutOpen = true">
          {{ $t('dashboard.settings.yourData.optOut.action') }}
        </Button>
      </div>
      <div class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
        <div class="min-w-0 flex-1">
          <span class="block text-sm font-medium">{{ $t('dashboard.settings.yourData.erase.rowTitle') }}</span>
          <span class="block text-sm text-muted-foreground">
            {{ $t('dashboard.settings.yourData.erase.rowDescription') }}
          </span>
        </div>
        <Button variant="destructive" data-erase @click="askErase">
          {{ $t('dashboard.settings.yourData.erase.action') }}
        </Button>
      </div>
    </RowGroup>
    <p v-if="notice" data-notice class="px-3 py-3 text-sm text-muted-foreground">{{ notice }}</p>
    <p v-if="failure" role="alert" class="px-3 py-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>

  <ConfirmDialog
    v-model:open="eraseOpen"
    :title="$t('dashboard.settings.yourData.erase.confirmTitle')"
    :description="$t('dashboard.settings.yourData.erase.confirmDescription')"
    :consequences="consequences"
    :action="$t('dashboard.settings.yourData.erase.confirmAction')"
    :pending="erase.isPending.value"
    @confirm="confirmErase"
  />
  <ConfirmDialog
    v-model:open="optOutOpen"
    :title="$t('dashboard.settings.yourData.optOut.confirmTitle')"
    :description="$t('dashboard.settings.yourData.optOut.confirmDescription')"
    :action="$t('dashboard.settings.yourData.optOut.confirmAction')"
    :pending="optOut.isPending.value"
    @confirm="confirmOptOut"
  />
</template>
