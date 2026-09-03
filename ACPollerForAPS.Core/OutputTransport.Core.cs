namespace ACPollerForAPS.Core
{
    // =====================================================================
    // Configuration du TRANSPORT de sortie d'un canal : où et comment
    // déposer le fichier généré (système de fichiers ou FTPS).
    //
    // Le MODÈLE est ici (partagé UI + service). L'IMPLÉMENTATION réelle
    // (dépôt FTP via FluentFTP) est dans le service uniquement : l'UI édite
    // la config, elle ne dépose rien.
    //
    // Le mot de passe FTPS est stocké CHIFFRÉ (DPAPI portée machine) dans
    // PasswordEncrypted, jamais en clair. Voir CredentialProtector.
    // =====================================================================

    public class OutputTransport
    {
        // Fs | Ftps | S3
        public string Type { get; set; } = "Fs";

        // nombre de tentatives de dépôt en cas d'échec réseau (>=1)
        public int RetryCount { get; set; } = 3;
        // délai entre deux tentatives (ms)
        public int RetryDelayMs { get; set; } = 5000;

        // --- FTPS (ignoré si Type != Ftps) ---
        public string Host { get; set; } = "";
        public int Port { get; set; } = 21;
        public string RemoteFolder { get; set; } = "/";
        public string Username { get; set; } = "";

        // mot de passe CHIFFRÉ (DPAPI). Jamais en clair dans le JSON.
        public string PasswordEncrypted { get; set; } = "";

        // Explicit (FTPES, AUTH TLS sur port 21) | Implicit (port 990)
        public string FtpsMode { get; set; } = "Explicit";

        // valider le certificat serveur ? false = accepter tout certificat
        // (pratique en test, à éviter en prod)
        public bool ValidateCertificate { get; set; } = true;

        // --- S3 / compatible S3 (ignoré si Type != S3) ---
        public string S3Bucket { get; set; } = "";
        // préfixe de clé (dossier logique) ; le nom de fichier est ajouté après
        public string S3KeyPrefix { get; set; } = "";
        public string S3Region { get; set; } = "eu-west-1";
        // endpoint custom pour MinIO / compatible S3 ; vide = AWS standard
        public string S3ServiceUrl { get; set; } = "";
        // requis par MinIO et beaucoup de compatibles S3
        public bool S3ForcePathStyle { get; set; } = false;
        public string S3AccessKey { get; set; } = "";
        // clé secrète CHIFFRÉE (DPAPI). Jamais en clair dans le JSON.
        public string S3SecretKeyEncrypted { get; set; } = "";
    }
}
