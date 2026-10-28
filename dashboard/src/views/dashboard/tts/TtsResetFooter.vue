<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The footer of both rule panels: one reset button that returns every message rule to the bot defaults.
// It is idle when the channel already matches those defaults. The two panels share it, so they never differ.
import { computed } from 'vue';

import { useTtsConfig, type TtsConfig } from '@/api/tts';
import { useResetTtsRules, useTtsDefaults } from '@/api/ttsRules';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const { channelId } = useCurrentChannel();
const config = useTtsConfig(channelId);
const defaults = useTtsDefaults(channelId);
const reset = useResetTtsRules(channelId);

// The settings a reset touches.
const resetKeys = [
  'isEnabled',
  'mode',
  'defaultProvider',
  'maxCharacters',
  'minPermission',
  'skipBotMessages',
  'readUsernames',
  'profanityCensorEnabled',
  'modApprovalRequired',
  'viewerVoiceSelfServiceEnabled',
] as const satisfies readonly (keyof TtsConfig)[];

const atDefaults = computed<boolean>(() => {
  const current = config.data.value ?? null;
  const standard = defaults.data.value ?? null;

  if (current === null || standard === null)
    return true;

  const sameSettings = resetKeys.every((key) => String(current[key]) === String(standard[key]));
  const sameBits = Number(current.minBitsToTts ?? 0) === Number(standard.minBitsToTts ?? 0);

  return sameSettings && sameBits;
});
</script>

<template>
  <Button
    data-tts-reset
    variant="outline"
    size="sm"
    :disabled="atDefaults || reset.isPending.value"
    @click="reset.mutate()"
  >
    {{ $t("dashboard.tts.speaking.reset.label") }}
  </Button>
  <span class="text-sm text-muted-foreground">{{ $t("dashboard.tts.speaking.reset.note") }}</span>
</template>
