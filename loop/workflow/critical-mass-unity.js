export const meta = {
  name: 'critical-mass-unity-loop',
  description: 'Critical Mass (Unity) build loop: batches from loop/items/_ORDER.md; up to 12 code-only builders in worktrees (the launch builder limit, U-037; never run Unity); one Opus integrator per batch running loop/bin/integrate.py (project setup, one compile; coupling is per item, U-033: an uncoupled item drops alone on the first red, a red in the coupled set goes to one Opus fix seat, max 2 rounds, and the set lands whole or halts); one PR to main per batch; one Opus reviewer per landed batch (BLOCKING stops the next batch). Scheduling is plain code; no idle, dispatch or heartbeat agents.',
  whenToUse: 'Launch FRESH only, with args = the JSON in loop/logs/launch-args.json written by loop/bin/launch.sh. Never resumeFromRunId (the resume cache is positional). Record the returned taskId and runId at once with: python3 loop/bin/cmloop.py record-launch --task-id <w...> --run-id <wf_...>; builders refuse to start until both are recorded.',
  phases: [
    { title: 'Build', detail: 'builders: halt-check, lane worktree on loop/<ID>, code + named NUnit tests + docs, scope gate (git only), push. Never Unity.' },
    { title: 'Integrate', detail: 'one Opus integrator per batch: loop/bin/integrate.py (merge, gates, drop the red item, PR to main, squash, prove landed), spend check' },
    { title: 'Fix', detail: 'coupled batches only (U-026): one Opus fix seat repairs the red item inside its files_allowed; at most 2 rounds' },
    { title: 'Review', detail: 'one Opus reviewer per landed batch, a different lens; BLOCKING findings in loop/review/<batch>.md stop the next batch' },
  ],
}

// ───────────────────────────── args (written by loop/bin/launch.sh; no filesystem access in here) ─────────────────────────────
const A = (typeof args !== 'undefined' && args) || {}
const BAD = []
if (!(Number(A.capTokens) > 0)) BAD.push('capTokens missing (launch.sh refuses without --cap)')
if (!Array.isArray(A.items)) BAD.push('items missing')
if (typeof A.mainRoot !== 'string' || !A.mainRoot) BAD.push('mainRoot missing')
const MAIN = A.mainRoot || '/Users/wilsonguenther/critical-mass-unity'
const LANES = A.lanesRoot || '/Users/wilsonguenther/critical-mass-unity-lanes'
const REPO = A.repo || 'wilsonguenther-dev/critical-mass-unity'
const TAG = String(A.runTag || 'untagged')
const CM = `python3 ${MAIN}/loop/bin/cmloop.py`
const BUILDER_CEILING = 12 // U-037: the owner's machine-wide agent limit for this loop
const MAX_BUILDERS = Math.max(1, Math.min(BUILDER_CEILING, Number(A.maxBuilders) || 5)) // from launch.sh --builders N
const OPUS = 'opus' // U-046: every seat that builds, fixes, integrates or reviews is Opus; tightly_defined never changes it
const BATCH_MAX = Math.max(1, Math.min(6, Number(A.batchMax) || 6))
const MAX_RETURNS = Math.max(0, Number(A.maxReturns) || 2)
const MAX_FIX_ROUNDS = 2 // U-026
const PAYLOAD_CAP = 40000 // bytes; agents write details to files and return paths
const CAPS = { builder: 60, integrator: 40, reviewer: 50, fixer: 60 }

// ───────────────────────────── guardrail block (verbatim in every seat) ─────────────────────────────
const GUARD = (cap, role) => `
GUARDRAILS (HARD, every seat of this loop):
- Do NOT spawn subagents. Never use the Agent, Task or Workflow tools. You are the one agent on this one task; never delegate.
- Cap yourself at ${cap} tool calls. If you run out, push what you have (builders) and report honestly what is incomplete.
- NEVER pkill / killall / kill by pattern. Kill only a PID you started yourself, after checking it with \`ps -p <pid> -o command=\`.
- ${role === 'builder'
    ? 'NEVER run Unity: no Unity binary, no tools/unity-*.sh, no loop/gates/* except loop/gates/scope, no builds, no tests. You write code and tests only; the integrator compiles once per batch.'
    : 'Unity runs only through loop/bin/integrate.py and loop/gates/*, under the compile lock, one Unity process at a time.'}
- NEVER read an exit code through a pipe. Run the command bare, then \`echo $?\`.
- Never report success you have not observed. Written is not compiled is not tested is not merged.
- Never edit DECISIONS.md, loop/items/*.md, loop/bin/*, loop/gates/*, loop/watchers/* or loop/workflow/* (decision ids are allocated by the orchestrator only, U-011).
- A missing answer is an OPEN question: write it with your recommendation to ${MAIN}/loop/logs/questions/<ID>.md and do not decide it.
- Never push to main, never force-push, never use --admin or --no-verify, never open or merge a PR yourself (integrate.py does).
- Send nothing outbound (no texts, email, posts). Never modify ~/.claude/settings.json. Never touch the main checkout's working tree.
- Agents never play-test and never open GUI apps or visible windows.
- Stamp any time you write from \`date -u\`.
- Keep your final structured answer small (ids, counts, paths); detail goes to files.
`

