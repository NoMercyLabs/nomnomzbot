<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The page people of a suspended channel are sent to when they need the platform. One address for the whole
// platform; an empty field clears it. Save is outline: the page's main action is picking a tenant.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useSaveSupportContact, useSupportContact } from '@/api/adminTenants';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { can } = usePlatformCan();

const contact = useSupportContact();
const save = useSaveSupportContact();
const state = usePanelState(contact);

const draft = ref('');
const stored = computed<string>(() => contact.data.value?.url ?? '');
const dirty = computed<boolean>(() => draft.value.trim() !== stored.value);

watch(stored, (url: string) => {
  draft.value = url;
}, { immediate: true });

function submit(): void {
  save.mutate(draft.value.trim(), {
    onSuccess: () => toast.success(t('dashboard.admin.supportContact.saved')),
    onError: (error: Error) => toast.error(errorText(error, t)),
  });
}
</script>

<template>
  <PanelCard
    data-view="support-contact"
    :title="t('dashboard.admin.supportContact.title')"
    :description="t('dashboard.admin.supportContact.description')"
    :state="state"
    :error-text="t('dashboard.admin.supportContact.loadFailed')"
    @retry="contact.refetch()"
  >
    <form class="flex gap-2" @submit.prevent="submit">
      <Input
        v-model="draft"
        data-support-url
        type="url"
        :disabled="!can('tenant:suspend')"
        :aria-label="t('dashboard.admin.supportContact.label')"
        :placeholder="t('dashboard.admin.supportContact.placeholder')"
      />
      <Button
        v-if="can('tenant:suspend')"
        type="submit"
        variant="outline"
        data-support-save
        :disabled="!dirty || save.isPending.value"
      >
        {{ t("dashboard.admin.supportContact.save") }}
      </Button>
    </form>
  </PanelCard>
</template>
