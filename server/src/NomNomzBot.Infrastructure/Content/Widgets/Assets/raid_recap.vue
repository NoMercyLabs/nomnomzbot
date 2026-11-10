<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, watch, nextTick, onMounted, onUnmounted } from 'vue';

// The stream recap, shown as the ending scene. The `raid_recap.show` hub event names the stream; the widget
// reads that stream's numbers through nnz.data.streamStats (the one stream-stats family), so it never counts
// anything itself and its numbers cannot drift from the API. When the read fails it shows the event payload.
// Every visible word comes from the `labels` setting (translated in the dashboard). A key the installed settings lack
// falls back to DEFAULT_LABELS, the English words of the catalogue (a spec test keeps the two lists equal).
//
// Two layouts share the data: `credits` (the default: a header and a rolling list, like an end screen) and `grid`
// (cards; Stoney's own look). The file reads top to bottom: settings, data, sections (credits), cards (grid), styles.

// ── Settings: every look knob is a setting; its default is in the catalogue and the settings schema ──────────────

type Layout = 'credits' | 'grid';

const LAYOUTS = ['credits', 'grid'] as const satisfies readonly Layout[];
const ROLL_SECONDS_MIN = 10;
const ROLL_SECONDS_MAX = 300;
// The neutral hue when hueByDay is off: the hue of the dashboard's brand color (--primary, tokens.css), about 262 in hsl.
const NEUTRAL_HUE = 262;
const REDUCED_MOTION = '(prefers-reduced-motion: reduce)';

const DASH = '—';

const DEFAULT_LABELS: Record<string, string> = {
  title: 'Stream recap',
  'kpi.duration': 'Time live',
  'kpi.peak': 'Peak viewers',
  'kpi.messages': 'Chat messages',
  'kpi.commands': 'Commands run',
  'block.subs': 'Subscriptions',
  'block.raids': 'Raids in',
  'block.chatters': 'Top chatters',
  'block.songs': 'Song requests',
  'block.bits': 'Bits',
  'block.followers': 'New followers',
  'block.redemptions': 'Redemptions',
  'block.topCommands': 'Top commands',
  'subs.gifted': 'Gifted',
  'subs.own': 'Subscribed',
  'raids.unit.one': 'raid',
  'raids.unit.other': 'raids',
  'viewers.unit.one': 'viewer',
  'viewers.unit.other': 'viewers',
  'chatters.unit.one': 'chatter',
  'chatters.unit.other': 'chatters',
  'songs.unit.one': 'request',
  'songs.unit.other': 'requests',
  'redemptions.unit': 'redeemed',
  thanks: 'Thanks for watching! You spent {duration} with us.',
  'hours.unit.one': 'hour',
  'hours.unit.other': 'hours',
  hypeTrain: 'Hype train reached level {level}.',
};

// Every metric the recap can show; the `metrics` setting picks a subset.
const ALL_METRICS = [
  'duration',
  'peakViewers',
  'followers',
  'newFollowers',
  'subs',
  'gifters',
  'selfSubs',
  'bits',
  'raidsIn',
  'redemptions',
  'songRequesters',
  'chat',
  'topChatters',
  'commandsRun',
] as const satisfies readonly NnzStreamMetricKey[];

const cfg = reactive({
  layout: 'credits' as Layout,
  holdSeconds: 60,
  hueByDay: false,
  accentColor: '',
  timeZone: '',
  rollSeconds: 40,
  roll: true,
  showSubs: true,
  showRaids: true,
  showChatters: true,
  showSongs: true,
  showBits: true,
  showFollowers: true,
  showRedemptions: true,
  showTopCommands: true,
  metrics: [...ALL_METRICS] as NnzStreamMetricKey[],
  labels: {} as Record<string, string>,
});

// An empty or unknown zone means the viewer's own browser time zone.
function knownZone(value: string): string {
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: value });
    return value;
  } catch {
    return '';
  }
}