// ───────────────────────────── schemas (small by design) ─────────────────────────────
const BUILD_SCHEMA = {
  type: 'object',
  properties: {
    status: { type: 'string', enum: ['built', 'blocked', 'halted'] },
    branch: { type: 'string' },
    head: { type: 'string' },
    files_changed: { type: 'integer' },
    tests_written: { type: 'array', items: { type: 'string' }, maxItems: 30 },
    questions_file: { type: 'string' },
    note: { type: 'string', maxLength: 400 },
  },
  required: ['status', 'note'],
}
const INT_SCHEMA = {
  type: 'object',
  properties: {
    result: { type: 'string', enum: ['landed', 'all-dropped', 'red-coupled', 'halt-coupled', 'error', 'halted'] },
    red_items: { type: 'array', items: { type: 'string' }, maxItems: 10 },
    evidence: { type: 'string' },
    blocking_open: { type: 'integer' },
    landed: { type: 'array', items: { type: 'string' }, maxItems: 10 },
    dropped: {
      type: 'array', maxItems: 10,
      items: { type: 'object', properties: { id: { type: 'string' }, gate: { type: 'string' }, counted: { type: 'boolean' }, parked: { type: 'boolean' } }, required: ['id', 'gate', 'counted', 'parked'] },
    },
    pr: { type: ['integer', 'null'] },
    report: { type: 'string' },
    spend_action: { type: 'string', enum: ['ok', 'stop-dispatch', 'halt', 'no-data'] },
    note: { type: 'string', maxLength: 600 },
  },
  required: ['result', 'landed', 'dropped', 'spend_action', 'note'],
}
const FIX_SCHEMA = {
  type: 'object',
  properties: { status: { type: 'string', enum: ['fixed', 'cannot'] }, items: { type: 'array', items: { type: 'string' }, maxItems: 10 }, note: { type: 'string', maxLength: 400 } },
  required: ['status', 'note'],
}
const REVIEW_SCHEMA = {
  type: 'object',
  properties: { findings: { type: 'integer' }, blocking: { type: 'integer' }, file: { type: 'string' }, note: { type: 'string', maxLength: 400 } },
  required: ['findings', 'blocking', 'file'],
}

