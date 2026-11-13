#!/usr/bin/env bash
# CI offload kit helper (ci/offload-2, 2026-11-13): run Unity player builds and Blender jobs on GitHub's runners instead
# of this Mac's one heavy slot, wait cheaply, download and prepare the result. Generic: project values come from
# tools/ci/offload.conf (repo, workflows, app name, play-test host). Guide: docs/CI-OFFLOAD-KIT.md.
#
#   offload.sh unity-build --ref REF [--target mac|windows] [--development] [--out DIR] [--then-playtest [--mode smoke|captures]]
#   offload.sh blender --ref REF --script PATH [--blend PATH] [--args 'A B'] [--env K=V]... [--plan JSON|FILE]
#                      [--inputs PATH]... [--keep-inputs] [--outputs GLOB]... [--gl egl|xvfb] [--out DIR]
#   offload.sh prepare-mac-app APP.app [--expect-sha SHA]     (exec bits, quarantine, ad-hoc signature, stamp)
#   common: [--push] (push HEAD to REF first; never main) [--max-wait MIN] [--wf-ref BRANCH] (default CI_WORKFLOW_REF)
#
# --ref is any branch, tag or sha that exists on GitHub (origin/ prefixes are stripped). The workflow is dispatched FROM
# CI_WORKFLOW_REF (main) with the ref as an input, so old branches get today's workflow and main's warm caches.
# Waiting: one `gh run watch --interval CI_POLL_SECONDS` (120 s, LOW-CHURN-NOTICE), no other polling.
# Prints ONE JSON line (also appended to CI_LOG_FILE when set). Exit: 0 pass, 1 fail (build, job or play-test),
# 75 could not start or timed out (the run may still be going on GitHub), 64 usage.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(git -C "$PWD" rev-parse --show-toplevel 2>/dev/null || (cd "$HERE/../.." && pwd))"
# shellcheck source=offload.conf
source "$HERE/offload.conf"
cmd="${1:-}"; shift || true
REF=''; TARGET=mac; DEV=0; OUT=''; THEN_PT=0; PT_MODE=captures; PUSH=0; MAXW="$CI_MAX_WAIT_MIN"; WFREF="$CI_WORKFLOW_REF"
SCRIPT=''; BLEND=''; ARGS=''; ENVS=''; PLAN=''; INPUTS=(); KEEP_INPUTS=0; OUTGLOBS=''; GL=egl; EXPECT=''; APPARG=''
while [[ $# -gt 0 ]]; do
  case "$1" in
    --ref) REF="${2:?}"; shift 2 ;;
    --target) TARGET="${2:?}"; shift 2 ;;
    --development) DEV=1; shift ;;
    --out) OUT="${2:?}"; shift 2 ;;
    --then-playtest) THEN_PT=1; shift ;;
    --mode) PT_MODE="${2:?}"; shift 2 ;;
    --push) PUSH=1; shift ;;
    --max-wait) MAXW="${2:?}"; shift 2 ;;
    --wf-ref) WFREF="${2:?}"; shift 2 ;;
    --script) SCRIPT="${2:?}"; shift 2 ;;
    --blend) BLEND="${2:?}"; shift 2 ;;
    --args) ARGS="${2?}"; shift 2 ;;
    --env) ENVS="${ENVS}${2:?}"$'\n'; shift 2 ;;
    --plan) PLAN="${2:?}"; shift 2 ;;
    --inputs) INPUTS+=("${2:?}"); shift 2 ;;
    --keep-inputs) KEEP_INPUTS=1; shift ;;
    --outputs) OUTGLOBS="${OUTGLOBS}${2:?}"$'\n'; shift 2 ;;
    --gl) GL="${2:?}"; shift 2 ;;
    --expect-sha) EXPECT="${2:?}"; shift 2 ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    -*) echo "offload: unknown option $1" >&2; exit 64 ;;
    *) APPARG="$1"; shift ;;
  esac
done