function applySettings(s: Record<string, unknown>): void {
  if (isFinite(Number(s.holdSeconds)) && Number(s.holdSeconds) > 0) cfg.holdSeconds = Number(s.holdSeconds);
  if (LAYOUTS.some((layout) => layout === s.layout)) cfg.layout = s.layout as Layout;
  if (typeof s.hueByDay === 'boolean') cfg.hueByDay = s.hueByDay;
  if (typeof s.roll === 'boolean') cfg.roll = s.roll;
  if (typeof s.accentColor === 'string') cfg.accentColor = s.accentColor;
  if (typeof s.timeZone === 'string') cfg.timeZone = knownZone(s.timeZone);
  if (typeof s.rollSeconds === 'number' && isFinite(s.rollSeconds)) {
    cfg.rollSeconds = Math.min(ROLL_SECONDS_MAX, Math.max(ROLL_SECONDS_MIN, s.rollSeconds));
  }
  const flags = ['showSubs', 'showRaids', 'showChatters', 'showSongs', 'showBits', 'showFollowers', 'showRedemptions', 'showTopCommands'] as const;
  flags.forEach((flag) => {
    const value = s[flag];

    if (typeof value === 'boolean') cfg[flag] = value;
  });
  if (Array.isArray(s.metrics)) {
    const known: readonly string[] = ALL_METRICS;
    const picked = s.metrics.filter((m): m is NnzStreamMetricKey => typeof m === 'string' && known.includes(m));
    if (picked.length) cfg.metrics = picked;
  }
  if (s.labels && typeof s.labels === 'object') cfg.labels = { ...(s.labels as Record<string, string>) };
}

// ── Data: the recap, the words and the formats both layouts share ────────────────────────────────────────────────

interface NamedCount { name: string; count: number }
interface Ranked { total?: number | null; top?: NamedCount[] | null }
interface RecapDto {
  stream?: {
    id?: string | null;
    startedAt?: string | null;
    endedAt?: string | null;
    title?: string | null;
    game?: string | null;
  } | null;
  channel?: { name?: string | null; avatarUrl?: string | null } | null;
  duration?: number | null;
  peakViewers?: number | null;
  followers?: number | null;
  newFollowers?: string[] | null;
  subs?: { total?: number | null } | null;
  gifters?: NamedCount[] | null;
  selfSubs?: string[] | null;
  bits?: Ranked | null;
  raidsIn?: { name: string; viewers: number }[] | null;
  redemptions?: Ranked | null;
  songRequesters?: Ranked | null;
  chat?: { messages?: number | null; uniqueChatters?: number | null } | null;
  topChatters?: NamedCount[] | null;
  commandsRun?: Ranked | null;
  hypeTrain?: { level: number; trains: number } | null;
}

interface Row { kind: 'row' | 'heading'; name: string; value: string; rank: number }
interface Card { key: string; big: string; unit: string; rows: Row[]; note?: string }

const recap = ref<RecapDto | null>(null);
const visible = ref(false);
const root = ref<HTMLElement | null>(null);
let hideTimer: ReturnType<typeof setTimeout> | undefined;

function label(key: string): string {
  return cfg.labels[key] || DEFAULT_LABELS[key] || '';
}

// A counted word in its singular or plural form: `<key>.one` for exactly 1, `<key>.other` otherwise.
function counted(key: string, n: number): string {
  return label(`${key}.${n === 1 ? 'one' : 'other'}`);
}

function stat(value: number | null | undefined): string {
  return typeof value === 'number' && isFinite(value) ? value.toLocaleString('en-US') : DASH;
}

function formatDuration(seconds: number | null | undefined): string {
  if (typeof seconds !== 'number' || !isFinite(seconds)) return DASH;
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  return `${hours}h ${String(minutes).padStart(2, '0')}m`;
}

const zone = computed<string | undefined>(() => cfg.timeZone || undefined);

function clock(iso: string | null | undefined, withZone: boolean): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: zone.value,
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZoneName: withZone ? 'short' : undefined,
  }).format(date);
}

const dateText = computed<string>(() => {
  const start = recap.value?.stream?.startedAt;
  if (!start) return '';
  return new Intl.DateTimeFormat('en-US', { timeZone: zone.value, dateStyle: 'medium' }).format(new Date(start));
});

// The hue of the whole recap: one fixed neutral hue, or (hueByDay) one color per day of the month, 31 steps around
// the wheel, by the day the stream started in the shown time zone.
const baseHue = computed<number>(() => {
  if (!cfg.hueByDay) return NEUTRAL_HUE;
  const start = recap.value?.stream?.startedAt;
  const date = start ? new Date(start) : null;
  if (!date || isNaN(date.getTime())) return 0;
  const day = Number(new Intl.DateTimeFormat('en-GB', { timeZone: zone.value, day: 'numeric' }).format(date));
  return ((day - 1) * 360) / 31;
});

const rootStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = { '--base': `${baseHue.value}deg` };
  if (cfg.accentColor) style['--nnz-accent'] = cfg.accentColor;
  return style;
});

const avatarUrl = computed<string>(() => recap.value?.channel?.avatarUrl || '');
const channelName = computed<string>(() => recap.value?.channel?.name || '');

// "Thanks for watching! You spent 14 hours ..." : the label carries the words, the widget fills {duration} with whole hours.
const thanks = computed<string>(() => {
  const seconds = recap.value?.duration;
  if (typeof seconds !== 'number' || !isFinite(seconds)) return '';
  const hours = Math.max(1, Math.round(seconds / 3600));
  return label('thanks').replace('{duration}', `${hours} ${counted('hours.unit', hours)}`);
});

function rowsOf(items: NamedCount[] | null | undefined, ranked: boolean): Row[] {
  return (items || []).map((item, i) => ({
    kind: 'row' as const,
    name: item.name,
    value: stat(item.count),
    rank: ranked ? i + 1 : 0,
  }));
}

function nameRows(names: string[] | null | undefined): Row[] {
  return (names || []).map((name) => ({ kind: 'row' as const, name, value: '', rank: 0 }));
}

function heading(text: string): Row[] {
  return text ? [{ kind: 'heading', name: text, value: '', rank: 0 }] : [];
}

function subsRows(r: RecapDto): Row[] {
  const gifted = rowsOf(r.gifters, false);
  const own: Row[] = nameRows(r.selfSubs);
  return [
    ...(gifted.length ? [...heading(label('subs.gifted')), ...gifted] : []),
    ...(own.length ? [...heading(label('subs.own')), ...own] : []),
  ];
}

// The hype train line: empty (so hidden) when no train ended during the stream.
function hypeLine(r: RecapDto): string {
  return r.hypeTrain ? label('hypeTrain').replace('{level}', String(r.hypeTrain.level)) : '';
}

// One card per topic, switched by the show* settings; a topic with no data is left out.
function buildCards(r: RecapDto): Card[] {
  const raids = r.raidsIn || [];
  const raiders = raids.reduce((sum, x) => sum + (Number(x.viewers) || 0), 0);
  const all: (Card & { on: boolean; has: boolean })[] = [
    {
      key: 'subs',
      big: stat(r.subs?.total),
      unit: '',
      rows: subsRows(r),
      note: hypeLine(r),
      on: cfg.showSubs,
      has: !!r.subs?.total || !!r.gifters?.length || !!r.selfSubs?.length,
    },
    {
      key: 'raids',
      big: stat(raids.length),
      unit: raids.length ? `${counted('raids.unit', raids.length)} · ${stat(raiders)} ${counted('viewers.unit', raiders)}` : '',
      rows: raids.map((x) => ({ kind: 'row', name: x.name, value: stat(x.viewers), rank: 0 })),
      on: cfg.showRaids,
      has: raids.length > 0,
    },
    { key: 'chatters', big: r.chat ? stat(r.chat.uniqueChatters) : '', unit: r.chat ? counted('chatters.unit', Number(r.chat.uniqueChatters)) : '', rows: rowsOf(r.topChatters, true), on: cfg.showChatters, has: !!r.topChatters?.length || !!r.chat?.uniqueChatters },
    { key: 'songs', big: stat(r.songRequesters?.total), unit: counted('songs.unit', Number(r.songRequesters?.total)), rows: rowsOf(r.songRequesters?.top, true), on: cfg.showSongs, has: !!r.songRequesters?.total || !!r.songRequesters?.top?.length },
    {
      key: 'bits',
      big: stat(r.bits?.total),
      unit: '',
      rows: rowsOf(r.bits?.top, false),
      on: cfg.showBits,
      has: !!r.bits?.total,
    },
    { key: 'followers', big: stat(r.followers), unit: '', rows: nameRows(r.newFollowers), on: cfg.showFollowers, has: !!r.followers },
    {
      key: 'redemptions',
      big: stat(r.redemptions?.total),
      unit: label('redemptions.unit'),
      rows: rowsOf(r.redemptions?.top, false),
      on: cfg.showRedemptions,
      has: !!r.redemptions?.total,
    },
    { key: 'topCommands', big: '', unit: '', rows: rowsOf(r.commandsRun?.top, true), on: cfg.showTopCommands, has: !!r.commandsRun?.top?.length },
  ];
  return all.filter((c) => c.on && c.has).map(({ key, big, unit, rows, note }) => ({ key, big, unit, rows, note: note || '' }));
}