// ───────────────────────────── prompts ─────────────────────────────
const builderPrompt = (it, n) => `You are BUILDER for item ${it.id} ("${it.title}") in batch ${n} of the Critical Mass Unity build loop (run ${TAG}).
${GUARD(CAPS.builder, 'builder')}
STEPS (in order):
1. Run \`bash ${MAIN}/loop/bin/halt-check.sh\` bare. If it exits non-zero, stop: return status "halted" with its message as the note.
2. Create your lane: \`${CM} lanes create ${it.id}${it.returns > 0 ? ` --base origin/loop/${it.id}` : ''}\`. Work only in ${LANES}/${it.id} on branch loop/${it.id}.
3. Read the item: ${MAIN}/loop/items/${it.id}.md. It carries the method, files_allowed, files_forbidden, tests_required and pass_check. Follow the method exactly; it has no options to choose between.${it.returns > 0 ? `
   This item was returned ${it.returns} time(s). The lane is cut from origin/loop/${it.id} and lanes create has already merged origin/main into it (U-038), so upstream code that landed since is present. FIRST read ${MAIN}/loop/state/failures/${it.id}.md (the gate output that dropped it) and fix exactly that. At most ${MAX_RETURNS} returns; the next red parks it for the architect.` : ''}
4. Write the code, the NUnit tests named in tests_required (exact names), and any docs the item names, all in this branch. Touch only files_allowed. Never write a .meta, .unity, .prefab, .asset or .mat file and never choose a GUID (U-027): Unity generates metas on the integrator's import and editor-made assets come from CriticalMass.Editor.ProjectSetup.Apply; the scope gate drops a branch that carries one. The one hand-written Unity data file is Controls.inputactions (plain JSON). The Sim assembly never references UnityEngine (U-002).
   Upstream contract (panel revision 2026-11-12): ${(it.waits_on || []).length ? `this item waits on ${(it.waits_on || []).join(', ')}. Any of those not yet on origin/main is being built right now in parallel and is NOT in your lane. For each, read ${MAIN}/loop/items/<ID>.md (its Create, API and signature tables) and ${MAIN}/loop/items/_CONVENTIONS.md section 11, and call exactly those signatures, verbatim. Never stub, redeclare or guess an upstream member. A missing upstream type is expected and is not an open question; a signature the upstream spec does not give is one (step 7).` : 'this item waits on nothing.'} Never read another item's lane under ${LANES}.
   Browser oracle: read it ONLY with \`git -C /Users/wilsonguenther/critical-mass show 09e0904:<path>\`. Never cat, ls or grep the /Users/wilsonguenther/critical-mass working tree: it is checked out at an older commit and differs from the oracle.
5. Run \`${MAIN}/loop/gates/scope --item ${it.id} --ref HEAD --base origin/main --project ${LANES}/${it.id}\` bare (git only, no Unity). Fix any violation it lists.
6. Commit with subject "${it.id}: <what changed>" (never a line starting "loop-landed:"), then \`git push -u origin loop/${it.id}\`.
7. If the item cannot be built without an answer it does not give, write the question with your recommendation to ${MAIN}/loop/logs/questions/${it.id}.md, push nothing unfinished as if finished, and return status "blocked".
Return: status, branch, head (the pushed sha), files_changed, tests_written, questions_file (if any), note (one plain sentence).`

// U-030, the one sentence on order; identical to SETUP_ORDER in loop/bin/cmloop.py (a test compares them).
const SETUP_ORDER = 'CriticalMass.Editor.ProjectSetup.Apply runs on the integration branch after every item branch of the batch is merged and before the compile gate, on every batch (it is idempotent); nothing runs Unity on that branch before it.'

const integratorPrompt = (n, ids, cset, fixRound, culprit) => { const coupled = cset.length > 0; return `You are the INTEGRATOR for batch ${n} of the Critical Mass Unity build loop (run ${TAG}). Items, in this order: ${ids.join(', ')}. ${coupled ? `Coupled set (U-026, U-033): ${cset.join(', ')}; those land whole or not at all; fix rounds spent so far: ${fixRound} of ${MAX_FIX_ROUNDS}.${cset.length < ids.length ? ` Every other item is uncoupled and drops alone on its first red.` : ''}` : 'Every item is uncoupled: a red item is dropped on the first red.'}
${GUARD(CAPS.integrator, 'integrator')}
STEPS:
1. Run \`bash ${MAIN}/loop/bin/halt-check.sh --wait 0\` bare. Exit 3 (HALT/STALL) = return result "halted". Exit 4 or 6 = still integrate (the batch is already built).
2. Run, bare, from ${MAIN}: \`python3 ${MAIN}/loop/bin/integrate.py --batch ${n} --items ${ids.join(',')} --run-tag ${TAG}${coupled ? ` --coupled-set ${cset.join(',')} --fix-round ${fixRound}` : ''}${culprit ? ` --culprit ${culprit}` : ''}\`, then \`echo $?\`. It takes the compile lock, merges the item branches, then: ${SETUP_ORDER} After the compile it commits the files Unity generated (every new .meta, and what ProjectSetup.Apply wrote) as a harness commit, never an item's, and the clean-reimport gate proves a second import leaves git status clean (U-030). It runs the gates once, ${coupled ? 'and on a red writes the evidence file and stops (no drops)' : 'drops a red item at once (no fix attempts)'}, opens ONE PR to main and squash-merges a green batch, runs the soak gate on the new main, and prints one JSON line.
3. Only if its JSON flags a red with no item named: open the evidence or report file it names. If, and only if, an error line names a file:line that one item's diff created or changed, re-run step 2 ONCE adding \`${coupled ? '--culprit' : '--drop'} <ID>="<gate>: <the error line>"\`. Otherwise leave it for the architect.
4. Never edit code, never fix an item, never run gh pr create or gh pr merge yourself, never run Unity outside integrate.py.
5. Run \`python3 ${MAIN}/loop/bin/spend.py check\` bare, then \`echo $?\` (0 ok, 10 stop-dispatch, 11 halt, 12 no-data).
Return: result (landed | all-dropped | red-coupled | halt-coupled | error | halted), landed ids, dropped [{id, gate, counted, parked}], red_items and evidence (coupled red), blocking_open, pr, report (the path integrate.py printed), spend_action, note (one plain sentence).` }

