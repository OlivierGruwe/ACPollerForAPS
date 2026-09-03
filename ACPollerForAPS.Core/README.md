# ACPollerForAPS.Core — DLL partagée

Bibliothèque de classes (.NET Framework 4.8) contenant les éléments communs au
service **et** à l'UI de configuration, pour qu'il n'existe qu'UNE seule
définition et aucune divergence possible entre les deux.

## Contenu

| Fichier | Rôle |
|---|---|
| PipelineSettings.cs | modèle de config (PipelineSettings, OutputChannel, PipelineField, formats, schedule) |
| PipelineEngine.cs | moteur : routage par Buyer + génération CSV et XML |
| ConfigValidator.cs | validation (erreurs bloquantes + avertissements) |

Namespace unique : `ACPollerForAPS.Core`.

Cible **net48** : compatible à la fois avec le service (v4.8, ancien format
csproj) et l'UI (net481, SDK-style). Une DLL net481 ne serait PAS référençable
par le service en v4.8 ; net48 est le dénominateur commun.

Aucune dépendance externe : uniquement le framework (System.Xml). La
(dé)sérialisation JSON reste à la charge des projets appelants (Newtonsoft).

## Structure de solution recommandée

    Solution/
      ACPollerForAPS.Core/        <- cette DLL
        ACPollerForAPS.Core.csproj
        PipelineSettings.cs
        PipelineEngine.cs
        ConfigValidator.cs
      ACPollerForAPS/             <- le service (référence la DLL)
      PipelineConfig/            <- l'UI (référence la DLL)

## Intégration côté SERVICE (ACPollerForAPS)

Déjà fait dans les fichiers fournis :
- `PipelineSettings.cs` et `PipelineEngine.cs` ont été RETIRÉS du projet service
  (ils sont désormais dans la DLL).
- `PipelineWorker.cs`, `Settings.cs`, `ConversionService.cs` ont reçu
  `using ACPollerForAPS.Core;`.
- Le `.csproj` a une `<ProjectReference>` vers la DLL.

Dans Visual Studio : ajouter le projet DLL à la solution, puis vérifier la
référence (clic droit sur le service -> Ajouter -> Référence -> Projets ->
ACPollerForAPS.Core).

## Intégration côté UI (PipelineConfig)

À faire de ton côté (l'UI compile déjà avec ses classes locales ; il s'agit de
basculer sur la DLL) :

1. SUPPRIMER du projet UI les fichiers dont le contenu est maintenant dans la DLL :
   - `PipelineConfig.cs` (le modèle)
   - `PipelineEngine.cs`
   - `ConfigValidator.cs`
2. Ajouter la référence projet vers `ACPollerForAPS.Core`.
3. Dans les fichiers restants de l'UI (`MainForm.cs`, `GeneralTab.cs`,
   `ScheduleTab.cs`, `ChannelsTab.cs`, `ConfigStore.cs`), ajouter en tête :
   `using ACPollerForAPS.Core;`
4. Renommer les types si nécessaire pour correspondre à la DLL :
   - l'UI utilisait `CsvFormat`/`XmlFormat`/`FieldMapping`/`Schedule` ;
     la DLL les nomme `PipelineCsvFormat`/`PipelineXmlFormat`/`PipelineField`/
     `PipelineSchedule`. Adapter les références dans les onglets.
   - (le modèle DLL a été préfixé "Pipeline" pour éviter toute collision de noms
     génériques ; c'est le seul renommage à propager dans l'UI.)

`ConfigStore.cs` RESTE dans l'UI (choix retenu) : la sauvegarde atomique + .bak
est un besoin de l'éditeur, pas du service.

## Bénéfice

Le jour où le format se précise (après le call Marko : vrai format APS, sens
débit/crédit, format Optima), une seule modification dans la DLL se propage
automatiquement au service ET à l'UI. Plus de risque que l'aperçu de l'éditeur
diverge de la production.