const cards = computed<Card[]>(() => (recap.value ? buildCards(recap.value) : []));

// ── Sections (credits layout): one column of sections, each a heading and a list of names ────────────────────────

interface Section { key: string; heading: string; rows: Row[] }

// The order the sections roll in; a section with no rows is left out.
const SECTION_ORDER = ['chatters', 'subs', 'followers', 'raids', 'redemptions', 'songs', 'bits', 'topCommands'] as const;

const sections = computed<Section[]>(() =>
  SECTION_ORDER.flatMap((key) => {
    const card = cards.value.find((c) => c.key === key);

    return card?.rows.length ? [{ key, heading: label(`block.${key}`), rows: card.rows }] : [];
  }),
);

const hype = computed<string>(() => (recap.value && cfg.showSubs ? hypeLine(recap.value) : ''));

const liveText = computed<string>(() => `${label('kpi.duration')} ${formatDuration(recap.value?.duration)}`);

// The roll: `roll` moves the column, `still` holds the first screenful (a still image), `reduced` follows the viewer's
// prefers-reduced-motion and shows the sections as a plain list that scrolls by hand.
type Motion = 'roll' | 'still' | 'reduced';

const reducedMotion = ref(false);
let motionQuery: MediaQueryList | undefined;

function syncMotion(): void {
  reducedMotion.value = !!motionQuery?.matches;
}

const motion = computed<Motion>(() => {
  if (reducedMotion.value) return 'reduced';

  return cfg.roll ? 'roll' : 'still';
});

// The loop is the same column twice, and the roll moves up by one column.
const copies = computed<number[]>(() => (motion.value === 'roll' ? [0, 1] : [0]));

const rollStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = {};
  if (motion.value === 'roll') style['--roll-seconds'] = `${cfg.rollSeconds}s`;
  return style;
});

// ── Cards (grid layout): Stoney's own look, kept as it was ───────────────────────────────────────────────────────

// How many rows of each card fit whole; a card not measured yet shows all of its rows.
const shown = ref<Record<string, number>>({});

const timeRange = computed<string>(() => {
  const s = recap.value?.stream;
  const from = clock(s?.startedAt, false);
  const to = clock(s?.endedAt, true);
  return from && to ? `${from} to ${to}` : '';
});

const subtitle = computed<string>(() => {
  const s = recap.value?.stream;
  return [channelName.value, s?.game, dateText.value].filter(Boolean).join(' · ');
});

const kpis = computed(() => {
  const r = recap.value;
  return [
    { key: 'duration', value: formatDuration(r?.duration), note: timeRange.value, lead: true },
    { key: 'peak', value: stat(r?.peakViewers), note: '', lead: false },
    { key: 'messages', value: stat(r?.chat?.messages), note: '', lead: false },
    { key: 'commands', value: stat(r?.commandsRun?.total), note: '', lead: false },
  ];
});

// One grid for every block: 12 columns, so any row of 1 to 4 blocks adds up to the full width and every card edge
// lines up with the top row. Blocks without data are gone. Up to 3 blocks share one row, 4 are 2 over 2, 5 are 3 over 2,
// 6 are 3 over 3, 7 are 4 over 3, 8 are 4 over 4, and 9 to 12 are three rows (9 are 3/3/3, 12 are 4/4/4). The channel
// avatar is a faint watermark inside whichever data card sits bottom left, never a card of its own (Stoney, 2026-10-21).
interface Placed { card: Card; span: number; tall: boolean; watermark: boolean }

const GRID_COLUMNS = 12;
const MAX_CARDS = 12;

