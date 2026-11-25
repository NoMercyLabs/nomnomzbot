<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel's one theme color for all overlays. A color a widget sets for itself wins over it; it wins over the
// color of the widget's theme. Reset clears it, so each overlay shows its theme's own color again. A color saves
// once it is a full hex value and the typing or dragging has paused.
import { useDebounceFn } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useOverlayThemeColor, useSaveOverlayThemeColor } from '@/api/overlayThemeColor';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import ActionRow from '@/components/layout/ActionRow.vue';
import { Button } from '@/components/ui/button';
import { ColorPicker } from '@/components/ui/color-picker';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

import { cleanColor } from './settingField';

// In the drawer the sheet frames the panel, so it drops its own border.
defineProps<{ flush?: boolean }>();

const savePause = 400;
const fullHex = /^#[0-9a-f]{6}$/i;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const stored = useOverlayThemeColor(channelId);
const save = useSaveOverlayThemeColor(channelId);

const writable = computed(() => can('widget:write'));
const saved = computed(() => stored.data.value?.color ?? '');
const draft = ref(saved.value);
const failure = computed<string>(() => {
  const error = save.error.value;

  if (!error)
    return '';

  return errorText(error, t);
});

const state = computed<PanelState>(() => {
  if (stored.isError.value)
    return 'error';

  return stored.isPending.value
    ? 'loading'
    : 'ready';
});

watch(saved, (color) => {
  draft.value = color;
}, { immediate: true });

// A refused save puts the saved color back in the field.
function putSavedBack(): void {
  draft.value = saved.value;
}

const commit = useDebounceFn((): void => {
  if (!fullHex.test(draft.value) || draft.value === saved.value) return;

  save.mutate(draft.value, { onError: putSavedBack });
}, savePause);

function type(color: string): void {
  draft.value = cleanColor(color);
  commit();
}

function reset(): void {
  draft.value = '';
  save.mutate('', { onError: putSavedBack });
}
</script>

<template>
  <PanelCard
    data-view="theme-color"
    pad
    :flush="flush"
    :title="$t('dashboard.overlays.themeColor.title')"
    :description="$t('dashboard.overlays.themeColor.description')"
    :state="state"
    :error-text="$t('dashboard.overlays.themeColor.loadError')"
    @retry="stored.refetch()"
  >
    <RowGroup>
      <ActionRow :title="$t('dashboard.overlays.themeColor.label')">
        <ColorPicker
          class="w-40 font-mono"
          :model-value="draft"
          :label="$t('dashboard.overlays.themeColor.label')"
          placeholder="#RRGGBB"
          :disabled="!writable"
          @update:model-value="type"
        />
        <Button
          variant="ghost"
          size="sm"
          :disabled="!writable || saved === ''"
          @click="reset"
        >
          {{ $t("dashboard.overlays.themeColor.reset") }}
        </Button>
      </ActionRow>
      <InlineNotice
        v-if="failure"
        tone="error"
        compact
        class="px-3 py-2"
        data-theme-color-error
      >
        {{ failure }}
      </InlineNotice>
    </RowGroup>
  </PanelCard>
</template>
