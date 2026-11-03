"""Compose terrain-only verification in memory. Does not import project scripts."""
from pathlib import Path
import re, sys
ROOT=Path(__file__).resolve().parents[2]
OLD=ROOT/'tools/MiningStationRound2'
files=[OLD/name for name in ['Inspect.cs','Sampling.cs','SolidPacking.cs','Scatter.cs','Build.cs','Capture.cs','Verification.cs']]
files.append(Path(__file__).with_name('Baseline.cs'))
if '--performance' in sys.argv:
    files.append(Path(__file__).with_name('Performance.cs'))
    files.append(Path(__file__).with_name('OptimizedMatch.cs'))
usings=[]; bodies=[]
for path in files:
    source=path.read_text(encoding='utf-8-sig')
    for line in re.findall(r'^using [^\n]+;\s*$',source,re.M):
        if line.strip() not in usings: usings.append(line.strip())
    bodies.append('// '+path.name+'\n'+re.sub(r'^using [^\n]+;\s*$','',source,flags=re.M))
out=ROOT/'Temp/TerrainPerformance/TerrainPerformance.cs'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text('\n'.join(usings)+'\n\n'+'\n\n'.join(bodies),encoding='utf-8')
print(out)
