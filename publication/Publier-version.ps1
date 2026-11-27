<#
.SYNOPSIS
  Construit une version de MultiDEC v3 à distribuer : compilation Release, signature (facultative), installateur
  Inno Setup, fiche version.json lue par la mise à jour automatique.

.DESCRIPTION
  À lancer sur le serveur de développement, dans la session de l'utilisateur (le certificat SimplySign est dans le
  magasin « Utilisateur actuel » : le runner GitHub, qui tourne sous le compte Système, ne le voit pas).
  Sans -Signer, rien n'est signé (en attendant le certificat).

.EXAMPLE
  .\Publier-version.ps1 -Version 3.0.1 -Libelle "v3.0 bêta 2" -Notes "Installateur et mise à jour automatique"

.EXAMPLE
  # Avec le certificat (SimplySign Desktop connecté depuis l'appli du téléphone) :
  .\Publier-version.ps1 -Version 3.0.2 -Signer
#>
param(
    # Numéro comparable par la mise à jour : 3.0.1, 3.1.0… (toujours plus grand que la version précédente).
    [string]$Version = '',
    # Libellé affiché (écran de démarrage, À propos) ; par défaut « v<Version> ».
    [string]$Libelle = '',
    # Nouveautés affichées dans la proposition de mise à jour.
    [string]$Notes = '',
    # Dossier où déposer l'installateur et version.json (par défaut publication\versions).
    [string]$Destination = '',
    # Adresse de la fiche version.json (https://… ou dossier) écrite dans mise-a-jour.txt à côté de MultiDEC.exe.
    # Par défaut : contenu de publication\adresse-mises-a-jour.txt.
    [string]$AdresseMisesAJour = '',
    # Signer l'exe, les DLL MultiDEC et l'installateur (certificat Certum via SimplySign Desktop).
    [switch]$Signer,
    # Sujet du certificat (signtool /n) ; vide = choix automatique du meilleur certificat (/a).
    [string]$Certificat = '',
    [string]$Horodatage = 'http://time.certum.pl',
    # Dossier de l'application déjà compilée (utilisé par la CI) ; vide = compilation ici.
    [string]$Source = '',
    [string]$NumeroBuild = 'publication',
    # Dossier contenant « Blocs 3D - Echafaudage.dwg » et multidec.bib, livrés dans {app}\Bibliotheque (remarque 62).
    # Par défaut : actions-runner\bibliotheque du profil Windows (dossier de référence sur le serveur de dev).
    [string]$Bibliotheque = '',
    # Envoyer la version sur le site retotub.com (SFTP) : installateur à nom fixe, copie numérotée, puis version.json.
    [switch]$Envoyer,
    # Ressaisir les paramètres de connexion SFTP (enregistrés chiffrés dans le profil Windows).
    [switch]$ReconfigurerSftp
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$racine = Split-Path -Parent $PSScriptRoot
if (-not $Libelle) { $Libelle = "v$Version" }
if (-not $Destination) { $Destination = Join-Path $PSScriptRoot 'versions' }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$Destination = (Resolve-Path $Destination).Path
if (-not $AdresseMisesAJour) {
    $fichierAdresse = Join-Path $PSScriptRoot 'adresse-mises-a-jour.txt'
    if (Test-Path $fichierAdresse) {
        $AdresseMisesAJour = (Get-Content $fichierAdresse | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') } | Select-Object -First 1)
    }
}

# Connexion SFTP (Laurent 27/11/2026 : publication sur retotub.com). Les paramètres et le mot de passe sont saisis une
# fois et enregistrés dans le profil Windows de l'utilisateur (Export-Clixml : mot de passe chiffré DPAPI, lisible
# uniquement par cet utilisateur sur ce poste). Rien n'est enregistré dans le dépôt.
# Outils téléchargés (Inno Setup, WinSCP). Sous le compte Système (runner de la CI), le profil est dans
# C:\Windows\system32, redirigé pour les programmes 32 bits (ISCC) : ProgramData.
$baseOutils = if ($env:LOCALAPPDATA -and $env:LOCALAPPDATA -notmatch '\\system32\\') { $env:LOCALAPPDATA } else { $env:ProgramData }
$fichierSftp = Join-Path $env:APPDATA 'MultiDEC-publication\sftp.xml'

function Charger-WinSCP {
    $versionWinScp = '6.7.1'
    $dossier = Join-Path $baseOutils "MultiDEC-outils\winscp-$versionWinScp"
    if (-not (Test-Path (Join-Path $dossier 'tools\WinSCP.exe'))) {
        Write-Host "Téléchargement de WinSCP $versionWinScp…"
        New-Item -ItemType Directory -Force -Path $dossier | Out-Null
        $zip = Join-Path $dossier 'winscp.zip'
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/winscp/$versionWinScp/winscp.$versionWinScp.nupkg" -OutFile $zip -UseBasicParsing
        Expand-Archive $zip -DestinationPath $dossier -Force
        Remove-Item $zip
    }
    Add-Type -Path (Join-Path $dossier 'lib\net40\WinSCPnet.dll')
    return (Join-Path $dossier 'tools\WinSCP.exe')
}

function Options-Session($config) {
    $o = New-Object WinSCP.SessionOptions -Property @{
        Protocol              = [WinSCP.Protocol]::Sftp
        HostName              = $config.Hote
        PortNumber            = [int]$config.Port
        UserName              = $config.Utilisateur
        SecurePassword        = $config.MotDePasse
        SshHostKeyFingerprint = $config.Empreinte
    }
    return $o
}

function Configurer-Sftp($winscp) {
    Write-Host 'Paramètres de publication SFTP (enregistrés chiffrés dans votre profil Windows) :'
    $c = [ordered]@{
        Hote            = Read-Host 'Serveur SFTP (ex. ftp.retotub.com)'
        Port            = Read-Host 'Port (Entrée = 22)'
        Utilisateur     = Read-Host 'Utilisateur'
        MotDePasse      = Read-Host 'Mot de passe' -AsSecureString
        Dossier         = Read-Host 'Dossier distant des mises à jour (ex. /www/multidec)'
        AdressePublique = Read-Host 'Adresse web de ce dossier (ex. https://www.retotub.com/multidec)'
        Empreinte       = ''
    }
    if (-not $c.Port) { $c.Port = 22 }
    $session = New-Object WinSCP.Session
    $session.ExecutablePath = $winscp
    try {
        $o = New-Object WinSCP.SessionOptions -Property @{ Protocol = [WinSCP.Protocol]::Sftp; HostName = $c.Hote; PortNumber = [int]$c.Port }
        $empreinte = $session.ScanFingerprint($o, 'SHA-256')
    } finally { $session.Dispose() }
    Write-Host "Empreinte de la clé du serveur : $empreinte"
    if ((Read-Host 'Cette empreinte est-elle bien celle du serveur de retotub.com ? (o/n)') -ne 'o') { throw 'Configuration abandonnée' }
    $c.Empreinte = $empreinte
    $config = [pscustomobject]$c
    New-Item -ItemType Directory -Force -Path (Split-Path $fichierSftp) | Out-Null
    $config | Export-Clixml $fichierSftp
    return $config
}

$configSftp = $null
$winscp = $null
if ($Envoyer -or $ReconfigurerSftp) {
    $winscp = Charger-WinSCP
    $configSftp = if ($ReconfigurerSftp -or -not (Test-Path $fichierSftp)) { Configurer-Sftp $winscp } else { Import-Clixml $fichierSftp }
    # L'adresse publique du dossier donne l'adresse des mises à jour écrite dans l'installateur.
    if (-not $AdresseMisesAJour) { $AdresseMisesAJour = $configSftp.AdressePublique.TrimEnd('/') + '/version.json' }
    # Test de connexion avant de compiler.
    $session = New-Object WinSCP.Session
    $session.ExecutablePath = $winscp
    try { $session.Open((Options-Session $configSftp)); Write-Host "Connexion SFTP OK ($($configSftp.Hote))" } finally { $session.Dispose() }
    if (-not $Envoyer) { Write-Host 'Configuration SFTP enregistrée.'; return }
}

if ($Version -notmatch '^\d+\.\d+(\.\d+){0,2}$') { throw "Version « $Version » : attendu un numéro comme 3.0.1" }

function Trouver-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $outil = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like '*\x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $outil) { throw "signtool.exe introuvable (installer le SDK Windows 10 avec Visual Studio)" }
    return $outil.FullName
}

function Options-Signature {
    $o = @('sign', '/fd', 'sha256', '/tr', $Horodatage, '/td', 'sha256')
    if ($Certificat) { $o += @('/n', $Certificat) } else { $o += '/a' }
    return $o
}

# 1. Compilation Release avec le numéro de version.
if (-not $Source) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = & $vswhere -latest -version '[17.0,18.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'MSBuild de Visual Studio 2022 introuvable' }
    Write-Host "Compilation de MultiDEC $Libelle ($Version)…"
    & $msbuild (Join-Path $racine 'src\MultiDEC.App\MultiDEC.App.csproj') /restore /m /nologo /v:minimal /p:Configuration=Release "/p:Version=$Version" "/p:LibelleVersion=$Libelle" "/p:NumeroBuild=$NumeroBuild"
    if ($LASTEXITCODE -ne 0) { throw "Compilation en échec ($LASTEXITCODE)" }
    $Source = Join-Path $racine 'src\MultiDEC.App\bin\Release\net48'
}
$Source = (Resolve-Path $Source).Path

