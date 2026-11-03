"""Bundle independent tool fragments for Unity Pipeline's in-memory run_script.
No Unity project script import, source YAML, domain reload, or original asset overwrite.
Usage: bundled output is Temp/MiningRound2/MiningRound2.cs.
"""
from pathlib import Path
import re, sys
ROOT=Path(__file__).resolve().parents[2]
FILES=['Inspect.cs','Sampling.cs','SolidPacking.cs','Scatter.cs','Build.cs','Capture.cs','Verification.cs','Benchmark.cs']
if '--build-only' in sys.argv:
    FILES.remove('Verification.cs')
    FILES.remove('Benchmark.cs')
usings=[];bodies=[]
for name in FILES:
    text=(Path(__file__).parent/name).read_text(encoding='utf-8-sig')
    for line in re.findall(r'^using [^\n]+;\s*$',text,re.M):
        line=line.strip()
        if line not in usings:usings.append(line)
    bodies.append('// ---- '+name+' ----\n'+re.sub(r'^using [^\n]+;\s*$','',text,flags=re.M))
if '--build-only' not in sys.argv:bodies.append('''namespace Starframe.MiningRound2 { public static partial class MiningRound2 {
public static string Verify() { return VerifyAssets(OutputRoot+"/Assets",OutputRoot+"/Layouts/LocalDensityPatch.asset",EvidenceRoot+"/verification-report.json"); }
} }''')
out=ROOT/'Temp/MiningRound2/MiningRound2.cs';out.parent.mkdir(parents=True,exist_ok=True)
out.write_text('\n'.join(usings)+'\n\n'+'\n\n'.join(bodies),encoding='utf-8')
print('Bundled',len(FILES),'fragments;',out)
