#!/usr/bin/env bash
# Shared by tools/remote-playtest.sh and tools/remote-run.sh (sourced, not run). Talks to the second Mac over ssh with
# key auth only (BatchMode: never a password prompt), keeps every file on it under ~/cm-remote, and drives one detached
# job there through ~/cm-remote/bin/air-run.sh, which holds the box's queue lock (~/cm-remote/lock.d).
REMOTE_HOST="${CM_REMOTE_HOST:-wilsons-macbook-air.local}"
REMOTE_USER="${CM_REMOTE_USER:-$USER}"
SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=15 -o ServerAliveInterval=20 -o ServerAliveCountMax=6)
REMOTE_JOB=""

rssh() { ssh "${SSH_OPTS[@]}" "$REMOTE_USER@$REMOTE_HOST" "$@"; }
# Over the internet tunnel (2026-11-13 evening: the Air is reached through cloudflared, see ~/.ssh/config): --partial keeps a
# dropped copy's progress, -z compresses, and a build upload reuses the box's newest older build as a copy base, so only
# changed files cross the link.
rsync_to() {
  local extra=() last="${!#}"
  if [[ "$last" == *:cm-remote/builds/* ]]; then
    local prev; prev="$(rssh 'ls -t ~/cm-remote/builds 2>/dev/null | head -2' 2>/dev/null | tr '\n' ' ')"
    for b in $prev; do [[ "$last" == *"/builds/$b/" || "$last" == *"/builds/$b" ]] || extra+=("--copy-dest=../$b/"); done
  fi
  rsync -a --partial -z --timeout=300 ${extra[@]+"${extra[@]}"} -e "ssh ${SSH_OPTS[*]}" "$@"
}

remote_preflight() {  # reachable, ~/cm-remote exists, the runner is current
  rssh 'mkdir -p ~/cm-remote/bin ~/cm-remote/builds ~/cm-remote/runs ~/cm-remote/work' 2>/dev/null \
    || { echo "remote: cannot reach $REMOTE_USER@$REMOTE_HOST with key auth (is the Air awake and on this network?)" >&2; exit 69; }
  local here; here="$(dirname "${BASH_SOURCE[0]}")"
  rsync_to "$here/air-run.sh" "$REMOTE_USER@$REMOTE_HOST:cm-remote/bin/air-run.sh" \
    && rsync_to "$here/env.sh" "$REMOTE_USER@$REMOTE_HOST:cm-remote/env.sh" \
    || { echo "remote: could not copy the runner" >&2; exit 69; }
}

remote_abort() {  # Ctrl-C or our own timeout: ask the runner to stop the job (it restores mute and frees the lock)
  [[ -n "$REMOTE_JOB" ]] && rssh "touch ~/cm-remote/runs/$REMOTE_JOB/ABORT" 2>/dev/null
  echo "remote: aborted job $REMOTE_JOB" >&2
}

relay_state() {  # the remote waiter's state changes (its stderr) -> one readable line each, on our stderr
  local l
  while read -r l; do
    case "$l" in
      waiting*) echo "remote: waiting in the queue, the box is busy: ${l#waiting }" >&2 ;;
      running*) echo "remote: running on $REMOTE_HOST" >&2 ;;
    esac
  done
}

# remote_start ID MAX_WAIT_S air-run-args...   starts the job detached, then waits in one ssh session; prints a waiting
# line when the box is busy with someone else's job. Sets REMOTE_CODE to the job's exit code (124 when MAX_WAIT_S ran out).
remote_start() {
  local id="$1" max="$2"; shift 2
  REMOTE_JOB="$id"
  trap 'remote_abort; exit 130' INT TERM
  local q; q="$(printf ' %q' "$@")"
  rssh "mkdir -p ~/cm-remote/runs/$id && cd ~/cm-remote && { nohup /bin/bash bin/air-run.sh --id $id$q > runs/$id/runner.log 2>&1 < /dev/null & } ; echo started" >/dev/null \
    || { echo "remote: could not start the job" >&2; exit 69; }
  # Low churn on the caller (machine rule 2026-11-13): ONE ssh session waits on the box and prints only state changes;
  # the loop runs there. A dropped connection is retried after 120 s, at most 5 times.
  local t0=$SECONDS st="" fails=0 left
  while :; do
    left=$(( max - (SECONDS - t0) )); (( left < 1 )) && left=1
    st="$(rssh "cd ~/cm-remote/runs/$id 2>/dev/null || exit 3; last=''; n=0
      while :; do s=\$(cat state 2>/dev/null || echo starting)
        case \"\$s\" in done*) echo \"\$s\"; exit 0 ;; esac
        if [ \"\$s\" != \"\$last\" ]; then echo \"\$s\" >&2; last=\$s; fi
        n=\$((n+5)); [ \$n -ge $left ] && { echo timeout; exit 0; }; sleep 5; done" 2> >(relay_state))" || st=""
    case "$st" in
      done*) REMOTE_CODE="${st#done }"; trap - INT TERM; return ;;
      timeout) remote_abort; REMOTE_CODE=124; trap - INT TERM; return ;;
    esac
    fails=$((fails + 1))
    if (( fails > 5 )); then echo "remote: lost the box; the job may still finish there (runs/$id)" >&2; REMOTE_CODE=69; trap - INT TERM; return; fi
    echo "remote: connection dropped, retrying in 120 s" >&2; sleep 120
  done
}

remote_collect() {  # ID LOCAL_DIR: copy the run folder back, then delete it on the box
  mkdir -p "$2"
  rsync_to "$REMOTE_USER@$REMOTE_HOST:cm-remote/runs/$1/" "$2/" && rssh "rm -rf ~/cm-remote/runs/$1"
}
