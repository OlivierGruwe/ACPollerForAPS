using System;
using System.IO;
using System.Threading;
using ACPollerForAPS.Core;
using FluentFTP;
using NLog;

namespace ConversionService
{
    /// <summary>
    /// Dépôt d'un fichier généré vers sa destination (FS ou FTPS).
    /// L'implémentation vit dans le service (pas dans la DLL Core) car elle
    /// porte la dépendance FluentFTP et manipule les credentials déchiffrés.
    /// </summary>
    public interface IOutputTransport
    {
        /// <summary>Dépose le contenu sous le nom donné. Lève en cas d'échec.</summary>
        void Deliver(string fileName, byte[] content);
    }

    public static class TransportFactory
    {
        public static IOutputTransport Create(OutputChannel ch)
        {
            var t = ch.Transport ?? new OutputTransport();
            switch ((t.Type ?? "Fs").ToLowerInvariant())
            {
                case "ftps": return new FtpsTransport(t);
                case "s3": return new S3Transport(t);
                default: return new FileSystemTransport(ch.OutputFolder);
            }
        }
    }

    /// <summary>Dépôt sur le système de fichiers.</summary>
    public class FileSystemTransport : IOutputTransport
    {
        private readonly string _folder;
        public FileSystemTransport(string folder) { _folder = folder; }

        public void Deliver(string fileName, byte[] content)
        {
            Directory.CreateDirectory(_folder);
            var dest = Path.Combine(_folder, fileName);
            // écriture atomique : .tmp puis renommage
            var tmp = dest + ".tmp";
            File.WriteAllBytes(tmp, content);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }
    }

    /// <summary>Dépôt via FTPS (explicite ou implicite) avec FluentFTP.</summary>
    public class FtpsTransport : IOutputTransport
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly OutputTransport _t;

        public FtpsTransport(OutputTransport t) { _t = t; }

        public void Deliver(string fileName, byte[] content)
        {
            var password = CredentialProtector.Decrypt(_t.PasswordEncrypted);

            using (var client = new FtpClient(_t.Host, _t.Username, password, _t.Port))
            {
                bool implicitMode = string.Equals(_t.FtpsMode, "Implicit",
                    StringComparison.OrdinalIgnoreCase);

                client.Config.EncryptionMode = implicitMode
                    ? FtpEncryptionMode.Implicit
                    : FtpEncryptionMode.Explicit;
                client.Config.DataConnectionEncryption = true;
                client.Config.ValidateAnyCertificate = !_t.ValidateCertificate;

                if (!_t.ValidateCertificate)
                    Log.Warn("FTPS: validation de certificat désactivée pour {0}", _t.Host);

                client.Connect();
                try
                {
                    if (!string.IsNullOrWhiteSpace(_t.RemoteFolder) && _t.RemoteFolder != "/")
                        client.CreateDirectory(_t.RemoteFolder);

                    var remotePath = CombineRemote(_t.RemoteFolder, fileName);
                    var status = client.UploadBytes(content, remotePath,
                        FtpRemoteExists.Overwrite, createRemoteDir: true);

                    if (status != FtpStatus.Success)
                        throw new IOException("FTPS upload non confirmé (" + status + ") pour " + remotePath);
                }
                finally
                {
                    client.Disconnect();
                }
            }
        }

        private static string CombineRemote(string folder, string file)
        {
            if (string.IsNullOrWhiteSpace(folder)) return file;
            return folder.TrimEnd('/') + "/" + file;
        }
    }

    /// <summary>Dépôt vers S3 ou compatible S3 (MinIO…) via le SDK AWS.</summary>
    public class S3Transport : IOutputTransport
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly OutputTransport _t;

        public S3Transport(OutputTransport t) { _t = t; }

        public void Deliver(string fileName, byte[] content)
        {
            var secret = CredentialProtector.Decrypt(_t.S3SecretKeyEncrypted);
            var creds = new Amazon.Runtime.BasicAWSCredentials(_t.S3AccessKey, secret);

            var config = new Amazon.S3.AmazonS3Config();
            if (!string.IsNullOrWhiteSpace(_t.S3ServiceUrl))
            {
                // endpoint custom (MinIO, compatible S3)
                config.ServiceURL = _t.S3ServiceUrl;
                config.ForcePathStyle = _t.S3ForcePathStyle;
            }
            else
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_t.S3Region);
            }

            using (var client = new Amazon.S3.AmazonS3Client(creds, config))
            {
                var key = CombineKey(_t.S3KeyPrefix, fileName);
                using (var ms = new MemoryStream(content))
                {
                    var req = new Amazon.S3.Model.PutObjectRequest
                    {
                        BucketName = _t.S3Bucket,
                        Key = key,
                        InputStream = ms
                    };
                    // appel synchrone (le worker tourne déjà sur son thread)
                    var resp = client.PutObjectAsync(req).GetAwaiter().GetResult();
                    if ((int)resp.HttpStatusCode >= 300)
                        throw new IOException("S3 PutObject a renvoyé " + resp.HttpStatusCode + " pour " + key);
                }
            }
        }

        private static string CombineKey(string prefix, string file)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return file;
            return prefix.TrimEnd('/') + "/" + file;
        }
    }

    /// <summary>
    /// Enveloppe un transport avec une logique de retry configurable.
    /// Réussit si un essai passe ; lève la dernière exception sinon.
    /// </summary>
    public static class TransportRunner
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void DeliverWithRetry(OutputChannel ch, string fileName,
            byte[] content, CancellationToken token)
        {
            var t = ch.Transport ?? new OutputTransport();
            int attempts = Math.Max(1, t.RetryCount);
            int delay = Math.Max(0, t.RetryDelayMs);
            var transport = TransportFactory.Create(ch);

            Exception last = null;
            for (int i = 1; i <= attempts; i++)
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                try
                {
                    transport.Deliver(fileName, content);
                    if (i > 1) Log.Info("Dépôt réussi à la tentative {0} ({1})", i, fileName);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Log.Warn("Dépôt échoué (tentative {0}/{1}) pour {2} : {3}",
                        i, attempts, fileName, ex.Message);
                    if (i < attempts) token.WaitHandle.WaitOne(delay);
                }
            }
            throw new IOException("Échec du dépôt après " + attempts + " tentative(s) : " + fileName, last);
        }
    }
}
