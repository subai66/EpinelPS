//#define GameAssemblyNeedsPatch // remove if running on versions before v124 or on v137+
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace ServerSelector;

public class ServerSwitcher
{
    private const string HostsStartMarker = "# begin ServerSelector entries";
    private const string HostsEndMarker = "# end ServerSelector entries";
    private static readonly string[] HostsEntries =
    [
        "global-lobby.nikke-kr.com",
        "cloud.nikke-kr.com",
        "jp-lobby.nikke-kr.com",
        "us-lobby.nikke-kr.com",
        "kr-lobby.nikke-kr.com",
        "sea-lobby.nikke-kr.com",
        "hmt-lobby.nikke-kr.com",
        "aws-na-dr.intlgame.com",
        "sg-vas.intlgame.com",
        "aws-na.intlgame.com",
        "na-community.playerinfinite.com",
        "common-web.intlgame.com",
        "li-sg.intlgame.com",
        "na.fleetlogd.com",
        "www.jupiterlauncher.com",
        "data-aws-na.intlgame.com",
        "sentry.io"
    ];

    private static PathUtil util = new();

    public static bool IsUsingLocalServer()
    {
        return File.ReadAllText(util.SystemHostsFile).Contains("global-lobby.nikke-kr.com");
    }

    public static bool IsOffline()
    {
        return File.ReadAllText(util.SystemHostsFile).Contains("cloud.nikke-kr.com");
    }

    public static (bool, string?) SetBasePath(string basePath)
    {
        return util.SetBasePath(basePath);
    }

    public static async Task<string> CheckIntegrity()
    {
        if (!IsUsingLocalServer())
            return "Official server";

        if (File.Exists(util.LauncherCertificatePath))
        {
            string certList1 = await File.ReadAllTextAsync(util.LauncherCertificatePath);

            if (!certList1.Contains("Good SSL Ca"))
                return "SSL Cert Patch missing Launcher";
        }

        if (File.Exists(util.GameCertificatePath))
        {
            string certList2 = await File.ReadAllTextAsync(util.GameCertificatePath);

            if (!certList2.Contains("Good SSL Ca"))
                return "SSL Cert Patch missing Game";
        }

        // TODO: Check sodium lib
        // TODO: check hosts file

        return "OK";
    }

