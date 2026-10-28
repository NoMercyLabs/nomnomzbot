<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The blast-radius preview every platform-default edit shows before Apply: how many channels the change
// reaches, how many keep their own setting, and a few names. When the server calls the change dangerous
// it also asks for one explicit tick. The owner of the dialog feeds it from usePlatformDefaultPreview.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PlatformDefaultBlastRadius } from '@/api/adminPlatformDefaults';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Spinner } from '@/components/ui/spinner';

const props = defineProps<{
  preview: PlatformDefaultBlastRadius | undefined;
  pending: boolean;
  failed: boolean;
}>();

defineEmits<{ retry: [] }>();

const dangerConfirmed = defineModel<boolean>('dangerConfirmed', { default: false });

const { t } = useI18n();

const channels = (count: number | string): string =>
  t('dashboard.admin.platformDefaults.preview.channels', Number(count));

const names = computed<string>(() => (props.preview?.sampleChannelNames ?? []).join(', '));
</script>

<template>
  <div data-preview class="grid gap-2 rounded-lg border border-border bg-muted/40 p-3 text-sm" aria-live="polite">
    <p v-if="pending" class="flex items-center gap-2 text-muted-foreground">
      <Spinner />
      {{ $t('dashboard.admin.platformDefaults.preview.counting') }}
    </p>
    <div v-else-if="failed" role="alert" class="flex items-center justify-between gap-3 text-destructive">
      <span>{{ $t('dashboard.admin.platformDefaults.preview.failed') }}</span>
      <Button type="button" variant="outline" size="sm" @click="$emit('retry')">
        {{ $t('dashboard.admin.platformDefaults.preview.retry') }}
      </Button>
    </div>
    <template v-else-if="preview">
      <p class="font-medium">
        {{ $t('dashboard.admin.platformDefaults.preview.changes', { channels: channels(preview.channelsAffected) }) }}
      </p>
      <p class="text-muted-foreground">
        {{ $t('dashboard.admin.platformDefaults.preview.keeping', { channels: channels(preview.channelsKeepingOwnSetting) }) }}
      </p>
      <p v-if="names" class="text-muted-foreground">
        {{ $t('dashboard.admin.platformDefaults.preview.examples', { names }) }}
      </p>
      <label
        v-if="preview.requiresDangerConfirmation"
        class="flex cursor-pointer items-start gap-2 rounded-md border border-destructive/40 p-2 text-destructive"
      >
        <Checkbox v-model="dangerConfirmed" data-confirm-danger class="mt-0.5" />
        <span>{{ $t('dashboard.admin.platformDefaults.preview.danger') }}</span>
      </label>
    </template>
  </div>
</template>