# 2. Copie de travail : l'adresse des mises à jour y est ajoutée, les fichiers y sont signés.
$etape = Join-Path $env:TEMP "MultiDEC-publication-$Version"
if (Test-Path $etape) { Remove-Item $etape -Recurse -Force }
robocopy $Source $etape /MIR /NFL /NDL /NJH /NJS /R:1 /W:1 /XF *.vshost.* | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copie de $Source impossible" }

# Bibliothèque de blocs et catalogue des articles livrés avec l'application (Laurent 27/11/2026, remarque 62).
if (-not $Bibliotheque) {
    $Bibliotheque = Get-ChildItem (Join-Path $env:USERPROFILE 'actions-runner') -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'biblioth*' } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Bibliotheque -or -not (Test-Path $Bibliotheque)) { throw "Dossier de la bibliothèque introuvable (option -Bibliotheque) : '$Bibliotheque'" }
$dossierBib = Join-Path $etape 'Bibliotheque'
New-Item -ItemType Directory -Force -Path $dossierBib | Out-Null
foreach ($nom in 'Blocs 3D - Echafaudage.dwg', 'multidec.bib') {
    $f = Join-Path $Bibliotheque $nom
    if (-not (Test-Path $f)) { throw "Fichier absent de la bibliothèque : $f" }
    Copy-Item $f $dossierBib -Force
    Write-Host ("Livré avec l'application : Bibliotheque\$nom (" + [math]::Round((Get-Item $f).Length / 1MB, 1) + ' Mo)')
}