function rowSizes(count: number): number[] {
  if (count <= 3) return count ? [count] : [];

  const rows = count <= 8 ? 2 : 3;
  const base = Math.floor(count / rows);
  const extra = count % rows;

  return Array.from({ length: rows }, (_, i) => base + (i < extra ? 1 : 0));
}

function flow(list: Card[], width: number): Placed[] {
  const spans = rowSizes(list.length).flatMap((size) => Array.from({ length: size }, () => width / size));

  const lastRowStart = rowSizes(list.length).slice(0, -1).reduce((sum, size) => sum + size, 0);

  return list.map((card, i) => ({ card, span: spans[i] ?? width, tall: false, watermark: i === lastRowStart }));
}

function place(list: Card[]): Placed[] {
  return flow(list.slice(0, MAX_CARDS), GRID_COLUMNS);
}

const placed = computed<Placed[]>(() => place(cards.value));

const gridStyle = computed<Record<string, string>>(() => {
  const rows = rowSizes(placed.value.length).length;

  return { gridTemplateRows: `repeat(${rows}, minmax(0, 1fr))` };
});

function cellStyle(p: Placed): Record<string, string> {
  const style: Record<string, string> = { gridColumn: `span ${p.span}` };
  if (p.tall) style.gridRow = 'span 2';
  return style;
}

// A list shows only whole rows. Every row is drawn once, the rows whose bottom edge passes the card's edge are cut from
// the list, and a heading left at the end of a list goes with them.
function fitRows(box: HTMLElement): number {
  const edge = box.getBoundingClientRect().bottom;
  const items = Array.from(box.querySelectorAll<HTMLElement>('.list > *'));
  let count = items.filter((el) => el.getBoundingClientRect().bottom <= edge + 0.5).length;

  while (count > 0 && items[count - 1]?.classList.contains('sect')) count--;

  return count;
}

function measure(): void {
  if (!root.value) return;
  const next: Record<string, number> = {};
  root.value.querySelectorAll<HTMLElement>('[data-block] .scroll').forEach((box) => {
    const key = box.closest('[data-block]')?.getAttribute('data-block') || '';
    next[key] = fitRows(box);
  });
  shown.value = next;
}

async function refit(): Promise<void> {
  shown.value = {};
  await nextTick();
  measure();
}

watch(recap, refit);

// ── Show: read the numbers, show the recap, hide it after holdSeconds ────────────────────────────────────────────

async function read(shown: RecapDto): Promise<RecapDto> {
  const stream = shown.stream?.id || undefined;
  try {
    return (await NomNomz.data.streamStats({ stream, metrics: [...cfg.metrics, 'channel', 'hypeTrain'] })) as RecapDto;
  } catch {
    return shown;
  }
}

async function onShow(data: RecapDto | null | undefined): Promise<void> {
  if (!data) return;
  recap.value = await read(data);
  visible.value = true;
  clearTimeout(hideTimer);
  hideTimer = setTimeout(() => {
    visible.value = false;
  }, Math.max(1, cfg.holdSeconds) * 1000);
}

onMounted(() => {
  if (typeof matchMedia === 'function') {
    motionQuery = matchMedia(REDUCED_MOTION);
    syncMotion();
    motionQuery.addEventListener('change', syncMotion);
  }
  if (typeof NomNomz === 'undefined') return;
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (s && typeof s === 'object') applySettings({ ...s });
  });
  // The recap event is not in the typed event map yet; the payload is the bot's StreamRecapDto.
  NomNomz.on('raid_recap.show' as keyof NnzWidgetEventMap, onShow as never);
  window.addEventListener('resize', refit);
});

