# UX rules for the code editor and the pipeline editor

Binding for every slice of the 2026-10-06 editor overhaul (SHORTCOMINGS-EXECUTION-PLAN.md, section
"OWNER REQUEST 2026-10-06"). A slice names the rule numbers it serves; its Done-when includes the rule's
check. The Sleak skill and frontend-design-system.md still decide tokens and hierarchy.

Goal: "CodePen goes VS Code" for streamers who are not programmers, with power-user depth.
Rules ranked by impact. Each rule has a principle, a rule, and a check.
Check type: P = person walks through it, S = script or UI test can run it.

## Evidence status
Fetched and read this run:
- Nielsen 10 heuristics: https://www.nngroup.com/articles/ten-usability-heuristics/ (all 10 names confirmed)
- Bret Victor, Learnable Programming: https://worrydream.com/LearnableProgramming/ (read vocabulary, follow flow, see state, create by reacting, create by abstracting)
- Doherty threshold 400 ms (Doherty and Thadani, IBM Systems Journal 1982): https://lawsofux.com/doherty-threshold/
- Sleak skill files read: SKILL.md, ux.md, content-copy.md, components.md, principles.md (C:/Users/patri/.claude/skills/sleak/references/).
- frontend-design-system.md section 4 (catalogue) read. Existing patterns it lists: ConfirmDialog, InlineError, PipelineBindPicker, FileTree, ResizableSplit, TemplateHelpersDialog, Tooltip, Combobox, Skeleton, Toast (file lines 242-260).

