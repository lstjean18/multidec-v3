; Installateur de MultiDEC v3 (Inno Setup 6), construit par publication\Publier-version.ps1.
; Installation par utilisateur, sans droits administrateur (%LOCALAPPDATA%\Programs\RETOTUB\MultiDEC v3) :
; la mise à jour automatique peut donc remplacer l'application sans demande d'élévation.
; Définitions passées par le script : Version, Libelle, Source (dossier de l'appli compilée), Sortie ;
; Signer (facultatif) avec /Ssigntool=... pour signer l'installateur et le désinstalleur.

#ifndef Version
  #define Version "3.0.0"
#endif
#ifndef Libelle
  #define Libelle "v" + Version
#endif
#ifndef Source
  #define Source "..\src\MultiDEC.App\bin\Release\net48"
#endif
#ifndef Sortie
  #define Sortie "sortie"
#endif

[Setup]
AppId={{1209E86F-A2E1-4183-8132-6B3F5EB9A238}
AppName=MultiDEC v3
AppVersion={#Version}
AppVerName=MultiDEC {#Libelle}
AppPublisher=RETOTUB
VersionInfoVersion={#Version}
VersionInfoCompany=RETOTUB
VersionInfoProductName=MultiDEC v3
DefaultDirName={autopf}\RETOTUB\MultiDEC v3
DefaultGroupName=MultiDEC v3
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
OutputDir={#Sortie}
; Nom fixe : des liens du site retotub.com y pointent (le script en fait aussi une copie numérotée).
OutputBaseFilename=MultiDEC-v3-Setup
SetupIconFile=..\src\MultiDEC.App\MultiDEC.ico
UninstallDisplayIcon={app}\MultiDEC.exe
UninstallDisplayName=MultiDEC v3
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
#ifdef Signer
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "bureau"; Description: "Créer une icône sur le Bureau"; GroupDescription: "Icônes :"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Excludes: "*.vshost.*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MultiDEC v3"; Filename: "{app}\MultiDEC.exe"
Name: "{autodesktop}\MultiDEC v3"; Filename: "{app}\MultiDEC.exe"; Tasks: bureau

[Registry]
; Association des projets .mdc3 (double-clic → MultiDEC).
Root: HKA; Subkey: "Software\Classes\.mdc3"; ValueType: string; ValueName: ""; ValueData: "MultiDEC.Projet3"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\MultiDEC.Projet3"; ValueType: string; ValueName: ""; ValueData: "Projet MultiDEC v3"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\MultiDEC.Projet3\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MultiDEC.exe,0"
Root: HKA; Subkey: "Software\Classes\MultiDEC.Projet3\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MultiDEC.exe"" ""%1"""

[Run]
; Aussi en mode silencieux (mise à jour automatique) : MultiDEC est relancé après l'installation.
; /NOLANCEMENT=1 l'évite (test d'installation de la CI).
Filename: "{app}\MultiDEC.exe"; Description: "Lancer MultiDEC v3"; Flags: nowait postinstall; Check: Lancer

[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('MultiDEC v3 nécessite Microsoft .NET Framework 4.8, absent de ce poste.' + #13#10 +
      'Installez-le depuis le site de Microsoft (ou via Windows Update), puis relancez cette installation.',
      mbCriticalError, MB_OK, IDOK);
end;

function Lancer(): Boolean;
begin
  Result := ExpandConstant('{param:NOLANCEMENT|0}') <> '1';
end;