if ($AdresseMisesAJour) {
    [IO.File]::WriteAllText((Join-Path $etape 'mise-a-jour.txt'), $AdresseMisesAJour.Trim() + "`r`n", (New-Object Text.UTF8Encoding $false))
    Write-Host "Adresse des mises à jour : $AdresseMisesAJour"
} else {
    Write-Warning 'Aucune adresse de mises à jour : la version installée ne cherchera pas de mise à jour (publication\adresse-mises-a-jour.txt).'
}

# 3. Signature de nos fichiers (les DLL VectorDraw sont signées par leur éditeur).
$signtool = $null
if ($Signer) {
    $signtool = Trouver-SignTool
    $fichiers = @(Get-ChildItem $etape -Include 'MultiDEC.exe', 'MultiDEC.*.dll' -Recurse | ForEach-Object { $_.FullName })
    Write-Host "Signature de $($fichiers.Count) fichiers…"
    & $signtool (Options-Signature) $fichiers
    if ($LASTEXITCODE -ne 0) { throw "Signature en échec : SimplySign Desktop est-il connecté (jeton de l'appli du téléphone) ?" }
    & $signtool verify /pa /q $fichiers
    if ($LASTEXITCODE -ne 0) { throw 'Vérification de la signature en échec' }
} else {
    Write-Warning 'Version NON signée (option -Signer absente).'
}

# 4. Inno Setup (paquet NuGet Tools.InnoSetup, téléchargé une fois).
$versionInno = '6.4.3'
$outils = Join-Path $baseOutils "MultiDEC-outils\innosetup-$versionInno"
$iscc = Join-Path $outils 'tools\ISCC.exe'
if (-not (Test-Path $iscc)) {
    Write-Host "Téléchargement d'Inno Setup $versionInno…"
    New-Item -ItemType Directory -Force -Path $outils | Out-Null
    $zip = Join-Path $outils 'innosetup.zip'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$versionInno/tools.innosetup.$versionInno.nupkg" -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $outils -Force
    Remove-Item $zip
}