Cited from known literature, NOT re-fetched this run (check before quoting):
- Hick 1952 / Hyman 1953 (Hick's law); Fitts 1954; Shneiderman 1983 "Direct manipulation" (IEEE Computer);
  Sweller 1988 cognitive load theory; Sweller and Cooper 1985 worked-example effect;
  Zeigarnik 1927; Hull 1932 goal-gradient; Kivetz et al. 2006 (goal gradient in loyalty cards);
  Miller 1956 (7 +/- 2, weak evidence, use Cowan 2001 "4 chunks" instead);
  Jakob's law (NN/G); Norman, "The Design of Everyday Things" (affordances, mapping, feedback).

## Ranked rules

### Tier 1: first minute and feedback loop (highest impact)

1. Never show a blank editor. [Recognition over recall; Nielsen 6; Zeigarnik]
   Rule: a new or empty script/pipeline shows a starter picker (3 to 5 real starters, e.g. "Welcome new follower", "Shoutout on raid") plus "Start empty".
   Check (S): open the editor with an empty doc; assert the picker exists and no empty text area is focused. (P): a first-time user gets to a running starter in 2 clicks.

2. Every starter and every action card has a filled, runnable example. [Worked-example effect, Sweller; Victor "create by reacting"]
   Rule: picking a starter yields a working result that already does something visible; the user edits it, never writes from zero.
   Check (S): for each starter in the catalogue, run it against a test event; assert it produces output without edits.

3. Preview updates within 300 ms of a change. [Doherty 400 ms; Nielsen 1; Victor immediate connection]
   Rule: preview, validation and output refresh within 300 ms of the last keystroke or drag (debounce <= 150 ms). Slower work shows a progress state within 100 ms.
   Check (S): UI test types a char, measures time to preview repaint, p95 < 300 ms. Fail the test above 400 ms.

4. Edits, drags and toggles respond within 100 ms. [Doherty; Sleak ux.md rule 6; Shneiderman: rapid, incremental, reversible]
   Rule: pressed state, spinner or optimistic update on every action.
   Check (S): measure input-to-first-paint on every button and node drag; p95 < 100 ms.

5. An error points to the exact place and offers the fix. [Nielsen 9; Sleak content-copy rule 6]
   Rule: error text = what failed + what to do, in plain words, with the line (code) or the node (pipeline) highlighted. A one-click fix when one exists ("Add missing variable {user}"). No "Oops", no stack traces as the main message.
   Check (S): feed 10 known-bad scripts and 10 broken pipelines; assert each error has a location and a fix text, and no banned words. (P): read only the message; can you fix it?

6. Edits are reversible: undo/redo everywhere, and a safe test mode. [Nielsen 3 and 5; Shneiderman reversibility]
   Rule: Ctrl/Cmd+Z works for text, node moves, deletes, and property changes. "Test run" never posts to real chat unless the user picks "Run live". Delete shows undo toast, not a modal, for anything restorable.
   Check (S): do 20 mixed actions, undo 20, assert the doc equals the start. Run test mode, assert zero outbound chat calls.

7. Unsaved or lost work cannot happen silently. [Nielsen 5; Sleak ux.md rule 7]
   Rule: autosave draft locally every change; leaving with unsaved edits warns; an error never clears the editor; draft vs live is always visible ("Draft", "Live").
   Check (S): force a save error and a reload; assert content restored. (P): kill the tab; reopen; work is back.

### Tier 2: structure and learnability

8. Plain words first, code words on demand. [Nielsen 2 match real world; Victor "read the vocabulary"]
   Rule: nodes and actions use streamer language ("When someone follows", "Say in chat"), not API names (channel.follow, SendMessage). The technical name sits behind a "show code" toggle.
   Check (S): lint every user-visible node/action label against a banned-term list (event ids, namespaces). (P): 5 non-coders name what each card does; 4 of 5 right.

9. One-line plain description on every action card and variable. [Recognition over recall; Nielsen 6 and 10]
   Rule: every card, trigger and template variable shows <= 1 line of plain description on hover and in the picker, with a live sample value ("{user} = Stoney_Eagle").
   Check (S): for every registered action/variable, assert description non-empty, <= 100 chars, and sample value present.

10. Pickers, not typing, for anything with a closed set. [Recognition over recall; Hick; existing Combobox/EntityPickerField]
    Rule: user names, rewards, sounds, scenes, commands, variables are picked from searchable lists, never typed as raw ids. Autocomplete on `{` shows variables. A typed value that is not in the set is flagged immediately.
    Check (S): scan editor property schemas; any string field bound to a known entity type must use a picker component. Grep for raw text fields with ids.

11. Progressive disclosure: simple view by default, full power one click away. [Progressive disclosure, Nielsen 7 and 8; Sweller cognitive load]
    Rule: three layers: (a) Simple form/blocks, (b) pipeline graph, (c) full code editor with VS Code features (find, multi-cursor, minimap, command palette). Switching layers keeps the same document. Advanced node options sit under "More options", collapsed.
    Check (S): default view of a new action shows <= 5 visible fields. (P): a power user reaches Ctrl+Shift+P and multi-cursor without leaving the page.

12. Limit choices per decision point. [Hick's law; Sleak "cap visible lists at five"]
    Rule: add-node menu shows <= 7 grouped categories, with search; each category <= 7 items before scrolling, with "Recent" and "Suggested next" on top. Toolbars: one primary action per group (Sleak core rule 1).
    Check (S): count top-level items in add menu and toolbar; fail above 7. Count filled-primary buttons per group; fail above 1.

13. Direct manipulation of the pipeline. [Shneiderman 1983; Victor "see the state"]
    Rule: drag to connect, drag to reorder, click a node to edit in place; the thing you touch is the thing that changes. Invalid connections refuse with a reason on hover, before drop (error prevention). Show the data on each link from the last test run.
    Check (S): UI test drags a valid and an invalid connection; assert valid connects, invalid shows reason without mutating the graph. (P): last test values visible on links.

14. Follow the flow: step-through of a test run. [Victor "follow the flow" and "see the state"]
    Rule: "Test" runs with a sample event; each node lights up in order and shows its input and output. A failing node stops the run and shows why.
    Check (S): run a 5-node test; assert 5 ordered node-status events and per-node input/output payloads recorded.

15. Dangerous actions are guarded in proportion to harm. [Nielsen 5; Sleak ux.md rule 2]
    Rule: ban, timeout, spend points, post to chat, delete are marked with a clear risk label on the card. Destructive buttons are demoted until the point of no return; the confirm names the action ("Delete pipeline 'Raid welcome'"). Mass-effect actions show blast radius (how many users/channels) before saving.
    Check (S): for each action flagged dangerous, assert risk label and confirm text contains the object name. (P): try to delete by accident; fail to.

### Tier 3: onboarding and motivation

16. Goal-gradient setup checklist, not a tour. [Hull 1932 goal gradient; Kivetz 2006; Zeigarnik; Sleak "show don't tell"]
    Rule: first run shows a numbered checklist of at most 4 steps with visible progress ("2 of 4"), starting pre-completed where honest (e.g. step 1 done by picking a starter). No pop-over tour that blocks the editor. One next step at the end.
    Check (S): first-run state has <= 4 steps, a progress indicator, and no blocking modal. (P): time-to-first-working-automation under 3 minutes, measured with 5 new users.

17. Immediate first win: a first result visible before any setup. [Victor "create by reacting"; Doherty]
    Rule: preview/test works with sample data without any account link or channel setup; real data swaps in later.
    Check (S): with a fresh account and no integrations, a starter's Test shows output.

18. Create by abstracting: concrete first, variables later. [Victor "create by abstracting"; Sweller fading]
    Rule: user can type a literal ("Thanks Stoney!"), then click a word to turn it into a variable ({user}). Same for repeated nodes: "make this reusable".
    Check (P): replace a literal with a variable in <= 2 clicks. (S): UI test of the click-to-variable action.

19. Contextual help beats a docs page. [Nielsen 10; Sleak "help text <= 1 line"]
    Rule: every card has a "?" that opens a short example in place; the docs link opens the single-topic page for that card. Empty states explain what goes here and offer the first action.
    Check (S): every card type has a help example id; every empty state has a primary action button.

### Tier 4: interaction mechanics and consistency

20. Large, close targets; keyboard for everything. [Fitts's law; Sleak components.md; Nielsen 7]
    Rule: pointer targets >= 32 px on desktop, >= 44 px on touch; gaps between adjacent buttons >= 8 px; the action sits beside the thing it acts on (node toolbar on the node). Every action reachable by keyboard; shortcuts shown in tooltips; command palette lists all actions.
    Check (S): measure hit boxes in UI test; run an a11y audit; assert each registered action has a palette entry.

21. Consistency and standard patterns. [Nielsen 4; Jakob's law]
    Rule: VS Code keybindings in the code editor (Ctrl+S, Ctrl+F, Ctrl+Z, Ctrl+/); node-editor conventions (space-drag pan, scroll zoom, Delete removes). Same component for the same job across both editors. Only shadcn catalogue components, no hardcoded colors or dp.
    Check (S): keybinding table test; the design-system enforcement test (spec section 8) passes. (P): a VS Code user needs no keybinding lookup.

22. Status is always visible. [Nielsen 1]
    Rule: a persistent status line shows saved/saving/error, draft/live, connected/disconnected, and run state. Layout does not shift when states change (reserve space).
    Check (S): screenshot diff of state transitions shows no layout shift; assert status element exists in every editor state.

23. Validation timing: on blur or pause, not per keystroke, except where inline helps. [Sleak ux.md forms; Nielsen 9]
    Rule: syntax squiggles after a 300-500 ms pause; field errors on blur; inline immediately only for character limits and name clashes. Errors keep the user's input.
    Check (S): type a half-written value; assert no error until pause/blur; assert input intact after the error.

24. Cognitive load: one thing in focus, chunks of about 4. [Cognitive load theory, Sweller 1988; Cowan 2001]
    Rule: the properties panel shows only the selected node; groups of related fields in chunks of <= 5; long pipelines can collapse into a named group; the canvas offers auto-layout. No more than 1 accent-filled element in view (Sleak scarce accent).
    Check (S): count visible fields per panel (<= 7 incl. collapsed headers) and accent-filled elements per screen (<= 1). (P): 5-node and 30-node pipeline both stay readable.

25. Action-first copy with concrete numbers. [Sleak content-copy; Nielsen 2]
    Rule: buttons are verb + object ("Save draft", "Test run"), headlines name the task, no filler words ("simply", "just"), no "Oops". Specific limits stated ("Max 500 characters").
    Check (S): lint all editor strings against banned list (Get started, Submit, Oops, Please, simply, just) and require verb-first on primary buttons.

## Handling the two audiences
- Streamers: rules 1, 2, 8, 9, 10, 13, 14, 16, 17 carry them. Default layer is (a) or (b).
- Power users: rules 11, 20, 21 carry them. Layer (c) must feel like VS Code: no capability removed by the simple layers.
- Never fork the document: one source of truth, three views (rule 11).

## Suggested acceptance script (one gate)
A single UI test suite "editor-ux-gates" covering the S checks: timing (3, 4), starter/example runs (1, 2), error shape (5), undo (6), label lint (8, 25), description coverage (9), picker coverage (10), add-menu counts (12), a11y and targets (20), design-system enforcement (21). P checks go in a one-page 5-person walkthrough run before each editor release.

## Not checked
- Whether the current editors already meet any rule (no code audit done in this task).
- The exact latency of the current Wasm preview path.
- Sources under "cited from known literature" were not re-fetched.
