<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The voice listener in the page header: when it last reported a match, and a button that opens it
// in its own small Chrome window. The link carries the listener token, so it is opened, never shown.
import ArrowUpRight from '~icons/nnz/arrow-up-right';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { VoiceTrigger } from '@/api/voiceTriggers';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { agoText } from '@/lib/time';

const props = defineProps<{ triggers: VoiceTrigger[]; link: string | undefined }>();

const { t, locale } = useI18n();

const latest = computed<string | undefined>(() =>
  props.triggers
    .map((item): string | null | undefined => item.lastFiredAt)
    .filter((at): at is string => typeof at === 'string')
    .sort()
    .at(-1));

const label = computed<string>(() => {
  if (latest.value === undefined)
    return t('dashboard.voiceTriggers.listener.none');

  const now = new Date();

  if (now.getTime() - new Date(latest.value).getTime() < 60_000)
    return t('dashboard.voiceTriggers.listener.justNow');

  return t('dashboard.voiceTriggers.listener.last', { ago: agoText(latest.value, now, locale.value) });
});

function popOut(): void {
  if (props.link) {
    window.open(props.link, 'nnz-voice-listener', 'popup,width=520,height=560');
  }
}
</script>

<template>
  <div data-voice-listener-header class="flex items-center gap-2">
    <!-- The dot stays neutral: a past match is history, not proof a listener tab is open now. -->
    <StatusBadge
      data-voice-listener-status
      tone="off"
      :label="label"
    />
    <Button
      data-voice-listener-popout
      type="button"
      variant="outline"
      size="sm"
      :disabled="!link"
      @click="popOut"
    >
      <ArrowUpRight class="size-4" aria-hidden="true" />
      {{ t('dashboard.voiceTriggers.listener.popout') }}
    </Button>
  </div>
</template>
