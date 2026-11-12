export const meta = {
  name: 'critical-mass-unity-loop',
  description: 'Critical Mass (Unity) build loop: up to 5 code-only builders in worktrees (never run Unity), one Opus integrator per batch of up to 6 that runs loop/bin/integrate.py (one compile per round, first-red drop, one PR to main, squash), one Opus review pass at the end. Scheduling is plain code in this script; no idle, dispatch or heartbeat agents.',
  whenToUse: 'Launch FRESH only, with args = the JSON in loop/logs/launch-args.json written by loop/bin/launch.sh. Never resumeFromRunId (the resume cache is positional). Record the returned taskId and runId at once with: python3 loop/bin/cmloop.py record-launch --task-id <w...> --run-id <wf_...>; builders refuse to start until both are recorded.',
  phases: [
    { title: 'Build', detail: 'builders: halt-check, lane worktree on loop/<ID>, code + named NUnit tests + docs, scope gate (git only), push. Never Unity.' },
    { title: 'Integrate', detail: 'one Opus integrator per batch: loop/bin/integrate.py (merge, gates, drop the red item, PR to main, squash, prove landed), spend check' },
    { title: 'Review', detail: 'one Opus reviewer with a different lens after everything lands; findings go to a file for the architect' },
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
const MAX_BUILDERS = Math.max(1, Math.min(5, Number(A.maxBuilders) || 5)) // U-015: 5 builders + 1 integrator = 6
const BATCH_MAX = Math.max(1, Math.min(6, Number(A.batchMax) || 6))
const MAX_RETURNS = Math.max(0, Number(A.maxReturns) || 2)
const PAYLOAD_CAP = 40000 // bytes; agents write details to files and return paths
const CAPS = { builder: 60, integrator: 40, reviewer: 50 }

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
    result: { type: 'string', enum: ['landed', 'all-dropped', 'error', 'halted'] },
    landed: { type: 'array', items: { type: 'string' }, maxItems: 6 },
    dropped: {
      type: 'array', maxItems: 6,
      items: { type: 'object', properties: { id: { type: 'string' }, gate: { type: 'string' }, counted: { type: 'boolean' }, parked: { type: 'boolean' } }, required: ['id', 'gate', 'counted', 'parked'] },
    },
    pr: { type: ['integer', 'null'] },
    report: { type: 'string' },
    spend_action: { type: 'string', enum: ['ok', 'stop-dispatch', 'halt', 'no-data'] },
    note: { type: 'string', maxLength: 600 },
  },
  required: ['result', 'landed', 'dropped', 'spend_action', 'note'],
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
   This item was returned ${it.returns} time(s). FIRST read ${MAIN}/loop/state/failures/${it.id}.md (the gate output that dropped it) and fix exactly that. At most ${MAX_RETURNS} returns; the next red parks it for the architect.` : ''}
4. Write the code, the NUnit tests named in tests_required (exact names), and any docs the item names, all in this branch. Touch only files_allowed. The Sim assembly never references UnityEngine (U-002). Read real signatures before calling them.
5. Run \`${MAIN}/loop/gates/scope --item ${it.id} --ref HEAD --base origin/main --project ${LANES}/${it.id}\` bare (git only, no Unity). Fix any violation it lists.
6. Commit with subject "${it.id}: <what changed>" (never a line starting "loop-landed:"), then \`git push -u origin loop/${it.id}\`.
7. If the item cannot be built without an answer it does not give, write the question with your recommendation to ${MAIN}/loop/logs/questions/${it.id}.md, push nothing unfinished as if finished, and return status "blocked".
Return: status, branch, head (the pushed sha), files_changed, tests_written, questions_file (if any), note (one plain sentence).`

const integratorPrompt = (n, ids) => `You are the INTEGRATOR for batch ${n} of the Critical Mass Unity build loop (run ${TAG}). Items, in this order: ${ids.join(', ')}.
${GUARD(CAPS.integrator, 'integrator')}
STEPS:
1. Run \`bash ${MAIN}/loop/bin/halt-check.sh --wait 0\` bare. Exit 3 (HALT/STALL) = return result "halted". Exit 4 (STOP-DISPATCH) = still integrate (the batch is already built).
2. Run, bare, from ${MAIN}: \`python3 ${MAIN}/loop/bin/integrate.py --batch ${n} --items ${ids.join(',')} --run-tag ${TAG}\`, then \`echo $?\`. It takes the compile lock, merges, runs the gates once per round, drops a red item at once (no fix attempts), opens ONE PR to main and squash-merges it, and prints one JSON line.
3. Only if its JSON flags "red with no item attributed": open the report file it names and the gate logs it points to. If, and only if, an error line names a file:line that one item's diff created or changed, re-run step 2 ONCE adding \`--drop <ID>="<gate>: <the error line>"\`. Otherwise leave it: the batch stays dropped uncounted and the architect sees the flag.
4. Never edit code, never fix an item, never run gh pr create or gh pr merge yourself, never run Unity outside integrate.py.
5. Run \`python3 ${MAIN}/loop/bin/spend.py check\` bare, then \`echo $?\` (0 ok, 10 stop-dispatch, 11 halt, 12 no-data).
Return: result (landed | all-dropped | error | halted), landed ids, dropped [{id, gate, counted, parked}], pr, report (the path integrate.py printed), spend_action, note (one plain sentence).`

const reviewerPrompt = (landedIds) => `You are the REVIEWER for run ${TAG} of the Critical Mass Unity build loop. Items landed this run: ${landedIds.join(', ')}.
${GUARD(CAPS.reviewer, 'reviewer')}
Your lens is NOT the builders' (they wrote to the item's method). Yours: (a) determinism of CriticalMass.Sim (no UnityEngine, no wall clock, no unseeded randomness, double math, fixed 30 Hz tick, U-002); (b) fidelity to the browser oracle (ported tests assert the same ticks, counts and outcomes; positions within 1e-4); (c) Runtime never writes game state; (d) anything that touched a file outside its item's files_allowed.
Read only: \`git -C ${MAIN} fetch -q origin\`, \`git -C ${MAIN} log origin/main --grep '^loop-landed:' -n 40\`, the squash commits' diffs, and the item files in ${MAIN}/loop/items/. Do not run Unity, do not edit code, do not open PRs.
Write findings to ${MAIN}/loop/logs/review-${TAG}.md, one per finding: BLOCKING or ADVISORY, file:line evidence, the item id, and a recommendation for the architect (who writes any fix item; builders never decide).
Return: findings (count), blocking (count), file (path), note.`

// ───────────────────────────── scheduling: plain code ─────────────────────────────
let halted = null
let stopDispatch = null
const halt = (why) => { if (!halted) { halted = why; log(`HALT: ${why}`) } }

async function seat(prompt, opts, what) {
  if (halted) return null
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
const builders = semaphore(MAX_BUILDERS)

const ITEMS = (A.items || []).filter((i) => i && i.owner === 'loop' && typeof i.id === 'string') // max-owned items are never dispatched
const byId = Object.fromEntries(ITEMS.map((i) => [i.id, { ...i, returns: Number(i.returns) || 0 }]))
const ORDER = ITEMS.map((i) => i.id) // stable order from the launcher (phase, then natural id): positional-cache safe
const landed = new Set(A.landed || [])
const parked = new Set(A.parked || [])
const setAside = new Map() // id -> reason (blocked by a question, or dropped uncounted too often)
const uncounted = {}
const inflight = new Set()
const landedThisRun = []
const batches = []

function readyIds() {
  return ORDER.filter((id) => !landed.has(id) && !parked.has(id) && !setAside.has(id) && !inflight.has(id)
    && (byId[id].waits_on || []).every((w) => landed.has(w))).slice(0, BATCH_MAX)
}

async function buildOne(id, n) {
  await builders.take()
  try {
    if (halted || stopDispatch) return null
    const it = byId[id]
    const r = await seat(builderPrompt(it, n), {
      label: `build ${id}`, phase: 'Build', schema: BUILD_SCHEMA,
      model: it.tightly_defined === true ? 'sonnet' : 'opus', // decision 2: Sonnet only for tightly_defined items
    }, `builder ${id}`)
    if (!r) return null
    if (r.status === 'halted') { stopDispatch = stopDispatch || `builder ${id}: ${r.note}`; return null }
    if (r.status === 'blocked') { setAside.set(id, `open question: ${r.questions_file || r.note}`); log(`${id} blocked: ${r.note}`); return null }
    return { id, ...r }
  } finally {
    builders.give()
  }
}

async function integrateBatch(n, ids) {
  const r = await seat(integratorPrompt(n, ids), {
    label: `integrate batch ${n}`, phase: 'Integrate', schema: INT_SCHEMA, model: 'opus', effort: 'high',
  }, `integrator batch ${n}`)
  const rec = { batch: n, items: ids, result: r ? r.result : 'null', landed: r ? r.landed : [], pr: r ? r.pr : null, report: r ? r.report : null }
  batches.push(rec)
  if (!r) { ids.forEach((id) => inflight.delete(id)); return }
  for (const id of r.landed || []) { landed.add(id); landedThisRun.push(id) }
  for (const d of r.dropped || []) {
    if (!byId[d.id]) continue
    if (d.parked) parked.add(d.id)
    else if (d.counted) byId[d.id].returns += 1
    else {
      uncounted[d.id] = (uncounted[d.id] || 0) + 1
      if (uncounted[d.id] > MAX_RETURNS) setAside.set(d.id, `dropped uncounted ${uncounted[d.id]} times (unattributed red or time box)`)
    }
  }
  ids.forEach((id) => inflight.delete(id))
  log(`batch ${n}: ${r.result}; landed ${(r.landed || []).join(', ') || 'none'}; dropped ${(r.dropped || []).map((d) => d.id).join(', ') || 'none'}; spend ${r.spend_action}`)
  if (r.result === 'halted') halt(`integrator batch ${n}: ${r.note}`)
  if (r.result === 'error') halt(`integrator batch ${n} error: ${r.note} (report ${r.report || 'none'})`)
  if (r.spend_action === 'halt') halt('spend cap reached (spend.py)')
  if (r.spend_action === 'stop-dispatch') stopDispatch = stopDispatch || 'spend at 90 % of the cap, or the hard stop (spend.py / clock)'
}

if (BAD.length) {
  log(`refusing to start: ${BAD.join('; ')}`)
  return { halted: `bad args: ${BAD.join('; ')}`, landedThisRun: [] }
}
log(`run ${TAG}: ${ORDER.length} loop items, ${landed.size} already landed, ${parked.size} parked; cap ${A.capTokens} total tokens (watched by spend.py)`)

let n = 0
let pending = null // the one integration in flight; builders of the next batch run beside it (5 + 1 = 6 agents)
while (!halted && !stopDispatch) {
  const take = readyIds()
  if (!take.length) {
    if (pending) { await pending; pending = null; continue }
    break
  }
  n += 1
  const bn = n
  take.forEach((id) => inflight.add(id))
  phase('Build')
  log(`batch ${bn}: building ${take.join(', ')}`)
  const built = (await parallel(take.map((id) => () => buildOne(id, bn)))).filter(Boolean).filter((b) => b.status === 'built')
  const builtIds = take.filter((id) => built.some((b) => b.id === id)) // keep the stable order
  take.filter((id) => !builtIds.includes(id)).forEach((id) => inflight.delete(id))
  if (pending) { await pending; pending = null }
  if (halted || !builtIds.length) continue
  pending = integrateBatch(bn, builtIds)
}
if (pending) { await pending; pending = null }

let review = null
if (!halted && landedThisRun.length) {
  phase('Review')
  await builders.take() // the reviewer takes a builder slot (U-015)
  try {
    review = await seat(reviewerPrompt(landedThisRun), { label: 'review', phase: 'Review', schema: REVIEW_SCHEMA, model: 'opus', effort: 'high' }, 'reviewer')
  } finally { builders.give() }
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
  review,
  next: 'Run loop/bin/shutdown.sh (watchers by pid, lanes, resume note). Relaunch fresh with launch.sh; never resume.',
}
