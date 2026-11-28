using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MultiDEC.Core.Droits;

/// <summary>
/// Clé privée de signature de RETOTUB, chiffrée par mot de passe (fichier cle-signature.json du dossier de données de
/// MultiDEC Admin) : PBKDF2-HMAC-SHA256, AES-256-CBC puis HMAC-SHA256 du chiffré. Le mot de passe n'est jamais
/// enregistré ; sans lui, le fichier est inutilisable.
/// </summary>
public static class CoffreCle
{
    public const string NomFichier = "cle-signature.json";
    public const int Iterations = 200_000;
    public const int TailleCle = 3072;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Nouvelle paire de clés RSA de <see cref="TailleCle"/> bits. Sous .NET Framework, RSA.Create() donne un
    /// RSACryptoServiceProvider qui ignore la taille demandée après sa création (1 024 bits, constaté le 28/11/2026) :
    /// MultiDEC Admin passe donc une fabrique (new RSACng(3072)) et la taille obtenue est toujours contrôlée.
    /// </summary>
    public static RSA Generer(Func<RSA>? fabrique = null)
    {
        var rsa = fabrique?.Invoke() ?? RSA.Create();
        try
        {
            if (fabrique == null)
            {
                rsa.KeySize = TailleCle;
            }

            int bits = rsa.ExportParameters(false).Modulus!.Length * 8; // force la génération
            if (bits < TailleCle)
            {
                throw new CryptographicException($"Clé générée de {bits} bits au lieu de {TailleCle}.");
            }

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    /// <summary>Taille en bits d'une clé publique XML « RSAKeyValue ».</summary>
    public static int TailleBits(string clePubliqueXml)
    {
        using var rsa = ListePostes.CreerCle(clePubliqueXml);
        return rsa.ExportParameters(false).Modulus!.Length * 8;
    }

    /// <summary>Vrai si la clé est assez longue pour signer la liste (<see cref="TailleCle"/> bits au moins).</summary>
    public static bool EstSuffisante(string clePubliqueXml) => TailleBits(clePubliqueXml) >= TailleCle;

    /// <summary>Clé publique au format XML « RSAKeyValue » (compilée dans MultiDEC).</summary>
    public static string ClePubliqueXml(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        return $"<RSAKeyValue><Modulus>{Convert.ToBase64String(p.Modulus!)}</Modulus><Exponent>{Convert.ToBase64String(p.Exponent!)}</Exponent></RSAKeyValue>";
    }

    /// <summary>Empreinte courte de la clé publique (contrôle visuel : MultiDEC et MultiDEC Admin l'affichent).</summary>
    public static string Empreinte(string clePubliqueXml)
    {
        using var sha = SHA256.Create();
        byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(clePubliqueXml.Trim()));
        return BitConverter.ToString(h, 0, 8).Replace("-", ":");
    }

    public static string Chiffrer(RSA rsa, string motDePasse)
    {
        VerifierMotDePasse(motDePasse);
        var p = rsa.ExportParameters(true);
        if (p.Modulus!.Length * 8 < TailleCle)
        {
            throw new ArgumentException($"Clé de {p.Modulus.Length * 8} bits trop courte : {TailleCle} bits au moins.");
        }

        var cle = new CleSerialisee
        {
            Modulus = B(p.Modulus), Exponent = B(p.Exponent), D = B(p.D), P = B(p.P), Q = B(p.Q), DP = B(p.DP), DQ = B(p.DQ), InverseQ = B(p.InverseQ),
        };
        byte[] clair = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cle, Options));

        byte[] sel = Aleatoire(16), iv = Aleatoire(16);
        Deriver(motDePasse, sel, Iterations, out byte[] cleAes, out byte[] cleMac);
        byte[] chiffre;
        using (var aes = Aes.Create())
        {
            aes.Key = cleAes;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var t = aes.CreateEncryptor();
            chiffre = t.TransformFinalBlock(clair, 0, clair.Length);
        }

