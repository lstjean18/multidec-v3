# Publier une version de MultiDEC v3

Notice pour RETOTUB (27/11/2026). La CI compile et teste à chaque push, et construit un installateur **de test**
(artefact « MultiDEC-v3-installateur-… »). Une version destinée aux utilisateurs se publie depuis le serveur de
développement, dans la session de Laurent, avec `publication\Publier-version.ps1`.

L'envoi sur le site peut aussi se faire avec **MultiDEC Admin** (onglet « Versions de MultiDEC », voir
[administration.md](administration.md)) : construire l'installateur avec le script sans `-Envoyer`, puis le publier
depuis l'application, qui gère aussi les licences.

## Une fois pour toutes

1. Configurer l'envoi sur le site retotub.com (SFTP) :
   `.\publication\Publier-version.ps1 -ReconfigurerSftp`. Le script demande le serveur, le port, l'utilisateur, le mot
   de passe, le dossier distant et l'adresse web de ce dossier, affiche l'empreinte de la clé du serveur à confirmer,
   puis teste la connexion. Ces paramètres sont enregistrés dans `%APPDATA%\MultiDEC-publication\sftp.xml`, mot de
   passe chiffré par Windows (lisible seulement par cet utilisateur sur ce poste) ; rien n'est mis dans le dépôt.
   WinSCP (paquet NuGet) est téléchargé au premier envoi.
2. Quand le certificat de signature sera disponible (Certum Cloud Code Signing via SSLmentor) : installer SimplySign
   Desktop (fichier .msi) et le SDK Windows 10 (signtool, livré avec Visual Studio 2022).

## À chaque version

```powershell
cd C:\...\multidec-v3            # dépôt à jour (git pull)
.\publication\Publier-version.ps1 -Version 3.0.1 -Libelle "v3.0 bêta 2" -Notes "Installateur et mises à jour" -Envoyer
```

- `-Version` : toujours plus grand que la version précédente (3.0.1, 3.0.2, 3.1.0…), c'est lui que compare la mise à jour.
- Le script compile en Release, construit l'installateur (Inno Setup, téléchargé au premier lancement) et écrit
  `version.json` (version, empreinte SHA-256, notes) dans `publication\versions\` (ou `-Destination`).
- L'installateur garde toujours le même nom, **`MultiDEC-v3-Setup.exe`**, car des liens du site y pointent. Le script
  en fait aussi une copie numérotée, `MultiDEC-v3-Setup-<version>.exe` : c'est elle que la fiche désigne et que la
  mise à jour télécharge, si bien qu'un poste ne récupère jamais l'installateur d'une autre version que celle annoncée.
- Le guide utilisateur (`docs\manuel\MultiDEC-v3-Guide-utilisateur.pdf`, régénéré par `docs\manuel\generer.py`) est
  livré avec l'application (menu Aide > Guide utilisateur) et publié sous ce nom fixe à côté de l'installateur.
- Avec `-Envoyer`, les fichiers partent sur le site dans cet ordre : copie numérotée, installateur à nom fixe, guide
  utilisateur, puis `version.json` en dernier (les postes ne voient la nouvelle version qu'une fois son installateur en place).
  Le script relit ensuite la fiche sur le web pour vérifier qu'elle annonce bien la nouvelle version.
- L'adresse des mises à jour écrite dans l'installateur (`mise-a-jour.txt` à côté de `MultiDEC.exe`) est l'adresse
  web du dossier suivie de `/version.json`.
- Sans `-Envoyer`, les fichiers restent dans `publication\versions\` pour un envoi à la main (même ordre).

### Avec signature (quand le certificat sera là)

1. Ouvrir SimplySign Desktop et s'y connecter avec le jeton de l'appli SimplySign du téléphone (valable ~2 h).
2. Ajouter `-Signer` : `MultiDEC.exe`, les DLL MultiDEC, l'installateur et le désinstalleur sont signés
   (`signtool sign /fd sha256 /tr http://time.certum.pl /td sha256 /a`). `-Certificat "RETOTUB"` choisit le
   certificat par son nom si plusieurs sont présents.

Une fois les versions signées, MultiDEC n'accepte plus qu'un installateur signé par le même éditeur.

## Sécurité de la mise à jour

- Empreinte SHA-256 de l'installateur contrôlée après téléchargement (fiche `version.json`).
- Si MultiDEC.exe est signé : signature Authenticode de l'installateur vérifiée (WinVerifyTrust) et même éditeur exigé.
- Installation par utilisateur, sans droits administrateur : la mise à jour se fait sans élévation.
