#!/usr/bin/env python3
"""Build the Unity update (v4) of the Critical Mass parity board.

Reads previous-2026-11-09.html (the live v3 page, byte-checked against the
artifact before editing), applies targeted string replacements, writes
index.html and edits.json (the same replacements, in order, for the
artifact server's edit_version tool). Every old text must occur exactly once
in the document at the moment it is applied, which is the server's rule too.
The <script> block is never touched; the build asserts that.

Usage: python3 -I build.py "<central stamp>" "<utc stamp>" "<unity commit>"
"""
import json, re, sys, os

HERE = os.path.dirname(os.path.abspath(__file__))
CENTRAL, UTC, COMMIT = sys.argv[1], sys.argv[2], sys.argv[3]
REPO = "https://github.com/wilsonguenther-dev/critical-mass-unity"
ARCH = "Browser demo, archived 2026-11-09"

base = open(os.path.join(HERE, "previous-2026-11-09.html"), encoding="utf-8").read()

# Build order of the look phase (decided plan, order of work).
ORDER = {"p05": 1, "p06": 2, "p07": 3, "p08": 4, "p09": 5, "p10": 6, "p11": 6,
         "p12": 7, "p13": 7, "p14": 8, "p15": 9, "p04": 10, "p03": 11}

# Required features per row, from the Visual Asset Bible (spec recon, section 2).
FEATS = {
 "p03": ["Chunky, rounded, toy-like forms with strong negative space",
         "Flat cel finish with restrained highlights, never glossy or photoreal",
         "Oversized tools, hands and shields (staff 0.97, pick-hammer 0.63, shield 0.63 of body height)",
         "Floating-ruin materials: grey stone, moss, blue crystals, waterfalls",
         "Team and objective states readable at gameplay zoom, never by colour alone",
         "No gore, grimdark or dense detail that vanishes with 32 characters on screen"],
 "p04": ["Two-line wordmark: CRITICAL in azure with a teal bevel, MASS below in coral-red and larger",
         "Thick dark navy outline on both words",
         "Dark faceted stone egg behind the words, teal seams left and red seams right",
         "Floating rock chunks with moss, blue crystals left, corruption plume right, small waterfall",
         "Lockup sits on the Void Navy ground, upper left of the title screen",
         "Compact badge for the favicon, loading tile and launcher icon",
         "Exact spelling CRITICAL MASS"],
 "p05": ["Void Navy #18233B", "Azure Team #16C7D9", "Crimson Team #FF5A5F", "Warm Cream #F7F0E5",
         "Biomass #B7F238", "Corruption #8E3FD1", "Stone #6F7282", "Tool Brass #C79245",
         "Bungee for display text, mostly uppercase",
         "Nunito Sans for interface text and controls",
         "JetBrains Mono for timers, cooldowns, counts and scores",
         "Team, corruption and objective state never shown by colour alone"],
 "p06": ["Round shield on the left arm: wood planks, segmented stone rim, glowing cyan core; never removed",
         "Broad grey stone cleaver with a glowing cyan edge and a wrapped grip",
         "Dark-brown spiky hair in a high topknot",
         "Brass ring tying the topknot",
         "Teal scarf at the neck and teal tabard with a zigzag hem",
         "Grey stone-slab armour (pauldrons, knee guards) with cyan cracks and brass rivets",
         "Brass buckle disc on a brown belt",
         "Stocky, wide, bottom-heavy silhouette in a slight crouch",
         "Flat toon shading",
         "Crimson team swaps only the scarf, tabard, shield core and blade edge to coral"],
 "p07": ["Round glass backpack tank of glowing lime biomass, leaves on top, brass ring frame",
         "Teal side canisters and a hose running to the staff",
         "Hooked staff rising above the head (0.97 of body height)",
         "Staff head: brass hook, bone crescent blade and glowing lime bulb",
         "Wavy dark-brown hair in a messy bun with a teal band",
         "Brass goggles with teal lenses pushed up on the forehead",
         "Cream work jacket over navy overalls",
         "Root-seed pod with lime leaves and a vine-burst impact",
         "Biomass glow is always lime; team colour only on teal or coral trim"],
 "p08": ["Hard-hat helmet with brass trim and a large glowing blue crystal lamp",
         "Thick dark-brown beard, heavy brows, round red nose",
         "Square wooden crate backpack with brass corners and an X brace",
         "Cream chest plate in a brass frame with a blue hex crystal",
         "Pick-hammer with a blue crystal pick head (0.63 of body height) that folds into a stone hammer",
         "Barricade in three states: folded, deployed, damaged",
         "Widest, most compact body: barrel torso on short legs",
         "Crimson team turns the blue crystals and trim red-orange"],
 "p09": ["All eight facings for each class: N, NE, E, SE, S, SW, W, NW",
         "Consistent feet, scale and ground shadow in every facing",
         "Idle", "Run", "Attack", "Ability", "Carry or interact", "Hit", "Knockout",
         "Backpacks, shields, tools and top surfaces readable under the high camera",
         "Azure and Crimson versions of each class"],
 "p10": ["Egg: dark slate faceted plates with glowing lime seams and a lime ground glow",
         "Neutral hatchling: small quadruped, moss, lime crystal spikes, cute face",
         "Azure adult: cyan cracks, cyan crystal crown, moss, tan belly plate",
         "Crimson adult: the same body with orange-red crystals and cracks",
         "Berserk adult: magenta-violet crystals, fractured glow, floating shards, rearing pose",
         "Roar with cyan shock rings",
         "Ground slam with rock debris",
         "Red circular telegraph marker on the ground",
         "One recognisable anatomy in every state"],
 "p11": ["Heavy quadruped with gorilla-like weight, not a fast dinosaur",
         "Shoulders far wider than the hips",
         "Long forelimbs planted on boulder fists",
         "Short hind legs with stubby rock toes and moss",
         "Small expressive face, low between the shoulders",
         "Crystal ridge down the spine to a crystal-tipped tail",
         "Front, three-quarter, profile and rear views",
         "Idle, walk, roar and slam wind-up"],
 "p12": ["Kaiju shrine to the north: the egg between two curved stone horns",
         "Central circular hill plaza with a crown emblem",
         "Azure pad to the west and Crimson pad to the east, with banners and crystals",
         "Stone-paved roads and wooden rope bridges, several of them diagonal",
         "Waterfalls off the island edges, round trees, crystal clusters along routes",
         "Floating islands in a sea of cloud"],
 "p13": ["Opposing protected bases at clear ends",
         "One central circular hill with at least three approach routes",
         "Kaiju shrine in a distant district or corner",
         "Biomass off the shortest route to the hill",
         "Breakable Miner shortcuts are never the only path",
         "Safe, readable edges: no accidental fall deaths",
         "Strong landmarks from the high camera",
         "A Kaiju path wide enough for its telegraphs",
         "12 to 18 seconds from base to hill; shrine detour 8 to 12 seconds",
         "Fixed camera angle, no rotation in normal play"],
 "p14": ["Biomass plants in small, medium and large",
         "Loose biomass orb and the carried bundle",
         "Corruption growth",
         "Shrine in Azure and Crimson versions",
         "Hill in neutral, Azure and Crimson versions",
         "Wall intact, cracked and destroyed",
         "Barricade folded, deployed and damaged",
         "Team banners for both sides",
         "Respawn pad", "Rope bridge", "Stone ramp",
         "Objective markers in cyan, grey and red",
         "Floor tiles: flagstone, mossy stone, dirt and grass, corrupted",
         "Every damage state made as its own piece, no procedural destruction"],
 "p15": ["Match block, top centre: clock pill, Azure and Crimson score panels with pips",
         "Hill-control bar with the crown medallion under the clock",
         "Kaiju block, top right: egg portrait, segmented meter, allegiance and hatch pips",
         "Player block, bottom left: portrait, hearts, health bar",
         "Three ability buttons with their key labels",
         "World layer: objective arrows, contested state, crown marker over the hill",
         "Minimap, bottom right",
         "Priority order: win condition, then Kaiju, then player"],
}
assert sorted(FEATS) == ["p%02d" % i for i in range(3, 16)]

