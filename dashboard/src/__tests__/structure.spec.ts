// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

import { rungNames } from '../api/permissions';
import {
  emoteProviders,
  musicProviders,
  musicRoleRungs,
  streamPlatforms,
  trustTiers,
} from '../api/vocabulary';
import { merge, oneOf } from '../lib/array';
import * as utils from '../lib/utils';

const src = join(__dirname, '..');
const dumpingGround = /^(utils?|helpers?|misc|common)(\.\w+)?$/i;

function allPaths(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    return entry.isDirectory()
      ? [path, ...allPaths(path)]
      : [path];
  });
}

// Section 2a of frontend-vue-foundation.md: code lives by topic, never in a grab bag.
describe('folder layout', () => {
  it('keeps lib/utils.ts to the cn() that shadcn-vue needs', () => {
    expect(Object.keys(utils)).toEqual(['cn']);
  });

  it('has no utils, helpers or misc file or folder besides lib/utils.ts and the shadcn-vue output', () => {
    const offenders: string[] = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => dumpingGround.test(path.split('/').pop() ?? ''))
      .filter((path) => path !== 'lib/utils.ts' && !path.startsWith('components/ui/'));

    expect(offenders).toEqual([]);
  });

  // The shadcn-vue CLI adds a Google Fonts import on every `add`; Inter is bundled from npm instead.
  it('loads no style from another origin', () => {
    const remote: string[] = allPaths(join(src, 'styles'))
      .filter((path) => path.endsWith('.css'))
      .filter((path) => /@import\s+url\(\s*['"]?https?:/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(remote).toEqual([]);
  });

  // V18: Pinia holds local state only; everything from the API lives in TanStack Query (V6).
  it('keeps API data out of the Pinia stores', () => {
    const fetching: string[] = allPaths(join(src, 'stores'))
      .filter((path) => path.endsWith('.ts') && !path.includes('__tests__'))
      .filter((path) =>
        /from ['"](@\/api\/|\.\.\/api\/|@tanstack\/)/.test(readFileSync(path, 'utf8')),
      )
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(fetching).toEqual([]);
  });

  // Stoney 2026-10-13 (translated from Dutch): "the point is that we do not use Google with the privacy-invading junk".
  it('calls no Google font or tracking host from any source file or the page shell', () => {
    const google = /googleapis\.com|gstatic\.com|google-analytics\.com|googletagmanager\.com/;
    const offenders: string[] = [...allPaths(src), join(src, '..', 'index.html')]
      .filter((path) => /\.(vue|ts|css|html|json)$/.test(path) && !path.endsWith('schema.d.ts'))
      .filter((path) => google.test(readFileSync(path, 'utf8')))
      .map((path) => relative(join(src, '..'), path).replace(/\\/g, '/'));

    expect(offenders).toEqual([]);
  });

  // The CLI parts carry English screen-reader labels, and the raw-text lint is off in components/ui.
  // A caller hides the built-in close button and brings a translated one, and uses its own
  // translated button instead of SidebarTrigger or BreadcrumbEllipsis. DialogScrollContent cannot
  // hide its close label, so it is not used.
  // A raw browser select ignores the design system. OptionSelect (components/layout) is the one picker; a long
  // list sets its `searchable` prop.
  it('builds no native select element anywhere in the dashboard', () => {
    const offenders: string[] = allPaths(src)
      .filter((path) => path.endsWith('.vue') && !path.endsWith('OptionSelect.vue'))
      .filter((path) => /<select[\s>]/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(offenders).toEqual([]);
  });

  it('shows no untranslated shadcn label on any page', () => {
    const ownFiles = allPaths(src).filter(
      (path) =>
        path.endsWith('.vue') &&
        !relative(src, path).replace(/\\/g, '/').startsWith('components/ui/'),
    );
    const untranslated: string[] = ownFiles
      .filter((path) => {
        const text: string = readFileSync(path, 'utf8');
        const closable: string[] = text.match(/<(SheetContent|DialogContent)\b[^>]*>/g) ?? [];

        return (
          closable.some((tag) => !tag.includes(':show-close-button="false"')) ||
          /<(SidebarTrigger|BreadcrumbEllipsis|DialogScrollContent)\b/.test(text) ||
          /<DialogFooter\b[^>]*show-close-button(?!="false")/.test(text)
        );
      })
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(untranslated).toEqual([]);
  });

  // styles/frame.css gives harm one look: variant="destructive". A red class on another variant
  // makes a second harm style (a red underlined link) that the frame rules never approved.
  // Exception (734a9eb33, 2026-10-21): the Integrations row Disconnect is a deliberate quiet ghost button
  // with red text, so a loud filled button does not outrank the row's Edit.
  // Exception (2026-10-26): the editor footer Delete (EditorFooter) is the same quiet ghost with red text, on
  // the left of every editor, so it never outranks Save; its confirm dialog carries the danger.
  it('marks a harmful button only with the destructive variant, never a red class', () => {
    const quietDelete: readonly string[] = [
      'views/dashboard/settings/integrations/IntegrationsView.vue',
      'components/layout/EditorFooter.vue',
    ];
    const tinted: string[] = allPaths(src)
      .filter(
        (path) =>
          path.endsWith('.vue') &&
          !relative(src, path).replace(/\\/g, '/').startsWith('components/ui/') &&
          !quietDelete.includes(relative(src, path).replace(/\\/g, '/')),
      )
      .filter((path) =>
        /<Button\b[^>]*\bclass="[^"]*\btext-destructive\b/.test(readFileSync(path, 'utf8')),
      )
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(tinted).toEqual([]);
  });

  // Owner 2026-10-17: gray hint lines under every field clutter a form. A field hint is a FieldInfo icon
  // after the label (hover, focus or tap), so no view builds a FieldDescription line.
  it('gives a field hint through FieldInfo, never a FieldDescription line', () => {
    const lined: string[] = allPaths(src)
      .filter(
        (path) =>
          path.endsWith('.vue') &&
          !relative(src, path).replace(/\\/g, '/').startsWith('components/ui/'),
      )
      .filter((path) => /<FieldDescription\b/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(lined).toEqual([]);
  });

  // A phone swipes a sideways row like the Twitch app; a scrollbar under the profile tabs looked broken
  // (2026-10-14). A code block keeps its bar: a desktop reader needs it to reach a long line.
  it('shows no scrollbar under a sideways row, except a code block', () => {
    const barred: string[] = allPaths(src)
      .filter(
        (path) =>
          path.endsWith('.vue') &&
          !relative(src, path).replace(/\\/g, '/').startsWith('components/ui/'),
      )
      .flatMap((path) =>
        [...readFileSync(path, 'utf8').matchAll(/<(\w+)\b[^>]*?\bclass="([^"]*\boverflow-x-auto\b[^"]*)"/g)]
          .filter(([, tag, classes]) => tag !== 'pre' && !/\bscrollbar-none\b/.test(classes ?? ''))
          .map(([, tag]) => `${relative(src, path).replace(/\\/g, '/')} <${tag}>`));

    expect(barred).toEqual([]);
  });

  // A page that builds its own segmented control loses the shared look (a settings copy showed no chosen
  // option at all). Pages use SegmentedControl or FilterBar from components/layout.
  it('builds a segmented control only inside the shared layout blocks', () => {
    const own: string[] = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter(
        (path) =>
          path.endsWith('.vue') &&
          !path.startsWith('components/ui/') &&
          !path.startsWith('components/layout/'),
      )
      .filter((path) =>
        readFileSync(join(src, path), 'utf8').includes('@/components/ui/toggle-group'),
      );

    expect(own).toEqual([]);
  });

  // Stoney 2026-10-13: "please hardcode the background color for light and darkmode systems so i don't get
  // flashbang'd". The page shell paints the theme background before any script or stylesheet loads.
  it('paints the theme background in the page shell before any stylesheet loads', () => {
    const shell: string = readFileSync(join(src, '..', 'index.html'), 'utf8');
    const tokens: string = readFileSync(join(src, 'styles', 'tokens.css'), 'utf8');
    const dark = /:root\s*\{[^}]*--background:\s*([^;]+);/.exec(tokens)?.[1] ?? 'missing';
    const head: string = shell.slice(0, shell.indexOf('</head>'));
    const darkPattern = dark.replace(/[()]/g, '\\$&');

    expect(head).toMatch(/<meta name="color-scheme" content="dark"\s*\/?>/);
    expect(head).toMatch(new RegExp(`<style>[^<]*html\\s*\\{[^}]*background:\\s*${darkPattern}`));
  });

  // Stoney 2026-10-13: "don't border an already bordered icon within a button". A tray of icon buttons is
  // one edged surface; the frame takes the edge off its buttons, so every tray must carry data-tray.
  it('takes the edge off icon buttons inside a tray, and the chat line tray is one', () => {
    const frame: string = readFileSync(join(src, 'styles', 'frame.css'), 'utf8');
    const chatLine: string = readFileSync(
      join(src, 'views', 'dashboard', 'chat', 'components', 'ChatLine.vue'),
      'utf8',
    );

    expect(frame).toMatch(
      /\[data-tray\]\s+\.group\\\/button\[data-size\^='icon'\]\s*\{[^}]*border-color:\s*transparent/,
    );

    expect(chatLine).toMatch(/<div\b[^>]*\bdata-tray\b/);
  });

  // Stoney 2026-10-13: "there needs to be more contrast on the hovered chat message and also on the quick
  // action button box and the hover on each button should raise it further". The tray is a raised surface
  // with a 3:1 edge, and a hovered tray button lifts above it.
  it('raises the tray above its row and a hovered tray button above the tray', () => {
    const frame: string = readFileSync(join(src, 'styles', 'frame.css'), 'utf8');

    expect(frame).toMatch(/\[data-tray\]\s*\{[^}]*border-color:\s*var\(--input\)[^}]*box-shadow:/);

    expect(frame).toMatch(
      /\[data-tray\]\s+\.group\\\/button\[data-size\^='icon'\]:hover\s*\{[^}]*background:[^}]*box-shadow:/,
    );
  });

  // A tab bar names a place by its icon, its label and the filled active tab. The ghost-button underline reads as
  // a link, so on the phone bottom bar every label would look like a link: the frame takes it off.
  it('draws no link underline under the labels of the phone bottom bar', () => {
    const frame: string = readFileSync(join(src, 'styles', 'frame.css'), 'utf8');

    expect(frame).toMatch(
      /\[data-slot='bottom-bar'\]\s+\.group\\\/button\[data-variant='ghost'\]\s*\{[^}]*text-decoration:\s*none/,
    );
  });

  // Stoney 2026-10-14: "remove all double side margins for a better mobile app feel". On a phone a page body
  // drops its side margin and its panels run edge to edge; the panel pages must carry the marker.
  it('runs the panels of a page body edge to edge on a phone', () => {
    const frame: string = readFileSync(join(src, 'styles', 'frame.css'), 'utf8');
    const bodies = [
      'components/layout/BoardGrid.vue',
      'views/dashboard/chat/ChatView.vue',
      'views/dashboard/admin/AdminTenantsView.vue',
      'components/layout/ListDetail.vue',
    ];

    expect(frame).toMatch(
      /@media \(width < 48rem\)\s*\{\s*\[data-page-body\]\s*\{\s*padding-inline:\s*0;\s*\}\s*\[data-page-body\] \[data-slot='panel-card'\]\s*\{[^}]*border-inline-width:\s*0;[^}]*border-radius:\s*0;/,
    );

    const marked = (path: string): boolean => {
      const text: string = readFileSync(join(src, path), 'utf8');

      return /<(div|section|PageBody)\b[^>]*\bdata-page-body\b/.test(text);
    };

    const unmarked = bodies.filter((path) =>
      !marked(path));

    expect(unmarked).toEqual([]);
  });

  // A card in a scrolling column keeps its height: PanelCard is min-h-0 and overflow-hidden, so a page body that
  // is not PageBody (no shrink-0 on its children) squeezes every panel down to its title row.
  it('builds no scrolling page column by hand where PageBody owns it', () => {
    const byHand = /<div\b[^>]*\bclass="[^"]*flex-col[^"]*overflow-y-auto p-6[^"]*"/;
    const offenders: string[] = allPaths(join(src, 'views'))
      .filter((path) => path.endsWith('.vue'))
      .filter((path) => byHand.test(readFileSync(path, 'utf8')))
      .map((path) => relative(src, path));

    expect(offenders).toEqual([]);
  });

  // A search field or a back button outside a panel would touch the screen edge once the body drops its margin;
  // it keeps one panel padding instead.
  it('keeps a loose control of a page body one panel padding from the phone edge', () => {
    const frame: string = readFileSync(join(src, 'styles', 'frame.css'), 'utf8');
    const loose = [
      'views/dashboard/people/PeopleView.vue',
      'views/dashboard/overlays/components/OverlayDetail.vue',
      'views/dashboard/scripts/components/ScriptDetail.vue',
    ];

    expect(frame).toMatch(/\[data-page-body\] \[data-page-inset\]\s*\{\s*margin-inline:\s*1rem;/);

    const unmarked = loose.filter((path) =>
      !readFileSync(join(src, path), 'utf8').includes('data-page-inset'));

    expect(unmarked).toEqual([]);
  });

  // A toast or tooltip works on every page only when the frame mounts its host once; a page that mounts its
  // own stacks a second host, and a page without one shows nothing. The popout window and the viewer
  // page (no sidebar, no live management connection) are each their own frame. So is a home page preview:
  // it is a separate Vue app with its own page tree, and the app around it never sees its tooltips.
  it('mounts the toast host and the tooltip provider once per frame', () => {
    const own = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => path.endsWith('.vue') && !path.startsWith('components/ui/'));
    const hosts: string[] = own.filter((path) => {
      const text: string = readFileSync(join(src, path), 'utf8');

      return /<(Toaster|TooltipProvider)\b/.test(text);
    });

    expect(hosts).toEqual([
      'layouts/DashboardLayout.vue',
      'layouts/PopoutLayout.vue',
      'layouts/ViewerLayout.vue',
      'views/site/home/preview/PreviewRoot.vue',
    ]);
  });

  // Stoney 2026-10-13: "the icon size in the navbar is too small, it should match the font better and follow
  // the correct visual hirarchy". A nav icon fills the text line (20px); a group chevron stays below it (14px).
  it('sizes a nav icon to its text line and keeps the group chevron smaller', () => {
    const frame: string = readFileSync(join(src, 'styles', 'frame.css'), 'utf8');

    expect(frame).toMatch(
      /\[data-sidebar='menu-button'\]\s*>\s*svg\s*\{[^}]*width:\s*20px;[^}]*height:\s*20px/,
    );

    expect(frame).toMatch(
      /\[data-slot='sidebar-group-label'\]\s*>\s*svg\s*\{[^}]*width:\s*14px;[^}]*height:\s*14px/,
    );
  });

  // Stoney 2026-10-13: "If I have to correct you on icons then you're fired" and "use the moooom icons
  // from my designer". Icons come from the designer pack (`~icons/nnz/<name>`, assets/icons/line); brand
  // logos from simple-icons, drawn by PlatformLogo from the library's own path. Lucide stays only inside
  // the shadcn-vue CLI output. A hand-drawn svg is a guess.
  it('takes every icon from the designer pack: no Lucide import and no hand-drawn svg', () => {
    const own = allPaths(src)
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => /\.(vue|ts)$/.test(path) && !path.startsWith('components/ui/'));
    const offenders: string[] = own.filter((path) => {
      const text: string = readFileSync(join(src, path), 'utf8');
      const drawn =
        path !== 'components/layout/PlatformLogo.vue' &&
        path.endsWith('.vue') &&
        /<svg\b/.test(text);

      return drawn || /from ['"](@lucide\/vue|lucide-vue-next)['"]/.test(text);
    });

    expect(offenders).toEqual([]);
  });

  // Stoney 2026-10-13 (translated from Dutch): "you are really going to town with all those em dashes". Copy uses plain sentences.
  it('writes no em dash in any words file', () => {
    const dashed: string[] = allPaths(src)
      .filter((path) => path.endsWith('.json') && /[\\/](i18n|locales)[\\/]/.test(path))
      .filter((path) => readFileSync(path, 'utf8').includes('—'))
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(dashed).toEqual([]);
  });

  // The shadcn parts read --radius and --popover-foreground; neither was defined, so the search key cap and
  // every toast drew square. A token that is read must be defined somewhere. A name built at run time
  // (`--color-${key}` from a chart config) ends in "-" here and is set by its component. The reka drawer
  // sets its swipe distance itself, so the names in its own file count as defined.
  it('defines every CSS variable that a style or part reads', () => {
    const files = allPaths(src).filter((path) =>
      /\.(vue|ts|css)$/.test(path) && !path.includes('__tests__'));
    const text: string = files.map((path) =>
      readFileSync(path, 'utf8')).join('\n');
    const drawer: string = readFileSync(
      join(src, '..', 'node_modules', 'reka-ui', 'dist', 'Drawer', 'utils.js'),
      'utf8',
    );
    const read = new Set([...text.matchAll(/var\((--[\w-]+)/g)].map((found) =>
      found[1]!));
    const declared = [...text.matchAll(/(--[\w-]+)['"]?\s*:/g)].map((found) =>
      found[1]!);
    const setByDrawer = [...drawer.matchAll(/"(--[\w-]+)"/g)].map((found) =>
      found[1]!);
    const defined = new Set(merge(declared, setByDrawer));
    const missing = [...read].filter((name) =>
      !name.endsWith('-') && !defined.has(name));

    expect(missing).toEqual([]);
  });

  // styles/frame.css sizes every button outside any layer, so it beats a plain Tailwind class. The bottom
  // bar's "h-auto px-1" lost to it and squashed every label to 3px on a phone. A button that sets its own
  // height, side padding or gap marks the class important ("h-auto!"), or the frame silently wins.
  it('marks a button size class important so the frame does not silently overrule it', () => {
    const sized = /<Button\b[^>]*?\bclass="([^"]*)"/gs;
    const frameOwned = /^(h-|px-|ps-|pe-|gap-)[\w.[\]()-]+$/;
    const overruled: string[] = allPaths(src)
      .filter((path) =>
        path.endsWith('.vue') && !relative(src, path).replace(/\\/g, '/').startsWith('components/ui/'))
      .flatMap((path) =>
        [...readFileSync(path, 'utf8').matchAll(sized)]
          .flatMap((found) =>
            found[1]!.split(/\s+/))
          .filter((name) =>
            frameOwned.test(name))
          .map((name) =>
            `${relative(src, path).replace(/\\/g, '/')}: ${name}`));

    expect(overruled).toEqual([]);
  });

  it('gives each page translation file only the keys of its own page', () => {
    const misplaced: string[] = allPaths(join(src, 'views'))
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => /^views\/[^/]+\/[^/]+\/i18n\/[^/]+\.json$/.test(path))
      .flatMap((path) => {
        const [, area, folder] = path.split('/');
        // A folder name is kebab-case; its page key is the camelCase of it (channel-points = channelPoints).
        const slug = folder!.replace(/-([a-z])/g, (_, letter: string) =>
          letter.toUpperCase());
        const text: string = readFileSync(join(src, path), 'utf8');
        const words: Record<string, Record<string, unknown>> = JSON.parse(text);
        const keys = Object.entries(words).flatMap(([top, pages]) =>
          Object.keys(pages).map((page) => `${top}.${page}`),
        );

        return keys.filter((key) => key !== `${area}.${slug}`).map((key) => `${path}: ${key}`);
      });

    expect(misplaced).toEqual([]);
  });

  // Stoney 2026-10-16: "the obs page is also overflowing without a contained scroll pannel". The frame's <main>
  // is overflow-hidden, so every routed page owns its scroll region: a PageBody, a BoardGrid, a ListDetail, a
  // virtual list, a scroll area or its own overflow-y-auto, in the page file or in a part beside it in the same folder (a
  // panel under components/ scrolling inside itself is not a page scroll region).
  it('gives every routed dashboard page a contained scroll region', () => {
    const scrolls = /PageBody|data-page-body|BoardGrid|ListDetail|ScrollArea|VirtualList|overflow-y-auto|overflow-auto/;
    const sideBySideVue = /from '\.\/([A-Za-z]+\.vue)'/g;

    function withParts(path: string, seen: Set<string>): string {
      if (seen.has(path))
        return '';

      seen.add(path);

      const text: string = readFileSync(path, 'utf8');
      const parts = [...text.matchAll(sideBySideVue)].map((match) =>
        withParts(join(path, '..', match[1] ?? ''), seen));

      return merge([text], parts).join('\n');
    }

    const pages: string[] = allPaths(join(src, 'views', 'dashboard'))
      .filter((path) => path.endsWith('View.vue') && !path.includes('components'))
      .filter((path) => readFileSync(path, 'utf8').includes('<PageHeader'));

    const scrollsIn = (path: string): boolean => {
      const seen = new Set<string>();
      const text = withParts(path, seen);

      return scrolls.test(text);
    };

    const loose = pages
      .filter((path) => !scrollsIn(path))
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(pages.length).toBeGreaterThan(15);
    expect(loose).toEqual([]);
  });
});