onUnmounted(() => {
  clearTimeout(hideTimer);
  motionQuery?.removeEventListener('change', syncMotion);
  window.removeEventListener('resize', refit);
  if (typeof NomNomz === 'undefined') return;
  NomNomz.off('raid_recap.show' as keyof NnzWidgetEventMap, onShow as never);
});
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); hueByDay only changes the accent. -->
  <div v-if="recap" ref="root" class="nnz-widget recap" :data-visible="visible ? 'true' : 'false'" :data-layout="cfg.layout" :data-motion="motion" :data-hue="cfg.hueByDay ? 'day' : undefined" :style="rootStyle">
    <div v-if="cfg.layout === 'credits'" class="credits">
      <header class="nnz-card top">
        <img v-if="avatarUrl" class="nnz-avatar avatar" :src="avatarUrl" crossorigin="anonymous" alt="" />
        <div class="who">
          <h1 class="nnz-title title">{{ label('title') }}</h1>
          <div v-if="channelName" class="channel">{{ channelName }}</div>
          <div class="meta">
            <span class="date">{{ dateText }}</span>
            <span class="nnz-accent-text live">{{ liveText }}</span>
          </div>
        </div>
      </header>

      <div class="stage">
        <div class="roll" :style="rollStyle">
          <div v-for="copy in copies" :key="copy" class="track" :aria-hidden="copy > 0 ? 'true' : undefined">
            <section v-for="s in sections" :key="s.key" class="credit" :data-section="s.key">
              <h2 class="nnz-chip">{{ s.heading }}</h2>
              <template v-for="(r, ri) in s.rows" :key="ri">
                <h3 v-if="r.kind === 'heading'" class="group">{{ r.name }}</h3>
                <div v-else class="nnz-row entry">
                  <span class="n">{{ r.name }}</span>
                  <span v-if="r.value" class="v">{{ r.value }}</span>
                </div>
              </template>
            </section>
            <p v-if="hype" class="hype">{{ hype }}</p>
            <p v-if="thanks" class="thanks">{{ thanks }}</p>
          </div>
        </div>
      </div>
    </div>

    <div v-else class="wrap">
      <header>
        <div class="nnz-card brand">
          <svg class="logo" viewBox="0 0 130 130" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
            <path fill-rule="evenodd" clip-rule="evenodd" d="M49.9746 104.882C45.1151 108.166 42.6854 109.807 40.6684 109.696C38.9115 109.598 37.2859 108.735 36.2217 107.334C35 105.725 35 102.792 35 96.9276V33.0725C35 27.2077 35 24.2753 36.2217 22.6666C37.2859 21.2653 38.9115 20.4018 40.6684 20.3045C42.6854 20.1929 45.1151 21.8346 49.9747 25.1181L97.2274 57.0456C101.17 59.7098 103.142 61.0419 103.831 62.7154C104.434 64.1789 104.434 65.8212 103.831 67.2847C103.142 68.9583 101.17 70.2904 97.2274 72.9545L49.9746 104.882ZM126 21.0001C128.209 21.0001 130 22.7909 130 25.0001V105C130 107.209 128.209 109 126 109H116.5C114.291 109 112.5 107.209 112.5 105V25.0001C112.5 22.7909 114.291 21.0001 116.5 21.0001H126Z" style="fill: var(--nnz-accent)" />
            <path fill-rule="evenodd" clip-rule="evenodd" d="M9.35914 108.676C5.3744 111.369 0 108.514 0 103.705V26.2953C0 21.4863 5.37442 18.6314 9.35916 21.3238L66.6421 60.0285C70.1633 62.4077 70.1633 67.5925 66.6421 69.9716L9.35914 108.676ZM91 21.0001C93.2091 21.0001 95 22.7909 95 25.0001V105C95 107.209 93.2091 109 91 109H81.5C79.2909 109 77.5 107.209 77.5 105V25.0001C77.5 22.7909 79.2909 21.0001 81.5 21.0001H91Z" style="fill: var(--nnz-text)" />
          </svg>
          <div>
            <h1 class="nnz-title">{{ label('title') }}</h1>
            <div class="nnz-sub sub">{{ subtitle }}</div>
          </div>
        </div>
        <div v-if="thanks" class="thanks">{{ thanks }}</div>
      </header>

      <section class="kpis">
        <div v-for="k in kpis" :key="k.key" class="nnz-card card kpi" :class="{ lead: k.lead }" :data-block="k.key">
          <div class="nnz-chip label">{{ label('kpi.' + k.key) }}</div>
          <div class="nnz-title num">{{ k.value }}</div>
          <div v-if="k.note" class="note">{{ k.note }}</div>
        </div>
      </section>

      <section class="grid" :style="gridStyle">
        <div v-for="p in placed" :key="p.card.key" class="nnz-card card block" :class="{ sparse: p.card.rows.length <= 3, bare: p.card.rows.length === 0 }" :data-block="p.card.key" :style="cellStyle(p)">
          <img v-if="p.watermark && avatarUrl" class="watermark" :src="avatarUrl" crossorigin="anonymous" alt="" />
          <div class="nnz-chip label">{{ label('block.' + p.card.key) }}</div>
          <div v-if="p.card.big" class="nnz-title big">{{ p.card.big }}<small v-if="p.card.unit">{{ p.card.unit }}</small></div>
          <div v-if="p.card.rows.length" class="scroll" :class="{ fit: p.card.note }">
            <div class="list">
              <template v-for="(r, ri) in p.card.rows.slice(0, shown[p.card.key] ?? p.card.rows.length)" :key="ri">
                <div v-if="r.kind === 'heading'" class="sect">{{ r.name }}</div>
                <div v-else class="nnz-row row">
                  <span v-if="r.rank" class="nnz-num r">{{ r.rank }}</span>
                  <span class="n">{{ r.name }}</span>
                  <span class="v">{{ r.value }}</span>
                </div>
              </template>
            </div>
          </div>
          <div v-if="p.card.note" class="hype">{{ p.card.note }}</div>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
