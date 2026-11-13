#!/usr/bin/env bash
# Every builder and integrator runs this first. Exit 0 = go on; 3 = HALT or STALL; 4 = STOP-DISPATCH (spend 90 %,
# hard stop), or the machine hold (DG-43): ~/cm-incidents/STOP-DISPATCH from cm-machine-guard, PAUSED-AFTER-UNCLEAN-RESTART,
# or the owner playing (a process named "Critical Mass"); 5 = no running loop recorded, or the launch ids were never recorded (waits up to 120 s for them).
exec python3 "$(dirname "${BASH_SOURCE[0]}")/cmloop.py" halt-check "$@"