$arguments = @("/DVersion=$Version", "/DLibelle=$Libelle", "/DSource=$etape", "/DSortie=$Destination", '/Q')
if ($Signer) {
    $commande = '$q' + $signtool + '$q ' + ((Options-Signature) -join ' ') + ' $f'
    $arguments += @('/DSigner', "/Ssigntool=$commande")
}
& $iscc @arguments (Join-Path $racine 'installateur\MultiDEC-v3.iss')
if ($LASTEXITCODE -ne 0) { throw "Construction de l'installateur en échec ($LASTEXITCODE)" }
# Nom fixe (des liens du site y pointent, Laurent 27/11/2026) + copie numérotée, téléchargée par la mise à jour :
# un poste qui lit la fiche d'une version ne récupère jamais l'installateur d'une autre.
$installateur = Join-Path $Destination 'MultiDEC-v3-Setup.exe'
$numerote = Join-Path $Destination "MultiDEC-v3-Setup-$Version.exe"
Copy-Item $installateur $numerote -Force

# Guide utilisateur (Laurent 27/11/2026 : publié avec chaque version, nom fixe). Livré aussi avec l'application.
$manuel = Join-Path $Destination 'MultiDEC-v3-Guide-utilisateur.pdf'
$manuelSource = Join-Path $Source 'MultiDEC-v3-Guide-utilisateur.pdf'
if (-not (Test-Path $manuelSource)) { $manuelSource = Join-Path $racine 'docs\manuel\MultiDEC-v3-Guide-utilisateur.pdf' }
if (Test-Path $manuelSource) { Copy-Item $manuelSource $manuel -Force } else { Write-Warning 'Guide utilisateur introuvable : il ne sera pas publié.'; $manuel = $null }

# 5. Fiche de version lue par MultiDEC (version.json, à côté de l'installateur).
$fiche = [ordered]@{
    version      = $Version
    libelle      = $Libelle
    installateur = (Split-Path $numerote -Leaf)
    sha256       = (Get-FileHash $numerote -Algorithm SHA256).Hash.ToLowerInvariant()
    date         = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    notes        = $Notes
}
[IO.File]::WriteAllText((Join-Path $Destination 'version.json'), ($fiche | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
Remove-Item $etape -Recurse -Force -ErrorAction SilentlyContinue

# 6. Envoi sur le site : copie numérotée, installateur à nom fixe, guide utilisateur, puis la fiche (en dernier : un poste ne voit la
#    nouvelle version qu'une fois son installateur en place).
if ($Envoyer) {
    $session = New-Object WinSCP.Session
    $session.ExecutablePath = $winscp
    try {
        $session.Open((Options-Session $configSftp))
        $distant = $configSftp.Dossier.TrimEnd('/') + '/'
        foreach ($f in @($numerote, $installateur, $manuel, (Join-Path $Destination 'version.json')) | Where-Object { $_ }) {
            Write-Host "Envoi de $(Split-Path $f -Leaf)…"
            $session.PutFiles($f, $distant).Check()
        }
    } finally { $session.Dispose() }
    # Contrôle : la fiche publiée sur le web est bien celle de cette version.
    $publiee = Invoke-RestMethod ($AdresseMisesAJour + '?t=' + [DateTime]::UtcNow.Ticks) -UseBasicParsing
    if ($publiee.version -ne $Version -or $publiee.sha256 -ne $fiche.sha256) {
        Write-Warning "La fiche lue sur $AdresseMisesAJour ne correspond pas à la version envoyée (cache du site ou adresse publique erronée ?)."
    } else {
        Write-Host "Publiée : $AdresseMisesAJour annonce la version $Version."
    }
}

Write-Host ''
Write-Host "Installateur : $installateur ($([math]::Round((Get-Item $installateur).Length / 1MB, 1)) Mo)"
Write-Host "Fiche        : $(Join-Path $Destination 'version.json')"
if ($manuel) { Write-Host "Guide        : $manuel" }
if (-not $Signer) { Write-Host 'Non signé : Windows SmartScreen et certains antivirus peuvent avertir à l''installation.' }
