<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
