"""Génère MultiDEC-v3-Guide-utilisateur.pdf depuis manuel.html (Chromium de Playwright, via Node).

Deux passes : la première donne la page de chaque titre (liens du sommaire), la seconde les écrit dans le sommaire.
Prérequis : Node + playwright, Python + pypdf.  Usage : python3 generer.py [sortie.pdf]
"""
import json, os, subprocess, sys, tempfile
from pypdf import PdfReader

ici = os.path.dirname(os.path.abspath(__file__))
sortie = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ici, 'MultiDEC-v3-Guide-utilisateur.pdf'))

JS = r"""
const { chromium } = require('playwright');
(async () => {
  const [html, pdf, pages] = process.argv.slice(2);
  const b = await chromium.launch();
  const p = await b.newPage();
  await p.addInitScript(`window.PAGES = ${pages};`);
  await p.goto('file://' + html, { waitUntil: 'networkidle' });
  await p.evaluate(() => document.fonts.ready);
  await p.pdf({ path: pdf, preferCSSPageSize: true, printBackground: true, tagged: true, outline: true });
  await b.close();
})();
"""

def rendre(pdf, pages):
    with tempfile.NamedTemporaryFile('w', suffix='.js', delete=False) as f:
        f.write(JS)
    subprocess.run(['node', f.name, os.path.join(ici, 'manuel.html'), pdf, json.dumps(pages)], check=True,
                   env={**os.environ, 'NODE_PATH': subprocess.run(['npm', 'root', '-g'], capture_output=True, text=True).stdout.strip()})

def pages_des_titres(pdf):
    r = PdfReader(pdf)
    num = {id(pg.indirect_reference): i for i, pg in enumerate(r.pages)}
    idx = {(pg.indirect_reference.idnum): i for i, pg in enumerate(r.pages)}
    res = {}
    for nom, dest in r.named_destinations.items():
        n = r.get_destination_page_number(dest)
        res[nom.lstrip('/')] = n + 1
    return res

tmp = sortie + '.tmp.pdf'
rendre(tmp, {})
pages = pages_des_titres(tmp)
os.remove(tmp)
rendre(sortie, pages)
print(f'{sortie} : {len(PdfReader(sortie).pages)} pages ; titres : {pages}')