const fixPrompt = (n, redIds, evidence, round) => `You are the FIX SEAT for coupled batch ${n} of the Critical Mass Unity build loop (run ${TAG}), fix round ${round} of ${MAX_FIX_ROUNDS} (U-026). Red item(s): ${redIds.join(', ')}.
${GUARD(CAPS.fixer, 'builder')}
STEPS:
1. Read the exact compiler/test output in ${evidence}. Read each red item's spec: ${redIds.map((id) => `${MAIN}/loop/items/${id}.md`).join(', ')}.
2. For each red item: \`${CM} lanes create <ID> --base origin/loop/<ID>\` (it merges origin/main into the lane first, U-038; if it reports a conflict, return "cannot" naming the files), repair ONLY files inside that item's files_allowed so the output above goes away, without changing what the spec asks for. Never write a .meta, .unity, .prefab, .asset or .mat file (U-027). Never touch another item's files, even if the error appears there: report that in the note instead.
3. Run \`${MAIN}/loop/gates/scope --item <ID> --ref HEAD --base origin/main --project ${LANES}/<ID>\` bare; fix any violation; commit "<ID>: fix round ${round}: <what>"; \`git push origin loop/<ID>\`; then \`${CM} lanes remove <ID>\`.
4. If the repair needs a change outside those files, or a decision the spec does not give, return status "cannot" and say why in one sentence.
Return: status (fixed | cannot), items (ids you pushed), note.`

const reviewerPrompt = (n, landedIds) => `You are the REVIEWER for batch ${n} of run ${TAG} of the Critical Mass Unity build loop. Items that landed in this batch: ${landedIds.join(', ')}.
${GUARD(CAPS.reviewer, 'reviewer')}
Your lens is NOT the builders' (they wrote to the item's method). Yours: (a) determinism of CriticalMass.Sim (no UnityEngine, no wall clock, no unseeded randomness, double math, fixed 30 Hz tick, U-002); (b) fidelity to the browser oracle (ported tests assert the same ticks, counts and outcomes; positions within 1e-4); (c) Runtime never writes game state; (d) anything outside its item's files_allowed.
Read only: \`git -C ${MAIN} fetch -q origin\`, the batch's squash commit on origin/main (\`git -C ${MAIN} log origin/main --grep '^loop-landed:' -n 5\`) and its diff, and the item files in ${MAIN}/loop/items/. Do not run Unity, do not edit code, do not open PRs.
Write ${MAIN}/loop/review/${TAG}-${n}.md (ruling 6), one line per finding:
  "- BLOCKING R${TAG}-${n}-<k>: <item id> <file:line> <what is wrong> <recommendation>"  for anything that must be fixed before the next batch
  "- ADVISORY R${TAG}-${n}-<k>: ..."  for the rest
Never write RESOLVED or WAIVED lines (the architect does). A BLOCKING line stops the next batch until the architect resolves it with an item or waives it with a U-### line.
Last step (U-040): run \`python3 ${MAIN}/loop/bin/spend.py batch-report --batch ${n} --tag ${TAG}\` bare, then \`echo $?\` (the per-seat-kind token and rounds report for this batch; a non-zero exit goes in your note, it never blocks).
Return: findings (count), blocking (count), file (path), note.`

// ───────────────────────────── scheduling: plain code ─────────────────────────────
let halted = null
let stopDispatch = null
const halt = (why) => { if (!halted) { halted = why; log(`HALT: ${why}`) } }

