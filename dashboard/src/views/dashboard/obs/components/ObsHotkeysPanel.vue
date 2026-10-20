<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The hotkeys OBS knows, grouped by purpose. One click fires the hotkey by its name.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronDown from '~icons/nnz/chevron-down';

import { useObsHotkeys, useObsTriggerHotkey } from '@/api/obsControl';
import { useObsSceneItemPlaces } from '@/api/obsSources';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { describeObsHotkey, groupObsHotkeys } from '../obsHotkeyNames';
import { toastObsError } from '../obsToast';

const { t } = useI18n();
const props = defineProps<{ offline: boolean }>();

const { channelId } = useCurrentChannel();
const connected = computed<boolean>(() => !props.offline);
const hotkeys = useObsHotkeys(channelId, connected);
const trigger = useObsTriggerHotkey(channelId);
const loaded = usePanelState(hotkeys);
const search = ref('');

const state = computed(() => {
  if (props.offline)
    return 'empty';

  return loaded.value === 'ready' && hotkeys.data.value?.length === 0
    ? 'empty'
    : loaded.value;
});

function label(hotkeyName: string): string {
  const described = describeObsHotkey(hotkeyName);

  return described.kind === 'readable'
    ? described.text
    : t(`dashboard.obs.hotkeys.names.${described.key}`, { target: described.target ?? '' });
}

// A hotkey matches the search by its plain words or by the name OBS gave it.
const matching = computed<string[]>(() => {
  const typed = search.value.trim().toLowerCase();

  return (hotkeys.data.value ?? []).filter(
    (name) => typed === '' || `${label(name)} ${name}`.toLowerCase().includes(typed),
  );
});

const groups = computed(() => groupObsHotkeys(matching.value));
const shown = computed(() => groups.value.filter((entry) => entry.group !== 'unresolved'));
const unresolved = computed(() => groups.value.find((entry) => entry.group === 'unresolved')?.names ?? []);
const sceneItems = useObsSceneItemPlaces(channelId, connected);

type SceneHotkey = { name: string; text: string };

function isSceneItem(name: string): boolean {
  const described = describeObsHotkey(name);

  return described.kind === 'known' && (described.key === 'showSceneItem' || described.key === 'hideSceneItem');
}

// A scene-item hotkey names the item by number; it is told as its source only when exactly one scene item has that number.
function sceneItemOf(name: string): (SceneHotkey & { sceneName: string }) | null {
  const described = describeObsHotkey(name);

  if (described.kind !== 'known' || !isSceneItem(name))
    return null;

  const found = sceneItems.value.get(described.target ?? '');
  const [only] = found ?? [];

  return found?.length === 1 && only
    ? {
        name,
        sceneName: only.sceneName,
        text: t(`dashboard.obs.hotkeys.sceneItem.${described.key}`, { source: only.sourceName }),
      }
    : null;
}

const byScene = computed(() => {
  const scenes = new Map<string, SceneHotkey[]>();

  unresolved.value.forEach((name) => {
    const item = sceneItemOf(name);

    if (item) {
      const inScene = scenes.get(item.sceneName) ?? [];

      scenes.set(item.sceneName, [...inScene, { name, text: item.text }]);
    }
  });

  return [...scenes].map(([sceneName, hotkeys]) => ({ sceneName, hotkeys }));
});

// Hotkeys OBS ties to no source: ones with plain words stay as buttons; scene items it cannot place are only counted.
const other = computed(() => unresolved.value.filter((name) => sceneItemOf(name) === null));
const otherButtons = computed(() => other.value.filter((name) => !isSceneItem(name)));

function fire(hotkeyName: string): void {
  trigger.mutate(hotkeyName, { onError: toastObsError });
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.obs.hotkeys.title')"
    :description="$t('dashboard.obs.hotkeys.description')"
    :state="state"
    :empty-text="offline ? $t('dashboard.obs.hotkeys.offline') : $t('dashboard.obs.hotkeys.empty')"
    :error-text="$t('dashboard.obs.hotkeys.loadError')"
    @retry="hotkeys.refetch()"
  >
    <div class="flex flex-col gap-3 p-3">
      <Input
        v-model="search"
        type="search"
        :placeholder="$t('dashboard.obs.hotkeys.search')"
        :aria-label="$t('dashboard.obs.hotkeys.search')"
      />
      <p v-if="matching.length === 0" class="text-sm text-muted-foreground">
        {{ $t("dashboard.obs.hotkeys.noMatch") }}
      </p>
      <section
        v-for="entry in shown"
        :key="entry.group"
        :data-obs-hotkey-group="entry.group"
        class="flex flex-col gap-2"
      >
        <h3 class="text-sm font-medium">
          {{ $t(`dashboard.obs.hotkeys.groups.${entry.group}`) }}
        </h3>
        <div class="flex flex-wrap gap-2">
          <Button
            v-for="name in entry.names"
            :key="name"
            variant="outline"
            :data-obs-hotkey="name"
            @click="fire(name)"
          >
            {{ label(name) }}
          </Button>
        </div>
      </section>
      <section
        v-for="scene in byScene"
        :key="scene.sceneName"
        :data-obs-hotkey-scene="scene.sceneName"
        class="flex flex-col gap-2"
      >
        <h3 class="text-sm font-medium">
          {{ $t("dashboard.obs.hotkeys.inScene", { scene: scene.sceneName }) }}
        </h3>
        <div class="flex flex-wrap gap-2">
          <Button
            v-for="hotkey in scene.hotkeys"
            :key="hotkey.name"
            variant="outline"
            :data-obs-hotkey="hotkey.name"
            @click="fire(hotkey.name)"
          >
            {{ hotkey.text }}
          </Button>
        </div>
      </section>
      <Collapsible v-if="other.length > 0" class="flex flex-col gap-2">
        <CollapsibleTrigger
          data-obs-hotkey-other
          class="group flex items-center gap-2 rounded-md text-left text-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          <ChevronDown
            class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
            aria-hidden="true"
          />
          {{ $t("dashboard.obs.hotkeys.other", { count: other.length }) }}
        </CollapsibleTrigger>
        <CollapsibleContent class="flex flex-col gap-2">
          <p class="text-sm text-muted-foreground">
            {{ $t("dashboard.obs.hotkeys.otherNote") }}
          </p>
          <div class="flex flex-wrap gap-2">
            <Button
              v-for="name in otherButtons"
              :key="name"
              variant="outline"
              :data-obs-hotkey-unresolved="name"
              @click="fire(name)"
            >
              {{ label(name) }}
            </Button>
          </div>
        </CollapsibleContent>
      </Collapsible>
    </div>
  </PanelCard>
</template>