T0=$(date +%s); STEPS=''
step() { STEPS="${STEPS}${STEPS:+,}\"$1\":$2"; }        # name seconds
jstr() { python3 -I -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"; }
emit() {  # status exit [extra json]
  local line
  line=$(printf '{"tool":"ci-offload","cmd":"%s","ts":"%s","ref":"%s","status":"%s","exit":%d,"seconds":%d,"steps":{%s}%s}' \
    "$cmd" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$REF" "$1" "$2" "$(( $(date +%s) - T0 ))" "$STEPS" "${3:-}")
  echo "$line"; [[ -n "$CI_LOG_FILE" ]] && echo "$line" >> "$CI_LOG_FILE"; exit "$2"
}

# ---- prepare-mac-app: what a downloaded .app needs before macOS will start it ----------------------------------------
prepare_mac_app() {  # APP [EXPECT_SHA] -> sets PREP_JSON, returns 0 ok / 1 not runnable
  local app="${1%/}" exp="${2:-}" exe sig_before sig_after archs stamp_v stamp_c ok=0
  [[ -d "$app/Contents/MacOS" ]] || { PREP_JSON=",\"prepare\":{\"error\":\"no app at $app\"}"; return 1; }
  chmod -R u+rwX,go+rX "$app"; chmod +x "$app"/Contents/MacOS/* 2>/dev/null   # zip artifacts drop these; tar keeps them
  xattr -cr "$app" 2>/dev/null                                                 # quarantine and other inherited xattrs
  if codesign -v "$app" 2>/dev/null; then sig_before=valid; else sig_before=invalid; fi
  if [[ $sig_before == invalid ]]; then codesign --force --deep -s - "$app" >/dev/null 2>&1; fi   # ad-hoc (no identity)
  if codesign -v "$app" 2>/dev/null; then sig_after=valid; else sig_after=invalid; ok=1; fi
  exe="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist" 2>/dev/null)"
  archs="$(lipo -archs "$app/Contents/MacOS/$exe" 2>/dev/null)"
  stamp_v="$(LC_ALL=C grep -a -o -m1 -E 'version=[0-9][0-9A-Za-z.]{0,30}' "$app/$CI_STAMP_FILE" 2>/dev/null | head -1 | cut -d= -f2)"
  stamp_c="$(LC_ALL=C grep -a -o -m1 -E 'commit=[0-9a-f]{7,40}' "$app/$CI_STAMP_FILE" 2>/dev/null | head -1 | cut -d= -f2)"
  local match=null
  if [[ -n "$exp" ]]; then
    if [[ -n "$stamp_c" && "$exp" == "$stamp_c"* ]]; then match=true; else match=false; ok=1; fi
  fi
  [[ -x "$app/Contents/MacOS/$exe" ]] || ok=1
  PREP_JSON=$(printf ',"app":%s,"prepare":{"signature_before":"%s","signature_after":"%s","archs":"%s","stamp_version":"%s","stamp_commit":"%s","stamp_matches_sha":%s}' \
    "$(jstr "$app")" "$sig_before" "$sig_after" "$archs" "$stamp_v" "$stamp_c" "$match")
  return $ok
}

if [[ "$cmd" == prepare-mac-app ]]; then
  [[ -n "$APPARG" ]] || { echo "usage: offload.sh prepare-mac-app APP.app [--expect-sha SHA]" >&2; exit 64; }
  if prepare_mac_app "$APPARG" "$EXPECT"; then emit pass 0 "$PREP_JSON"; else emit fail 1 "$PREP_JSON"; fi
fi

case "$cmd" in unity-build|blender) ;; *) sed -n '2,17p' "$0" >&2; exit 64 ;; esac
[[ -n "$REF" ]] || REF="$(git -C "$ROOT" rev-parse --abbrev-ref HEAD)"
REF="${REF#origin/}"; REF="${REF#refs/heads/}"
gh auth status >/dev/null 2>&1 || emit could-not-start 75 ',"reason":"gh not authenticated or GitHub unreachable"'
if [[ $PUSH -eq 1 ]]; then
  [[ "$REF" == main || "$REF" == "$WFREF" ]] && emit could-not-start 64 ',"reason":"refusing to push the default branch"'
  git -C "$ROOT" push -q origin "HEAD:refs/heads/$REF" 2>/dev/null || emit could-not-start 75 ',"reason":"git push failed"'
fi
# The commit GitHub will build (branch head on GitHub, or the sha/tag itself).
SHA="$(git -C "$ROOT" ls-remote origin "refs/heads/$REF" "refs/tags/$REF" 2>/dev/null | head -1 | cut -f1)"
[[ -n "$SHA" ]] || { [[ "$REF" =~ ^[0-9a-f]{7,40}$ ]] && SHA="$REF"; }
[[ -n "$SHA" ]] || emit could-not-start 64 ',"reason":"ref not found on GitHub (push it first, or pass --push)"'
RID="rq$(date -u +%H%M%S)$$"
OUT="${OUT:-$CI_OUT_ROOT/$cmd-$(date -u +%Y%m%dT%H%M%SZ)-${REF//\//_}}"
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"

# ---- inputs and dispatch -----------------------------------------------------------------------------------------------
ASSET=''
if [[ "$cmd" == unity-build ]]; then
  WF="$CI_UNITY_WORKFLOW"
  case "$TARGET" in mac|osx|StandaloneOSX) UT=StandaloneOSX ;; windows|win|StandaloneWindows64) UT=StandaloneWindows64 ;; *) emit could-not-start 64 ',"reason":"--target mac|windows"' ;; esac
  FIELDS=(-f "ref=$REF" -f "target=$UT" -f "development=$([[ $DEV -eq 1 ]] && echo true || echo false)" -f "request-id=$RID")
else
  WF="$CI_BLENDER_WORKFLOW"
  [[ -n "$SCRIPT" || -n "$PLAN" ]] || emit could-not-start 64 ',"reason":"--script PATH or --plan JSON|FILE"'
  if [[ -n "$PLAN" ]]; then
    [[ -f "$PLAN" ]] && PLAN="$(python3 -I -c 'import json,sys; print(json.dumps(json.load(open(sys.argv[1]))))' "$PLAN")"
    SCRIPT="{kit}/blender_plan.py"; ENVS="${ENVS}CI_PLAN=${PLAN}"$'\n'
  fi
  if [[ ${#INPUTS[@]} -gt 0 ]]; then
    ti=$(date +%s); tb="$(mktemp -d)"; tarf="$tb/in.tar.gz"
    for p in "${INPUTS[@]}"; do [[ -e "$p" ]] || emit could-not-start 64 ",\"reason\":$(jstr "input not found: $p")"; done
    # Flat bundle: each --inputs path lands at the bundle root under its own name.
    args=(); for p in "${INPUTS[@]}"; do args+=(-C "$(cd "$(dirname "$p")" && pwd)" "$(basename "$p")"); done
    COPYFILE_DISABLE=1 tar -czf "$tarf" "${args[@]}" || emit could-not-start 75 ',"reason":"could not pack inputs"'
    ASSET="inputs-$(shasum -a 256 "$tarf" | cut -c1-16).tar.gz"; mv "$tarf" "$tb/$ASSET"
    gh release view "$CI_INPUTS_TAG" -R "$CI_REPO" >/dev/null 2>&1 || gh release create "$CI_INPUTS_TAG" -R "$CI_REPO" --draft --prerelease \
      --title "CI inputs (draft: visible only to collaborators with push access)" \
      --notes "Input bundles for blender.yml, uploaded by tools/ci/offload.sh and deleted after each job. Never publish this release: publishing makes every asset public." >/dev/null \
      || emit could-not-start 75 ',"reason":"could not create the draft release for inputs"'
    gh release upload "$CI_INPUTS_TAG" "$tb/$ASSET" -R "$CI_REPO" --clobber >/dev/null || emit could-not-start 75 ',"reason":"input upload failed"'
    step inputs_upload $(( $(date +%s) - ti )); rm -rf "$tb"
  fi
  FIELDS=(-f "ref=$REF" -f "script=$SCRIPT" -f "args=$ARGS" -f "blend=$BLEND" -f "env=$ENVS" -f "inputs-asset=$ASSET"
          -f "outputs=$OUTGLOBS" -f "gl=$GL" -f "request-id=$RID")
fi
td=$(date +%s)
err=$(gh workflow run "$WF" -R "$CI_REPO" --ref "$WFREF" "${FIELDS[@]}" 2>&1) \
  || emit could-not-start 75 ",\"reason\":$(jstr "dispatch refused: $(head -1 <<<"$err")")"
# Find our run by the request id in its run-name: one short wait, then at most two slower looks.
RUN=''
for wait_s in 20 40 120; do
  sleep "$wait_s"
  RUN=$(gh run list -R "$CI_REPO" --workflow "$WF" --limit 30 --json databaseId,displayTitle \
        -q "[.[] | select(.displayTitle | contains(\"$RID\"))][0].databaseId // empty" 2>/dev/null)
  [[ -n "$RUN" ]] && break
done
[[ -n "$RUN" ]] || emit could-not-start 75 ',"reason":"run did not appear within 3 min"'
URL="https://github.com/$CI_REPO/actions/runs/$RUN"
step dispatch $(( $(date +%s) - td ))
echo "offload: $WF for $REF (${SHA:0:12}) -> $URL; waiting, one check per ${CI_POLL_SECONDS} s, max ${MAXW} min" >&2
tw=$(date +%s)
perl -e 'alarm shift; exec @ARGV' $(( MAXW * 60 )) gh run watch "$RUN" -R "$CI_REPO" --interval "$CI_POLL_SECONDS" >/dev/null 2>&1
concl=$(gh run view "$RUN" -R "$CI_REPO" --json status,conclusion -q '.status + " " + (.conclusion // "")' 2>/dev/null)
step run $(( $(date +%s) - tw ))
[[ "$concl" == completed* ]] || emit timed-out 75 ",\"run\":$RUN,\"url\":\"$URL\",\"reason\":$(jstr "still ${concl:-unknown} after ${MAXW} min (keeps going on GitHub)")"
concl="${concl#completed }"
gh run view "$RUN" -R "$CI_REPO" --json jobs -q '[.jobs[] | {name, conclusion, startedAt, completedAt}]' > "$OUT/jobs.json" 2>/dev/null
[[ "$concl" == success ]] || gh run view "$RUN" -R "$CI_REPO" --log-failed > "$OUT/failed.log" 2>/dev/null
# ---- download --------------------------------------------------------------------------------------------------------
tdl=$(date +%s)
if [[ "$cmd" == unity-build ]]; then
  ART="player-$UT"
  [[ "$concl" == success ]] || emit fail 1 ",\"run\":$RUN,\"url\":\"$URL\",\"conclusion\":\"$concl\",\"out\":$(jstr "$OUT")"
  gh run download "$RUN" -R "$CI_REPO" -n "$ART" -D "$OUT/artifact" >/dev/null 2>&1 \
    || emit fail 1 ",\"run\":$RUN,\"url\":\"$URL\",\"reason\":\"artifact $ART missing\",\"out\":$(jstr "$OUT")"
  mkdir -p "$OUT/player" && tar -xzf "$OUT/artifact/player.tgz" -C "$OUT/player" && rm -f "$OUT/artifact/player.tgz"
  step download_unpack $(( $(date +%s) - tdl ))
  INFO=",\"run\":$RUN,\"url\":\"$URL\",\"sha\":\"$SHA\",\"out\":$(jstr "$OUT/player"),\"build_info\":$(cat "$OUT/artifact/build-info.json" 2>/dev/null || echo null)"
  if [[ "$UT" != StandaloneOSX ]]; then emit pass 0 "$INFO"; fi
  APP="$OUT/player/$CI_MAC_APP"
  [[ -d "$APP" ]] || APP="$(find "$OUT/player" -maxdepth 2 -name '*.app' -type d | head -1)"
  tp=$(date +%s)
  prepare_mac_app "$APP" "$SHA"; prc=$?
  step prepare $(( $(date +%s) - tp ))
  INFO="$INFO$PREP_JSON"
  [[ $prc -eq 0 ]] || emit fail 1 "$INFO,\"reason\":\"downloaded app not runnable (signature, exec bit or stamp; see prepare)\""
  echo "offload: app ready at $APP" >&2
  if [[ $THEN_PT -eq 1 ]]; then
    tt=$(date +%s)
    "$ROOT/$CI_PLAYTEST_CMD" --host "$CI_PLAYTEST_HOST" --app "$APP" --mode "$PT_MODE" --out "$OUT/playtest" > "$OUT/playtest.log" 2>&1
    ptc=$?
    step playtest $(( $(date +%s) - tt ))
    summ="$(cat "$OUT/playtest/summary.json" 2>/dev/null | python3 -I -c 'import json,sys
try: d=json.load(sys.stdin)
except Exception: d=None
print(json.dumps(d))' 2>/dev/null)"
    INFO="$INFO,\"playtest\":{\"host\":\"$CI_PLAYTEST_HOST\",\"mode\":\"$PT_MODE\",\"exit\":$ptc,\"out\":$(jstr "$OUT/playtest"),\"summary\":${summ:-null}}"
    [[ $ptc -eq 0 ]] || emit fail 1 "$INFO,\"reason\":\"play-test failed (see playtest.log)\""
  fi
  emit pass 0 "$INFO"
else
  gh run download "$RUN" -R "$CI_REPO" -n blender-out -D "$OUT" >/dev/null 2>&1 || echo "offload: no blender-out artifact" >&2
  step download $(( $(date +%s) - tdl ))
  if [[ -n "$ASSET" && $KEEP_INPUTS -eq 0 ]]; then gh release delete-asset "$CI_INPUTS_TAG" "$ASSET" -R "$CI_REPO" -y >/dev/null 2>&1; fi
  INFO=",\"run\":$RUN,\"url\":\"$URL\",\"sha\":\"$SHA\",\"conclusion\":\"$concl\",\"out\":$(jstr "$OUT"),\"report\":$(cat "$OUT/blender-report.json" 2>/dev/null || echo null)"
  [[ "$concl" == success ]] && emit pass 0 "$INFO"
  emit fail 1 "$INFO"
fi
