#!/usr/bin/env bash
# b15 rules pass 3: ONE command to re-measure the comeback default sets (B15RuleSetsTests, A..E at 24 then 72 seeds) on the
# current commit, optionally with a bot-brain switch on (feat/b15-bot-brain).
#   tools/ci/b15-rulesets-probe.sh                      today's bots
#   tools/ci/b15-rulesets-probe.sh Bots.TeamBrain       the GameConfig bool at that field path set true on every arm
# It makes a throwaway branch ci/b15-rulesets-<sha>-<time> from HEAD in a temporary worktree (your checkout is untouched),
# narrows tools/ci/sim-tests.sh to that one test, writes the brain path into B15RuleSetsTests.Brain, commits, and pushes
# (sim-tests runs on every push to ci/**). Read the result: gh run list --branch <printed branch> --workflow sim-tests.yml,
# then the "[b15 sets" lines in sim-results.xml. Delete the branch when read: git push origin --delete <branch>.
# Never pushes main, never force-pushes.
set -euo pipefail
BRAIN="${1:-}"
ROOT="$(git rev-parse --show-toplevel)"
SHA="$(git -C "$ROOT" rev-parse --short=8 HEAD)"
BR="ci/b15-rulesets-${SHA}-$(date -u +%Y%m%d%H%M%S)"
WT="$(mktemp -d)/probe"
git -C "$ROOT" worktree add -q --detach "$WT" HEAD
trap 'git -C "$ROOT" worktree remove --force "$WT" >/dev/null 2>&1 || true' EXIT
cd "$WT"
sed -i.bak "s/^WHERE='cat != Slow'/WHERE='test =~ \"B15RuleSetsTests\"'/" tools/ci/sim-tests.sh && rm -f tools/ci/sim-tests.sh.bak
grep -q "B15RuleSetsTests" tools/ci/sim-tests.sh || { echo "b15-rulesets-probe: could not narrow sim-tests.sh" >&2; exit 1; }
T=Assets/_Game/Sim.Tests/B15RuleSetsTests.cs
sed -i.bak "s/public const string Brain = \"[^\"]*\";/public const string Brain = \"${BRAIN}\";/" "$T" && rm -f "$T.bak"
grep -q "public const string Brain = \"${BRAIN}\";" "$T" || { echo "b15-rulesets-probe: could not set Brain" >&2; exit 1; }
git add tools/ci/sim-tests.sh "$T"
git commit -q -m "probe: B15RuleSetsTests only${BRAIN:+, brain $BRAIN}"
git push -q origin "HEAD:refs/heads/$BR"
echo "$BR"
