# Providers ERP (mécanisme de plugin)

Architecture **hybride** pour la transformation XML → format ERP :

- **Par défaut : mapping par configuration.** La plupart des ERP (convertibles
  en CSV ou XML par simple mapping de champs) ne nécessitent AUCUNE DLL. Ils
  passent par le provider intégré `mapping` (MappingExporter), piloté par le
  JSON du canal. Ajouter un tel ERP = ajouter un canal dans la config.

- **Pour les cas spécifiques : plugin DLL.** Si un ERP demande une
  transformation qui ne se laisse pas décrire par le mapping (agrégations,
  structure de sortie très particulière, logique métier), on fournit une DLL
  dédiée déposée dans le dossier **`providers/`** à la racine du module. Le
  service la découvre au démarrage. Le reste du pipeline (routage par Buyer,
  merge, transport, archivage) ne change pas.

## Comment le service découvre les providers

Au démarrage, `ProviderLoader.LoadAll` :
1. enregistre le provider par défaut `mapping` ;
2. scanne `<dossier de l'exe>/providers/*.dll` ;
3. pour chaque DLL, instancie les types qui implémentent `IErpExporter` et les
   enregistre sous le(s) nom(s) qu'ils déclarent.

Robustesse : une DLL illisible, incompatible ou dont un type ne se charge pas
est **loggée et ignorée** — elle ne fait jamais planter le service.

## Comment un canal choisit son provider

Dans la config du canal (settings.json), le champ `Provider` :
- `"mapping"` (défaut, ou vide) → transformation par configuration ;
- `"<nom déclaré par une DLL>"` → cette DLL est utilisée.

Si le nom référencé est introuvable, le service retombe sur `mapping` en le
signalant dans les logs.

## Créer un nouveau provider (ERP spécifique)

1. Nouveau projet **class library** référençant `ACPollerForAPS.Core`.
2. Implémenter `IErpExporter` :

```csharp
using System.Collections.Generic;
using System.Text;
using ACPollerForAPS.Core;

public class OptimaExporter : IErpExporter
{
    public IEnumerable<string> ProviderNames => new[] { "optima" };

    public ExportResult Export(IEnumerable<string> inputXmls, OutputChannel channel)
    {
        var result = new ExportResult();
        // ... transformation spécifique ...
        // result.Warnings.Add("..."); // avertissements non bloquants
        result.Content = Encoding.UTF8.GetBytes(/* le fichier produit */);
        return result;
    }
}
```

3. Compiler la DLL, la déposer dans `providers/` à côté de l'exe.
4. Dans la config, mettre `"Provider": "optima"` sur le canal concerné.

Aucune recompilation du service. Le contrat (`IErpExporter`, `ExportResult`,
`OutputChannel`) est dans la DLL Core, partagée — donc le provider voit
exactement le même modèle que le service et l'UI.

## Ce qui reste commun à tous les providers

Le provider ne s'occupe QUE de produire le contenu du fichier. Tout le reste est
géré par le pipeline, identique quel que soit le provider :
- détection et lecture des fichiers d'entrée,
- routage par Buyer, merge par lot,
- nommage du fichier de sortie (jetons {date}, {time}, {guid}),
- dépôt via le transport du canal (FS / FTPS / S3) avec retry,
- archivage des sources seulement si le dépôt a réussi.
