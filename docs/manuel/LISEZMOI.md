# Manuel utilisateur MultiDEC v3

- Source : `manuel.html` + `manuel.css` (page de garde dans le style de la notice 2018, intérieur sobre).
- Images : `images/ihm` (captures de l'interface) et `images/3d` (rendus), produites sur le serveur par le CI :
  test `CapturesManuelTests` (préfixe `MANUEL-`) et `BibliothequeDwgTests` (scènes de contrôle), images en base64 dans le journal.
- PDF : `python3 generer.py` (Node + playwright, Python + pypdf) écrit `MultiDEC-v3-Guide-utilisateur.pdf`,
  en deux passes pour les numéros de page du sommaire.
- À mettre à jour à chaque nouvelle fonction.