// Stoney 2026-10-14: "make sure buttons never get pancaked flat when they should be rounded". A lone icon in a
// text-sized button gets text padding and draws as a flat oval; it takes an icon size instead.
const loneIconButton = /<Button\b([^>]*)>\s*<[A-Z]\w*\b[^>]*\/>\s*<\/Button>/g;

function textSized(attributes: string): boolean {
  return !/\bsize="icon/.test(attributes) && !/:size=/.test(attributes);
}

describe('button shapes', () => {
  it('spots a lone icon in a text-sized button', () => {
    const matches = [...'<Button variant="outline" size="sm"><Inbox aria-hidden="true" /></Button>'
      .matchAll(loneIconButton)];

    expect(matches.filter((match) =>
      textSized(match[1] ?? ''))).toHaveLength(1);
  });

  it('gives every icon-only button an icon size', () => {
    const flat: string[] = allPaths(src)
      .filter((path) => path.endsWith('.vue') && !path.includes('components/ui') && !path.includes('components\\ui'))
      .flatMap((path) =>
        [...readFileSync(path, 'utf8').matchAll(loneIconButton)]
          .filter((match) => textSized(match[1] ?? ''))
          .map((match) => `${relative(src, path).replace(/\\/g, '/')}: ${match[0].split('\n')[0]}`));

    expect(flat).toEqual([]);
  });
});