async function seat(prompt, opts, what) {
  if (halted) return null
  if (opts.model !== OPUS) { halt(`${what}: refused a seat on model ${opts.model} (U-046: every seat is Opus)`); return null }
  let r
  try {
    r = await agent(prompt, opts)
  } catch (e) {
    halt(`${what}: agent() threw (${String((e && e.message) || e).slice(0, 200)})`)
    return null
  }
  if (r === null || r === undefined) {
    halt(`${what}: agent() returned null, treated as a usage limit (check status.claude.com and the newest transcripts for "error" before relaunching fresh)`)
    return null
  }
  const size = JSON.stringify(r).length
  if (size > PAYLOAD_CAP) { halt(`${what}: returned ${size} bytes, over the 40 KB cap`); return null }
  return r
}

function semaphore(n) {
  let free = n
  const waiters = []
  return {
    take: () => (free > 0 ? (free--, Promise.resolve()) : new Promise((res) => waiters.push(res))),
    give: () => { const w = waiters.shift(); if (w) w(); else free++ },
  }
}
const slots = semaphore(MAX_BUILDERS) // builders run together (up to the launch's limit, ceiling 12); fix seat, integrator and reviewer run alone after them

const ITEMS = (A.items || []).filter((i) => i && i.owner === 'loop' && typeof i.id === 'string') // max-owned items are never dispatched
const byId = Object.fromEntries(ITEMS.map((i) => [i.id, { ...i, returns: Number(i.returns) || 0 }]))
const ORDER = ITEMS.map((i) => i.id) // stable order from the launcher (phase, then natural id): positional-cache safe
const BATCHES = Array.isArray(A.batches) ? A.batches.filter((b) => b && Array.isArray(b.items)) : [] // from _ORDER.md (U-026)
const landed = new Set(A.landed || [])
const parked = new Set(A.parked || [])
const setAside = new Map() // id -> reason (blocked by a question, or dropped uncounted too often)
const uncounted = {}
const reusable = new Set() // built and pushed, dropped only because an item it waits on was dropped
const landedThisRun = []
const batches = []
let batchNo = 0

async function buildOne(id, n) {
  await slots.take()
  try {
    if (halted || stopDispatch) return null
    const it = byId[id]
    const r = await seat(builderPrompt(it, n), {
      label: `build ${id}`, phase: 'Build', schema: BUILD_SCHEMA,
      model: OPUS, // binding ledger: Opus in every seat (panel revision 2026-11-12; the tightly_defined -> Sonnet switch is removed)
    }, `builder ${id}`)
    if (!r) return null
    if (r.status === 'halted') { stopDispatch = stopDispatch || `builder ${id}: ${r.note}`; return null }
    if (r.status === 'blocked') { setAside.set(id, `open question: ${r.questions_file || r.note}`); log(`${id} blocked: ${r.note}`); return null }
    return { id, ...r }
  } finally {
    slots.give()
  }
}

// Builds the given ids wave by wave (the plan's waves for this batch, else one wave), every wave through the shared
// slots (at most MAX_BUILDERS at once); returns the ids that are built, in stable order.
async function buildAll(ids, n, waves) {
  const need = ids.filter((id) => !reusable.has(id))
  const plan = (Array.isArray(waves) && waves.length ? waves.map((w) => w.filter((id) => need.includes(id))) : [need]).filter((w) => w.length)
  const left = need.filter((id) => !plan.flat().includes(id))
  if (left.length) plan.push(left)
  const done = []
  for (const w of plan) {
    if (halted || stopDispatch) break
    done.push(...(await parallel(w.map((id) => () => buildOne(id, n)))).filter(Boolean).filter((b) => b.status === 'built'))
  }
  return ids.filter((id) => reusable.has(id) || done.some((b) => b.id === id))
}

async function integrate(n, ids, cset, fixRound, culprit) {
  return seat(integratorPrompt(n, ids, cset, fixRound, culprit), {
    label: `integrate batch ${n}${fixRound ? ` (after fix ${fixRound})` : ''}`, phase: 'Integrate', schema: INT_SCHEMA, model: OPUS, effort: 'high',
  }, `integrator batch ${n}`)
}

function applyDrops(r) {
  for (const d of (r && r.dropped) || []) {
    if (!byId[d.id] || d._applied) continue
    d._applied = true
    if (d.parked) parked.add(d.id)
    else if (d.counted) { byId[d.id].returns += 1; reusable.delete(d.id) }
    else if (d.gate === 'dependency') reusable.add(d.id)
    else {
      uncounted[d.id] = (uncounted[d.id] || 0) + 1
      if (uncounted[d.id] > MAX_RETURNS) setAside.set(d.id, `dropped uncounted ${uncounted[d.id]} times (unattributed red, upstream type missing, or time box)`)
    }
  }
}

