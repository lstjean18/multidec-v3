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
    [Parameter(Mandatory = $true)][string]$Version,
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
    [string]$NumeroBuild = 'publication'
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
$outils = Join-Path $env:LOCALAPPDATA "MultiDEC-outils\innosetup-$versionInno"
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
$installateur = Join-Path $Destination "MultiDEC-v3-Setup-$Version.exe"

# 5. Fiche de version lue par MultiDEC (version.json, à côté de l'installateur).
$fiche = [ordered]@{
    version      = $Version
    libelle      = $Libelle
    installateur = (Split-Path $installateur -Leaf)
    sha256       = (Get-FileHash $installateur -Algorithm SHA256).Hash.ToLowerInvariant()
    date         = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    notes        = $Notes
}
[IO.File]::WriteAllText((Join-Path $Destination 'version.json'), ($fiche | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
Remove-Item $etape -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "Installateur : $installateur ($([math]::Round((Get-Item $installateur).Length / 1MB, 1)) Mo)"
Write-Host "Fiche        : $(Join-Path $Destination 'version.json')"
if (-not $Signer) { Write-Host 'Non signé : Windows SmartScreen et certains antivirus peuvent avertir à l''installation.' }
