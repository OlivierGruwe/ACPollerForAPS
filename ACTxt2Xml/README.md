# ACTxt2Xml

Service Windows multithread (.NET Framework 4.8.1) qui convertit :
- les `.txt` du dossier A vers `.xml` dans le dossier B
- les `.xml` du dossier C vers `.txt` dans le dossier D

Chaque sens tourne sur son propre thread, piloté par Start / Pause / Continue / Stop
du gestionnaire de services Windows. Détection via `FileSystemWatcher` + polling de
secours, avec contrôle de complétude des fichiers (taille stable + lock exclusif).
Logs via NLog. Paramètres dans `settings.json`.

## Prérequis

- .NET Framework 4.8.1 Developer Pack (machine de build, pas seulement le runtime)
- Visual Studio 2022 ou `dotnet` / `msbuild` en ligne de commande

## Build

```
dotnet build -c Release
```
ou ouvrir le projet dans Visual Studio et compiler en Release.

Le binaire et les fichiers `settings.json` / `NLog.config` se trouvent dans
**`bin\Release\net481\`** (ou `bin\Debug\net481\` en Debug). L'exe produit est
**`ACTxt2Xml.exe`** (defini par `AssemblyName` dans le `.csproj`).

> Important : avec un projet SDK-style ciblant `net481`, la sortie est dans le
> sous-dossier `net481`, PAS directement dans `bin\`. Toutes les commandes
> d'installation ci-dessous se lancent depuis ce sous-dossier.

## Noms a garder alignes

Trois endroits doivent porter le MEME nom de service, sinon le service ne demarre pas :

| Emplacement                           | Valeur      |
|---------------------------------------|-------------|
| `ProjectInstaller.cs` -> ServiceName  | `ACTxt2Xml` |
| Constructeur du service -> ServiceName| `ACTxt2Xml` |
| `install.bat` / `sc start`            | `ACTxt2Xml` |

Le nom de l'exe (`ACTxt2Xml.exe`) est independant du ServiceName mais on les a
alignes ici pour simplifier.

## Configuration

Editer `settings.json` (copie a cote de l'exe) :

| Champ                  | Description                                          |
|------------------------|------------------------------------------------------|
| SourceFolder           | Dossier surveille en entree                          |
| TargetFolder           | Dossier de sortie                                    |
| PollingIntervalSeconds | Intervalle du balayage de secours (le watcher gere le temps reel) |
| FileFilter             | Filtre de fichiers (`*.txt`, `*.xml`, ...)           |
| StableCheckMs          | Delai entre 2 mesures de taille pour juger un fichier "pret" |

## Le mappage (a implementer)

La logique de conversion reelle n'est PAS encore ecrite. Voir les methodes
`MapToXml` dans `TxtToXmlWorker.cs` et `MapToTxt` dans `XmlToTxtWorker.cs`
(actuellement des placeholders).

## Debug en console

```
ACTxt2Xml.exe --console
```
Commandes : `P` (pause), `R` (resume), `Q` (quit).

## Installation comme service

En invite de commandes **administrateur**, depuis le dossier de sortie
(`bin\Release\net481\`) :

```
install.bat
```

Le `.bat` doit se trouver dans le meme dossier que `ACTxt2Xml.exe`
(il resout l'exe via son propre emplacement). S'il n'y est pas, copie-le ou lance
l'installation manuellement :

```
cd /d D:\devs\ACTxt2Xml\bin\Release\net481
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe" ACTxt2Xml.exe
sc start ACTxt2Xml
```

Desinstallation :

```
uninstall.bat
```

## Pilotage

```
sc start ACTxt2Xml
sc pause ACTxt2Xml
sc continue ACTxt2Xml
sc stop ACTxt2Xml
```

## Erreurs frequentes

- **FileNotFoundException sur l'exe a l'install** -> tu lances `InstallUtil` depuis
  `bin\` au lieu de `bin\...\net481\`, ou tu vises le mauvais nom d'exe.
- **`[SC] OpenService 1060` (service inexistant)** -> l'install a echoue avant, donc
  le service n'a jamais ete cree ; corrige l'install d'abord.
- **"le service n'a pas repondu a temps"** -> ServiceName different entre le
  ProjectInstaller et le constructeur du service ; aligne-les.
- **CS0234 sur System.Configuration.Install** -> reference d'assembly manquante dans
  le `.csproj`, ou Developer Pack 4.8.1 absent.
