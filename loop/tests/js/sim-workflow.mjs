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
    if (f02.model !== 'sonnet' || f01.model !== 'opus') p.push(`models: F-02 ${f02.model}, F-01 ${f01.model}`)
    if (!out.review || calls.filter((c) => c.label === 'review').length !== 1) p.push('no single review seat')
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

const ok = results.every(Boolean)
console.log(`sim-workflow: ${results.filter(Boolean).length}/${results.length} scenarios passed`)
process.exit(ok ? 0 : 1)