/* Shared: the full-screen theme surface, the accent (hueByDay swaps only the accent) and the fade in. */
.recap {
  --base: 0deg;
  position: fixed;
  inset: 0;
  color: var(--nnz-text);
  background: color-mix(in srgb, var(--nnz-surface) 90%, var(--nnz-accent));
  opacity: 0;
  transition: opacity 0.8s ease;
}
.recap[data-hue='day'] { --nnz-accent: hsl(var(--base) 100% 45%); } /* theme-exempt: the accent computed from the stream date (hueByDay) */
[data-nnz-theme='ink'] .recap[data-hue='day'] { --nnz-accent: hsl(var(--base) 100% 62%); } /* theme-exempt: the stream-date accent, lighter on the black surface */
/* The theme tokens that read the accent are set again here, so a day hue or the streamer's accentColor reaches them. */
:root:not([data-nnz-theme]) .recap,
[data-nnz-theme='sticker'] .recap { --nnz-shadow: 6px 6px 0 var(--nnz-accent), 6px 6px 0 4px var(--nnz-line); --nnz-num-text: var(--nnz-accent); }
[data-nnz-theme='ink'] .recap { --nnz-num-bg: var(--nnz-accent); }
.recap .nnz-card { zoom: 1; }
.recap[data-visible='true'] { opacity: 1; }
.hype { font-weight: 500; color: var(--nnz-muted); }