def esc(t):
    return t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

edits = []
doc = base

def edit(old, new):
    global doc
    n = doc.count(old)
    assert n == 1, (n, old[:120])
    doc = doc.replace(old, new)
    edits.append({"old_text": old, "new_text": new})

# 1. CSS plus the new top section, placed right after "Who is scoring".
CSS = """.new.uplan{border-left-color:var(--brass)}
.uplan h2{font-size:20px;text-wrap:balance}
.uplan .stamp{margin:0 0 6px}
.uplan h3{font:800 16px/1.3 "Nunito Sans",system-ui,sans-serif;color:var(--brass);margin:18px 0 4px;text-wrap:balance}
.uplan p,.uplan li{max-width:78ch}
.uplan ol{margin:4px 0;padding-left:22px} .uplan ol li{margin:6px 0}
.asks{display:grid;gap:8px;margin:8px 0 2px;padding:0;list-style:none}
.asks li{background:var(--panel2);border:1px solid var(--line);border-radius:10px;padding:8px 12px}
.pill.unity{background:transparent;color:var(--brass);border:1px solid var(--brass)}
.inu{background:var(--panel2);border:1px solid var(--line);border-left:4px solid var(--brass);border-radius:10px;padding:12px 14px;margin:12px 0 4px}
.inu p{margin:4px 0}
.feats{list-style:none;margin:8px 0;padding:0;display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:4px 18px;font-size:14px}
.feats li{display:flex;gap:8px;align-items:flex-start}
.box{flex:none;width:16px;height:16px;margin-top:3px;border:2px solid var(--dim);border-radius:4px}
.arch{border:1px dashed var(--line);border-radius:10px;padding:10px 14px 14px;margin-top:14px}
.archlbl{margin:0 0 4px;display:flex;gap:8px;flex-wrap:wrap;align-items:center}
.t.arch{background:#3a4560;color:var(--ink);border:1px solid #4a5674}
.archtag{margin:2px 0 6px}
"""