// Stoney 2026-10-18: "make a date component that can return any variation of time we need including relative
// to display and ensure it applies the user's locale formatting". Every time on screen goes through formatTime
// (lib/time.ts), <DateTime> or useTime(), which read the i18n locale. These files still build their own
// formatter; the sweep removes them from the list, and a file that is clean must leave it (below).
const ownFormatter = /Intl\.DateTimeFormat|toLocale(Date|Time)String|useTimeAgo|new Date\([^)]*\)\.toLocaleString/;
const ownFormatterAllowlist: readonly string[] = [];

function ownsFormatter(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return ownFormatter.test(text);
}

function timeSourceFiles(): string[] {
  return allPaths(src)
    .filter((path) => /\.(vue|ts)$/.test(path) && !/__tests__|\.spec\.ts$|schema\.d\.ts$/.test(path))
    .map((path) => relative(src, path).replace(/\\/g, '/'))
    .filter((path) => path !== 'lib/time.ts' && path !== 'components/layout/DateTime.vue');
}

// Stoney 2026-10-18: "i expect you to make tools like this for repeatables". A switch that falls back to an
// on/off badge for a person who may not change it is <GatedSwitch>; a view never pairs a Switch with an
// EnabledState by hand. These files still do; the sweep removes them from the list (none left today).
const handGatedSwitchAllowlist: readonly string[] = [];

