from __future__ import annotations

from pathlib import Path
import runpy

readme = Path("VoiceCraft.Endstone/README.md")
marker_old = "\n<!-- dev-ui44-baseline: UI4.3 / 0.2.5 -->\n"
marker_new = "\n<!-- dev-ui44-baseline: UI4.4 / 0.2.6 -->\n"
original = readme.read_text(encoding="utf-8")
readme.write_text(original + marker_old, encoding="utf-8")
try:
    runpy.run_path("tools/dev_apply_ui44.py", run_name="__main__")
finally:
    current = readme.read_text(encoding="utf-8")
    current = current.replace(marker_new, "").replace(marker_old, "")
    readme.write_text(current, encoding="utf-8")