function applyIntegration(n, ids, r) {
  batches.push({ batch: n, items: ids, result: r ? r.result : 'null', landed: r ? r.landed : [], pr: r ? r.pr : null, report: r ? r.report : null })
  if (!r) return
  for (const id of r.landed || []) { landed.add(id); landedThisRun.push(id); reusable.delete(id) }
  applyDrops(r)
  log(`batch ${n}: ${r.result}; landed ${(r.landed || []).join(', ') || 'none'}; dropped ${(r.dropped || []).map((d) => d.id).join(', ') || 'none'}; spend ${r.spend_action}`)
  if (r.result === 'halted') halt(`integrator batch ${n}: ${r.note}`)
  if (r.result === 'error') halt(`integrator batch ${n} error: ${r.note} (report ${r.report || 'none'})`)
  if (r.result === 'halt-coupled') halt(`coupled batch ${n} halted for the architect: ${r.note} (evidence ${r.evidence || r.report})`)
  if (r.spend_action === 'halt') halt('spend cap reached (spend.py)')
  if (r.spend_action === 'stop-dispatch') stopDispatch = stopDispatch || 'spend at 90 % of the cap, or the hard stop (spend.py / clock)'
  if (Number(r.blocking_open) > 0) stopDispatch = stopDispatch || `${r.blocking_open} unresolved BLOCKING review entr(y/ies) (soak red or review); the architect resolves or waives them`
}

// Ruling 6: one Opus reviewer per landed batch; BLOCKING findings stop the next batch until the architect acts.
async function reviewBatch(n, ids) {
  if (halted || !ids.length) return
  await slots.take()
  let r
  try {
    r = await seat(reviewerPrompt(n, ids), { label: `review batch ${n}`, phase: 'Review', schema: REVIEW_SCHEMA, model: OPUS, effort: 'high' }, `reviewer batch ${n}`)
  } finally { slots.give() }
  if (r && Number(r.blocking) > 0) stopDispatch = stopDispatch || `review of batch ${n}: ${r.blocking} BLOCKING finding(s) in ${r.file}; the architect resolves or waives them`
}

// U-026/U-033 batch with a coupled set: all items built (in the plan's waves), one compile; a red in the coupled set goes
// to ONE fix seat (max 2 rounds) and the set lands whole or halts; an uncoupled item of the batch drops alone and is
// retried afterwards like any uncoupled item.
async function runCoupled(n, ids, cset, waves) {
  const built = await buildAll(ids, n, waves)
  if (halted) return
  const missing = cset.filter((i) => !built.includes(i))
  if (missing.length) {
    stopDispatch = stopDispatch || `coupled set of batch ${n} cannot land whole: not built ${missing.join(', ')}`
    return
  }
  let active = built.slice()
  active.forEach((id) => reusable.add(id))
  for (let fixRound = 0; fixRound <= MAX_FIX_ROUNDS && !halted; fixRound++) {
    const r = await integrate(n, active, cset, fixRound, '')
    if (r && r.result === 'red-coupled') {
      for (const d of r.dropped || []) active = active.filter((id) => id !== d.id) // uncoupled drops of this round
      applyDrops(r)
    }
    if (!r || r.result !== 'red-coupled') {
      applyIntegration(n, active, r)
      if (r && r.result === 'landed') await reviewBatch(n, r.landed)
      const rest = ids.filter((id) => !cset.includes(id) && !landed.has(id) && !parked.has(id) && !setAside.has(id))
      if (rest.length && r && r.result === 'landed' && !halted && !stopDispatch) { batchNo += 1; await runUncoupled(batchNo, rest) }
      return
    }
    if (fixRound >= MAX_FIX_ROUNDS) { halt(`coupled batch ${n} still red after ${fixRound} fix rounds (evidence ${r.evidence})`); return }
    await slots.take()
    let f
    try {
      f = await seat(fixPrompt(n, r.red_items || [], r.evidence, fixRound + 1), { label: `fix batch ${n} round ${fixRound + 1}`, phase: 'Fix', schema: FIX_SCHEMA, model: OPUS, effort: 'high' }, `fix seat batch ${n}`)
    } finally { slots.give() }
    if (!f) return
    if (f.status !== 'fixed') { halt(`fix seat for coupled batch ${n} could not repair it: ${f.note} (evidence ${r.evidence})`); return }
  }
}

