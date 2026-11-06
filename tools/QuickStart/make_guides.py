"""Stable entry point for the current bilingual one-page quick-start guides."""
from pathlib import Path
import runpy

if __name__ == '__main__':
    runpy.run_path(str(Path(__file__).with_name('make_guides_v2.py')), run_name='__main__')