function pairsSwitchWithBadge(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return /<Switch\b/.test(text) && /<EnabledState\b/.test(text);
}

function viewFiles(): string[] {
  return allPaths(src)
    .filter((path) => path.endsWith('.vue'))
    .map((path) => relative(src, path).replace(/\\/g, '/'))
    .filter((path) => path.startsWith('views/'));
}

describe('gated switches', () => {
  it('spots a Switch paired with an EnabledState', () => {
    const textual = '<Switch v-if="can(\'a\')" /><EnabledState v-else :enabled="on" />';

    expect(/<Switch\b/.test(textual) && /<EnabledState\b/.test(textual)).toBe(true);
  });

  it('pairs no Switch with an EnabledState in a view, except the files on the sweep list', () => {
    const offenders = viewFiles()
      .filter((path) => pairsSwitchWithBadge(path))
      .filter((path) => !handGatedSwitchAllowlist.includes(path));

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the sweep list', () => {
    const stale = handGatedSwitchAllowlist.filter((path) => !pairsSwitchWithBadge(path));

    expect(stale).toEqual([]);
  });
});

// Stoney 2026-10-18: "90 days is crooked". Every chart axis (one slot per day, thinned labels) is drawn by
// <ChartPanel>; a view never builds its own Unovis axis.
const ownChartAxis = /<VisAxis\b|<VisXYContainer\b/;

describe('chart axes', () => {
  it('spots a hand-written chart axis', () => {
    expect(ownChartAxis.test('<VisAxis type="x" />')).toBe(true);
    expect(ownChartAxis.test('<ChartPanel kind="bar" />')).toBe(false);
  });

  it('draws no axis outside components/layout/ChartPanel.vue', () => {
    const drawsAxis = (path: string): boolean => {
      const text: string = readFileSync(join(src, path), 'utf8');

      return ownChartAxis.test(text);
    };

    const offenders = viewFiles().filter(drawsAxis);

    expect(offenders).toEqual([]);
  });
});

describe('time formatting', () => {
  it('builds no date formatter outside lib/time.ts, except the files on the sweep list', () => {
    const offenders = timeSourceFiles()
      .filter((path) => ownsFormatter(path))
      .filter((path) => !ownFormatterAllowlist.includes(path));

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the sweep list', () => {
    const stale = ownFormatterAllowlist.filter((path) => !ownsFormatter(path));

    expect(stale).toEqual([]);
  });
});

// Stoney 2026-10-27: "changing language does not live change the language and requires a reload". A number, a
// currency or a list written in the language of the browser instead of the language chosen in the app never
// changes on a switch: it follows the i18n locale (useI18n().n / locale.value) or it is wrong.
const browserLocaleFormat = /Intl\.(NumberFormat|ListFormat|PluralRules|RelativeTimeFormat)\(\s*(undefined\s*[,)]|\))|\.toLocaleString\(\s*\)/;

describe('language of formatted numbers', () => {
  it('spots a number formatted in the language of the browser', () => {
    expect(browserLocaleFormat.test('new Intl.NumberFormat(undefined, { style: "currency" })')).toBe(true);
    expect(browserLocaleFormat.test('{{ value.toLocaleString() }}')).toBe(true);
    expect(browserLocaleFormat.test('new Intl.NumberFormat(locale.value)')).toBe(false);
    expect(browserLocaleFormat.test('bits.toLocaleString(locale.value)')).toBe(false);
  });

  it('formats no number, list or plural without the language of the app', () => {
    const formatsInBrowserLocale = (path: string): boolean => {
      const text: string = readFileSync(join(src, path), 'utf8');

      return browserLocaleFormat.test(text);
    };

    const offenders = timeSourceFiles().filter(formatsInBrowserLocale);

    expect(offenders).toEqual([]);
  });
});

