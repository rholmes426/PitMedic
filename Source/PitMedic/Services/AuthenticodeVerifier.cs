using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace PitMedic.Services;

public static class AuthenticodeVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static void VerifyTrustedPitMedicInstaller(string path, string expectedPublisher)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidDataException("The downloaded installer is missing.");

        var fileInfo = new WintrustFileInfo(path);
        var trustData = new WintrustData(fileInfo);
        try
        {
            var result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, trustData);
            if (result != 0)
                throw new InvalidDataException($"Windows did not trust the downloaded installer's Authenticode signature (0x{result:X8}).");

            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            var publisher = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false).Trim();
            if (!publisher.Equals(expectedPublisher, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The downloaded installer was signed by an unexpected publisher ('{publisher}').");

            if (!HasCodeSigningUsage(signer))
                throw new InvalidDataException("The downloaded installer's certificate is not valid for code signing.");
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("The downloaded installer did not contain a valid Authenticode signature.", ex);
        }
        finally
        {
            trustData.Dispose();
            fileInfo.Dispose();
        }
    }

    private static bool HasCodeSigningUsage(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>())
        {
            if (extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.3"))
                return true;
        }
        return false;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        WintrustData pWVTData);

    private sealed class WintrustFileInfo : IDisposable
    {
        private readonly IntPtr _filePathPtr;
        public readonly IntPtr StructPtr;

        public WintrustFileInfo(string filePath)
        {
            _filePathPtr = Marshal.StringToCoTaskMemUni(filePath);
            var value = new WintrustFileInfoNative
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustFileInfoNative>(),
                pcwszFilePath = _filePathPtr,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            StructPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfoNative>());
            Marshal.StructureToPtr(value, StructPtr, false);
        }

        public void Dispose()
        {
            if (StructPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(StructPtr);
            if (_filePathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(_filePathPtr);
        }
    }

    private sealed class WintrustData : IDisposable
    {
        private readonly IntPtr _structPtr;

        public WintrustData(WintrustFileInfo fileInfo)
        {
            var value = new WintrustDataNative
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustDataNative>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = 2,
                fdwRevocationChecks = 0,
                dwUnionChoice = 1,
                pFile = fileInfo.StructPtr,
                dwStateAction = 0,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = 0x00000020,
                dwUIContext = 0
            };
            _structPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustDataNative>());
            Marshal.StructureToPtr(value, _structPtr, false);
        }

        public static implicit operator IntPtr(WintrustData data) => data._structPtr;

        public void Dispose()
        {
            if (_structPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(_structPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfoNative
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustDataNative
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}