/* Credits layout: the look knobs are the variables below; the type sizes follow the 1920 x 1080 scene. */
.recap[data-layout='credits'] {
  --page-pad: 64px;
  --column: 900px;
  --gap: 56px;
  --fade: 120px;
  --avatar: 128px;
  --title-size: 64px;
  --name-size: 38px;
  --heading-size: 22px;
}
.credits { position: absolute; inset: var(--page-pad); display: grid; grid-template-rows: auto minmax(0, 1fr); gap: 32px; }
.top { justify-content: flex-start; align-items: center; gap: 32px; padding: 24px 32px; justify-self: start; }
.avatar { width: var(--avatar); height: var(--avatar); }
.title { font-size: var(--title-size); }
.channel { margin-top: 10px; font-size: 30px; font-weight: 700; }
.meta { margin-top: 8px; display: flex; gap: 24px; font-size: 24px; font-weight: 600; color: var(--nnz-muted); }
.meta .live { font-weight: 800; }
.stage { position: relative; overflow: hidden; container-type: size; mask-image: linear-gradient(to bottom, transparent 0, #000 var(--fade), #000 calc(100% - var(--fade)), transparent 100%); } /* theme-exempt: a fade mask, not a visible color */
.roll { display: flex; flex-direction: column; align-items: center; }
.track { width: min(100%, var(--column)); min-height: 100cqh; padding-bottom: var(--gap); display: flex; flex-direction: column; align-items: stretch; }
.credit { text-align: center; margin-bottom: var(--gap); }
.credit h2 { margin: 0 0 20px; padding: 8px 16px; font-size: var(--heading-size); letter-spacing: 3px; text-transform: uppercase; }
.group { margin: 20px 0 8px; font-size: 18px; font-weight: 800; letter-spacing: 2px; text-transform: uppercase; color: var(--nnz-muted); }
.entry { justify-content: center; align-items: baseline; gap: 16px; padding: 2px 20px; font-size: var(--name-size); font-weight: 700; line-height: 1.5; }
.entry .n { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.entry .v { font-weight: 600; font-variant-numeric: tabular-nums; color: var(--nnz-muted); }
.credits .hype { text-align: center; font-size: 26px; margin: 0 0 var(--gap); }
.credits .thanks { text-align: center; font-size: 34px; font-weight: 700; margin: 0; }
.recap[data-motion='roll'] .roll { animation: roll var(--roll-seconds) linear infinite; will-change: transform; }
.recap[data-motion='still'] .stage { mask-image: linear-gradient(to bottom, #000 calc(100% - var(--fade)), transparent 100%); } /* theme-exempt: a fade mask, not a visible color */
.recap[data-motion='reduced'] .stage { overflow-y: auto; mask-image: none; scrollbar-width: none; }
.stage::-webkit-scrollbar { display: none; }
@keyframes roll { from { transform: translateY(0); } to { transform: translateY(-50%); } }

/* Grid layout (Stoney's own look): theme cards in a 12-column grid. */
.wrap { position: absolute; inset: 48px 64px; display: grid; grid-template-columns: minmax(0, 1fr); grid-template-rows: auto auto 1fr; gap: 28px; }
header { display: flex; align-items: flex-end; justify-content: space-between; }
.brand { display: flex; align-items: center; gap: 24px; padding: 20px 28px; }
.logo { width: 110px; height: 110px; flex: none; transform: skewX(-1deg); }
h1 { font-size: 64px; letter-spacing: -1px; line-height: 1; margin: 0; }
.sub { margin-top: 10px; font-size: 26px; }
.kpis { display: grid; grid-template-columns: repeat(4, 1fr); gap: 24px; }
.grid { display: grid; grid-template-columns: repeat(12, minmax(0, 1fr)); gap: 24px; min-height: 0; padding: 0 10px 10px 0; }
.block { position: relative; }
.block > :not(.watermark) { position: relative; }
.watermark { position: absolute; left: -8%; bottom: -12%; height: 90%; aspect-ratio: 1; border-radius: 50%; object-fit: cover; filter: grayscale(1); opacity: 0.12; pointer-events: none; }
.card {
  padding: 20px 24px;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.label { align-self: flex-start; font-size: 17px; letter-spacing: 1.4px; text-transform: uppercase; margin-bottom: 12px; }
.kpi .num { font-size: 60px; line-height: 1; font-variant-numeric: tabular-nums; }
.kpi.lead .num { font-size: 76px; color: var(--nnz-accent); }
.kpi .note { margin-top: 8px; font-size: 18px; color: var(--nnz-muted); font-weight: 600; }
.big { font-size: 44px; line-height: 1; margin-bottom: 12px; font-variant-numeric: tabular-nums; }
.big small { font-size: 20px; font-weight: 600; color: var(--nnz-muted); margin-left: 8px; }
.list { display: grid; gap: 6px; padding-bottom: 6px; }
.list .nnz-row + .nnz-row { margin-top: 0; }
.row { font-size: 20px; font-weight: 700; padding: 6px 12px; }
.row .n { flex: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; line-height: 1.35; }
.row .v { font-weight: 800; font-variant-numeric: tabular-nums; }
.row .r { min-width: 28px; height: 28px; font-size: 15px; }
.sect { font-size: 15px; font-weight: 800; letter-spacing: 1.4px; text-transform: uppercase; color: var(--nnz-muted); margin: 14px 0 8px; }
.scroll { flex: 1; min-height: 0; overflow: hidden; }
.scroll.fit { flex: 0 1 auto; }
.wrap .hype { margin-top: 12px; font-size: 19px; }
.wrap .thanks { max-width: 760px; text-align: right; font-size: 26px; font-weight: 600; color: inherit; }
/* A card with three rows or fewer would sit mostly empty at the list size, so its type grows to fill it. */
.sparse .big { font-size: 64px; }
.sparse .row { font-size: 28px; padding: 10px 14px; }
.sparse .sect { font-size: 17px; }
.bare .big { font-size: 112px; margin-bottom: 0; }
</style>
