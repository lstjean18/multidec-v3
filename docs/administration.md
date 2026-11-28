# MultiDEC Admin : licences et publication

Notice pour RETOTUB (27/11/2026). MultiDEC Admin est une application séparée de MultiDEC, réservée à RETOTUB. Elle
sert à gérer les **licences** (postes autorisés à utiliser MultiDEC), à **publier leur liste** sur retotub.com et à
**publier les versions** de MultiDEC (installateur et fiche de mise à jour). Elle peut être confiée à un collaborateur.

Elle se trouve, après chaque compilation, dans `C:\Users\lstjean\actions-runner\MultiDEC-Admin\MultiDEC-Admin.exe` sur
le serveur, et en artefact « MultiDEC-Admin-… » de la compilation GitHub (dossier à copier tel quel sur un autre poste :
il contient `WinSCP.exe`, utilisé pour l'envoi). .NET Framework 4.8 suffit, sans VectorDraw.

## Principe

1. Au démarrage, MultiDEC calcule le **code du poste** (16 caractères, `XXXX-XXXX-XXXX-XXXX`, tiré de l'identifiant de
   Windows et de l'UUID de la carte mère, avec une clé de contrôle contre les fautes de frappe).
2. Il lit `postes-autorises.json`, publié dans le même dossier du site que `version.json`.
3. Cette liste est **signée** avec la clé privée de RETOTUB : MultiDEC contient la clé publique et refuse toute liste
   modifiée ou fabriquée par un tiers.
4. Poste présent et non expiré : MultiDEC démarre. Sinon, il affiche le code et propose d'écrire à
   **multidec@retotub.com**.
5. Sans connexion, la dernière vérification réussie reste valable **30 jours** (mémorisée sur le poste, chiffrée par
   Windows). Une horloge reculée ou une ancienne liste rejouée sont refusées.

La liste publiée ne contient **que les codes et les dates d'expiration** : les noms, entreprises et e-mails restent
dans le registre de MultiDEC Admin.

## Mise en route (une seule fois)

1. **Onglet Paramètres › Dossier de données** : choisir un dossier partagé si plusieurs personnes gèrent les licences
   (par exemple sur le lecteur X:). Il contiendra `licences.json`, `cle-signature.json`, `publication.json`,
   `postes-autorises.json` (dernière liste publiée) et `journal.txt` (qui a fait quoi, et quand). À sauvegarder.
2. **Clé de signature › Créer la clé…** : choisir un mot de passe (10 caractères au moins). Il est demandé à chaque
   publication de la liste et n'est enregistré nulle part : le noter en lieu sûr et le transmettre de vive voix au
   collaborateur. La clé publique est copiée dans le presse-papiers : **la transmettre pour qu'elle soit compilée dans
   MultiDEC** (elle n'est pas secrète ; tant qu'elle n'y est pas, MultiDEC ne contrôle pas les droits).
3. **Exporter une sauvegarde…** de la clé (toujours protégée par son mot de passe), sur un support séparé. Si la clé
   est perdue, il faut en créer une nouvelle et diffuser une nouvelle version de MultiDEC.
4. **Serveur de publication** : « Reprendre la configuration du script » récupère les réglages saisis avec
   `Publier-version.ps1 -ReconfigurerSftp` (sur le poste et la session où ils ont été saisis). Sinon, renseigner le
   serveur, le port, l'utilisateur, le mot de passe, le dossier distant et l'adresse web du dossier, puis
   « Lire l'empreinte… » (à confirmer avec l'empreinte donnée par l'hébergeur). « Enregistrer », puis
   « Tester la connexion ». Les réglages du serveur sont partagés dans le dossier de données. Le mot de passe SFTP
   reste propre à chaque utilisateur Windows (chiffré par Windows).
5. **Autoriser les postes de RETOTUB** (serveur, postes du bureau) avant de diffuser une version qui contient la clé.

## Au quotidien

- **Nouvelle demande** : copier tout le texte de l'e-mail reçu sur multidec@retotub.com, « Nouvelle licence… »,
  « Coller l'e-mail… » : le code, la société, le nom et l'adresse sont repris. Vérifier, choisir l'expiration
  (« +1 an » ou « Sans limite »), « Enregistrer ».
- **Publier la liste…** (mot de passe de la clé) : les postes la voient au démarrage suivant de MultiDEC. Le bandeau
  sous la grille rappelle les modifications non publiées.
- **Écrire au demandeur…** : prépare l'e-mail de confirmation.
- **Retirer un droit** : cocher « Suspendue » (licence conservée) ou « Supprimer », puis publier. Le poste est refusé
  à son prochain démarrage connecté. Hors ligne, il ne le sera qu'à la fin de ses 30 jours de tolérance : pour un
  retrait certain, préférer des expirations raisonnables (un an).
- **Prolonger** : ouvrir la licence, « +1 an », enregistrer, publier.

## Publier une version de MultiDEC

Onglet **Versions de MultiDEC** : choisir l'installateur construit par `publication\Publier-version.ps1` (sans
`-Envoyer` ; avec `-Signer` quand le certificat sera là), vérifier le numéro (lu dans l'installateur), le libellé et les
nouveautés, puis « Publier la version… ». L'application envoie, dans cet ordre, la copie numérotée
`MultiDEC-v3-Setup-<version>.exe`, l'installateur à nom fixe `MultiDEC-v3-Setup.exe` (liens du site), puis
`version.json` en dernier, et relit la fiche sur le site. C'est le même résultat que `Publier-version.ps1 -Envoyer`,
qui reste utilisable.

## Limites

Une application .NET peut se décompiler : la protection décourage la copie, mais n'arrête pas un spécialiste
déterminé. Un obscurcissement du code (ex. ConfuserEx) peut être ajouté plus tard pour compliquer la tâche.
