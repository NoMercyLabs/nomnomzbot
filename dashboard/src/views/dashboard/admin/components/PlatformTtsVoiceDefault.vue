<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform text-to-speech voice: what a channel speaks with until it picks its own. Editing opens a
// dialog with every candidate voice grouped by language, each with a play button; pick one, read the counted
// blast radius, Apply.
import Check from '~icons/nnz/check';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  previewTtsVoiceDefault,
  useAdminTtsVoiceCandidates,
  useAdminTtsVoiceDefault,
  useSaveTtsVoiceDefault,
  type TtsVoiceCandidate,
  type TtsVoiceDefault,
} from '@/api/adminPlatformDefaults';
import FormDialog from '@/components/layout/FormDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { usePlatformDefaultListError } from '@/composables/usePlatformDefaultListError';
import { usePlatformDefaultPreview } from '@/composables/usePlatformDefaultPreview';
import PlaySampleButton from '@/views/dashboard/tts/PlaySampleButton.vue';
import { groupVoices, localeLabel } from '@/views/dashboard/tts/voiceGroups';

import PlatformDefaultPreview from './PlatformDefaultPreview.vue';

const { t, locale } = useI18n();
const { can } = usePlatformCan();

const current = useAdminTtsVoiceDefault();
const save = useSaveTtsVoiceDefault();

const editing = ref(false);
const chosen = ref<string>();
const failure = ref<string>();

const candidates = useAdminTtsVoiceCandidates(editing);

const preview = usePlatformDefaultPreview(() =>
  previewTtsVoiceDefault(chosen.value ?? ''));

const panelState = usePanelState(current, (voice: TtsVoiceDefault | undefined) =>
  voice === undefined);

const listError = usePlatformDefaultListError(() => current.error.value);

const items = computed(() =>
  groupVoices(
    (candidates.data.value ?? []).map((voice: TtsVoiceCandidate) => ({
      ...voice,
      id: voice.voiceId,
    })),
    locale.value,
  ));

const applyLabel = computed<string>(() =>
  t('dashboard.admin.platformDefaults.preview.apply', {
    channels: t(
      'dashboard.admin.platformDefaults.preview.channels',
      preview.channelsAffected.value,
    ),
  }),
);

function startEdit(): void {
  chosen.value = current.data.value?.voiceId;
  failure.value = undefined;
  editing.value = true;
  preview.reset();
}

function close(): void {
  editing.value = false;
  preview.reset();
}

function pick(voiceId: string): void {
  chosen.value = voiceId;
  void preview.request();
}

function apply(): void {
  if (!chosen.value || !preview.canApply.value) return;

  save.mutate(
    { voiceId: chosen.value, confirmedChannelsAffected: preview.channelsAffected.value },
    {
      onSuccess: () => {
        toast.success(t('dashboard.admin.platformDefaults.voice.saved'));
        close();
      },
      onError: (error: Error) => {
        failure.value = t('dashboard.admin.platformDefaults.voice.saveError', {
          reason: error.message,
        });

        void preview.request();
      },
    },
  );
}
</script>

<template>
  <PanelCard
    data-section="tts-voice"
    :title="$t('dashboard.admin.platformDefaults.voice.title')"
    :description="$t('dashboard.admin.platformDefaults.voice.description')"
    :state="panelState"
    :empty-text="$t('dashboard.admin.platformDefaults.voice.empty')"
    :error-text="listError"
    @retry="current.refetch()"
  >
    <div v-if="current.data.value" data-voice-current class="flex items-center gap-4 px-4 py-3">
      <div class="min-w-0 flex-1">
        <p class="text-sm font-medium">
          {{ current.data.value.displayName }}
        </p>
        <p class="text-xs text-muted-foreground">
          {{
            $t("dashboard.admin.platformDefaults.voice.detail", {
              language: localeLabel(current.data.value.locale, locale),
              provider: current.data.value.provider,
            })
          }}
        </p>
        <p class="text-xs text-muted-foreground">
          {{
            $t(
              "dashboard.admin.platformDefaults.voice.following",
              Number(current.data.value.channelsFollowing),
            )
          }}
        </p>
        <p class="text-xs text-muted-foreground">
          {{
            $t(
              "dashboard.admin.platformDefaults.voice.overrides",
              Number(current.data.value.channelsWithOwnVoice),
            )
          }}
        </p>
      </div>
      <Button v-if="can('platform:defaults:manage')"
              variant="outline"
              size="sm"
              data-edit
              @click="startEdit"
      >
        {{ $t("dashboard.admin.platformDefaults.voice.edit") }}
      </Button>
    </div>
  </PanelCard>

  <FormDialog
    v-if="can('platform:defaults:manage')"
    :open="editing"
    :title="$t('dashboard.admin.platformDefaults.voice.dialog.title')"
    :description="$t('dashboard.admin.platformDefaults.voice.dialog.description')"
    :action="applyLabel"
    :pending="save.isPending.value"
    :disabled="!preview.canApply.value"
    :error="failure"
    @update:open="(open: boolean) => !open && close()"
    @submit="apply"
  >
    <p v-if="candidates.isPending.value" class="text-sm text-muted-foreground">
      {{ $t("dashboard.admin.platformDefaults.voice.dialog.loading") }}
    </p>
    <p v-else-if="candidates.isError.value" role="alert" class="text-sm text-destructive">
      {{ $t("dashboard.admin.platformDefaults.voice.dialog.loadError") }}
    </p>
    <div
      v-else
      role="radiogroup"
      data-voice-list
      :aria-label="$t('dashboard.admin.platformDefaults.voice.dialog.list')"
      class="max-h-80 overflow-y-auto"
    >
      <template v-for="item in items" :key="item.key">
        <div
          v-if="item.kind === 'heading'"
          data-voice-group
          class="px-3 pt-4 pb-1 text-xs font-medium text-muted-foreground"
        >
          {{ item.label }}
        </div>
        <SummaryRow
          v-else
          data-voice-row
          :data-voice-id="item.voice.voiceId"
          pick
          role="radio"
          :aria-checked="item.voice.voiceId === chosen"
          :selected="item.voice.voiceId === chosen"
          :title="item.voice.displayName"
          :detail="item.voice.gender"
          @click="pick(item.voice.voiceId)"
        >
          <template #leading>
            <Check
              class="size-4 flex-none"
              :class="item.voice.voiceId === chosen ? '' : 'invisible'"
              aria-hidden="true"
            />
          </template>
          <template #trailing>
            <PlaySampleButton :voice-id="item.voice.voiceId" compact />
          </template>
        </SummaryRow>
      </template>
    </div>
    <PlatformDefaultPreview
      v-if="preview.pending.value || preview.failed.value || preview.preview.value"
      v-model:danger-confirmed="preview.dangerConfirmed.value"
      :preview="preview.preview.value"
      :pending="preview.pending.value"
      :failed="preview.failed.value"
      @retry="preview.request()"
    />
    <p v-else class="text-sm text-muted-foreground">
      {{ $t("dashboard.admin.platformDefaults.preview.pick") }}
    </p>
  </FormDialog>
</template>
