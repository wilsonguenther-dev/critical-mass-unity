// Dry run of loop/workflow/critical-mass-unity.js WITHOUT the Workflow tool: the script body is evaluated with mock
// agent()/parallel()/log()/phase() so its scheduling (batching, waits_on, 5+1 concurrency, first-red requeue,
// parking, null = halt, payload cap, stop-dispatch) is checked by plain code. No agent runs, nothing is spent.
//   node loop/tests/js/sim-workflow.mjs        exit 0 = every scenario passed
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const here = path.dirname(fileURLToPath(import.meta.url))
const src = readFileSync(path.join(here, '../../workflow/critical-mass-unity.js'), 'utf8')
if (!/^export const meta = \{/.test(src)) throw new Error('script must begin with export const meta')
const body = src.replace(/^export const meta =/, 'const meta =')
// eslint-disable-next-line no-new-func
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
const make = () => new AsyncFunction('args', 'agent', 'parallel', 'log', 'phase', 'budget', body + '\n')

const ITEMS = [
  { id: 'F-01', title: 'Game config', owner: 'loop', waits_on: [], phase: '0' },
  { id: 'F-02', title: 'Seeded RNG', owner: 'loop', waits_on: [], phase: '0', tightly_defined: true },
  { id: 'F-03', title: 'Ground grid', owner: 'loop', waits_on: ['F-01'], phase: '0' },
  { id: 'F-04', title: 'Flow field', owner: 'loop', waits_on: ['F-03'], phase: '0' },
  { id: 'F-05', title: 'Base rules', owner: 'loop', waits_on: [], phase: '0' },
  { id: 'F-06', title: 'Bots', owner: 'loop', waits_on: [], phase: '0' },
  { id: 'F-07', title: 'Controls', owner: 'loop', waits_on: [], phase: '0' },
  { id: 'F-08', title: 'Placeholder view', owner: 'loop', waits_on: [], phase: '0' },
  { id: 'CM-30', title: 'Dig and place', owner: 'max', waits_on: [], phase: '1' },
]
const ARGS = { now: '2026-11-12T04:00:00Z', runTag: '20261112T0400', mainRoot: '/tmp/cmu', lanesRoot: '/tmp/lanes',
  repo: 'x/y', capTokens: 30000000, items: ITEMS, landed: [], parked: [], maxBuilders: 5, batchMax: 6, maxReturns: 2 }

async function scenario(name, behave, check, args = ARGS) {
  const calls = []
  let live = 0, peak = 0
  const logs = []
  const agent = async (prompt, opts) => {
    live++; peak = Math.max(peak, live)
    calls.push({ label: opts.label, model: opts.model, prompt })
    await new Promise((r) => setTimeout(r, 2 + (calls.length % 3)))
    try { return behave(opts.label, prompt, calls) } finally { live-- }
  }
  const parallel = async (thunks) => Promise.all(thunks.map((t) => t().catch(() => null)))
  const out = await make()(args, agent, parallel, (m) => logs.push(m), () => {}, { total: null, spent: () => 0, remaining: () => Infinity })
  const problems = check(out, { calls, peak, logs }) || []
  console.log(`${problems.length ? 'FAIL' : 'PASS'}  ${name}${problems.length ? ': ' + problems.join('; ') : ''}`)
  return problems.length === 0
}

const built = (label) => ({ status: 'built', branch: 'loop/' + label.split(' ')[1], head: 'abc', files_changed: 2, tests_written: [], note: 'ok' })
const ids = (prompt) => prompt.match(/Items, in this order: ([^.]+)\./)[1].split(', ')
const land = (prompt, drop = []) => {
  const all = ids(prompt)
  return { result: 'landed', landed: all.filter((i) => !drop.includes(i)), dropped: drop.filter((d) => all.includes(d)).map((id) => ({ id, gate: 'compile', counted: true, parked: false })), pr: 1, report: '/tmp/r.json', spend_action: 'ok', note: 'ok' }
}

const results = []
results.push(await scenario('happy path: everything loop-owned lands, waits_on respected, Max items never dispatched, 5+1 cap',
  (label, prompt) => label.startsWith('build') ? built(label) : label.startsWith('integrate') ? land(prompt) : { findings: 0, blocking: 0, file: '/tmp/r.md' },
  (out, { calls, peak }) => {
    const p = []
    const order = calls.filter((c) => c.label.startsWith('integrate')).map((c) => ids(c.prompt))
    if (out.landedThisRun.length !== 8) p.push(`landed ${out.landedThisRun.length} of 8`)
    if (calls.some((c) => c.label.includes('CM-30'))) p.push('a max-owned item was dispatched')
    if (peak > 6) p.push(`peak concurrency ${peak} > 6`)
    const flat = order.flat()
    if (flat.indexOf('F-03') < flat.indexOf('F-01') || flat.indexOf('F-04') < flat.indexOf('F-03')) p.push('waits_on order broken')
    if (order.some((b) => b.length > 6)) p.push('a batch over 6')
    if (!calls.every((c) => /Never use the Agent, Task or Workflow tools/.test(c.prompt) && /Cap yourself at \d+ tool calls/.test(c.prompt) && /NEVER pkill/.test(c.prompt))) p.push('a prompt lacks the guardrail block')
    if (!calls.filter((c) => c.label.startsWith('build')).every((c) => /NEVER run Unity/.test(c.prompt))) p.push('a builder prompt lacks never-run-Unity')
    const f02 = calls.find((c) => c.label === 'build F-02'), f01 = calls.find((c) => c.label === 'build F-01')
    if (f02.model !== 'opus' || f01.model !== 'opus') p.push(`models: F-02 ${f02.model}, F-01 ${f01.model}`)
    if (!calls.every((c) => c.model === 'opus')) p.push('a seat is not Opus (binding ledger: Opus in every seat)')
    if (!calls.filter((c) => c.label.startsWith('build')).every((c) => /show 09e0904:/.test(c.prompt) && /Never read another item's lane/.test(c.prompt))) p.push('a builder prompt lacks the pinned oracle or the no-sibling-lane rule')
    const reviews = calls.filter((c) => c.label.startsWith('review batch')).length
    if (reviews !== order.length) p.push(`${reviews} review seats for ${order.length} landed batches (ruling 6: one per batch)`)
    if (JSON.stringify(out).length > 40000) p.push('return over 40 KB')
    console.log('      batches: ' + order.map((b) => '[' + b.join(' ') + ']').join(' '), '| peak agents', peak)
    return p
  }))

results.push(await scenario('first red: F-01 dropped twice then parked; F-03/F-04 never start; the rest land',
  (label, prompt, calls) => {
    if (label.startsWith('build')) return built(label)
    if (label.startsWith('integrate')) {
      const n = calls.filter((c) => c.label.startsWith('integrate') && ids(c.prompt).includes('F-01')).length
      const r = land(prompt, ['F-01'])
      r.dropped = r.dropped.map((d) => ({ ...d, parked: n >= 3 }))
      return r
    }
    return { findings: 0, blocking: 0, file: '/tmp/r.md' }
  },
  (out, { calls }) => {
    const p = []
    const f01builds = calls.filter((c) => c.label === 'build F-01')
    if (f01builds.length !== 3) p.push(`F-01 built ${f01builds.length} times, want 3`)
    if (!/returned 2 time\(s\)/.test(f01builds[2].prompt) || !/failures\/F-01\.md/.test(f01builds[2].prompt)) p.push('return prompt lacks failure file / count')
    if (!out.parked.includes('F-01')) p.push('F-01 not parked')
    if (calls.some((c) => c.label === 'build F-03')) p.push('F-03 started though F-01 never landed')
    if (out.landedThisRun.length !== 5) p.push(`landed ${out.landedThisRun.length}, want 5`)
    return p
  }))

results.push(await scenario('agent() returns null: whole run halts, no new dispatch',
  (label, prompt) => (label === 'build F-02' ? null : label.startsWith('build') ? built(label) : land(prompt)),
  (out, { calls }) => {
    const p = []
    if (!out.halted || !/null/.test(out.halted)) p.push(`halted=${out.halted}`)
    if (calls.some((c) => c.label.startsWith('integrate'))) p.push('integrated after a null')
    if (out.review) p.push('reviewed after a halt')
    return p
  }))

results.push(await scenario('oversized payload halts',
  (label, prompt) => (label.startsWith('build') ? { ...built(label), note: 'x'.repeat(41000) } : land(prompt)),
  (out) => (/40 KB/.test(out.halted || '') ? [] : [`halted=${out.halted}`])))

results.push(await scenario('spend at 90 %: no new batch after the integrator reports stop-dispatch',
  (label, prompt) => (label.startsWith('build') ? built(label) : label.startsWith('integrate') ? { ...land(prompt), spend_action: 'stop-dispatch' } : { findings: 0, blocking: 0, file: 'r' }),
  (out, { calls }) => {
    const ints = calls.filter((c) => c.label.startsWith('integrate')).length
    return ints <= 2 && out.stopDispatch ? [] : [`integrations ${ints}, stopDispatch ${out.stopDispatch}`]
  }))

results.push(await scenario('builder blocked by an open question: set aside, never retried, dependants wait',
  (label, prompt) => (label === 'build F-01' ? { status: 'blocked', questions_file: '/tmp/q.md', note: 'needs the chunk size' } : label.startsWith('build') ? built(label) : label.startsWith('integrate') ? land(prompt) : { findings: 0, blocking: 0, file: 'r' }),
  (out, { calls }) => {
    const p = []
    if (calls.filter((c) => c.label === 'build F-01').length !== 1) p.push('F-01 retried')
    if (!out.setAside.some((s) => s.id === 'F-01')) p.push('F-01 not set aside')
    if (calls.some((c) => c.label === 'build F-03')) p.push('F-03 started')
    return p
  }))

results.push(await scenario('refuses to start without capTokens', () => built('build X'),
  (out, { calls }) => (calls.length === 0 && /capTokens/.test(out.halted || '') ? [] : [`calls ${calls.length} halted ${out.halted}`]),
  { ...ARGS, capTokens: undefined }))

// ── U-026 batches from _ORDER.md ──
const OITEMS = [
  ['F-01', []], ['F-02a', ['F-01']], ['F-03', ['F-02a']], ['F-04', ['F-03']], ['F-02b', ['F-04']],
  ...['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j'].map((x) => [`F-05${x}`, ['F-02b']]),
].map(([id, w]) => ({ id, title: 'Item ' + id, owner: 'loop', waits_on: w, phase: '0' }))
const OBATCHES = [
  { batch: 1, items: ['F-01', 'F-02a', 'F-03', 'F-04', 'F-02b'], coupled: false },
  { batch: 2, items: OITEMS.slice(5).map((i) => i.id), coupled: true, coupled_set: OITEMS.slice(5).map((i) => i.id) },
]
const OARGS = { ...ARGS, items: OITEMS, batches: OBATCHES }
const isCoupled = (prompt) => /Coupled set \(U-026, U-033\)/.test(prompt)
const fixSpent = (prompt) => Number((prompt.match(/fix rounds spent so far: (\d+)/) || [0, 0])[1])
const review = { findings: 0, blocking: 0, file: '/tmp/r.md' }

results.push(await scenario('uncoupled chain batch: F-03 red drops itself and its dependants; only F-03 is rebuilt; then the coupled batch lands whole',
  (label, prompt, calls) => {
    if (label.startsWith('build')) return built(label)
    if (label.startsWith('review')) return review
    if (label.startsWith('integrate')) {
      const all = ids(prompt)
      if (!isCoupled(prompt) && all.includes('F-03') && calls.filter((c) => c.label === 'build F-03').length === 1) {
        return { result: 'all-dropped', landed: ['F-01', 'F-02a'].filter((i) => all.includes(i)), pr: 1, report: 'r', spend_action: 'ok', note: 'x',
          dropped: [{ id: 'F-03', gate: 'compile', counted: true, parked: false }, { id: 'F-04', gate: 'dependency', counted: false, parked: false }, { id: 'F-02b', gate: 'dependency', counted: false, parked: false }] }
      }
      return land(prompt)
    }
    return { status: 'fixed', note: 'x' }
  },
  (out, { calls, peak }) => {
    const p = []
    const n = (id) => calls.filter((c) => c.label === `build ${id}`).length
    if (n('F-03') !== 2 || n('F-04') !== 1 || n('F-02b') !== 1) p.push(`builds F-03 ${n('F-03')} F-04 ${n('F-04')} F-02b ${n('F-02b')}`)
    if (out.landedThisRun.length !== 15) p.push(`landed ${out.landedThisRun.length} of 15`)
    const coupledInts = calls.filter((c) => c.label.startsWith('integrate') && isCoupled(c.prompt))
    if (coupledInts.length !== 1 || ids(coupledInts[0].prompt).length !== 10) p.push('coupled batch not integrated once with all 10')
    if (peak > 6) p.push(`peak ${peak}`)
    return p
  }, OARGS))

results.push(await scenario('coupled batch red: no drop, one fix seat with the evidence, re-integrated, lands whole',
  (label, prompt) => {
    if (label.startsWith('build')) return built(label)
    if (label.startsWith('review')) return review
    if (label.startsWith('fix')) return { status: 'fixed', items: ['F-05c'], note: 'fixed' }
    if (isCoupled(prompt) && fixSpent(prompt) === 0) return { result: 'red-coupled', landed: [], dropped: [], red_items: ['F-05c'], evidence: '/tmp/ev.md', spend_action: 'ok', note: 'red' }
    return land(prompt)
  },
  (out, { calls }) => {
    const p = []
    const fixes = calls.filter((c) => c.label.startsWith('fix'))
    if (fixes.length !== 1 || !/\/tmp\/ev\.md/.test(fixes[0].prompt) || !/F-05c/.test(fixes[0].prompt) || !/ONLY files inside that item's files_allowed/.test(fixes[0].prompt)) p.push('fix seat prompt wrong or count != 1')
    if (calls.filter((c) => c.label.startsWith('build F-05')).length !== 10) p.push('coupled items rebuilt')
    if (!OITEMS.slice(5).every((i) => out.landedThisRun.includes(i.id))) p.push('coupled batch did not land whole')
    if (out.halted) p.push(`halted ${out.halted}`)
    return p
  }, OARGS))

results.push(await scenario('coupled batch still red after 2 fix rounds: HALT, nothing of it lands',
  (label, prompt) => {
    if (label.startsWith('build')) return built(label)
    if (label.startsWith('review')) return review
    if (label.startsWith('fix')) return { status: 'fixed', items: ['F-05c'], note: 'tried' }
    if (isCoupled(prompt)) return { result: fixSpent(prompt) >= 2 ? 'halt-coupled' : 'red-coupled', landed: [], dropped: [], red_items: ['F-05c'], evidence: '/tmp/ev.md', spend_action: 'ok', note: 'red' }
    return land(prompt)
  },
  (out, { calls }) => {
    const p = []
    if (calls.filter((c) => c.label.startsWith('fix')).length !== 2) p.push('fix seats != 2')
    if (calls.filter((c) => c.label.startsWith('integrate') && isCoupled(c.prompt)).length !== 3) p.push('coupled integrations != 3')
    if (!out.halted || !/coupled batch/.test(out.halted)) p.push(`halted=${out.halted}`)
    if (out.landedThisRun.some((i) => i.startsWith('F-05'))) p.push('part of the coupled batch landed')
    return p
  }, OARGS))

results.push(await scenario('BLOCKING review finding stops the next batch',
  (label, prompt) => (label.startsWith('build') ? built(label) : label.startsWith('review') ? { findings: 1, blocking: 1, file: '/x/loop/review/T-1.md' } : land(prompt)),
  (out, { calls }) => (/BLOCKING/.test(out.stopDispatch || '') && !calls.some((c) => c.label.startsWith('build F-05')) ? [] : [`stop=${out.stopDispatch}`]),
  OARGS))

results.push(await scenario('soak red (blocking_open from the integrator) stops the next batch',
  (label, prompt) => (label.startsWith('build') ? built(label) : label.startsWith('review') ? review : { ...land(prompt), blocking_open: 1 }),
  (out, { calls }) => (/BLOCKING/.test(out.stopDispatch || '') && !calls.some((c) => c.label.startsWith('build F-05')) ? [] : [`stop=${out.stopDispatch}`]),
  OARGS))

// ── U-033 per-item coupling, U-037 builder limit, U-046 Opus only, U-040 batch report, U-042 no stop on an uncoupled park ──
const MITEMS = [...OITEMS, { id: 'F-01b', title: 'Golden follow-up', owner: 'loop', waits_on: ['F-01'], phase: '0' },
  { id: 'V-01', title: 'Toon shader', owner: 'loop', waits_on: [], phase: '0' },
  { id: 'F-06a', title: 'Bots state', owner: 'loop', waits_on: ['F-05a'], phase: '0' }]
const MB = [
  { batch: 1, items: ['F-01', 'F-02a', 'F-03', 'F-04', 'F-02b'], coupled: false, coupled_set: [], waves: [['F-01', 'F-02a', 'F-03', 'F-04', 'F-02b']] },
  { batch: 2, items: ['F-01b', ...OITEMS.slice(5).map((i) => i.id), 'V-01'], coupled: true, coupled_set: OITEMS.slice(5).map((i) => i.id), waves: [['F-01b', ...OITEMS.slice(5).map((i) => i.id), 'V-01']] },
  { batch: 3, items: ['F-06a'], coupled: false, coupled_set: [], waves: [['F-06a']] },
]
const MARGS = { ...ARGS, items: MITEMS, batches: MB, maxBuilders: 12 }

results.push(await scenario('mixed batch: the uncoupled F-01b drops alone, the coupled set lands without it, F-01b is rebuilt and lands after; 12 builders in one wave',
  (label, prompt, calls) => {
    if (label.startsWith('build')) return built(label)
    if (label.startsWith('review')) return review
    if (label.startsWith('integrate')) {
      const all = ids(prompt)
      if (isCoupled(prompt) && all.includes('F-01b')) return land(prompt, ['F-01b'])
      return land(prompt)
    }
    return { status: 'fixed', note: 'x' }
  },
  (out, { calls, peak }) => {
    const p = []
    const n = (id) => calls.filter((c) => c.label === `build ${id}`).length
    if (n('F-01b') !== 2) p.push(`F-01b built ${n('F-01b')} times, want 2`)
    if (!out.landedThisRun.includes('F-01b') || !out.landedThisRun.includes('F-05j') || !out.landedThisRun.includes('F-06a')) p.push(`landed ${out.landedThisRun.join(' ')}`)
    const ci = calls.find((c) => c.label.startsWith('integrate') && isCoupled(c.prompt))
    if (!ci || !/--coupled-set F-05a,F-05b,F-05c,F-05d,F-05e,F-05f,F-05g,F-05h,F-05i,F-05j /.test(ci.prompt) || !/drops alone/.test(ci.prompt)) p.push('integrator prompt lacks the coupled set or the drop-alone line')
    if (peak !== 12) p.push(`peak ${peak}, want 12 (one wave of 12)`)
    if (out.halted) p.push(`halted ${out.halted}`)
    return p
  }, MARGS))

results.push(await scenario('an uncoupled item parked in batch 2 does not stop batch 3 (nothing waits on it); the reviewer writes the batch report',
  (label, prompt) => {
    if (label.startsWith('build')) return built(label)
    if (label.startsWith('review')) return review
    if (label.startsWith('integrate')) {
      const r = land(prompt, ['V-01'])
      r.dropped = r.dropped.map((d) => ({ ...d, parked: true }))
      return r
    }
    return { status: 'fixed', note: 'x' }
  },
  (out, { calls }) => {
    const p = []
    if (!out.parked.includes('V-01')) p.push('V-01 not parked')
    if (!out.landedThisRun.includes('F-06a')) p.push('batch 3 did not run after an uncoupled park')
    if (!calls.filter((c) => c.label.startsWith('review')).every((c) => /spend\.py batch-report --batch \d+ --tag /.test(c.prompt))) p.push('a reviewer prompt lacks the batch report step')
    return p
  }, MARGS))

results.push(await scenario('builder limit: --builders 20 clamps to 12, default is 5; no seat ever leaves Opus',
  (label, prompt) => (label.startsWith('build') ? built(label) : label.startsWith('integrate') ? land(prompt) : review),
  (out, { calls, peak }) => {
    const p = []
    if (peak > 12) p.push(`peak ${peak} > 12`)
    if (!calls.every((c) => c.model === 'opus')) p.push('a seat is not Opus')
    if (/model:\s*['"](?!opus)/.test(src) || /sonnet|haiku/i.test(src.replace(/\/\/.*$/gm, ''))) p.push('the workflow names a non-Opus model')
    if (!/opts\.model !== OPUS/.test(src)) p.push('seat() lacks the Opus guard')
    return p
  }, { ...MARGS, maxBuilders: 20 }))

// ── U-050: pushed lanes the launcher marked prebuilt go straight to the integrator; a returned item still rebuilds ──
const RITEMS = MITEMS.map((i) => (i.id.startsWith('F-05') || i.id === 'V-01' ? { ...i, prebuilt: 'abc123' }
  : i.id === 'F-01b' ? { ...i, prebuilt: '', returns: 1 } : i))
results.push(await scenario('relaunch: prebuilt lanes are never rebuilt and go straight to integration; the returned F-01b rebuilds once on its own branch',
  (label, prompt) => (label.startsWith('build') ? built(label) : label.startsWith('integrate') ? land(prompt) : review),
  (out, { calls, logs }) => {
    const p = []
    const builds = calls.filter((c) => c.label.startsWith('build')).map((c) => c.label.slice(6))
    const b2 = builds.filter((id) => id.startsWith('F-05') || id === 'V-01')
    if (b2.length) p.push(`rebuilt prebuilt lanes: ${b2.join(' ')}`)
    const f01b = calls.filter((c) => c.label === 'build F-01b')
    if (f01b.length !== 1 || !/--base origin\/loop\/F-01b/.test(f01b[0].prompt)) p.push('F-01b not rebuilt once as a returned item')
    const ci = calls.find((c) => c.label.startsWith('integrate') && isCoupled(c.prompt))
    if (!ci || !ids(ci.prompt).includes('F-05j') || !ids(ci.prompt).includes('V-01') || !ids(ci.prompt).includes('F-01b')) p.push('the batch-2 integrator did not get every item')
    if (!out.landedThisRun.includes('F-05a') || !out.landedThisRun.includes('F-06a')) p.push(`landed ${out.landedThisRun.join(' ')}`)
    if (!logs.some((l) => /reusing 11 pushed lane\(s\) without a builder \(U-050\)/.test(l))) p.push('no reuse log line')
    if (out.halted || out.stopDispatch) p.push(`stopped: ${out.halted || out.stopDispatch}`)
    return p
  }, { ...MARGS, items: RITEMS }))

results.push(await scenario('a prebuilt lane that a counted drop sends back is rebuilt as a returned item (cut from its own branch)',
  (label, prompt, calls) => {
    if (label.startsWith('build')) return built(label)
    if (label.startsWith('review')) return review
    if (label.startsWith('integrate')) {
      const first = calls.filter((c) => c.label.startsWith('integrate') && ids(c.prompt).includes('V-01')).length === 1
      return first && ids(prompt).includes('V-01') ? land(prompt, ['V-01']) : land(prompt)
    }
    return { status: 'fixed', note: 'x' }
  },
  (out, { calls }) => {
    const p = []
    const v = calls.filter((c) => c.label === 'build V-01')
    if (v.length !== 1 || !/--base origin\/loop\/V-01/.test(v[0].prompt)) p.push(`V-01 builds ${v.length}, want 1 returned build`)
    if (!out.landedThisRun.includes('V-01')) p.push('V-01 never landed')
    return p
  }, { ...MARGS, items: RITEMS }))

const ok = results.every(Boolean)
console.log(`sim-workflow: ${results.filter(Boolean).length}/${results.length} scenarios passed`)
process.exit(ok ? 0 : 1)
