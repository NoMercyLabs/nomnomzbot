<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The sticker choice of a voice trigger: one of the channel's uploaded images, or none.
import { useStickerAssets } from '@/api/voiceTriggers';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { noSticker } from './voiceTriggerDraft';

const model = defineModel<string>({ required: true });

const { channelId } = useCurrentChannel();
const stickers = useStickerAssets(channelId);
</script>

<template>
  <Field>
    <div class="flex items-center gap-1.5">
      <FieldLabel>{{ $t('dashboard.voiceTriggers.editor.sticker') }}</FieldLabel>
      <FieldInfo>{{ $t('dashboard.voiceTriggers.editor.stickerHelp') }}</FieldInfo>
    </div>
    <Select v-model="model">
      <SelectTrigger :aria-label="$t('dashboard.voiceTriggers.editor.sticker')" class="w-full">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem :value="noSticker">{{ $t('dashboard.voiceTriggers.editor.noSticker') }}</SelectItem>
        <SelectItem v-for="asset in stickers.data.value ?? []" :key="asset.id" :value="asset.id">
          {{ asset.displayName }}
        </SelectItem>
      </SelectContent>
    </Select>
    <p v-if="stickers.isError.value" role="alert" class="text-sm text-destructive">
      {{ $t('dashboard.voiceTriggers.editor.stickersError') }}
    </p>
  </Field>
</template>
