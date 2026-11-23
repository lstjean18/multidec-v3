# MultiDEC v3

Logiciel RETOTUB de conception d'échafaudages (Multi-RETO, Multi-RETO à embase, Multi-SYSTEM) :
saisie des paramètres, proposition de la structure, dessin 3D VectorDraw, décompte matériel.

Réécriture en C# / WPF de l'ancienne version VB.NET (dépôt `lstjean18/multiDEC`, en lecture seule).

## Prérequis

- Visual Studio 2022 (charge de travail « Développement .NET Desktop ») et .NET Framework 4.8.
- VectorDraw Developer Framework **8.8006** sous licence. Les DLL sont cherchées dans
  `C:\Program Files (x86)\VectorDraw\Common\` (propriété MSBuild `VectorDrawDir`).
  Sur un autre poste, créer à la racine un fichier `Directory.Build.props.user` (non versionné) :

  ```xml
  <Project><PropertyGroup><VectorDrawDir>D:\VectorDraw\Common\</VectorDrawDir></PropertyGroup></Project>
  ```

- La bibliothèque de blocs `Blocs-EM-3D.dxf` (choisie au premier lancement : *Fichier > Bibliothèque de blocs…*).

## Organisation

| Projet | Cible | Rôle |
|---|---|---|
| `src/MultiDEC.Core` | netstandard2.0 | Modèle, règles RETOTUB, génération des éléments positionnés, décompte, fichier projet `.mdc3` (JSON). Aucune dépendance à VectorDraw ni WPF. |
| `src/MultiDEC.Rendering.VectorDraw` | net48 | Ouverture de la bibliothèque DXF, insertion des blocs (`vdInsert`), bloc provisoire d'amarrage. |
| `src/MultiDEC.App` | net48 WPF | Fenêtre principale (paramètres, grilles éditables, vue 3D, décompte, alertes). |
| `tests/MultiDEC.Core.Tests` | net8.0 xUnit | Tests des règles, de l'orientation des blocs et du ViewModel. |
| `catalogue/` | — | Liste des blocs de `Blocs-EM-3D.dxf`, consoles de l'ancienne version. |

Le moteur produit une liste d'éléments (`ElementPlace` : référence = nom de bloc, origine, axe, face, maille).
Le rendu traduit ces éléments en matrices d'insertion ; les orientations se corrigent sans recompiler dans
`orientations-blocs.json`.

## Compiler et tester

```
dotnet test tests/MultiDEC.Core.Tests        # tests du cœur (Windows ou Linux)
msbuild MultiDEC.sln /p:Configuration=Debug  # solution complète (Windows + VectorDraw)
```

Voir `docs/regles-facade.md` pour les règles métier implémentées.

## Intégration continue

Le workflow `.github/workflows/compilation.yml` lance à chaque push :

- les tests du cœur sur un runner GitHub (Linux) ;
- la compilation de la solution complète sur le serveur de dev (runner auto-hébergé Windows, VS 2022 +
  VectorDraw 8.8006). L'application compilée est téléchargeable dans les artefacts du run pendant 14 jours.

Installation du runner sur le serveur : *Settings > Actions > Runners > New self-hosted runner* (Windows x64),
suivre les commandes affichées, puis l'installer comme service (`config.cmd` propose « Run as service »).
