using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class CryptographyHelper
{
    // Key size and other parameters should match Fernet's defaults if aiming for compatibility,
    // Fernet uses AES-128 in CBC mode and HMAC-SHA256.
    // A Fernet key is a 32-byte URL-safe base64-encoded string.
    // The first 16 bytes are for the encryption key, the last 16 for the signing key.

    public static byte[] GenerateKey()
    {
        // Generates a 32-byte key for AES-128 (16 bytes) and HMAC-SHA256 (16 bytes)
        byte[] key = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
        return key;
    }

    public static string Encrypt(byte[] data, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Key must be 32 bytes.");

        byte[] encryptionKey = new byte[16];
        byte[] signingKey = new byte[16];
        Buffer.BlockCopy(key, 0, encryptionKey, 0, 16);
        Buffer.BlockCopy(key, 16, signingKey, 0, 16);

        using (var aes = Aes.Create())
        {
            aes.Key = encryptionKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV(); // Generate a new IV for each encryption
            byte[] iv = aes.IV;

            using (var encryptor = aes.CreateEncryptor(aes.Key, iv))
            using (var ms = new MemoryStream())
            {
                // Prepend IV to the ciphertext
                ms.Write(iv, 0, iv.Length);
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(data, 0, data.Length);
                    cs.FlushFinalBlock();
                }
                byte[] encryptedData = ms.ToArray();

                // Sign the (IV + ciphertext)
                using (var hmac = new HMACSHA256(signingKey))
                {
                    byte[] signature = hmac.ComputeHash(encryptedData);
                    byte[] signedEncryptedData = new byte[encryptedData.Length + signature.Length];
                    Buffer.BlockCopy(encryptedData, 0, signedEncryptedData, 0, encryptedData.Length);
                    Buffer.BlockCopy(signature, 0, signedEncryptedData, encryptedData.Length, signature.Length);

                    // Fernet format also includes timestamp and version, which are omitted here for simplicity
                    // but would be needed for full Fernet compatibility.
                    // This implementation is a simplified AES CBC + HMAC.
                    return Convert.ToBase64String(signedEncryptedData);
                }
            }
        }
    }

    public static byte[] Decrypt(string base64EncryptedData, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Key must be 32 bytes.");

        byte[] encryptionKey = new byte[16];
        byte[] signingKey = new byte[16];
        Buffer.BlockCopy(key, 0, encryptionKey, 0, 16);
        Buffer.BlockCopy(key, 16, signingKey, 0, 16);

        byte[] signedEncryptedData = Convert.FromBase64String(base64EncryptedData);

        // Assuming the signature is the last 32 bytes (SHA256 hash)
        int signatureSize = 32;
        byte[] encryptedData = new byte[signedEncryptedData.Length - signatureSize];
        byte[] receivedSignature = new byte[signatureSize];
        Buffer.BlockCopy(signedEncryptedData, 0, encryptedData, 0, encryptedData.Length);
        Buffer.BlockCopy(signedEncryptedData, encryptedData.Length, receivedSignature, 0, signatureSize);

        // Verify signature
        using (var hmac = new HMACSHA256(signingKey))
        {
            byte[] computedSignature = hmac.ComputeHash(encryptedData);
            if (!CompareByteArrays(receivedSignature, computedSignature))
            {
                throw new CryptographicException("Invalid signature.");
            }
        }

        // Extract IV (first 16 bytes of encryptedData)
        byte[] iv = new byte[16]; // AES block size
        Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);

        using (var aes = Aes.Create())
        {
            aes.Key = encryptionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                {
                    // Write data to be decrypted (ciphertext, excluding the IV part)
                    cs.Write(encryptedData, iv.Length, encryptedData.Length - iv.Length);
                    cs.FlushFinalBlock();
                }
                return ms.ToArray();
            }
        }
    }

    private static bool CompareByteArrays(byte[] a1, byte[] a2)
    {
        if (a1.Length != a2.Length)
            return false;
        for (int i = 0; i < a1.Length; i++)
            if (a1[i] != a2[i])
                return false;
        return true;
    }
}