TOP = f"""<section class="new uplan" aria-labelledby="unih"><h2 id="unih">Now building in Unity</h2>
<p class="stamp dim small">Updated {CENTRAL} · Unity repo commit <code>{COMMIT}</code></p>
<h3>What changed</h3>
<p>The game is being rebuilt in Unity, the game engine (the software studios use to build and ship games) that Max works in. The browser demo is frozen: nothing new goes into it. It is now the rulebook and the test reference, so the Unity game has to play by the same rules and pass the same tests.</p>
<p>The new code lives at <a href="{REPO}">critical-mass-unity on GitHub</a>. The controls and Max's list of game items are in <a href="{REPO}/blob/main/MAX-ITEMS.md">MAX-ITEMS.md</a>.</p>
<h3>What exists tonight</h3>
<p>An empty Unity project that compiles, runs its tests and produces a Mac app. Nothing of the game is built in it yet, so every row below says <b>Not started in Unity</b>. The browser captures and Claude's browser scores stay inside each row, boxed and marked "{ARCH}", for comparison only.</p>
<h3>The plan, in four phases</h3>
<ol>
<li><b>Phase 0, foundation.</b> Game rules, ground, pathing, base rules, bots, controls, a placeholder view and a test runner. <b>Done when</b> a full 16 versus 16 bot match plays start to finish in grey-box (plain shapes, no art) and every test carried over from the browser demo passes.</li>
<li><b>Phase 1, mechanics.</b> Max's items, starting with Travis's four must-haves: dig and place ground, knock down structures, jumping, and crossing bridges diagonally.</li>
<li><b>Phase 2, look.</b> The shared toon shader first, then the bible rows in this order: p05 colour and type, p06 Warrior, p07 Farmer, p08 Miner, p09 animation set, p10 and p11 Kaiju, p12 and p13 map, p14 props, p15 HUD, p04 logo and title, p03 overall look. A row is finished when it passes the team's score (see How a row passes).</li>
<li><b>Phase 3, finish.</b> Effects, audio (the 42 free sounds), menus, and a Windows build for Travis.</li>
</ol>
<p>Phases 1 and 2 run side by side once Phase 0 is done. The plan writes down a finish line for Phase 0 only; the finish lines for Phases 1 and 3 get set when Wilson approves the plan.</p>
<h3>How the look gets made</h3>
<p>Every model uses one shared toon shader. A shader is the small program that decides how a surface is coloured and lit. This one gives flat bible-palette colours, two bands of light and shadow, a dark outline, a team-colour channel and a rim of light so each team reads at a glance. Nothing shiny or metallic. Until the art arrives, mechanics work uses team-coloured capsules and cubes, so gameplay never waits on art.</p>
<p>All three classes share one rig, the skeleton of joints a model moves by: the free KayKit rig with 41 joints, so all 76 free animation clips work on every class. Each character is sculpted by hand in Blender to its bible page, and its source file is kept in the repo. No script-generated stand-in bodies: those scored 3 to 4 in the browser.</p>
<p><b>Open point, the Warrior:</b> Max's Blender Warrior is the starting mesh if his file passes an audit (it has to fit the shared rig and have clean geometry). Either way the Warrior gets the five corrections bible page p06 needs:</p>
<ol>
<li>A round shield, the class identifier, never removed.</li>
<li>A stone cleaver with a glowing edge.</li>
<li>A dark-brown topknot tied with a brass ring.</li>
<li>Grey stone armour with cyan cracks.</li>
<li>Flat toon shading.</li>
</ol>
<h3>How a row passes now</h3>
<p>Each row gets a fixed set of framings: the bible's own poses and camera angles. A script renders every framing from the actual Unity build on a Void Navy ground and runs three measured checks:</p>
<ol>
<li><b>Colour.</b> Each named bible colour on that asset is within 10 on CIEDE2000, a standard score for how different two colours look to the eye (lower is closer).</li>
<li><b>Shape.</b> The character's outline overlaps the outline in the bible drawing by at least 85 percent.</li>
<li><b>Required features.</b> Every item on the row's checklist, for the Warrior the shield, cleaver, topknot, brass ring, teal scarf and stone armour, is confirmed by a reviewer looking at the capture, with a one-line reason.</li>
</ol>
<p>The old browser version measured the background as well as the character, which made its colour numbers unreliable. The new version also renders a mask of the same shot and measures only the character's own pixels.</p>
<p>Passing all three checks only moves a row to "ready for the team". <b>A row still passes only when every framing gets a 9 or 10 from the team.</b> Each row will show the bible crop and the Unity capture side by side, with the commit and the capture time, and will be marked out of date when the game has changed since its capture.</p>
<h3>What I need from you</h3>
<ul class="asks">
<li><b>Wilson:</b> approve this plan before anything is built.</li>
<li><b>Max:</b> push your Unity project and your Blender Warrior file so they can be audited.</li>
<li><b>Anyone who knows:</b> is Travis on Windows or Mac?</li>
</ul>
</section>
"""

