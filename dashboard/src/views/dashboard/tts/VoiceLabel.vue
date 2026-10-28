<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A voice by its display name. The catalog is asked for this one voice, so the name shows even when the
// voice is far down the list; while the answer is on its way (or the bot has no such voice) the id shows.
import { computed } from 'vue';

import { useTtsVoices } from '@/api/tts';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ voiceId: string }>();

const { channelId } = useCurrentChannel();
const catalog = useTtsVoices(channelId, () => ({ q: props.voiceId }));

const name = computed(() =>
  (catalog.data.value?.pages ?? [])
    .flatMap((page) => page.voices)
    .find((voice) => voice.id === props.voiceId)?.displayName);
</script>

<template>
  <span data-voice-label>{{ name ?? voiceId }}</span>
</template>
