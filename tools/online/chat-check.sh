#!/usr/bin/env bash
# b17-chat: did the host and the joiner exchange chat over the room connection? (online-smoke, local pair and cross-OS)
#   tools/online/chat-check.sh HOST_LOG JOIN_LOG [DROP_TICK]
# The joiner says "b17 hello from the joiner" at tick 150 and calls out "Good game" (quick-chat 7) at tick 180; the host
# says "b17 hello from the host" at tick 300. The host's line is required on the joiner only when the joiner was not
# dropped before it (DROP_TICK empty or past 330). Exit 1 when a line is missing.
set -u
H="$1"; J="$2"; D="${3:-}"
ok=0
grep -q "ONLINE CHAT .*: b17 hello from the joiner" "$H" && echo "chat: the host got the joiner's line" || { echo "chat: the host never got the joiner's line"; ok=1; }
grep -q "ONLINE CHAT .*: Good game" "$H" && echo "chat: the host got the joiner's callout" || { echo "chat: the host never got the joiner's callout"; ok=1; }
if [ -z "$D" ] || [ "$D" -gt 330 ]; then
  grep -q "ONLINE CHAT .*: b17 hello from the host" "$J" && echo "chat: the joiner got the host's line" || { echo "chat: the joiner never got the host's line"; ok=1; }
fi
grep -h "ONLINE CHAT\|ONLINE CALLOUT" "$H" "$J" | head -20
exit $ok
