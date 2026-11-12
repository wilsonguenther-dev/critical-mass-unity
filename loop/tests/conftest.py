"""Point every loop path at a throwaway folder before any test imports cmloop (paths are read at import)."""
import atexit
import os
import shutil
import sys
import tempfile
from pathlib import Path

_TMP = Path(tempfile.mkdtemp(prefix='cmloop-tests-'))
for k, sub in (('CM_LOOP_STATE', 'state'), ('CM_LOOP_LOGS', 'logs'), ('CM_LOOP_LANES', 'lanes'),
               ('CM_LOOP_LIVELOG_DIR', 'livelog'), ('CM_LOOP_PROJECTS_DIR', 'projects')):
    os.environ[k] = str(_TMP / sub)
    (_TMP / sub).mkdir(parents=True, exist_ok=True)
os.environ['CM_LOOP_TEST_TMP'] = str(_TMP)
atexit.register(shutil.rmtree, _TMP, True)  # tests delete what they bring in
os.environ['CM_LOOP_SEND_GAP'] = '0'
os.environ['CM_LOOP_NO_SEND'] = '1'  # belt and braces: tests never text
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bin'))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'watchers'))
