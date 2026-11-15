using System.Security.Cryptography;

namespace SIM800L_Flasher;

internal sealed record FirmwarePackage(string FullPath, long Size, string Sha256)
{
    public static FirmwarePackage Inspect(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("فایل firmware پیدا نشد.", path);

        string[] allowed = [".bin", ".img", ".lod", ".cfg", ".pac"];
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!allowed.Contains(extension)) throw new InvalidDataException("پسوند فایل firmware پشتیبانی نمی‌شود.");

        var info = new FileInfo(path);
        if (info.Length < 256) throw new InvalidDataException("فایل برای firmware معتبر بیش از حد کوچک است.");
        if (info.Length > 512L * 1024 * 1024) throw new InvalidDataException("حجم فایل از حد ایمن ۵۱۲ مگابایت بیشتر است.");

        using var stream = info.OpenRead();
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return new FirmwarePackage(info.FullName, info.Length, hash);
    }
}
