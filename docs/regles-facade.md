# Règles du module façade (v3)

Sources : ancien `Facade.vb` / `frmParam.vb`, NT-Echafaudage RETOTUB, décisions de Laurent (16 et 23/11/2026).
Les points marqués **[à valider]** sont des choix faits en v3 faute de règle explicite.

## Grille des mailles (proposition, ensuite éditable)
- Nombre d'accès proposé à chaque changement de longueur : 1 + partie entière (longueur / 20,50 m), formule de la v2 (CNAMTS : un accès tous les 20 m). Modifiable ensuite (validé par Laurent le 23/11/2026).
- Mailles d'accès à la longueur préférée, réparties entre `NombreAcces + 1` blocs de travail de taille égale
  (l'ancienne version mettait le surplus dans un seul bloc).
- Reste complété à droite par les longueurs standard 3,00 / 2,50 / 2,00 / 1,50 / 1,00 / 0,70, somme exacte si possible ;
  une maille courante est rendue au complément quand cela tombe plus juste (ex. 21,20 m en 2,50 → … + 3,00 + 0,70).
- Consoles (Laurent, 23/11/2026) : le type de console se choisit **par niveau** (grille des niveaux, colonne « Console ») ;
  chaque niveau à console ajoute à la grille des mailles une colonne de cases « Con » + hauteur (ex. « Con4m », « Con4,5m »),
  cochée sur toutes les mailles à l'ajout, puis décochable. Cochée = la maille porte la console à ce niveau (niveaux complets seulement).
- Case « Diagonale » par maille : cochée = diagonales verticales sur toute la hauteur de la maille. Pré-cochée selon la règle
  (voir Diagonales verticales), bouton « Proposer les diagonales » pour recocher.

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
- Protections proposées : Sans, Garde-corps MDS 2 (par défaut, extérieur et abouts), Lisses ; « Garde-corps MDS 1 » retiré
  des choix (Laurent, 23/11/2026), les anciens fichiers passent en MDS 2.
- MDS 2 : pas de plinthe si maille ≤ 1,25 m. Abouts : HMGCSE…GT / traverses selon le choix.
- Console dont la maille voisine n'a pas de console au même niveau (ou en bout de façade, si l'about de ce côté est demandé) :
  garde-corps d'about en lisses (lisse + sous-lisse de la largeur de la console) au bout de la console (Laurent, 23/11/2026).
- Les garde-corps d'about ont un sens : l'about gauche (x = 0) est le miroir de l'about droit (Laurent, 23/11/2026).
- Plateaux : 2 (0,70/0,75) ou 3 (1,00/1,05) plateaux de 0,30. Gammes : Optissimo, Standard, Standard NF (HMPLAF…CL6, par défaut) et Anti Contamination NF (HMPLAF…CL6-AC) ; la gamme alu/bois est supprimée (Laurent, 23/11/2026).
- Accès : trappe HMPLAL…602TR / 650TR (+EC > 2 m) côté intérieur, sens alterné par niveau, échelle si maille ≤ 2 m, 2 plateaux de départ au pied du 1er accès, lisse de sécurité 1,50 m au-dessus des plateaux : lisse ordinaire dans le plan des poteaux, montée sur les rosaces (tous les 0,50 m), sans collier.
- Niveau « accès seul » (au-dessus du sol) : chaque maille a une lisse des deux côtés, façade et extérieur (validé par Laurent le 23/11/2026).
- Plateaux au niveau 0 : lisses de pied des deux côtés de la maille (validé le 23/11/2026). Une seule paire de lisses de pied par maille, même avec diagonale ou option « lisses en pied ».

## Diagonales verticales
- Mailles choisies par la case « Diagonale » de la grille, pré-cochée une maille sur 5, ou une sur 2 avec l'option « diagonales 1 maille sur 2 », quel que soit le garde-corps (règle v2 version2024, Laurent 23/11/2026). Sens alterné d'une maille cochée à la suivante.
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

## Fonctions reprises de la v2 version2024 (codées le 24/11/2026)
- **Structure** : formes droite, en L, en U, fermée (faces S1…S4 en équerre, carré d'angle) et **volumique** (2 à 6 modules M1…
  côte à côte en profondeur, mêmes mailles que M1, largeur par module, poteaux et lisses communs dédoublonnés, protections
  seulement sur les côtés extérieurs, amarrages sur M1, diagonales en travers au-delà de 1,05 m ou sur socles à roues).
  Option « planchers en damier » : pas de plancher une maille sur deux, décalé d'un module à l'autre.
- **Pare-gravois** (case par niveau) : consoles HMPARGRA sur toute la face, tôles HMTOG200 inclinées tous les 0,80 m
  (dernière calée à L - 0,90), crochets et lisses.
- **Sous-mailles rapportées côté extérieur** (type de maille) : file de poteaux supplémentaire à y = -largeur - profondeur.
  - Accès rapporté 0,70 m : trappes et échelles en sens alterné sur toute la hauteur, garde-corps extérieur sur la file rapportée.
  - Module d'appro 2,00 m (niveaux cochés « Appro ») : plancher, poutrelles, garde-corps basculant HMGCAPPROSUP / -C / GC.
  - Recette (profondeur choisie, niveaux cochés « Recette ») : plancher et garde-corps en lisses.
  - Aux niveaux desservis, la maille principale n'a pas de garde-corps extérieur.
- **Escaliers** (type de maille, mailles de 2,50 m ; 3,00 m sans garde-corps de volée, absent de la bibliothèque) :
  - Escalier 1,50 m : une volée HMESCALU…200 par niveau, alternativement côté façade et côté extérieur, en sens opposés ;
    la dernière arrive côté façade. Garde-corps HMGCESCALU des deux côtés de la volée, extrémités HMGCESCALUEXTR au pied
    de la première et en haut de la dernière. Montée de 1,00 m : volée HMESCALU175100.
  - Demi-escalier 0,70 m (volées 0,65) ou 1,00 m (volées 88D) : une volée par niveau, toujours dans le même sens, garde-corps
    côté maille seulement.
  - Tour : traverses renforcées aux deux bouts à chaque niveau, lisses tous les 0,50 m le long de la file extérieure et en
    travers au bout du palier (aux deux bouts pour un demi-escalier et au dernier niveau), plinthes, diagonales extérieure et en travers.
  - Côté maille : garde-corps extérieur remplacé par la lisse de plancher et une fermeture (montant HMMONTGC(E) + 2 lisses + plinthe)
    sur 1,50 m (2,00 m en maille de 3 m), ouverte sur 1,00 m au bout d'arrivée ; demi-escalier aux niveaux intermédiaires :
    garde-corps central sur piquages HMPIQLIS(-E) + poteaux de 1 m, ouvert sur 0,75 m aux deux bouts.
- **Stabilisation** (colonne « Stab. ») : file à la distance choisie, lisses et diagonales jusqu'à 2 m sous le dernier plancher.
- **Pignon** (maille d'accès) : tour montée de +2, +4… m au-dessus du dernier plancher, planchers tous les 2 m et abouts.
- **Poutre de franchissement** (colonne « Poutre », sur la maille de gauche d'une paire de mailles de travail identiques) :
  deux poutres HMPOUT à 0,50 m sous le plancher choisi, file du milieu supprimée dessous, tubes en diagonale horizontale,
  diagonales en V au-dessus, abouts sur les mailles voisines ; refus avec alerte si les conditions ne sont pas réunies.
