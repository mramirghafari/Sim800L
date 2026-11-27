using System.Security.Cryptography;
using System.Text.Json;

namespace SIM800L_Flasher;

internal static class MobileSnapshot
{
    private const int MaxBytes = 1_048_576;
    private static ReadOnlySpan<byte> Header => "XAnten.MobileSnapshot.1\0"u8;

    internal static void Save(string path, MobileTarget target, MobileReading reading, DateTimeOffset readAt)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            SchemaVersion = 1, ReadAtUtc = readAt, target.Protocol, target.Id,
            reading.Fields, reading.Identifiers, reading.Capabilities
        });
        var temporaryPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                stream.Write(Protect(payload));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            File.Delete(temporaryPath);
        }
    }

    internal static void Verify(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Snapshot is too large.");
        var envelope = new byte[stream.Length];
        stream.ReadExactly(envelope);
        VerifyEnvelope(envelope);
    }

    internal static byte[] Protect(ReadOnlySpan<byte> payload)
    {
        var contents = new byte[SHA256.HashSizeInBytes + payload.Length];
        SHA256.HashData(payload, contents);
        payload.CopyTo(contents.AsSpan(SHA256.HashSizeInBytes));
        try
        {
            var encrypted = ProtectedData.Protect(contents, null, DataProtectionScope.CurrentUser);
            return [.. Header, .. encrypted];
        }
        finally { CryptographicOperations.ZeroMemory(contents); }
    }

    internal static void VerifyEnvelope(ReadOnlySpan<byte> envelope)
    {
        if (!envelope.StartsWith(Header) || envelope.Length <= Header.Length)
            throw new InvalidDataException("Unknown snapshot format.");

        var contents = ProtectedData.Unprotect(envelope[Header.Length..].ToArray(), null, DataProtectionScope.CurrentUser);
        try
        {
            if (contents.Length <= SHA256.HashSizeInBytes) throw new InvalidDataException("Snapshot is incomplete.");
            var checksum = contents.AsSpan(0, SHA256.HashSizeInBytes);
            var payload = contents.AsSpan(SHA256.HashSizeInBytes);
            if (!CryptographicOperations.FixedTimeEquals(checksum, SHA256.HashData(payload)))
                throw new InvalidDataException("Snapshot hash did not match.");
        }
        finally { CryptographicOperations.ZeroMemory(contents); }
    }
}
