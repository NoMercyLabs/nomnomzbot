<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The one Act as dialog: the reason, how long, and the person (the channel owner first). One confirm opens the
// support session for the channel and acts as the person in it. Opened without a channel, it asks for one first.
// The parent mounts it only while it is open.
import { refDebounced } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdminTenants, type AdminTenant, type AdminTenantsParams } from '@/api/admin';
import { useTenantMembers, type TenantMember } from '@/api/adminTenants';
import FormDialog from '@/components/layout/FormDialog.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useActAs } from '@/composables/useActAs';
import { errorText } from '@/lib/errors';

export type ActAsChannel = { id: string; name: string };

type Length = '30' | '60' | '120' | '240';

const props = defineProps<{ channel: ActAsChannel | null }>();
const emit = defineEmits<{ close: [] }>();

const { t } = useI18n();
const actAs = useActAs();

const lengths = ['30', '60', '120', '240'] as const satisfies readonly Length[];

const reason = ref('');
const length = ref<Length>('60');
const picked = ref<ActAsChannel | null>(props.channel);
const channelSearch = ref('');
const personSearch = ref('');
const person = ref<TenantMember | null>(null);
const failure = ref('');

const channelTyped = refDebounced(channelSearch, 300);
const personTyped = refDebounced(personSearch, 300);

const channelParams = computed<AdminTenantsParams>(() => ({
  search: channelTyped.value.trim(),
  page: 1,
  take: 50,
}));

const tenants = useAdminTenants(channelParams);
const channelOptions = computed<AdminTenant[]>(() => tenants.data.value?.data ?? []);
const channelId = computed<string | null>(() => picked.value?.id ?? null);
const members = useTenantMembers(channelId, personTyped);
const people = computed<TenantMember[]>(() => members.data.value?.data ?? []);

const lengthOptions = computed(() =>
  lengths.map((value) => ({ value, label: t(`dashboard.admin.actAs.lengths.${value}`) })));

// The chosen person stays chosen while a search shows other people.
const personId = computed<string>({
  get: () => person.value?.userId ?? '',
  set: (id: string) => {
    person.value = people.value.find((row) => row.userId === id) ?? person.value;
  },
});

const title = computed<string>(() =>
  picked.value === null
    ? t('dashboard.admin.actAs.titleAny')
    : t('dashboard.admin.actAs.title', { channel: picked.value.name }));

const ready = computed<boolean>(
  () => channelId.value !== null && personId.value !== '' && reason.value.trim() !== '',
);

// The owner is the person to act as unless the admin picks another. The members may already be cached.
watch(
  people,
  (rows: TenantMember[]) => {
    if (person.value !== null) return;

    person.value = rows.find((row) => row.relation === 'owner') ?? rows[0] ?? null;
  },
  { immediate: true },
);

function pickChannel(row: AdminTenant): void {
  picked.value = { id: row.id, name: row.name };
  person.value = null;
}

function relationLabel(row: TenantMember): string {
  return t(`dashboard.admin.actAs.relation.${row.relation}`);
}

async function submit(): Promise<void> {
  if (!ready.value) return;

  failure.value = '';

  try {
    await actAs.start({
      broadcasterId: channelId.value!,
      userId: personId.value,
      justification: reason.value.trim(),
      minutes: Number(length.value),
    });

    emit('close');
  } catch (error) {
    failure.value = errorText(error, t);
  }
}
</script>

<template>
  <FormDialog
    :open="true"
    :title="title"
    :description="t('dashboard.admin.actAs.description')"
    :action="t('dashboard.admin.actAs.action')"
    :pending="actAs.pending.value"
    :disabled="!ready"
    :error="failure || undefined"
    @update:open="emit('close')"
    @submit="submit"
  >
    <div v-if="channel === null" class="grid gap-1.5">
      <Label for="act-as-channel">{{ t('dashboard.admin.actAs.channel') }}</Label>
      <Input
        id="act-as-channel"
        v-model="channelSearch"
        data-act-as-channel-search
        autocomplete="off"
        :placeholder="t('dashboard.admin.actAs.channelSearch')"
      />
      <ul
        v-if="channelOptions.length > 0"
        class="max-h-40 overflow-auto rounded-lg border border-border p-1"
      >
        <li v-for="row in channelOptions" :key="row.id">
          <button
            type="button"
            data-act-as-channel-option
            :aria-pressed="row.id === channelId"
            class="flex min-h-9 w-full items-center rounded-md px-2 text-left text-sm outline-none hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring aria-pressed:bg-accent aria-pressed:font-medium"
            @click="pickChannel(row)"
          >
            {{ row.name }}
          </button>
        </li>
      </ul>
    </div>
    <div class="grid gap-1.5">
      <Label for="act-as-reason">{{ t('dashboard.admin.actAs.reason') }}</Label>
      <Input
        id="act-as-reason"
        v-model="reason"
        data-act-as-reason
        required
        :placeholder="t('dashboard.admin.actAs.reasonHint')"
      />
    </div>
    <div class="grid gap-1.5">
      <span class="text-sm font-medium">{{ t('dashboard.admin.actAs.length') }}</span>
      <SegmentedControl
        v-model="length"
        :label="t('dashboard.admin.actAs.length')"
        :options="lengthOptions"
      />
    </div>
    <div v-if="channelId !== null" class="grid gap-1.5">
      <Label for="act-as-person">{{ t('dashboard.admin.actAs.person') }}</Label>
      <Input
        v-model="personSearch"
        data-act-as-person-search
        autocomplete="off"
        :aria-label="t('dashboard.admin.actAs.personSearch')"
        :placeholder="t('dashboard.admin.actAs.personSearch')"
      />
      <Select v-model="personId">
        <SelectTrigger id="act-as-person" data-act-as-person class="w-full">
          <SelectValue :placeholder="t('dashboard.admin.actAs.pickPerson')">
            {{ person?.displayName }}
          </SelectValue>
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="row in people" :key="row.userId" :value="row.userId">
            {{ row.displayName }}
            <span class="text-muted-foreground">{{ relationLabel(row) }}</span>
          </SelectItem>
        </SelectContent>
      </Select>
      <p v-if="members.isError.value" role="alert" class="text-sm text-destructive">
        {{ errorText(members.error.value, t) }}
      </p>
    </div>
  </FormDialog>
</template>
