# Règles du module façade (v3)

Sources : ancien `Facade.vb` / `frmParam.vb`, NT-Echafaudage RETOTUB, décisions de Laurent (16 et 23/11/2026).
Les points marqués **[à valider]** sont des choix faits en v3 faute de règle explicite.

## Grille des mailles (proposition, ensuite éditable)
- Nombre d'accès proposé à chaque changement de longueur : 1 + partie entière (longueur / 20,50 m), formule de la v2 (CNAMTS : un accès tous les 20 m). Modifiable ensuite (validé par Laurent le 23/11/2026).
- Mailles d'accès à la longueur préférée, réparties entre `NombreAcces + 1` blocs de travail de taille égale
  (l'ancienne version mettait le surplus dans un seul bloc).
- Reste complété à droite par les longueurs standard 3,00 / 2,50 / 2,00 / 1,50 / 1,00 / 0,70, somme exacte si possible ;
  une maille courante est rendue au complément quand cela tombe plus juste (ex. 21,20 m en 2,50 → … + 3,00 + 0,70).
- Console définie **par maille**, appliquée à tous les niveaux de plancher complets de la maille.

## Niveaux
- Du dernier plancher vers le bas, tous les 2 m ; niveau 0 en « accès seul ». Éditables (complet / accès seul / accès seul + diagonale horizontale).

## Poteaux
- Pied : HMCALEBOIS (-0,33) + HMSOCREG (0) — cotes relevées dans `Blocs-EM-3D.dxf` **[à valider à l'écran]**.
- MR : HMPOTDP20 (ou HMPOTDP30 si départ 3 m) puis HMPOTST20. MRE : HMEMB + HMPOTDP10E puis HMPOTST. MS : HMEMB (+ HMPOT30 si départ 3 m) puis HMPOT20 + goupilles OECGOUP8S, décalage 0,046.
- Départ 3 m : côté extérieur et files d'extrémité (sans effet en MRE).
- Sommet : dernier plancher, + 1,00 m si garde-corps, + 0,50 m en couvreur. Poteau intérieur avec garde-corps si file d'extrémité ou protection intérieure sur une maille voisine sans console.

## Garde-corps, plinthes, plateaux, accès
- Extérieur à chaque niveau ≥ 0,50 m, sauf mailles de travail aux niveaux « accès seul ». Pas de plinthe aux niveaux d'accès.
- Intérieur : même règle **[à valider : l'ancienne version posait le GC intérieur même sans plancher]** ; remplacé par les lisses en bout de console.
- MDS 2 : pas de plinthe si maille ≤ 1,25 m. Abouts : HMGCSE / HMGCSE…GT / HMGCA / traverses selon le choix.
- Plateaux : 2 (0,70/0,75) ou 3 (1,00/1,05) plateaux de 0,30 ; alu/bois : 1 large (+ 1 complément en 1,00/1,05).
- Accès : trappe HMPLAL…602TR / 650TR (+EC > 2 m) côté intérieur, sens alterné par niveau, échelle si maille ≤ 2 m, 2 plateaux de départ au pied du 1er accès, lisse de sécurité au-dessus.
- Niveau « accès seul » (au-dessus du sol) : chaque maille a une lisse des deux côtés, façade et extérieur (validé par Laurent le 23/11/2026).
- Plateaux au niveau 0 : lisses de pied des deux côtés de la maille (validé le 23/11/2026). Une seule paire de lisses de pied par maille, même avec diagonale ou option « lisses en pied ».

## Diagonales verticales
- Une maille sur 5 (garde-corps MDS) ou sur 3 (lisses) ; renforcées : sur 3 ou sur 2. Sens alterné.
- Toute la hauteur ; hauteur non multiple de 2 m : une diagonale en pied puis colonne décalée.
- **Nouveau v3** : protection intérieure → diagonales aussi côté intérieur, mêmes mailles.
- Pas de diagonale sur un côté sans garde-corps (protection « Sans ») ; aucun des deux côtés → aucune diagonale et alerte (validé par Laurent le 23/11/2026). Le pas suit la protection extérieure, ou celle du côté façade si l'extérieur est sans.
- Conseil « diagonales renforcées » si les niveaux accès seul sont au moins aussi nombreux que les complets.

## Amarrages (NT-Echafaudage p. 8-13, validé le 23/11/2026) — placés sur le dessin
- Toutes les files ; 1er amarrage à 2 m ; extrémités tous les 4 m ; files courantes tous les 8 m (non bâché) ou 4 m (bâché).
- Quinconce : sur une file courante sur deux, le 2e amarrage est décalé d'un demi-pas (2, 6, 14, 22… au lieu de 2, 10, 18…) **[à valider]**.
- Contrôle : 1 ancrage / 24 m² (non bâché) ou / 10 m² (bâché) ; note de calcul au-delà de 24 m (recommandée si bâché).
- Tube : le plus court parmi HMTUBAMA0500 / HMTUBAMA (1,20) / 1500 / 2000 couvrant largeur + distance façade + 0,10 m.
- Dessin : bloc provisoire « cercle barré d'une croix » tant que les blocs HMTUBAMA manquent dans la bibliothèque.
