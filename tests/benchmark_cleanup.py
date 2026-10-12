import json
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'backend'))
from server import Engine
ROOT=Path(__file__).resolve().parents[1]
paragraph=' '.join(f"Dans le chapitre {i}, nous expliquons le parcours du personnage et la raison de son choix. Le contenu doit rester complet, avec les noms, les chiffres et les formulations en anglais comme run the tests." for i in range(1,11))
text='Euh '+paragraph+' On se retrouve mardi, ah non attends mercredi a quinze heures.'
engine=Engine();engine.load();assert engine.ready,engine.error
result=engine.cleanup(text)
metrics={'chars':len(text),'seconds':result['seconds'],'output_chars':len(result['text']),'applied':result['applied']}
metrics['mode']=result.get('mode');metrics['correction_ok']='mercredi' in result['text'] and 'mardi' not in result['text'] and 'attends' not in result['text'];metrics['chapters_preserved']=all(f'chapitre {i},' in result['text'] for i in range(1,11));metrics['no_filler']='euh' not in result['text'].lower();print(metrics,flush=True)
(ROOT/'test-results'/'cleanup-benchmark-after.json').write_text(json.dumps(metrics,indent=2),encoding='utf-8')
assert metrics['correction_ok'] and metrics['chapters_preserved'] and metrics['no_filler'], metrics