edit('</style></head><body><main>', CSS + '</style></head><body><main>')
edit('<tbody id="legendbody"></tbody></table></div></section>\n<p class="bar">',
     '<tbody id="legendbody"></tbody></table></div></section>\n' + TOP + '<p class="bar">')

# 2. Old "new since last update" box: label it as the browser demo's.
edit('<h2 id="newh">New since last update</h2>',
     '<h2 id="newh">Browser demo: last update, archived 2026-11-09</h2>')

# 3. Summary paragraph and overview table.
edit('<p class="summary">All 13 rows are FAIL.',
     '<p class="summary"><b>In Unity, all 13 rows are not started.</b> No Unity capture exists yet.</p>\n'
     f'<p class="summary"><span class="t arch">{ARCH}</span> All 13 rows are FAIL.')
m = re.search(r'<div class="tw"><table class="ov"><thead><tr><th>Page</th><th>Asset</th><th>Verdict</th>.*?</table></div>', doc)
old_tbl = m.group(0)
new_tbl = old_tbl.replace('<th>Asset</th><th>Verdict</th><th>Claude</th>',
                          '<th>Asset</th><th>Unity</th><th>Browser verdict (archived)</th><th>Claude (browser)</th>')
new_tbl = re.sub(r'(</td><td>[^<]*</td>)(<td><span class="v red">FAIL</span></td>)',
                 r'\1<td><span class="pill unity">Not started</span></td>\2', new_tbl)
