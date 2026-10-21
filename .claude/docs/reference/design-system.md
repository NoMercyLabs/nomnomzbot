# Design System

- **Source of truth:** shadcn/ui (new-york), ported 1:1 to the Vue dashboard (shadcn-vue) — full spec in `.claude/docs/design/spec/frontend-design-system.md`.
- **Tokens:** shadcn's closed **OKLCH** contract (Tailwind v4), **neutral base**; the accent is **dynamic — derived at runtime from the signed-in user's Twitch chat color** (subtle, light + dark).
- **Components:** a closed catalogue, variants-as-data, built on the shadcn-vue primitives in `dashboard/src/components`. **Icons:** the designer's pack (Line style), imported as `~icons/nnz/<name>`.
- **Enforcement (as-built, 2026-10-21):** the CI dashboard job runs `npm run lint` (oxlint, ESLint with the project rules in `dashboard/eslint-rules`, cspell spelling, and jscpd duplicate-code) and `npx vitest run`, which includes the design guard specs (`dashboard/src/__tests__/structure.spec.ts` and the per-area guard lists). They fail on a hard-coded color or size, an off-catalogue component, and a hand-written formatter.
- The old Figma (`MkKBuW2Ee6T5jC8fCtZsM0`) is discarded; the HTML mockups (`nomnomzbot-design` — external archived repo, not in this tree) are a loose historical reference only.
