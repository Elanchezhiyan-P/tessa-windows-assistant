using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WinCompanion.Services;

/// <summary>
/// Locks a backup with a password. The key is made from the password with PBKDF2-SHA256 (a slow, salted derivation, so guessing is
/// costly) and the data is sealed with AES-256-GCM, which also detects any change to the file. Layout:
/// "TSBK", version, iterations (4 bytes), salt (16), nonce (12), then the encrypted data followed by its 16-byte tag.
/// The header is covered by the tag, so it cannot be altered either.
/// </summary>
public static class BackupCrypto
{
    public const int DefaultIterations = 600_000;
    public const int MinIterations = 1_000;
    public const int MaxIterations = 5_000_000;
    public const int MinPasswordLength = 6;

    private static readonly byte[] Magic = "TSBK"u8.ToArray();
    private const byte Version = 1;
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32;
    private const int HeaderSize = 4 + 1 + 4 + SaltSize + NonceSize;

    /// <summary>True when the bytes start like a locked backup (a plain backup is an ordinary zip).</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.Length > HeaderSize + TagSize && data[..Magic.Length].SequenceEqual(Magic);

    public static bool IsEncryptedFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[Magic.Length];
            return stream.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(Magic);
        }
        catch (IOException) { return false; }
    }

    /// <summary>Why a password can't be used, or null when it is fine.</summary>
    public static string? CheckPassword(string? password) =>
        string.IsNullOrEmpty(password) || password.Length >= MinPasswordLength
            ? null
            : $"Use at least {MinPasswordLength} characters.";

    public static byte[] Encrypt(byte[] plain, string password, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password is needed.", nameof(password));
        iterations = Math.Clamp(iterations, MinIterations, MaxIterations);

        var output = new byte[HeaderSize + plain.Length + TagSize];
        Magic.CopyTo(output, 0);
        output[4] = Version;
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(5, 4), iterations);
        RandomNumberGenerator.Fill(output.AsSpan(9, SaltSize));
        RandomNumberGenerator.Fill(output.AsSpan(9 + SaltSize, NonceSize));

        var key = Derive(password, output.AsSpan(9, SaltSize), iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(output.AsSpan(9 + SaltSize, NonceSize), plain,
                output.AsSpan(HeaderSize, plain.Length), output.AsSpan(HeaderSize + plain.Length, TagSize), output.AsSpan(0, HeaderSize));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return output;
    }

    /// <summary>Opens a locked backup. A wrong password and a damaged file look the same, on purpose.</summary>
    /// <exception cref="InvalidDataException">The password is wrong, or the file is not a locked backup or was changed.</exception>
    public static byte[] Decrypt(byte[] data, string password)
    {
        if (!IsEncrypted(data) || data[4] != Version) throw new InvalidDataException("This is not a locked backup of mine.");
        var iterations = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(5, 4));
        if (iterations is < MinIterations or > MaxIterations) throw new InvalidDataException("This file is damaged.");

        var cipherLength = data.Length - HeaderSize - TagSize;
        var plain = new byte[cipherLength];
        var key = Derive(password ?? "", data.AsSpan(9, SaltSize), iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(data.AsSpan(9 + SaltSize, NonceSize), data.AsSpan(HeaderSize, cipherLength),
                data.AsSpan(HeaderSize + cipherLength, TagSize), plain, data.AsSpan(0, HeaderSize));
            return plain;
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("Wrong password, or the file was changed.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] Derive(string password, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeySize);
}
