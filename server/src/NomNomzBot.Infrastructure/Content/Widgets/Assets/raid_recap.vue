<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, watch, nextTick, onMounted, onUnmounted } from 'vue';

// The stream recap, shown as the ending scene. The `raid_recap.show` hub event names the stream; the widget
// reads that stream's numbers through nnz.data.streamStats (the one stream-stats family), so it never counts
// anything itself and its numbers cannot drift from the API. When the read fails it shows the event payload.
// Every visible word comes from the `labels` setting (translated in the dashboard). A key the installed settings lack
// falls back to DEFAULT_LABELS, the English words of the catalogue (a spec test keeps the two lists equal).

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

// Every metric the recap can show; the `metrics` setting picks a subset.
const ALL_METRICS = [
  'duration',
  'peakViewers',
  'followers',
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

interface Row { kind: 'row' | 'heading'; name: string; value: string; rank: number }
interface Card { key: string; big: string; unit: string; rows: Row[]; note?: string }

const DASH = '—';
const TIME_ZONE = 'Europe/Amsterdam';

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
  thanks: 'Thank you for {duration} of coding, tunes and vibes.',
  'hours.unit.one': 'hour',
  'hours.unit.other': 'hours',
  hypeTrain: 'Hype train reached level {level}.',
};

const cfg = reactive({
  holdSeconds: 20,
  accentColor: '',
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

const recap = ref<RecapDto | null>(null);
const visible = ref(false);
// How many rows of each card fit whole; a card not measured yet shows all of its rows.
const shown = ref<Record<string, number>>({});
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

function clock(iso: string | null | undefined, withZone: boolean): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat('en-GB', {
    timeZone: TIME_ZONE,
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZoneName: withZone ? 'short' : undefined,
  }).format(date);
}

// One colour per day of the month: 31 steps around the wheel, by the day the stream started (Amsterdam).
const baseHue = computed<number>(() => {
  const start = recap.value?.stream?.startedAt;
  const date = start ? new Date(start) : null;
  if (!date || isNaN(date.getTime())) return 0;
  const day = Number(new Intl.DateTimeFormat('en-GB', { timeZone: TIME_ZONE, day: 'numeric' }).format(date));
  return ((day - 1) * 360) / 31;
});

const avatarUrl = computed<string>(() => recap.value?.channel?.avatarUrl || '');

const subtitle = computed<string>(() => {
  const s = recap.value?.stream;
  const date = s?.startedAt
    ? new Intl.DateTimeFormat('en-US', { timeZone: TIME_ZONE, dateStyle: 'medium' }).format(new Date(s.startedAt))
    : '';
  return [recap.value?.channel?.name, s?.game, date].filter(Boolean).join(' · ');
});

// "Thank you for 14 hours ..." : the label carries the words, the widget fills {duration} with whole hours.
const thanks = computed<string>(() => {
  const seconds = recap.value?.duration;
  if (typeof seconds !== 'number' || !isFinite(seconds)) return '';
  const hours = Math.max(1, Math.round(seconds / 3600));
  return label('thanks').replace('{duration}', `${hours} ${counted('hours.unit', hours)}`);
});

const timeRange = computed<string>(() => {
  const s = recap.value?.stream;
  const from = clock(s?.startedAt, false);
  const to = clock(s?.endedAt, true);
  return from && to ? `${from} to ${to}` : '';
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

function rowsOf(items: NamedCount[] | null | undefined, ranked: boolean): Row[] {
  return (items || []).map((item, i) => ({
    kind: 'row' as const,
    name: item.name,
    value: stat(item.count),
    rank: ranked ? i + 1 : 0,
  }));
}

function heading(text: string): Row[] {
  return text ? [{ kind: 'heading', name: text, value: '', rank: 0 }] : [];
}

function subsRows(r: RecapDto): Row[] {
  const gifted = rowsOf(r.gifters, false);
  const own: Row[] = (r.selfSubs || []).map((name) => ({ kind: 'row', name, value: '', rank: 0 }));
  return [
    ...(gifted.length ? [...heading(label('subs.gifted')), ...gifted] : []),
    ...(own.length ? [...heading(label('subs.own')), ...own] : []),
  ];
}

// The hype train line: empty (so hidden) when no train ended during the stream.
function hypeLine(r: RecapDto): string {
  return r.hypeTrain ? label('hypeTrain').replace('{level}', String(r.hypeTrain.level)) : '';
}

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
    { key: 'followers', big: stat(r.followers), unit: '', rows: [], on: cfg.showFollowers, has: !!r.followers },
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

// One grid for every block: 12 columns, so any row of 1 to 4 blocks adds up to the full width and every card edge
// lines up with the top row. Blocks without data are gone. Up to 3 blocks share one row, 4 are 2 over 2, 5 are 3 over 2,
// 6 are 3 over 3, 8 are 4 over 4. With 7 blocks the heaviest one is tall (two rows) in the first column.
interface Placed { card: Card; span: number; tall: boolean }

const GRID_COLUMNS = 12;
const MAX_CARDS = 8;
const TALL_COUNT = 7;
const TALL_SPAN = 3;

function weight(card: Card): number {
  return card.rows.filter((r) => r.kind === 'row').length;
}

function rowSizes(count: number): number[] {
  if (count <= 3) return count ? [count] : [];

  const top = Math.ceil(count / 2);

  return [top, count - top];
}

function flow(list: Card[], width: number): Placed[] {
  const spans = rowSizes(list.length).flatMap((size) => Array.from({ length: size }, () => width / size));

  return list.map((card, i) => ({ card, span: spans[i] ?? width, tall: false }));
}

function heaviest(list: Card[]): Card {
  return list.reduce((best, card) => (weight(card) > weight(best) ? card : best));
}

function place(list: Card[]): Placed[] {
  const shown = list.slice(0, MAX_CARDS);

  if (shown.length !== TALL_COUNT) return flow(shown, GRID_COLUMNS);

  const tall = heaviest(shown);
  const rest = flow(shown.filter((card) => card !== tall), GRID_COLUMNS - TALL_SPAN);

  return [{ card: tall, span: TALL_SPAN, tall: true }].concat(rest);
}

const placed = computed<Placed[]>(() => place(cards.value));

const gridStyle = computed<Record<string, string>>(() => {
  const rows = placed.value.some((p) => p.tall) ? 2 : rowSizes(placed.value.length).length;

  return { gridTemplateRows: `repeat(${rows}, minmax(0, 1fr))` };
});

function cellStyle(p: Placed): Record<string, string> {
  const style: Record<string, string> = { gridColumn: `span ${p.span}` };
  if (p.tall) style.gridRow = 'span 2';
  return style;
}

const rootStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = { '--base': `${baseHue.value}deg` };
  if (cfg.accentColor) style['--accent'] = cfg.accentColor;
  return style;
});

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

function applySettings(s: Record<string, unknown>): void {
  if (isFinite(Number(s.holdSeconds)) && Number(s.holdSeconds) > 0) cfg.holdSeconds = Number(s.holdSeconds);
  if (typeof s.accentColor === 'string') cfg.accentColor = s.accentColor;
  const flags = ['showSubs', 'showRaids', 'showChatters', 'showSongs', 'showBits', 'showFollowers', 'showRedemptions', 'showTopCommands'] as const;
  flags.forEach((flag) => {
    if (typeof s[flag] === 'boolean') cfg[flag] = s[flag];
  });
  if (Array.isArray(s.metrics)) {
    const known: readonly string[] = ALL_METRICS;
    const picked = s.metrics.filter((m): m is NnzStreamMetricKey => typeof m === 'string' && known.includes(m));
    if (picked.length) cfg.metrics = picked;
  }
  if (s.labels && typeof s.labels === 'object') cfg.labels = { ...(s.labels as Record<string, string>) };
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return;
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (s && typeof s === 'object') applySettings(s as Record<string, unknown>);
  });
  // The recap event is not in the typed event map yet; the payload is the bot's StreamRecapDto.
  NomNomz.on('raid_recap.show' as keyof NnzWidgetEventMap, onShow as never);
  window.addEventListener('resize', refit);
});

