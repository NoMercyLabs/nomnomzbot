<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// Puts one Twitch clip on the streamer's YouTube channel: the title, a description and who may see it.
// The video is private unless the person chooses otherwise. A refusal keeps the dialog open with its reason.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useUploadClip,
  VIDEO_PRIVACY,
  type ChannelClip,
  type ClipUpload,
  type VideoPrivacy,
} from '@/api/clips';
import FormDialog from '@/components/layout/FormDialog.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const TITLE_MAX = 100;
const DESCRIPTION_MAX = 5000;

const props = defineProps<{ clip: ChannelClip | null }>();
const open = defineModel<boolean>('open', { required: true });
const emit = defineEmits<{ uploaded: [video: ClipUpload] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const upload = useUploadClip(channelId);
const page = 'dashboard.stream.clips';

const title = ref('');
const description = ref('');
const privacy = ref<VideoPrivacy>(VIDEO_PRIVACY[0]);
const refusal = ref('');

const privacyOptions = computed(() =>
  VIDEO_PRIVACY.map((value) => ({ value, label: t(`${page}.privacy.${value}`) })));

// Each clip starts from its own Twitch title, private.
watch(
  () => props.clip,
  (clip) => {
    title.value = (clip?.title ?? '').slice(0, TITLE_MAX);
    description.value = '';
    privacy.value = VIDEO_PRIVACY[0];
    refusal.value = '';
  },
  { immediate: true },
);

function submit(): void {
  if (props.clip === null || title.value.trim() === '') return;

  refusal.value = '';

  upload.mutate(
    {
      clipId: props.clip.id,
      words: {
        title: title.value.trim(),
        description: description.value,
        privacy: privacy.value,
      },
    },
    {
      onSuccess: (answer) => {
        if (answer?.outcome === 'uploaded') {
          open.value = false;
          emit('uploaded', answer);

          return;
        }

        refusal.value = t(`${page}.refused.${answer?.outcome ?? 'failed'}`);
      },
      onError: () => {
        refusal.value = t(`${page}.refused.failed`);
      },
    },
  );
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-clip-upload-dialog
    :title="t(`${page}.upload.title`)"
    :description="t(`${page}.upload.description`)"
    :action="t(`${page}.upload.action`)"
    :pending="upload.isPending.value"
    :disabled="title.trim() === ''"
    :error="refusal || undefined"
    @submit="submit"
  >
    <div class="grid gap-1.5">
      <Label for="clip-upload-title">{{ t(`${page}.upload.videoTitle`) }}</Label>
      <Input id="clip-upload-title" v-model="title" :maxlength="TITLE_MAX" />
    </div>
    <div class="grid gap-1.5">
      <Label for="clip-upload-description">{{ t(`${page}.upload.videoDescription`) }}</Label>
      <Textarea id="clip-upload-description" v-model="description" :maxlength="DESCRIPTION_MAX" rows="3" />
    </div>
    <div class="grid gap-1.5">
      <Label for="clip-upload-privacy">{{ t(`${page}.upload.privacy`) }}</Label>
      <OptionSelect id="clip-upload-privacy" v-model="privacy" data-clip-privacy :options="privacyOptions" />
    </div>
  </FormDialog>
</template>
