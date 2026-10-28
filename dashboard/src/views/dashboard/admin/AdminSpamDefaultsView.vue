<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The platform's spam-defense defaults: the values every new channel starts with. The form beneath renders one
// control per catalog entry the bot sends; this page owns the Save and sends the whole record.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import Check from '~icons/nnz/check';

import { useAdminSpamDefaults, useSaveAdminSpamDefaults } from '@/api/adminSpamDefaults';
import { ApiError } from '@/api/errors';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SpamSettingsForm, { type SpamFormState } from '@/components/spam/SpamSettingsForm.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

const { t } = useI18n();
const { can } = usePlatformCan();

const policy = useAdminSpamDefaults();
const save = useSaveAdminSpamDefaults();

const form = ref<SpamFormState | null>(null);

const panelState = usePanelState(policy);
const listError = computed<string>(() =>
  policy.error.value instanceof ApiError && policy.error.value.status === 403
    ? t('dashboard.admin.spamDefaults.forbidden')
    : t('dashboard.admin.spamDefaults.loadError'),
);

const copy = (key: string): string => t(`dashboard.admin.spamDefaults.${key}`);

function submit(): void {
  if (form.value === null) return;

  save.mutate(form.value.body, {
    onSuccess: () => toast.success(copy('saved')),
    onError: (error: Error) =>
      toast.error(t('dashboard.admin.spamDefaults.saveError', { reason: error.message })),
  });
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.admin.spamDefaults.title')"
    :crumb="$t('dashboard.admin.spamDefaults.crumb')"
  >
    <template #status>
      <span v-if="form?.dirty" class="text-sm text-muted-foreground" role="status">
        {{ $t("dashboard.admin.spamDefaults.unsaved") }}
      </span>
    </template>
    <template #actions>
      <Button
        v-if="can('featureflag:write')"
        data-save
        :disabled="!form?.dirty || form.invalid || save.isPending.value"
        @click="submit"
      >
        <Check aria-hidden="true" />
        {{
          save.isPending.value
            ? $t("dashboard.admin.spamDefaults.saving")
            : $t("dashboard.admin.spamDefaults.save")
        }}
      </Button>
    </template>
  </PageHeader>
  <PageBody data-page-body>
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.admin.spamDefaults.explain") }}</p>
    <p data-restricted class="text-sm text-muted-foreground">
      {{ $t("dashboard.admin.spamDefaults.restricted") }}
    </p>
    <SpamSettingsForm
      v-if="panelState === 'ready' && policy.data.value"
      :policy="policy.data.value"
      action="featureflag:write"
      platform
      :refusal="save.error.value"
      @change="(state) => (form = state)"
    />
    <PanelCard
      v-else
      tier="lead"
      class="min-h-40"
      :title="$t('dashboard.admin.spamDefaults.settingsTitle')"
      :state="panelState"
      :error-text="listError"
      @retry="policy.refetch()"
    />
  </PageBody>
</template>
