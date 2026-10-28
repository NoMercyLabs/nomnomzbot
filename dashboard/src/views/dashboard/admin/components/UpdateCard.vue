<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Whether a newer stable release exists than the build this bot runs. The release page holds the change list;
// and the update steps for each kind of install. A plain link: nothing here is urgent.
import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';

import { useUpdateCheck } from '@/api/updates';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { usePanelState } from '@/composables/usePanelState';

const props = defineProps<{ botVersion: string }>();

const { t, d } = useI18n();

const check = useUpdateCheck(toRef(props, 'botVersion'));
const state = usePanelState(check);

const result = computed(() => check.data.value);
</script>

<template>
  <PanelCard
    data-view="update-check"
    tier="quiet"
    pad
    :title="t('dashboard.admin.system.update.title')"
    :description="t('dashboard.admin.system.update.description')"
    :state="state"
    :error-text="t('dashboard.admin.system.update.failed')"
    @retry="check.refetch()"
  >
    <div v-if="result?.state === 'available'" data-update-available class="grid gap-3">
      <div class="flex flex-wrap items-center gap-3 text-sm">
        <Badge variant="secondary">{{ t("dashboard.admin.system.update.available") }}</Badge>
        <span class="font-medium">{{ result.version }}</span>
        <span class="text-muted-foreground">{{ d(new Date(result.publishedAt), "short") }}</span>
        <a
          :href="result.url"
          target="_blank"
          rel="noopener"
          class="underline"
        >
          {{ t("dashboard.admin.system.update.changes") }}
        </a>
      </div>
      <p class="text-sm text-muted-foreground">{{ t("dashboard.admin.system.update.how") }}</p>
    </div>
    <p v-else-if="result?.state === 'current'" data-update-current class="text-sm">
      {{ t("dashboard.admin.system.update.current") }}
    </p>
    <p v-else data-update-unknown class="text-sm text-muted-foreground">
      {{ t("dashboard.admin.system.update.unknown") }}
    </p>
  </PanelCard>
</template>
