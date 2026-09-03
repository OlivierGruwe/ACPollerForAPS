# PipelineConfigWpf — UI de configuration (WPF / MahApps.Metro)

Version WPF de la console de configuration du pipeline, pour un rendu moderne et
un socle pérenne (produit). Remplace la version WinForms.

## Pourquoi WPF + MahApps.Metro

- **WPF** : natif Microsoft, pérenne, thémable proprement, data binding réel.
  Le binding remplace les CommitFields/CommitGrid manuels de WinForms : les
  écrans et le modèle restent synchronisés automatiquement.
- **MahApps.Metro** : thème moderne mature et largement utilisé, pas une lib
  fragile. Look Metro/Fluent sans réécrire les contrôles.
- **MVVM** : logique (ViewModels) séparée de l'affichage (XAML). Maintenable.

## Réutilise la DLL Core

`ACPollerForAPS.Core` (modèle + moteur + validation) est référencée telle quelle.
La couche WPF ne fait que l'habiller. Aucune duplication : ce que l'UI sauvegarde
est exactement ce que le service lit.

## Fichiers

| Fichier | Rôle |
|---|---|
| Mvvm.cs | ObservableObject (notification) + RelayCommand |
| ViewModels.cs | FieldVm, ChannelVm : enveloppent le modèle Core, bindables |
| MainViewModel.cs | état global, commandes (New/Open/Save/Validate), preview |
| ConfigStore.cs | load/save JSON atomique + .bak (côté UI) |
| App.xaml | thème MahApps (Light.Blue) |
| MainWindow.xaml | MetroWindow, 3 onglets, tout le binding |
| MainWindow.xaml.cs | code-behind minimal (sélecteur de dossier) |

## Prérequis / build

- .NET Framework 4.8, SDK-style, `UseWPF` + `UseWindowsForms` (ce dernier
  uniquement pour le FolderBrowserDialog).
- NuGet restaure MahApps.Metro (2.4.x) et Newtonsoft.Json.
- Ajouter le projet à la solution à côté de `ACPollerForAPS.Core` (référencé) et
  compiler.

## Fonctionnel

- **General** : dossiers (entrée/archive/erreur), détection, routage
  (RecordPath + BuyerPath).
- **Schedule** : intervalle de passage + taille de lot, avec aperçu en secondes.
- **Output channels** : liste multi-canaux à gauche ; à droite, réglages du canal
  (buyers, format CSV/XML, dossier, chemins), grille de mapping, et prévisualisation
  sur un XML collé.
- Validation bloquante avant enregistrement (via ConfigValidator de la DLL),
  alerte modifications non enregistrées, sauvegarde atomique.

## Notes

- Le FolderBrowserDialog vient de WinForms (WPF n'en a pas de natif) — d'où
  `UseWindowsForms`. Alternative sans WinForms : Ookii.Dialogs.Wpf (NuGet), si tu
  veux retirer la dépendance WinForms.
- Les formats CSV/XML détaillés (séparateur, dates…) sont conservés dans le modèle
  mais pas encore édités visuellement ; à ajouter en onglet/section si besoin.
- NON COMPILÉ ici : à valider dans Visual Studio. Le XAML se prête bien à la
  relecture mais le rendu réel et d'éventuels ajustements de binding se voient à
  l'exécution.