// Uncoupled batch: drop on first red (integrate.py), dropped items rebuild and retry until landed, parked or set aside.
async function runUncoupled(n0, ids, waves) {
  let n = n0
  for (;;) {
    const todo = ids.filter((id) => !landed.has(id) && !parked.has(id) && !setAside.has(id))
    if (!todo.length || halted || stopDispatch) return
    const built = await buildAll(todo, n, waves)
    if (halted || !built.length) return
    const r = await integrate(n, built, [], 0, '')
    applyIntegration(n, built, r)
    if (r && r.landed && r.landed.length) await reviewBatch(n, r.landed)
    if (!r || halted) return
    batchNo += 1
    n = batchNo
  }
}

const outsideWaitsLanded = (ids) => ids.every((id) => (byId[id].waits_on || []).every((w) => landed.has(w) || ids.includes(w)))

if (BAD.length) {
  log(`refusing to start: ${BAD.join('; ')}`)
  return { halted: `bad args: ${BAD.join('; ')}`, landedThisRun: [] }
}
log(`run ${TAG}: ${ORDER.length} loop items, ${landed.size} already landed, ${parked.size} parked, ${BATCHES.length} batch(es) from _ORDER.md; cap ${A.capTokens} total tokens (watched by spend.py)`)

if (BATCHES.length) {
  for (const b of BATCHES) {
    if (halted || stopDispatch) break
    const ids = b.items.filter((id) => byId[id] && !landed.has(id) && !parked.has(id) && !setAside.has(id))
    if (!ids.length) continue
    if (!outsideWaitsLanded(ids)) { stopDispatch = `batch ${b.batch} waits on items that have not landed`; break }
    batchNo += 1
    const cset = (Array.isArray(b.coupled_set) ? b.coupled_set : (b.coupled ? b.items : [])).filter((id) => ids.includes(id))
    log(`batch ${batchNo} (_ORDER.md batch ${b.batch}): ${ids.join(', ')}; coupled set ${cset.join(', ') || 'none'}; waves ${(b.waves || [ids]).map((w) => w.length).join('+')} under ${MAX_BUILDERS} builders`)
    if (cset.length) await runCoupled(batchNo, ids, cset, b.waves)
    else await runUncoupled(batchNo, ids, b.waves)
    // U-033: an uncoupled item that did not land (parked, set aside) does not stop the run by itself; the next batch
    // stops only if it waits on that item (outsideWaitsLanded above). A coupled set that did not land always stops.
    const unl = b.items.filter((id) => byId[id] && !landed.has(id))
    if (unl.length) log(`batch ${b.batch}: not landed ${unl.join(', ')} (recorded in the ledger at shutdown, U-042)`)
    if (cset.some((id) => !landed.has(id)) && !halted) { stopDispatch = stopDispatch || `batch ${b.batch}: its coupled set did not land`; break }
  }
} else {
  // No _ORDER.md batches: form uncoupled batches of up to 6 from waits_on, in stable order.
  for (;;) {
    if (halted || stopDispatch) break
    const take = ORDER.filter((id) => !landed.has(id) && !parked.has(id) && !setAside.has(id)
      && (byId[id].waits_on || []).every((w) => landed.has(w))).slice(0, BATCH_MAX)
    if (!take.length) break
    batchNo += 1
    const before = landed.size + parked.size + setAside.size
    await runUncoupled(batchNo, take)
    if (landed.size + parked.size + setAside.size === before) break
  }
}

const notLanded = ORDER.filter((id) => !landed.has(id))
return {
  runTag: TAG,
  halted,
  stopDispatch,
  batches: batches.map((b) => ({ batch: b.batch, result: b.result, landed: b.landed, pr: b.pr, report: b.report })),
  landedThisRun,
  parked: [...parked],
  setAside: [...setAside.entries()].map(([id, why]) => ({ id, why })),
  notLanded: notLanded.slice(0, 100),
  next: 'Run loop/bin/shutdown.sh (watchers by pid, lanes, resume note). Relaunch fresh with launch.sh; never resume.',
}
