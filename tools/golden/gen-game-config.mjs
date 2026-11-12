#!/usr/bin/env node
// Golden tunables for F-01b (U-036): every leaf of GAME_CONFIG in the browser oracle's src/config/game.config.ts at
// the pinned commit, flattened to dotted paths, written as JSON. Run from the repo root:
//   node tools/golden/gen-game-config.mjs [--oracle ~/critical-mass] [--commit 09e0904] > tools/golden/game-config-09e0904.json
// Reads the oracle ONLY through `git show <commit>:<path>` (P-05), never its working tree. Needs node >= 22.6 (type stripping).
import { execFileSync } from 'node:child_process'
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir, homedir } from 'node:os'
import path from 'node:path'
import { pathToFileURL } from 'node:url'

const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? process.argv[i + 1] : d }
const oracle = arg('--oracle', path.join(homedir(), 'critical-mass'))
const commit = arg('--commit', '09e0904')
const src = execFileSync('git', ['-C', oracle, 'show', `${commit}:src/config/game.config.ts`], { encoding: 'utf8' })
const dir = mkdtempSync(path.join(tmpdir(), 'cm-golden-'))
try {
  const f = path.join(dir, 'game.config.ts')
  writeFileSync(f, src)
  const m = await import(pathToFileURL(f).href)
  const leaves = {}
  const walk = (v, p) => {
    if (Array.isArray(v)) v.forEach((x, i) => walk(x, `${p}[${i}]`))
    else if (v !== null && typeof v === 'object') for (const k of Object.keys(v)) walk(v[k], p ? `${p}.${k}` : k)
    else leaves[p] = v
  }
  walk(m.GAME_CONFIG, '')
  const out = { source: 'src/config/game.config.ts', commit, generator: 'tools/golden/gen-game-config.mjs',
    node: process.version, v8: process.versions.v8, export: 'GAME_CONFIG', leafCount: Object.keys(leaves).length,
    classIds: m.CLASS_IDS, teamIds: m.TEAM_IDS, leaves }
  process.stdout.write(JSON.stringify(out, null, 1) + '\n')
} finally { rmSync(dir, { recursive: true, force: true }) }
