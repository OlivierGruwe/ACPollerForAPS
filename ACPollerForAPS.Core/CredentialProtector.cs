using System;
using System.Security.Cryptography;
using System.Text;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Chiffre / déchiffre les secrets (mot de passe FTPS) via DPAPI, portée
    /// MACHINE. Le chiffrement est lié à la machine : un secret chiffré sur la
    /// machine cible n'est déchiffrable QUE sur cette machine.
    ///
    /// Conséquence de déploiement : il faut chiffrer le mot de passe SUR la
    /// machine où tournera le service (via l'UI installée là, ou un petit
    /// utilitaire), pas sur un autre poste.
    ///
    /// Limite de sécurité à connaître : DPAPI protège le fichier settings.json
    /// contre la lecture directe, mais quiconque peut exécuter du code sur la
    /// machine peut déchiffrer (comme le fait le service). Ce n'est pas un
    /// coffre-fort, c'est une protection "au repos".
    /// </summary>
    public static class CredentialProtector
    {
        // entropie optionnelle : complique un déchiffrement hors de notre code.
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("ACPollerForAPS.v1");

        /// <summary>Chiffre une chaîne en clair -> base64 stockable dans le JSON.</summary>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            var data = Encoding.UTF8.GetBytes(plainText);
            var enc = ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(enc);
        }

        /// <summary>Déchiffre le base64 -> chaîne en clair. "" si vide/illisible.</summary>
        public static string Decrypt(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) return "";
            try
            {
                var enc = Convert.FromBase64String(encryptedBase64);
                var data = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                // secret chiffré sur une autre machine, ou corrompu
                return "";
            }
        }
    }
}
