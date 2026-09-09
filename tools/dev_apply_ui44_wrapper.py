from __future__ import annotations

from pathlib import Path
import runpy

readme = Path("VoiceCraft.Endstone/README.md")
readme_marker_old = "\n<!-- dev-ui44-baseline: UI4.3 / 0.2.5 -->\n"
readme_marker_new = "\n<!-- dev-ui44-baseline: UI4.4 / 0.2.6 -->\n"
readme_original = readme.read_text(encoding="utf-8")
readme.write_text(readme_original + readme_marker_old, encoding="utf-8")

activity = Path("VoiceCraft.Server.Android/ModernMainActivity.cs")
activity_marker_old = "\n// dev-ui44-baseline UI4.3\n"
activity_marker_new = "\n// dev-ui44-baseline UI4.4\n"
activity_original = activity.read_text(encoding="utf-8")
activity.write_text(activity_original + activity_marker_old, encoding="utf-8")

try:
    runpy.run_path("tools/dev_apply_ui44.py", run_name="__main__")
finally:
    current = readme.read_text(encoding="utf-8")
    current = current.replace(readme_marker_new, "").replace(readme_marker_old, "")
    readme.write_text(current, encoding="utf-8")

    current_activity = activity.read_text(encoding="utf-8")
    current_activity = current_activity.replace(activity_marker_new, "").replace(activity_marker_old, "")
    activity.write_text(current_activity, encoding="utf-8")