// Stoney 2026-10-18: "why can't I select a command directly instead of a pipeline that calls a single command?"
// What a row runs (a command, an action or a pipeline) is chosen in <TargetPicker>. A view never lists the
// pipelines in a Select of its own. Only the picker reads the pipeline list into select items.
const ownTargetSelect = /usePipelines\(/;
const ownTargetSelectAllowlist = [
  'components/layout/TargetPicker.vue',
] as const satisfies readonly string[];

function ownsTargetSelect(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return ownTargetSelect.test(text) && /<(Select|Command)Item\b/.test(text);
}

describe('target selects', () => {
  it('spots a Select built from the pipeline list', () => {
    const textual = 'const flows = usePipelines(id); <SelectItem v-for="flow in flows" />';

    expect(ownTargetSelect.test(textual) && /<(Select|Command)Item\b/.test(textual)).toBe(true);
  });

  it('builds no pipeline or command Select outside TargetPicker', () => {
    const offenders = viewFiles()
      .concat(timeSourceFiles().filter((path) => path.startsWith('components/layout/')))
      .filter((path) => ownsTargetSelect(path))
      .filter((path) => oneOf(ownTargetSelectAllowlist, path) === undefined);

    expect(offenders).toEqual([]);
  });

  it('keeps TargetPicker as the one place that builds it', () => {
    expect(ownsTargetSelect('components/layout/TargetPicker.vue')).toBe(true);
  });
});

// Stoney 2026-10-27: "1000+ but they have to be useful and nicely organized". The list of template helpers is
// <HelperPanel> (grouped, collapsible, searchable, bounded). A place that lists helpers mounts it (TemplateField
// does, and CatalogView mounts it directly); no view groups, searches or lists the helper rows itself.
const ownHelperList = /groupHelpers\(|searchHelpers\(|data-helper-row/;
const ownHelperListAllowlist = [
  'components/layout/HelperPanel.vue',
  'components/layout/templatePlaceholders.ts',
] as const satisfies readonly string[];

function ownsHelperList(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return ownHelperList.test(text);
}

describe('template helper lists', () => {
  it('spots a hand-written helper list', () => {
    expect(ownHelperList.test('<button v-for="h in hs" :data-helper-row="h.key">')).toBe(true);
    expect(ownHelperList.test('const groups = groupHelpers(rows);')).toBe(true);
  });

  it('builds no helper list outside HelperPanel', () => {
    const offenders = timeSourceFiles()
      .filter((path) => ownsHelperList(path))
      .filter((path) => oneOf(ownHelperListAllowlist, path) === undefined);

    expect(offenders).toEqual([]);
  });

  it('keeps HelperPanel as the one place that builds it', () => {
    expect(ownsHelperList('components/layout/HelperPanel.vue')).toBe(true);
  });
});

// Stoney 2026-10-18: "extract things like these into reusables". "Is there room?" is asked through
// composables/useLayout.ts (its width table, useScreenFrom, usePhone, useFits), never with a media query or a
// size measure written in a view. The files left are height measures and the generated shadcn sidebar.
const ownRoomCheck = /useMediaQuery\(\s*['"`(]|useElementSize\(/;
const ownRoomCheckAllowlist = [
  'components/layout/ReplyModeField.vue',
  'components/ui/sidebar/SidebarProvider.vue',
  'views/dashboard/analytics/RecapPreview.vue',
  'views/dashboard/chat/ChatView.vue',
  'views/viewer/nav/ViewerNav.vue',
] as const satisfies readonly string[];

function ownsRoomCheck(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return ownRoomCheck.test(text);
}

describe('room checks', () => {
  it('spots a hand-written media query and a hand-written size measure', () => {
    expect(ownRoomCheck.test('const phone = useMediaQuery(\'(max-width: 767px)\');')).toBe(true);
    expect(ownRoomCheck.test('const { width } = useElementSize(board);')).toBe(true);
    expect(ownRoomCheck.test('const phone = usePhone();')).toBe(false);
  });

  it('asks nothing outside composables/useLayout.ts, except the files on the allowlist', () => {
    const offenders = timeSourceFiles()
      .filter((path) => path !== 'composables/useLayout.ts')
      .filter((path) => ownsRoomCheck(path))
      .filter((path) => oneOf(ownRoomCheckAllowlist, path) === undefined);

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the allowlist', () => {
    const stale = ownRoomCheckAllowlist.filter((path) => !ownsRoomCheck(path));

    expect(stale).toEqual([]);
  });
});

// Stoney 2026-10-27: "i want it to not reply in chat but directly output this to the chat overlay with html
// support". Where a simple reply goes (Twitch chat, chat overlay, both, nowhere) and its HTML switch are chosen
// in components/layout/ReplyDeliveryField.vue only. An editor places that field; it never spells the targets
// or builds a select or a segmented control of its own for them.
const ownReplyTarget = /\bReplyTarget\b|\breplyTargets\b|['"]overlay['"]\s*,\s*['"]both['"]|<(Select|SegmentedControl|ToggleGroup)\b[^>]*(reply-?target|replyDelivery)/i;

function ownsReplyTarget(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return ownReplyTarget.test(text);
}

describe('reply delivery', () => {
  it('spots a hand-written reply target list and a hand-written target select', () => {
    expect(ownReplyTarget.test('const targets = [\'chat\', \'overlay\', \'both\', \'none\'];')).toBe(true);
    expect(ownReplyTarget.test('<Select v-model="draft.replyTarget">')).toBe(true);
    expect(ownReplyTarget.test('<SegmentedControl v-model="draft.replyDelivery.target" />')).toBe(true);
    expect(ownReplyTarget.test('<ReplyDeliveryField v-model="draft.replyDelivery" />')).toBe(false);
  });

  it('writes no reply target choice in a view; an editor places ReplyDeliveryField', () => {
    const offenders = viewFiles().filter(ownsReplyTarget);

    expect(offenders).toEqual([]);
  });

  it('keeps ReplyDeliveryField as the one place that builds it', () => {
    const field: string = readFileSync(join(src, 'components/layout/ReplyDeliveryField.vue'), 'utf8');

    expect(field).toContain('<SegmentedControl');
    expect(field).toContain('replyTargets');
  });
});

// Stoney 2026-10-18: "things like these are also used everywhere... i expect you to make tools like this for
// repeatables". A role ladder, a platform list, a provider list or a trust list is spelled once, in
// api/vocabulary.ts (or the ladder in api/permissions.ts). A view imports it; a list of three or more of its
// words written anywhere else is a copy that drifts.
const vocabularyLists: Record<string, readonly string[]> = {
  roleLadder: rungNames,
  streamPlatforms,
  emoteProviders,
  musicProviders,
  musicRoleRungs,
  trustTiers,
};
const vocabularyHomes = ['api/vocabulary.ts', 'api/permissions.ts'] as const satisfies readonly string[];
// Two lists that only look like a ladder: the people page's filter tabs, and the three personas a visitor
// picks on the login page. Neither is a role the bot ranks.
const vocabularyLookalikes = [
  'views/dashboard/people/PeopleView.vue',
  'views/site/login/LoginView.vue',
] as const satisfies readonly string[];
const quotedList = /\[\s*((?:'[^'\n]+'\s*,\s*)+'[^'\n]+'\s*,?)\s*\]/g;

// The names of the vocabulary lists a text copies: a list with at least min(3, its length) of one list's words.
function copiedVocabulary(text: string): string[] {
  const written: string[][] = [...text.matchAll(quotedList)].map((match) =>
    [...(match[1] ?? '').matchAll(/'([^']+)'/g)].map((word) => (word[1] ?? '').toLowerCase()));

  return Object.entries(vocabularyLists)
    .filter(([, words]) => {
      const lowered = words.map((word) => word.toLowerCase());
      const need = Math.min(3, lowered.length);

      return written.some((list) => list.filter((word) => lowered.includes(word)).length >= need);
    })
    .map(([name]) => name);
}

function copiedIn(path: string): string[] {
  const text: string = readFileSync(join(src, path), 'utf8');

  return copiedVocabulary(text);
}

function vocabularySourceFiles(): string[] {
  return timeSourceFiles()
    .filter((path) => oneOf(vocabularyHomes, path) === undefined)
    .filter((path) => oneOf(vocabularyLookalikes, path) === undefined)
    .filter((path) => !path.startsWith('i18n/') && !path.startsWith('components/ui/'));
}

describe('domain vocabulary', () => {
  it('spots a role ladder, a platform list and a provider list written by hand', () => {
    expect(copiedVocabulary('const levels = [\'everyone\', \'vip\', \'moderators\', \'broadcaster\'];'))
      .toEqual(['roleLadder']);

    expect(copiedVocabulary('const p = [\'twitch\',\n  \'kick\',\n  \'youtube\'];')).toEqual(['streamPlatforms']);
    expect(copiedVocabulary('const p = [\'Twitch\', \'SevenTv\', \'Bttv\'];')).toEqual(['emoteProviders']);
    expect(copiedVocabulary('const sizes = [\'small\', \'medium\', \'large\'];')).toEqual([]);
  });

  it('writes no vocabulary list outside api/vocabulary.ts and api/permissions.ts', () => {
    const offenders = vocabularySourceFiles()
      .map((path) => [path, copiedIn(path)] as const)
      .filter(([, names]) => names.length > 0)
      .map(([path, names]) => `${path}: ${names.join(', ')}`);

    expect(offenders).toEqual([]);
  });
});

// Stoney 2026-10-18: "all overlay pannels need a close button". SidePanel hides the sheet's own X; the one
// close button (data-drawer-close) is drawn by PanelCard's head. A component placed in a <SidePanel> therefore
// roots in <PanelCard>, or the drawer has no way to close it.
const sidePanelBlock = /<SidePanel\b[^>]*>([\s\S]*?)<\/SidePanel>/g;
const childTag = /<([A-Z]\w*)\b/;
const defaultImport = /import\s+(\w+)\s+from\s+'([^']+)'/g;

function importedFrom(text: string, name: string): string | null {
  const found = [...text.matchAll(defaultImport)].find((match) => match[1] === name);

  return found?.[2] ?? null;
}

function resolveComponent(from: string, importPath: string): string {
  return importPath.startsWith('@/')
    ? join(src, importPath.slice(2))
    : join(from, '..', importPath);
}

function templateRoot(text: string): string | null {
  const found = /^<template>\s*<(\w+)/m.exec(text);

  return found?.[1] ?? null;
}

function sidePanelChildren(text: string): string[] {
  return [...text.matchAll(sidePanelBlock)]
    .map((block) => childTag.exec(block[1] ?? '')?.[1])
    .filter((name): name is string => name !== undefined);
}

function sidePanelChildrenOutsidePanelCard(path: string): string[] {
  const text: string = readFileSync(path, 'utf8');

  return sidePanelChildren(text).flatMap((name) => {
    const importPath = importedFrom(text, name);

    if (importPath === null)
      return [];

    const child = resolveComponent(path, importPath);

    return templateRoot(readFileSync(child, 'utf8')) === 'PanelCard'
      ? []
      : [`${relative(src, path).replace(/\\/g, '/')}: ${name}`];
  });
}

describe('side panels', () => {
  it('finds the component placed in a SidePanel and the root of a template', () => {
    const text = '<SidePanel v-model:open="o"><ChatIdentityPanel /></SidePanel>';

    expect(sidePanelChildren(text)).toEqual(['ChatIdentityPanel']);
    expect(templateRoot('<script setup></script>\n<template>\n  <div class="x">')).toBe('div');
    expect(templateRoot('<template>\n  <PanelCard title="x">')).toBe('PanelCard');
  });

  it('roots every component placed in a SidePanel in PanelCard, so it shows the close button', () => {
    const offenders = allPaths(src)
      .filter((path) => path.endsWith('.vue'))
      .flatMap((path) => sidePanelChildrenOutsidePanelCard(path));

    expect(offenders).toEqual([]);
  });
});

// Stoney 2026-10-18: "a back button does not belong on the right side on a LTR page". A page's way back is
// <PageHeader :back>, drawn at the start of the header; a pane's way back is <BackLink>. A view never draws its
// own back arrow, and never puts a back link in the header's actions.
const ownBackArrow = /~icons\/nnz\/arrow-left/;
const backLinkInActions = /<template #actions>(?:(?!<\/template>)[\s\S])*?(?:<BackLink|data-detail-back)/;

describe('back links', () => {
  it('spots a hand-made back arrow and a back link inside the header actions', () => {
    expect(ownBackArrow.test('import ArrowLeft from \'~icons/nnz/arrow-left\';')).toBe(true);
    expect(ownBackArrow.test('import Plus from \'~icons/nnz/plus\';')).toBe(false);
    expect(backLinkInActions.test('<template #actions><BackLink /></template>')).toBe(true);
    expect(backLinkInActions.test('<template #actions><Button /></template><BackLink />')).toBe(false);
  });

  it('draws no back arrow in a view and no back link in a header actions slot', () => {
    const offenders = viewFiles().filter((path) => {
      const text: string = readFileSync(join(src, path), 'utf8');

      return ownBackArrow.test(text) || backLinkInActions.test(text);
    });

    expect(offenders).toEqual([]);
  });
});

// Stoney 2026-10-13: "these are way too small to be readable even by me". An icon-only button is shadcn
// size="icon" (36px) with a size-5 icon; "icon-sm" (32px) and "icon-xs" (24px) never ship in the shell or the
// shared layout components. The page files below still carry the small form; their owners sweep them off.
const smallIconButton = /['"]icon-(sm|xs)['"]/;
const smallIconButtonAllowlist = [
  'views/dashboard/chat/components/ChatComposer.vue',
  'views/dashboard/chat/components/ChatLine.vue',
  'views/dashboard/chat/components/EmotePicker.vue',
  'views/dashboard/commands/components/RandomReplyList.vue',
  'views/dashboard/discord/DiscordView.vue',
  'views/dashboard/home/components/HappeningPanel.vue',
  'views/dashboard/music/MusicView.vue',
  'views/dashboard/pipelines/WorkbenchView.vue',
  'views/dashboard/pipelines/components/StepPalette.vue',
  'views/site/login/components/BotChooser.vue',
] as const satisfies readonly string[];

function ownsSmallIconButton(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return smallIconButton.test(text);
}

function uiSourceFiles(): string[] {
  return allPaths(src)
    .filter((path) => path.endsWith('.vue'))
    .map((path) => relative(src, path).replace(/\\/g, '/'))
    .filter((path) => !path.startsWith('components/ui/'));
}

describe('icon-only buttons', () => {
  it('spots the 32px and 24px icon sizes', () => {
    expect(smallIconButton.test('size="icon-sm"')).toBe(true);
    expect(smallIconButton.test(':size="count ? \'sm\' : \'icon-xs\'"')).toBe(true);
    expect(smallIconButton.test('size="icon"')).toBe(false);
  });

  it('uses no icon-sm or icon-xs outside the files on the sweep list', () => {
    const offenders = uiSourceFiles()
      .filter((path) => ownsSmallIconButton(path))
      .filter((path) => !smallIconButtonAllowlist.some((entry) => entry === path));

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the sweep list', () => {
    const stale = smallIconButtonAllowlist.filter((path) => !ownsSmallIconButton(path));

    expect(stale).toEqual([]);
  });
});

// Stoney 2026-10-18: "i expect you to make tools like this for repeatables". A refusal names its broken fields
// through refusalProblems (lib/errors.ts), which reads both a single violation and a list of problems. A view never
// reads error.violation or error.problems by hand.
const ownRefusalRead = /\berror\.(violation|problems)\b/;
const ownRefusalReadAllowlist: readonly string[] = [];

function readsRefusalByHand(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return ownRefusalRead.test(text);
}

describe('refusal fields', () => {
  it('spots a hand read of the violation or the problems of an error', () => {
    expect(ownRefusalRead.test('error.violation')).toBe(true);
    expect(ownRefusalRead.test('error.problems')).toBe(true);
    expect(ownRefusalRead.test('refusalProblems(error)')).toBe(false);
  });

  it('reads no refusal field by hand in a view, except the files on the sweep list', () => {
    const offenders = viewFiles()
      .filter((path) => readsRefusalByHand(path))
      .filter((path) => !ownRefusalReadAllowlist.includes(path));

    expect(offenders).toEqual([]);
  });
});

// Stoney 2026-10-14 and 2026-10-19 ("double borders ... a related pattern that keeps coming back"): a closed dialog,
// sheet, menu, popover or select handed the focus back to its button, and the browser drew a focus ring there. Every
// wrapper of a focus-returning reka part binds the keyboard-only focus return (lib/focusReturn.ts).
const focusReturningPart =
  /import\s*\{[^}]*\b(AlertDialogContent|DialogContent|DrawerContent|DropdownMenuContent|PopoverContent|SelectContent|ContextMenuContent|MenubarContent|ComboboxContent)\b[^}]*\}\s*from\s*'reka-ui'/;
const keyboardOnlyFocusReturn = '@close-auto-focus="returnFocusOnlyForKeyboard"';

function overlayWrappers(): string[] {
  return allPaths(join(src, 'components/ui'))
    .filter((path) => path.endsWith('.vue'))
    .filter((path) => focusReturningPart.test(readFileSync(path, 'utf8')))
    .map((path) => relative(src, path).replace(/\\/g, '/'));
}

describe('focus after an overlay closes', () => {
  it('spots a wrapper of a focus-returning reka part', () => {
    expect(focusReturningPart.test('import {\n  DialogContent,\n  DialogPortal,\n} from \'reka-ui\'')).toBe(true);
    expect(focusReturningPart.test('import type { DialogContentEmits } from \'reka-ui\'')).toBe(false);
    expect(focusReturningPart.test('import { DialogContent } from \'@/components/ui/dialog\'')).toBe(false);
  });

  it('hands the focus back only after a keyboard close in every overlay wrapper', () => {
    const wrappers = overlayWrappers();
    const unbound = wrappers.filter((path) =>
      !readFileSync(join(src, path), 'utf8').includes(keyboardOnlyFocusReturn));

    expect(wrappers).toHaveLength(8);
    expect(unbound).toEqual([]);
  });
});

// Stoney 2026-10-20 ("need i say more about this?"): the account menu opened with a focus ring on its first item. A
// panel that opens by a mouse or touch puts the focus on itself, not on its first button (lib/focusReturn.ts). Menus
// and selects already focus their own content, so only the six wrappers of a focus-scope panel bind it.
const pointerOpenFocus = '@open-auto-focus="focusPanelOnPointerOpen"';
const focusScopePanels = [
  'components/ui/alert-dialog/AlertDialogContent.vue',
  'components/ui/dialog/DialogContent.vue',
  'components/ui/dialog/DialogScrollContent.vue',
  'components/ui/drawer/DrawerContent.vue',
  'components/ui/popover/PopoverContent.vue',
  'components/ui/sheet/SheetContent.vue',
] as const;

describe('focus when an overlay opens', () => {
  it('puts the focus on the panel after a mouse open in all six focus-scope panel wrappers', () => {
    const unbound = focusScopePanels.filter((path) =>
      !readFileSync(join(src, path), 'utf8').includes(pointerOpenFocus));

    expect(unbound).toEqual([]);
  });
});

// Stoney 2026-10-19: "extra useless dropdowns". A menu that holds one item is a click with nothing to choose: that
// item belongs on the page as its own button or switch. A menu whose items come from a v-for is a list, never one.
const menuBlock = /<DropdownMenuContent\b[\s\S]*?<\/DropdownMenuContent>/g;
const menuEntry = /<DropdownMenu(?:Checkbox|Radio)?Item\b([^>]*)>|<DropdownMenuSub\b/g;

function singleItemMenus(text: string): number {
  return [...text.matchAll(menuBlock)].filter((block) => {
    const entries = [...block[0].matchAll(menuEntry)];

    return entries.length < 2 && !entries.some((entry) => (entry[1] ?? '').includes('v-for'));
  }).length;
}

function singleItemMenusIn(path: string): number {
  const text = readFileSync(join(src, path), 'utf8');

  return singleItemMenus(text);
}

describe('menus', () => {
  it('spots a menu with one item, and passes a list or a menu of two', () => {
    const one = '<DropdownMenuContent><DropdownMenuCheckboxItem :model-value="on" /></DropdownMenuContent>';
    const two = '<DropdownMenuContent><DropdownMenuItem>a</DropdownMenuItem><DropdownMenuItem>b</DropdownMenuItem></DropdownMenuContent>';
    const list = '<DropdownMenuContent><DropdownMenuItem v-for="x in xs">x</DropdownMenuItem></DropdownMenuContent>';

    expect(singleItemMenus(one)).toBe(1);
    expect(singleItemMenus(two)).toBe(0);
    expect(singleItemMenus(list)).toBe(0);
  });

  it('opens no menu that holds a single item', () => {
    const offenders = allPaths(src)
      .filter((path) => path.endsWith('.vue'))
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => !path.startsWith('components/ui/'))
      .filter((path) => singleItemMenusIn(path) > 0);

    expect(offenders).toEqual([]);
  });
});

describe('viewer pages', () => {
  // The operator's own viewer chat (/app/viewer-chat) is the one route with the old prefix; every other viewer page is a
  // viewer-* route under /me/:channel with its navigation in views/viewer/nav.
  const operatorViewerChat = 'dashboard-viewer-chat';

  it('names no viewer route dashboard-viewer-*', () => {
    const offenders = allPaths(src)
      .filter((path) => /\.(ts|vue)$/.test(path) && !path.includes('__tests__'))
      .filter((path) => (readFileSync(path, 'utf8').match(/dashboard-viewer-[a-z-]+/g) ?? [])
        .some((name) => name !== operatorViewerChat))
      .map((path) => relative(src, path).replace(/\\/g, '/'));

    expect(offenders).toEqual([]);
  });
});

// Stoney 2026-10-23: kitte switched to his profile and saw her own scripts. 120 paths of the API name no channel
// (scripts, giveaways, quotes, assets, audio mix, data sources...), so the bot resolves the channel from the call,
// and only the client sends the channel being managed. A call that bypasses the client with its own fetch would
// bring the bug back for that path. These files do and name no channel on purpose.
const bypassesClientAllowlist = new Set<string>([
  'api/client.ts',
  'api/auth.ts',
  'api/system.ts',
  'composables/useReadAloud.ts',
]);

describe('calls to the bot', () => {
  it('go through the client, which names the channel being managed, except the files on the list', () => {
    const offenders = allPaths(src)
      .filter((path) => /\.(ts|vue)$/.test(path) && !/__tests__|\.spec\.ts$|schema\.d\.ts$/.test(path))
      .map((path) => relative(src, path).replace(/\\/g, '/'))
      .filter((path) => !bypassesClientAllowlist.has(path))
      .filter((path) => {
        const text = readFileSync(join(src, path), 'utf8');

        return /(?<![.\w])fetch\(\s*[`'"][^`'"]*\/api\/v/.test(text);
      });

    expect(offenders).toEqual([]);
  });
});

// Stoney 2026-10-26: "make sure [create, edit and delete] have a stable spot in the ui ... the same for the
// search, i want the search to be baked into the list header like pipeline and overlay". A list page puts its
// search in <ListPanel> (the list head), its create button in <PageCreateButton>, its editor delete in
// <EditorFooter> and its row edit/delete in <RowEditDelete>. A view never builds those by hand.
// Phase 1 moved Pipelines, Overlays, Commands, Timers (and chat triggers), Scripts and Event responses.
// The files below are the PHASE 2 lane (the other 19 list pages of the list layout inventory, plus a few
// surfaces that only look like a list page); a file leaves its list when it moves, and a file that is clean
// must leave it (the "keeps no clean file" tests).
const handSearchInHead = /<template\s+#actions[^>]*>(?:(?!<\/template>)[\s\S])*?<SearchField\b/;
const handSearchInHeadAllowlist = [
  // Admin and people lists, and the permissions area list: head searches built by hand (phase 2).
  'views/dashboard/admin/AdminSupportView.vue',
  'views/dashboard/admin/AdminTenantsView.vue',
  'views/dashboard/admin/components/TenantMembersPanel.vue',
  'views/dashboard/people/PeopleView.vue',
  'views/dashboard/settings/permissions/components/ActionAreaList.vue',
] as const satisfies readonly string[];

const rawSearchInput = /<Input\b[^>]*(?:search|query)[^>]*>/i;
const rawSearchInputAllowlist = [
  // List pages with a bare search Input above the card (phase 2): picklists, quotes.
  'views/dashboard/picklists/PickListsView.vue',
  'views/dashboard/quotes/QuotesView.vue',
  // Pickers inside a popover or dialog: a search that narrows a choice, not a list page head.
  'views/dashboard/admin/components/ActAsDialog.vue',
  'views/dashboard/admin/components/AuditFilterPicker.vue',
  'views/dashboard/admin/components/SupportPersonPicker.vue',
  'views/dashboard/obs/components/ObsHotkeysPanel.vue',
  'views/dashboard/settings/permissions/components/PersonPicker.vue',
  'views/dashboard/stream/CategoryPicker.vue',
] as const satisfies readonly string[];

const handDeleteButton = /~icons\/nnz\/(?:trash|delete)/;
const handDeleteButtonAllowlist = [
  // List pages not moved yet (phase 2 lane): row-end or card delete icons written by hand.
  'views/dashboard/economy/components/CatalogPanel.vue',
  'views/dashboard/economy/components/EarningRules.vue',
  'views/dashboard/economy/components/LeaderboardsPanel.vue',
  'views/dashboard/economy/components/SavingsJars.vue',
  'views/dashboard/giveaways/GiveawaysView.vue',
  'views/dashboard/picklists/PickListRow.vue',
  'views/dashboard/quotes/QuoteRow.vue',
  'views/dashboard/settings/api-devices/components/TokensTable.vue',
  'views/dashboard/settings/custom-event-feeds/components/EventFeedRow.vue',
  'views/dashboard/settings/escalation/components/LadderPanel.vue',
  'views/dashboard/settings/permissions/components/PersonGrantsPanel.vue',
  'views/dashboard/settings/webhooks/components/InboundWebhookRow.vue',
  'views/dashboard/settings/webhooks/components/OutboundWebhookRow.vue',
  'views/dashboard/sounds/SoundClipRow.vue',
  'views/dashboard/tts/LexiconPanel.vue',
  'views/dashboard/voice-triggers/components/VoiceTriggerEditor.vue',
  // Other surfaces with a delete icon that are not a list page's entity delete (phase 2 decides each).
  'views/dashboard/admin/AdminFeatureFlagsView.vue',
  'views/dashboard/admin/components/TenantActions.vue',
  'views/dashboard/media-share/components/MediaShareQueueRow.vue',
  'views/dashboard/music/MusicView.vue',
  'views/dashboard/settings/EngagementView.vue',
  'views/dashboard/settings/federation/components/FederationPeerRow.vue',
  'views/dashboard/settings/permissions/components/TeamRolesPanel.vue',
  // Not an entity delete at all: a chat line action, a list item removed inside an editor, a step in a pipeline.
  'views/dashboard/chat/components/ChatLine.vue',
  'views/dashboard/overlays/components/StringListEditor.vue',
  'views/dashboard/pipelines/components/StepInspector.vue',
] as const satisfies readonly string[];

function viewText(path: string): string {
  return readFileSync(join(src, path), 'utf8');
}

function listLayoutOffenders(pattern: RegExp, allow: readonly string[]): string[] {
  return viewFiles()
    .filter((path) => pattern.test(viewText(path)))
    .filter((path) => !allow.includes(path));
}

function listLayoutStale(pattern: RegExp, allow: readonly string[]): string[] {
  return allow.filter((path) => !pattern.test(viewText(path)));
}

describe('list layout', () => {
  it('spots a search built by hand in a panel head', () => {
    const drifted = '<PanelCard><template #actions><SearchField v-model="q" /></template></PanelCard>';
    const clean = '<ListPanel v-model="q" /><template #actions><Button /></template><SearchField />';

    expect(handSearchInHead.test(drifted)).toBe(true);
    expect(handSearchInHead.test(clean)).toBe(false);
  });

  it('builds no search in a panel head by hand, except the phase 2 files', () => {
    expect(listLayoutOffenders(handSearchInHead, handSearchInHeadAllowlist)).toEqual([]);
  });

  it('keeps no clean file on the head search list', () => {
    expect(listLayoutStale(handSearchInHead, handSearchInHeadAllowlist)).toEqual([]);
  });

  it('spots a raw search Input', () => {
    expect(rawSearchInput.test('<Input v-model="search" :placeholder="p" />')).toBe(true);
    expect(rawSearchInput.test('<Input v-model="draft.name" data-field="name" />')).toBe(false);
  });

  it('builds no raw search Input in a view, except the phase 2 files and the picker popovers', () => {
    expect(listLayoutOffenders(rawSearchInput, rawSearchInputAllowlist)).toEqual([]);
  });

  it('keeps no clean file on the raw search list', () => {
    expect(listLayoutStale(rawSearchInput, rawSearchInputAllowlist)).toEqual([]);
  });

  it('spots a hand-written delete icon', () => {
    expect(handDeleteButton.test('import Trash from \'~icons/nnz/trash\';')).toBe(true);
    expect(handDeleteButton.test('import Plus from \'~icons/nnz/add\';')).toBe(false);
  });

  it('writes no delete button by hand in a view, except the phase 2 files', () => {
    expect(listLayoutOffenders(handDeleteButton, handDeleteButtonAllowlist)).toEqual([]);
  });

  it('keeps no clean file on the delete list', () => {
    expect(listLayoutStale(handDeleteButton, handDeleteButtonAllowlist)).toEqual([]);
  });
});