assert new_tbl.count('Not started</span>') == 13
edit(old_tbl, new_tbl)

# 4. Rows: status, Unity block with the feature checklist, archived browser material.
heads = list(re.finditer(r'<section class="row" id="(p\d\d)"><header><h2><span class="pg">p\d\d</span> (.*?)</h2><div class="hm">(<span class="v red big2">FAIL</span><span class="pill [a-z]+">([^<]*)</span><span class="item">([^<]*)</span>)</div></header>', base))
assert len(heads) == 13
for h in heads:
    row, title, hm, was, item = h.groups()
    tail = title[-14:]
    old = f'{tail}</h2><div class="hm">{hm}</div></header>'
    feats = "".join(f'<li><span class="box" aria-hidden="true"></span><span>{esc(f)}</span></li>' for f in FEATS[row])
    pg = row[1:]
    unity = (f'<div class="inu"><p><b>In Unity: not started.</b> No Unity capture yet. When this row is built, the Unity capture sits next to each bible crop here. '
             f'Look phase build order: {ORDER[row]} of 11.</p>'
             f'<p class="dim small">Required features from bible page {pg}. Each one is ticked only when a reviewer confirms it on the Unity capture, with a one-line reason.</p>'
             f'<ul class="feats" aria-label="Required features for {row}, none confirmed yet">{feats}</ul>'
             f'<p class="dim small">Any team scores already on this row were given to the browser demo on 9 Nov. Hold new scores until a Unity capture appears here.</p></div>')
    arch = (f'<div class="arch"><p class="archlbl"><span class="t arch">{ARCH}</span>'
            f'<span class="dim small">Browser verdict FAIL · browser status was "{esc(was)}" · browser item {item}. Kept for comparison only; do not score these captures as the Unity game.</span></p>')
    new = f'{tail}</h2><div class="hm"><span class="pill unity">Not started in Unity</span></div></header>{unity}{arch}'
    edit(old, new)
for r in sorted(FEATS):
    edit(f'</p><div class="board" data-row="{r}"><div class="claude"><div class="lbl">Claude\'s measured score</div>',
         f'</p></div><div class="board" data-row="{r}"><div class="claude"><div class="lbl">Claude\'s measured score</div><div class="archtag"><span class="t arch">{ARCH}</span></div>')

# 5. Capture labels.
for c in re.finditer(r'images/cap/([^"]+)" alt="Demo capture ([^"]*)"><span class="cap">demo</span>', base):
    f, what = c.groups()
    edit(f'{f}" alt="Demo capture {what}"><span class="cap">demo</span>',
         f'{f}" alt="Browser demo capture, archived 2026-11-09: {what}"><span class="cap">browser · archived 9 Nov</span>')

# 6. Parity items heading and version line (new line on top, old kept).
edit('<section class="note"><h2>Parity items</h2>',
     '<section class="note"><h2>Parity items: the browser demo\'s item history (archived 2026-11-09)</h2>')
edit('<p class="dim small">Version 2026-11-09T14:23:01Z (date -u).',
     f'<p class="dim small">Version {UTC} (date -u). v4 puts the Unity plan on top, marks every row not started in Unity, adds each row\'s required-feature checklist and archives the browser captures and scores. Unity repo commit {COMMIT}.</p>\n'
     '<p class="dim small">Version 2026-11-09T14:23:01Z (date -u).')

# Guards.
i0, i1 = base.index('</main><script>'), doc.index('</main><script>')
assert base[i0:] == doc[i1:], "script block changed"
assert doc.count('<img') == base.count('<img') == 75
assert len(edits) <= 64 and sum(len(e["old_text"]) for e in edits) < 64 * 1024
for r in sorted(FEATS):
    assert doc.count(f'id="{r}"') == 1
open(os.path.join(HERE, "index.html"), "w", encoding="utf-8").write(doc)
json.dump(edits, open(os.path.join(HERE, "edits.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
print("edits", len(edits), "old bytes", sum(len(e["old_text"]) for e in edits), "doc", len(base), "->", len(doc))
