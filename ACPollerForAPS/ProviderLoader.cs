using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ACPollerForAPS.Core;
using NLog;

namespace ConversionService
{
    /// <summary>
    /// Registre des providers ERP. Contient toujours le provider par défaut
    /// "mapping", plus ceux découverts dans le dossier providers/.
    /// La résolution par nom est insensible à la casse.
    /// </summary>
    public class ProviderRegistry
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<string, IErpExporter> _byName =
            new Dictionary<string, IErpExporter>(StringComparer.OrdinalIgnoreCase);

        public ProviderRegistry()
        {
            // provider par défaut, toujours présent
            Register(new MappingExporter());
        }

        public void Register(IErpExporter exporter)
        {
            if (exporter?.ProviderNames == null) return;
            foreach (var name in exporter.ProviderNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (_byName.ContainsKey(name))
                {
                    Log.Warn("Provider '{0}' déjà enregistré, ignoré (doublon).", name);
                    continue;
                }
                _byName[name] = exporter;
                Log.Info("Provider enregistré : '{0}' ({1})", name, exporter.GetType().FullName);
            }
        }

        /// <summary>Résout un provider par nom ; retombe sur "mapping" si introuvable.</summary>
        public IErpExporter Resolve(string providerName)
        {
            var name = string.IsNullOrWhiteSpace(providerName) ? MappingExporter.Name : providerName;
            IErpExporter exporter;
            if (_byName.TryGetValue(name, out exporter))
                return exporter;

            Log.Warn("Provider '{0}' introuvable, utilisation du provider par défaut '{1}'.",
                name, MappingExporter.Name);
            return _byName[MappingExporter.Name];
        }

        public IEnumerable<string> Names => _byName.Keys;
    }

    /// <summary>
    /// Charge les DLL de providers déposées dans le dossier providers/ à la
    /// racine du module. Robuste : une DLL illisible/incompatible est loggée
    /// et ignorée, elle ne fait jamais planter le service.
    /// </summary>
    public static class ProviderLoader
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static ProviderRegistry LoadAll(string baseDirectory)
        {
            var registry = new ProviderRegistry();

            var dir = Path.Combine(baseDirectory, "providers");
            if (!Directory.Exists(dir))
            {
                Log.Info("Dossier providers/ absent ({0}) : seuls les providers intégrés sont disponibles.", dir);
                return registry;
            }

            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    var types = SafeGetTypes(asm);
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || t.IsInterface) continue;
                        if (!typeof(IErpExporter).IsAssignableFrom(t)) continue;

                        try
                        {
                            var instance = (IErpExporter)Activator.CreateInstance(t);
                            registry.Register(instance);
                            Log.Info("Provider chargé depuis {0} : {1}", Path.GetFileName(dll), t.FullName);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Impossible d'instancier {0} depuis {1}", t.FullName, dll);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // DLL corrompue, mauvaise version .NET, dépendance manquante… : on ignore
                    Log.Error(ex, "Chargement du provider échoué : {0}", dll);
                }
            }

            return registry;
        }

        /// <summary>GetTypes qui ne jette pas sur les types partiellement chargeables.</summary>
        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                Log.Warn("Certains types de {0} n'ont pas pu être chargés.", asm.FullName);
                return ex.Types.Where(t => t != null);
            }
        }
    }
}
