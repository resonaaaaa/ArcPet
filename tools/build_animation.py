from pathlib import Path
import runpy
R=Path(__file__).resolve().parent.parent
runpy.run_path(str(R/"tools/verify_animation.py"),run_name="__main__")
runpy.run_path(str(R/"tools/build_previews.py"),run_name="__main__")
print("Current PNG validation and preview rebuild complete; no historical design dependency.")