    public static async Task RevertHostsFile(string hostsFilePath)
    {
        string[] lines = await File.ReadAllLinesAsync(hostsFilePath);
        int startIndex = -1;
        int endIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {

            if (startIndex == -1 && lines[i].Contains(HostsStartMarker, StringComparison.OrdinalIgnoreCase))
            {
                startIndex = i;
                continue;
            }

            if (startIndex != -1 && lines[i].Contains(HostsEndMarker, StringComparison.OrdinalIgnoreCase))
            {
                endIndex = i;
                break;
            }
        }

        List<string> newLines = [];
        bool changed = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (startIndex != -1 && endIndex != -1 && i >= startIndex && i <= endIndex)
            {
                changed = true;
                continue;
            }

            if (lines[i].Contains(HostsStartMarker, StringComparison.OrdinalIgnoreCase)
                || lines[i].Contains(HostsEndMarker, StringComparison.OrdinalIgnoreCase)
                || ShouldRemoveHostLine(lines[i]))
            {
                changed = true;
                continue;
            }


            newLines.Add(lines[i]);
        }
        if (changed)
        {
            await File.WriteAllLinesAsync(hostsFilePath, newLines);

        }
    }

    public static async Task<ServerSwitchResult> SaveCfg(bool useOffical, string ip, bool offlineMode)
    {
        string CAcert = await File.ReadAllTextAsync(AppDomain.CurrentDomain.BaseDirectory + "myCA.pem");
        string sodiumLib = AppDomain.CurrentDomain.BaseDirectory + "sodium.dll";

        bool supported = true;
        if (useOffical)
        {
            await RevertHostsFile(util.SystemHostsFile);
            if (OperatingSystem.IsLinux())
            {
                await RevertHostsFile(util.WineHostsFile);
            }

            try
            {
                // remove cert
                if (OperatingSystem.IsWindows())
                {
                    X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
                    store.Open(OpenFlags.ReadWrite);
                    store.Remove(new X509Certificate2(X509Certificate.CreateFromCertFile(AppDomain.CurrentDomain.BaseDirectory + "myCA.pfx")));
                    store.Close();
                }
            }
            catch
            {
                // may not be installed
            }

            // restore sodium
            if (!File.Exists(util.GameSodiumBackupPath))
            {
                throw new Exception("sodium backup does not exist. Repair the game in the launcher and switch to local server and back to official.");
            }
            File.Copy(util.GameSodiumBackupPath, util.GameSodiumPath, true);

            if (util.LauncherCertificatePath != null && File.Exists(util.LauncherCertificatePath))
            {
                string certList = await File.ReadAllTextAsync(util.LauncherCertificatePath);

                int goodSslIndex1 = certList.IndexOf("Good SSL Ca");
                if (goodSslIndex1 != -1)
                    await File.WriteAllTextAsync(util.LauncherCertificatePath, certList[..goodSslIndex1]);
            }

            if (File.Exists(util.GameCertificatePath))
            {
                string certList = await File.ReadAllTextAsync(util.GameCertificatePath);

                int newCertIndex = certList.IndexOf("Good SSL Ca");
                if (newCertIndex != -1)
                    await File.WriteAllTextAsync(util.GameCertificatePath, certList[..newCertIndex]);
            }
        }
        else
        {
            // add to hosts file
            string hosts = $@"{HostsStartMarker}
{ip} global-lobby.nikke-kr.com
";
            if (offlineMode)
            {
                hosts += $"{ip} cloud.nikke-kr.com" + Environment.NewLine;
            }

            hosts += $@"{ip} jp-lobby.nikke-kr.com
{ip} us-lobby.nikke-kr.com
{ip} kr-lobby.nikke-kr.com
{ip} sea-lobby.nikke-kr.com
{ip} hmt-lobby.nikke-kr.com
{ip} aws-na-dr.intlgame.com
{ip} sg-vas.intlgame.com
{ip} aws-na.intlgame.com
{ip} na-community.playerinfinite.com
{ip} common-web.intlgame.com
{ip} li-sg.intlgame.com
255.255.221.21 na.fleetlogd.com
{ip} www.jupiterlauncher.com
{ip} data-aws-na.intlgame.com
255.255.221.21 sentry.io
{HostsEndMarker}";

            await RevertHostsFile(util.SystemHostsFile);

            try
            {
                FileInfo fi = new(util.SystemHostsFile);
                if (fi.IsReadOnly)
                {
                    // try to remove readonly flag
                    fi.IsReadOnly = false;
                }

                if (!(await File.ReadAllTextAsync(util.SystemHostsFile)).Contains("global-lobby.nikke-kr.com"))
                {
                    using StreamWriter w = File.AppendText(util.SystemHostsFile);
                    w.WriteLine();
                    w.Write(hosts);
                }
            }
            catch
            {
                throw new Exception($"cannot modify \"{util.SystemHostsFile}\" file to redirect to server, check your antivirus software");
            }

            // Also change hosts file in wineprefix if running on linux
            if (OperatingSystem.IsLinux())
            {
                await RevertHostsFile(util.WineHostsFile);
                if (!(await File.ReadAllTextAsync(util.WineHostsFile)).Contains("global-lobby.nikke-kr.com"))
                {
                    using StreamWriter w = File.AppendText(util.WineHostsFile);
                    w.WriteLine();
                    w.Write(hosts);
                }
            }

            // trust CA. TODO is this needed?
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(AppDomain.CurrentDomain.BaseDirectory + "myCA.pfx")));
                    store.Close();
                }
            }
            catch { }

            if (!File.Exists(util.GameSodiumPath))
            {
                throw new Exception("expected sodium library to exist at path " + util.GameSodiumPath);
            }

            // copy backup if sodium size is correct
            byte[] sod = await File.ReadAllBytesAsync(util.GameSodiumPath);
            if (sod.Length <= 307200) // TODO this is awful
            {
                // orignal file size, copy backup
                await File.WriteAllBytesAsync(util.GameSodiumBackupPath, sod);
            }

            // write new sodium library
            await File.WriteAllBytesAsync(util.GameSodiumPath, await File.ReadAllBytesAsync(sodiumLib));

            // Add generated CA certificate to launcher/game curl certificate list
            if (util.LauncherCertificatePath != null)
            {
                await File.WriteAllTextAsync(util.LauncherCertificatePath,
                    await File.ReadAllTextAsync(util.LauncherCertificatePath)
                    + "\nGood SSL Ca\n===============================\n"
                    + CAcert);
            }

            await File.WriteAllTextAsync(util.GameCertificatePath,
                await File.ReadAllTextAsync(util.GameCertificatePath)
                + "\nGood SSL Ca\n===============================\n"
                + CAcert);
        }

        return new ServerSwitchResult(true, null, supported);
    }
    private static bool ShouldRemoveHostLine(string line)
    {
        foreach (string host in HostsEntries)
        {
            if (line.Contains(host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public class ServerSwitchResult(bool ok, Exception? exception, bool isSupported)
{
    public bool Ok { get; set; } = ok;
    public Exception? Exception { get; set; } = exception;
    public bool IsSupported { get; set; } = isSupported;
}
