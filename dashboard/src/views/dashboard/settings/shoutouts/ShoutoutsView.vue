<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How the channel gives a shoutout: whether the bot shouts out a known streamer on their first chat of a stream,
// and the message it announces. The switch saves at once. Save sends the message; Use the default clears it.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  useAutoShoutout,
  useSaveAutoShoutout,
  useSaveShoutoutTemplate,
  useShoutoutTemplate,
} from '@/api/shoutoutSettings';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

const TEMPLATE_MAX = 500;

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();

const auto = useAutoShoutout(channelId);
const template = useShoutoutTemplate(channelId);
const saveAuto = useSaveAutoShoutout(channelId);
const saveTemplate = useSaveShoutoutTemplate(channelId);

const canEdit = computed<boolean>(() => can('moderation:shoutout'));
const autoState = usePanelState(auto);
const templateState = usePanelState(template);

const enabled = computed<boolean>({
  get: () => auto.data.value?.enabled ?? false,
  set: (on: boolean) => changeAuto(on),
});

function changeAuto(on: boolean): void {
  saveAuto.mutate({ enabled: on }, {
    onError: (error: Error) => toast.error(errorText(error, t)),
  });
}

const stored = computed<string>(() => template.data.value?.template ?? '');
const text = ref('');

// A fresh read replaces the text only when the person has not edited it.
watch(
  stored,
  (next, previous) => {
    if (text.value === (previous ?? '') || text.value === '') {
      text.value = next;
    }
  },
  { immediate: true },
);

const dirty = computed<boolean>(() => text.value.trim() !== stored.value);

function submit(message: string): void {
  saveTemplate.mutate({ template: message }, {
    onSuccess: () => toast.success(t('dashboard.settings.shoutouts.saved')),
    onError: (error: Error) => toast.error(errorText(error, t)),
  });
}

function useDefault(): void {
  text.value = '';
  submit('');
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.shoutouts')"
    :crumb="$t('dashboard.settings.shoutouts.crumb')"
  />
  <PageBody data-page-body>
    <PanelCard
      :title="$t('dashboard.settings.shoutouts.auto.title')"
      :description="$t('dashboard.settings.shoutouts.auto.description')"
      :state="autoState"
      :error-text="$t('dashboard.settings.shoutouts.auto.loadError')"
      @retry="auto.refetch()"
    >
      <RowGroup>
        <SettingRow
          v-model="enabled"
          data-field="autoShoutout"
          :title="$t('dashboard.settings.shoutouts.auto.label')"
          :description="$t('dashboard.settings.shoutouts.auto.help')"
          :disabled="!canEdit || saveAuto.isPending.value"
          :readonly="!canEdit"
        />
      </RowGroup>
    </PanelCard>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.settings.shoutouts.message.title')"
      :description="$t('dashboard.settings.shoutouts.message.description')"
      :state="templateState"
      :error-text="$t('dashboard.settings.shoutouts.message.loadError')"
      @retry="template.refetch()"
    >
      <div class="grid gap-3">
        <TemplateField
          v-model="text"
          context="eventResponse"
          data-field="shoutoutTemplate"
          :label="$t('dashboard.settings.shoutouts.message.label')"
          :placeholder="$t('dashboard.settings.shoutouts.message.placeholder')"
          :maxlength="TEMPLATE_MAX"
          :rows="4"
          :disabled="!canEdit"
        />
        <div v-if="canEdit" class="flex flex-wrap gap-2">
          <Button
            size="sm"
            data-action="save"
            :disabled="!dirty || saveTemplate.isPending.value"
            @click="submit(text.trim())"
          >
            {{ $t("dashboard.settings.shoutouts.message.save") }}
          </Button>
          <Button
            variant="outline"
            size="sm"
            data-action="use-default"
            :disabled="stored === '' || saveTemplate.isPending.value"
            @click="useDefault"
          >
            {{ $t("dashboard.settings.shoutouts.message.useDefault") }}
          </Button>
        </div>
      </div>
    </PanelCard>
  </PageBody>
</template>
