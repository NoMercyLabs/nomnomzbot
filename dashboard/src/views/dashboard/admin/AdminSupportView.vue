<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The support desk: find a person on any tenant, then read their real state, history and the channel's own
// commands and pipelines. Every read carries a reason, which the bot keeps in the audit log, so nothing loads
// until the reason is given. Hosting tenants for third parties is the restricted saas mode, so the page says so.
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useSupportPeople, type SupportPerson } from '@/api/adminSupport';
import { ApiError } from '@/api/errors';
import DetailPane from '@/components/layout/DetailPane.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';

import SupportActivityPanels from './components/SupportActivityPanels.vue';
import SupportPersonPanels from './components/SupportPersonPanels.vue';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const reason = ref('');
const search = ref('');
const justification = refDebounced(reason, 300);
const typed = refDebounced(search, 300);

const people = useSupportPeople(typed, justification);
const rows = computed<SupportPerson[]>(() => people.data.value?.data ?? []);
const ready = computed(() => justification.value.trim() !== '');

const queryState = usePanelState(people, (page) =>
  (page.data?.length ?? 0) === 0);
const listState = computed<PanelState>(() =>
  !ready.value || typed.value.trim() === ''
    ? 'empty'
    : queryState.value,
);

const emptyText = computed<string>(() =>
  ready.value
    ? t('dashboard.admin.support.list.empty')
    : t('dashboard.admin.support.needReason'),
);
const listError = computed<string>(() =>
  people.error.value instanceof ApiError && people.error.value.status === 403
    ? t('dashboard.admin.support.forbidden')
    : t('dashboard.admin.support.list.error'),
);

const selectedId = computed<string | null>(() =>
  typeof route.query.person === 'string'
    ? route.query.person
    : null,
);
const selected = computed(() => rows.value.find((row) => row.userId === selectedId.value));

function pick(row: SupportPerson): void {
  void router.push({ query: { ...route.query, person: row.userId } });
}
</script>

<template>
  <PageHeader :title="t('dashboard.admin.support.title')" :crumb="t('dashboard.admin.crumb')" />
  <ListDetail :show-detail="selectedId !== null">
    <template #list>
      <div class="flex min-h-0 flex-1 flex-col gap-3">
        <Input
          v-model="reason"
          data-justification
          :aria-label="t('dashboard.admin.support.reason')"
          :placeholder="t('dashboard.admin.support.reason')"
        />
        <PanelCard
          tier="lead"
          :title="t('dashboard.admin.support.list.title')"
          :state="listState"
          :empty-text="emptyText"
          :error-text="listError"
          @retry="people.refetch()"
        >
          <template #actions>
            <SearchField
              v-model="search"
              class="-mr-2 w-40 rounded-[12px]"
              :placeholder="t('dashboard.admin.support.search')"
            />
          </template>
          <ul class="grid divide-y divide-border text-sm">
            <li v-for="row in rows" :key="row.userId" data-person-row>
              <button
                type="button"
                class="flex w-full min-w-0 flex-col px-2 py-2 text-left hover:bg-accent"
                :class="row.userId === selectedId ? 'bg-accent' : ''"
                @click="pick(row)"
              >
                <span class="truncate font-medium">{{ row.displayName }}</span>
                <span class="truncate text-muted-foreground">
                  {{
                    t("dashboard.admin.support.list.meta", {
                      username: row.username,
                      count: Number(row.tenantCount),
                    })
                  }}
                </span>
              </button>
            </li>
          </ul>
        </PanelCard>
      </div>
    </template>
    <template #detail>
      <PanelCard
        v-if="selectedId === null || !ready"
        :title="t('dashboard.admin.support.title')"
        state="empty"
        :empty-text="
          ready ? t('dashboard.admin.support.pick') : t('dashboard.admin.support.needReason')
        "
      />
      <DetailPane v-else>
        <template #header>
          <h2 class="text-lg font-semibold">{{ selected?.displayName }}</h2>
        </template>
        <SupportPersonPanels :person-id="selectedId" :justification="justification" />
        <SupportActivityPanels :person-id="selectedId" :justification="justification" />
      </DetailPane>
    </template>
  </ListDetail>
  <p data-saas-restricted class="px-6 pb-4 text-sm text-muted-foreground">
    {{ t("dashboard.admin.support.restricted") }}
  </p>
</template>