onUnmounted(() => {
  clearTimeout(hideTimer);
  window.removeEventListener('resize', refit);
  if (typeof NomNomz === 'undefined') return;
  NomNomz.off('raid_recap.show' as keyof NnzWidgetEventMap, onShow as never);
});
</script>

<template>
  <div v-if="recap" ref="root" class="recap" :data-visible="visible ? 'true' : 'false'" :style="rootStyle">
    <div class="wrap">
      <header>
        <div class="brand">
          <svg class="logo" viewBox="0 0 130 130" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
            <path fill-rule="evenodd" clip-rule="evenodd" d="M49.9746 104.882C45.1151 108.166 42.6854 109.807 40.6684 109.696C38.9115 109.598 37.2859 108.735 36.2217 107.334C35 105.725 35 102.792 35 96.9276V33.0725C35 27.2077 35 24.2753 36.2217 22.6666C37.2859 21.2653 38.9115 20.4018 40.6684 20.3045C42.6854 20.1929 45.1151 21.8346 49.9747 25.1181L97.2274 57.0456C101.17 59.7098 103.142 61.0419 103.831 62.7154C104.434 64.1789 104.434 65.8212 103.831 67.2847C103.142 68.9583 101.17 70.2904 97.2274 72.9545L49.9746 104.882ZM126 21.0001C128.209 21.0001 130 22.7909 130 25.0001V105C130 107.209 128.209 109 126 109H116.5C114.291 109 112.5 107.209 112.5 105V25.0001C112.5 22.7909 114.291 21.0001 116.5 21.0001H126Z" fill="var(--color-600)" />
            <path fill-rule="evenodd" clip-rule="evenodd" d="M9.35914 108.676C5.3744 111.369 0 108.514 0 103.705V26.2953C0 21.4863 5.37442 18.6314 9.35916 21.3238L66.6421 60.0285C70.1633 62.4077 70.1633 67.5925 66.6421 69.9716L9.35914 108.676ZM91 21.0001C93.2091 21.0001 95 22.7909 95 25.0001V105C95 107.209 93.2091 109 91 109H81.5C79.2909 109 77.5 107.209 77.5 105V25.0001C77.5 22.7909 79.2909 21.0001 81.5 21.0001H91Z" fill="var(--color-200)" />
          </svg>
          <div>
            <h1>{{ label('title') }}</h1>
            <div class="sub">{{ subtitle }}</div>
          </div>
        </div>
        <div v-if="thanks" class="thanks">{{ thanks }}</div>
      </header>

      <section class="kpis">
        <div v-for="k in kpis" :key="k.key" class="card kpi" :class="{ lead: k.lead }" :data-block="k.key">
          <div class="label">{{ label('kpi.' + k.key) }}</div>
          <div class="num">{{ k.value }}</div>
          <div v-if="k.note" class="note">{{ k.note }}</div>
        </div>
      </section>

      <section class="grid" :style="gridStyle">
        <div v-for="(p, pi) in placed" :key="p.card.key" class="card block" :data-block="p.card.key" :style="cellStyle(p)">
          <img v-if="avatarUrl && pi === 0" class="watermark" :src="avatarUrl" alt="" />
          <div class="label">{{ label('block.' + p.card.key) }}</div>
          <div v-if="p.card.big" class="big">{{ p.card.big }}<small v-if="p.card.unit">{{ p.card.unit }}</small></div>
          <div v-if="p.card.rows.length" class="scroll" :class="{ fit: p.card.note }">
            <div class="list">
              <template v-for="(r, ri) in p.card.rows.slice(0, shown[p.card.key] ?? p.card.rows.length)" :key="ri">
                <div v-if="r.kind === 'heading'" class="sect">{{ r.name }}</div>
                <div v-else class="row">
                  <span v-if="r.rank" class="r">{{ r.rank }}</span>
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
.recap {
  /* One hue (--base) with five shades, the same engine as the channel's Leaderboard and Logo widgets. */
  --base: 0deg;
  --color-100: hsl(var(--base), 100%, 90%);
  --color-200: hsl(var(--base), 100%, 80%);
  --color-300: hsl(var(--base), 100%, 70%);
  --color-600: hsl(var(--base), 100%, 40%);
  --color-900: hsl(var(--base), 100%, 10%);
  --accent: var(--color-300);
  --line: hsl(from var(--color-200) h s l / 25%);
  --mute: hsl(from var(--color-100) h s l / 70%);
  position: fixed;
  inset: 0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
  background: radial-gradient(1400px 800px at 15% 0%, hsl(from var(--color-900) h s 14%) 0, hsl(from var(--color-900) h 40% 4%) 75%);
  opacity: 0;
  transition: opacity 0.8s ease;
}
.recap[data-visible='true'] { opacity: 1; }
.wrap { position: absolute; inset: 48px 64px; display: grid; grid-template-columns: minmax(0, 1fr); grid-template-rows: auto auto 1fr; gap: 24px; }
header { display: flex; align-items: flex-end; justify-content: space-between; }
.brand { display: flex; align-items: center; gap: 24px; }
.logo { width: 110px; height: 110px; flex: none; transform: skewX(-1deg); }
h1 { font-size: 64px; font-weight: 900; letter-spacing: -1px; line-height: 1; color: var(--color-100); margin: 0; }
.sub { margin-top: 10px; font-size: 22px; color: var(--mute); font-weight: 500; }
.kpis { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; }
.grid { display: grid; grid-template-columns: repeat(12, minmax(0, 1fr)); gap: 16px; min-height: 0; }
.block { position: relative; }
.block > :not(.watermark) { position: relative; }
.watermark { position: absolute; left: -40px; bottom: -40px; width: 340px; height: 340px; border-radius: 50%; object-fit: cover; filter: grayscale(1) contrast(1.1); mix-blend-mode: luminosity; opacity: 0.22; pointer-events: none; -webkit-mask-image: radial-gradient(circle at 40% 60%, #000 35%, transparent 70%); mask-image: radial-gradient(circle at 40% 60%, #000 35%, transparent 70%); }
.card {
  background: linear-gradient(45deg, hsl(from var(--color-300) h s l / 50%), hsl(from var(--color-600) h s l / 50%) 15%, hsl(from var(--color-900) h s l / 75%));
  backdrop-filter: blur(24px);
  border-radius: 14px;
  padding: 20px 24px;
  border: 1px solid var(--line);
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.label { font-size: 15px; font-weight: 800; letter-spacing: 1.4px; text-transform: uppercase; color: var(--mute); margin-bottom: 10px; }
.kpi .num { font-size: 60px; font-weight: 900; line-height: 1; font-variant-numeric: tabular-nums; }
.kpi.lead { border-color: color-mix(in srgb, var(--accent) 60%, transparent); box-shadow: 0 0 28px color-mix(in srgb, var(--accent) 30%, transparent); }
.kpi.lead .label { color: var(--accent); }
.kpi.lead .num { font-size: 76px; }
.kpi .note { margin-top: 8px; font-size: 18px; color: var(--mute); font-weight: 500; }
.big { font-size: 44px; font-weight: 900; line-height: 1; margin-bottom: 12px; font-variant-numeric: tabular-nums; }
.big small { font-size: 20px; font-weight: 600; color: var(--mute); margin-left: 8px; }
.list { display: grid; gap: 6px; padding-bottom: 6px; }
.row { display: flex; align-items: center; gap: 10px; font-size: 20px; font-weight: 600; background: hsl(from var(--color-900) h s l / 55%); backdrop-filter: blur(4px); border-radius: 6px; padding: 6px 12px; }
.row .n { flex: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.row .v { font-weight: 800; font-variant-numeric: tabular-nums; }
.row .r { width: 18px; color: var(--mute); font-weight: 900; text-align: center; }
.sect { font-size: 15px; font-weight: 800; letter-spacing: 1.4px; text-transform: uppercase; color: var(--mute); margin: 14px 0 8px; }
.scroll { flex: 1; min-height: 0; overflow: hidden; }
.scroll.fit { flex: 0 1 auto; }
.hype { margin-top: 12px; font-size: 19px; font-weight: 500; color: var(--mute); }
.thanks { max-width: 620px; text-align: right; font-size: 24px; font-weight: 600; color: #fff; }
</style>
