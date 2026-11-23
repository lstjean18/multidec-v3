# Consignes pour Claude – MultiDEC v3

- Langue : français (code métier, commentaires, messages, documentation).
- **Ne jamais modifier, pousser ni ouvrir de PR sur `lstjean18/multiDEC`** (ancienne version VB.NET, lecture seule).
- Pile : C#, WPF, Visual Studio 2022, **VectorDraw Developer Framework 9.9003** (pas la v7).
- Les règles métier vont dans `MultiDEC.Core` (sans VectorDraw ni WPF) et sont couvertes par des tests xUnit.
- Les références générées doivent exister dans `catalogue/blocs-EM-3D.txt` (test dédié). Exception : HMTUBAMA*,
  remplacés par un bloc provisoire « cercle barré d'une croix » tant qu'ils manquent dans la bibliothèque.
- Orientation des blocs : relever la convention dans `Blocs-EM-3D.dxf`, ne pas reprendre les vecteurs d'extrusion
  de l'ancien code (plusieurs blocs ont changé d'orientation).
- Repère : X le long de la façade, Y = 0 file intérieure, Y = -largeur file extérieure, Z vers le haut, mètres.
- Pas d'import des anciens fichiers `.mdc`.