        Array.Clear(clair, 0, clair.Length);
        var fichier = new FichierCoffre
        {
            Format = 1,
            Iterations = Iterations,
            Sel = B(sel),
            Iv = B(iv),
            Donnees = B(chiffre),
            Mac = B(Mac(cleMac, iv, chiffre)),
            ClePublique = ClePubliqueXml(rsa),
        };
        return JsonSerializer.Serialize(fichier, Options);
    }

    /// <summary>Clé publique enregistrée en clair dans le coffre (sans mot de passe).</summary>
    public static string ClePublique(string coffre) => LireFichier(coffre).ClePublique ?? throw new InvalidDataException("Clé publique absente du fichier de clé.");

    /// <summary>
    /// Déchiffre la clé privée. Lève <see cref="UnauthorizedAccessException"/> si le mot de passe est faux (ou le
    /// fichier modifié).
    /// </summary>
    public static RSA Ouvrir(string coffre, string motDePasse)
    {
        var f = LireFichier(coffre);
        byte[] sel = Convert.FromBase64String(f.Sel!), iv = Convert.FromBase64String(f.Iv!), chiffre = Convert.FromBase64String(f.Donnees!);
        Deriver(motDePasse ?? string.Empty, sel, f.Iterations, out byte[] cleAes, out byte[] cleMac);
        if (!EgaliteConstante(Mac(cleMac, iv, chiffre), Convert.FromBase64String(f.Mac!)))
        {
            throw new UnauthorizedAccessException("Mot de passe de la clé de signature incorrect.");
        }

        byte[] clair;
        using (var aes = Aes.Create())
        {
            aes.Key = cleAes;
            aes.IV = iv;
            using var t = aes.CreateDecryptor();
            clair = t.TransformFinalBlock(chiffre, 0, chiffre.Length);
        }

        var c = JsonSerializer.Deserialize<CleSerialisee>(Encoding.UTF8.GetString(clair), Options)!;
        Array.Clear(clair, 0, clair.Length);
        var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = D(c.Modulus), Exponent = D(c.Exponent), D = D(c.D), P = D(c.P), Q = D(c.Q), DP = D(c.DP), DQ = D(c.DQ), InverseQ = D(c.InverseQ),
        });
        if (ClePubliqueXml(rsa) != f.ClePublique)
        {
            throw new InvalidDataException("Fichier de clé incohérent (clé publique différente de la clé privée).");
        }

        return rsa;
    }

    public static void VerifierMotDePasse(string motDePasse)
    {
        if (motDePasse == null || motDePasse.Length < 10)
        {
            throw new ArgumentException("Le mot de passe de la clé de signature doit comporter au moins 10 caractères.");
        }
    }

    private static FichierCoffre LireFichier(string coffre)
    {
        FichierCoffre? f;
        try
        {
            f = JsonSerializer.Deserialize<FichierCoffre>(coffre, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Fichier de clé de signature illisible.", ex);
        }

        if (f == null || f.Format != 1 || f.Iterations < 10_000 || f.Sel == null || f.Iv == null || f.Donnees == null || f.Mac == null)
        {
            throw new InvalidDataException("Fichier de clé de signature non reconnu.");
        }

        return f;
    }

    // PBKDF2-HMAC-SHA256 écrit ici : le constructeur Rfc2898DeriveBytes avec SHA-256 n'existe pas en .NET Standard 2.0.
    private static void Deriver(string motDePasse, byte[] sel, int iterations, out byte[] cleAes, out byte[] cleMac)
    {
        byte[] mdp = Encoding.UTF8.GetBytes(motDePasse);
        using var hmac = new HMACSHA256(mdp);
        byte[] sortie = new byte[64];
        for (int bloc = 1; bloc <= 2; bloc++)
        {
            byte[] u = hmac.ComputeHash(Concat(sel, new[] { (byte)(bloc >> 24), (byte)(bloc >> 16), (byte)(bloc >> 8), (byte)bloc }));
            byte[] t = (byte[])u.Clone();
            for (int i = 1; i < iterations; i++)
            {
                u = hmac.ComputeHash(u);
                for (int k = 0; k < t.Length; k++)
                {
                    t[k] ^= u[k];
                }
            }

            Buffer.BlockCopy(t, 0, sortie, (bloc - 1) * 32, 32);
        }

        cleAes = new byte[32];
        cleMac = new byte[32];
        Buffer.BlockCopy(sortie, 0, cleAes, 0, 32);
        Buffer.BlockCopy(sortie, 32, cleMac, 0, 32);
        Array.Clear(mdp, 0, mdp.Length);
    }

    private static byte[] Mac(byte[] cle, byte[] iv, byte[] chiffre)
    {
        using var h = new HMACSHA256(cle);
        return h.ComputeHash(Concat(iv, chiffre));
    }

    private static bool EgaliteConstante(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    private static byte[] Aleatoire(int n)
    {
        var r = new byte[n];
        using var g = RandomNumberGenerator.Create();
        g.GetBytes(r);
        return r;
    }

    private static string? B(byte[]? o) => o == null ? null : Convert.ToBase64String(o);

    private static byte[]? D(string? s) => s == null ? null : Convert.FromBase64String(s);

    private sealed class FichierCoffre
    {
        public int Format { get; set; }
        public int Iterations { get; set; }
        public string? Sel { get; set; }
        public string? Iv { get; set; }
        public string? Donnees { get; set; }
        public string? Mac { get; set; }
        public string? ClePublique { get; set; }
    }

    private sealed class CleSerialisee
    {
        public string? Modulus { get; set; }
        public string? Exponent { get; set; }
        public string? D { get; set; }
        public string? P { get; set; }
        public string? Q { get; set; }
        public string? DP { get; set; }
        public string? DQ { get; set; }
        public string? InverseQ { get; set; }
    }
}
