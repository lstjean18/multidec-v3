# Publier une version de MultiDEC v3

Notice pour RETOTUB (27/11/2026). La CI compile et teste à chaque push, et construit un installateur **de test**
(artefact « MultiDEC-v3-installateur-… »). Une version destinée aux utilisateurs se publie depuis le serveur de
développement, dans la session de Laurent, avec `publication\Publier-version.ps1`.

## Une fois pour toutes

1. Indiquer dans `publication\adresse-mises-a-jour.txt` l'adresse de la fiche `version.json` des versions publiées
   (adresse https, ou dossier partagé). Elle est copiée dans `mise-a-jour.txt`, à côté de `MultiDEC.exe` installé :
   c'est là que MultiDEC regarde au démarrage. Sans adresse, la version installée ne cherche pas de mise à jour.
2. Quand le certificat de signature sera disponible (Certum Cloud Code Signing via SSLmentor) : installer SimplySign
   Desktop (fichier .msi) et le SDK Windows 10 (signtool, livré avec Visual Studio 2022).

## À chaque version

```powershell
cd C:\...\multidec-v3            # dépôt à jour (git pull)
.\publication\Publier-version.ps1 -Version 3.0.1 -Libelle "v3.0 bêta 2" -Notes "Installateur et mises à jour"
```

- `-Version` : toujours plus grand que la version précédente (3.0.1, 3.0.2, 3.1.0…), c'est lui que compare la mise à jour.
- Le script compile en Release, construit `MultiDEC-v3-Setup-<version>.exe` (Inno Setup, téléchargé au premier
  lancement) et écrit `version.json` (version, empreinte SHA-256, notes) dans `publication\versions\`
  (ou `-Destination`).
- Déposer ensuite **l'installateur puis `version.json`** à l'adresse des mises à jour (dans cet ordre : un poste
  qui lirait la fiche avant l'arrivée de l'installateur échouerait).

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
