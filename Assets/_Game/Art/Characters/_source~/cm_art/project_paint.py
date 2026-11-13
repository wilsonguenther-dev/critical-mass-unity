"""Shim so class scripts can `from cm_art import project_paint`. The helper itself lives in
Assets/_Game/Art/_tools~/bible_fit/project_paint.py (numpy only, runs in Blender's bundled Python)."""
import os as _os
from importlib.machinery import SourceFileLoader as _Loader

_PATH = _os.path.normpath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..", "_tools~",
                                        "bible_fit", "project_paint.py"))
_mod = _Loader("_bible_fit_project_paint", _PATH).load_module()
globals().update({k: v for k, v in vars(_mod).items() if not k.startswith("__")})
