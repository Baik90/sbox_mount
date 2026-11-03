using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SboxMount;

public static class WorkshopBuildMenu
{
    private static bool running;

    [Menu( "Editor", "S&Box Mount/Build and Install" )]
    public static async void BuildAndInstallMenu()
    {
        if ( running ) { Log.Info( "S&Box Mount: Build and Install is already running." ); return; }
        try
        {
            await BuildAndInstall();
            EditorUtility.DisplayDialog( "S&Box Mount", "Build und Installation erfolgreich. Starte s&box neu, um die neue DLL zu laden.", "OK", null, null );
        }
        catch ( Exception e )
        {
            Log.Error( e );
            EditorUtility.DisplayDialog( "S&Box Mount: Fehler", e.Message + "\nDetails stehen in der Konsole.", "OK", null, null );
        }
    }

    public static async Task BuildAndInstall()
    {
        if ( running ) throw new InvalidOperationException( "Build and Install is already running." );
        string projectRoot = Project.Current?.GetRootPath() ?? throw new InvalidOperationException( "Open the Sbox Mount project first." );
        string script = Path.Combine( projectRoot, "Tools/Install-Mount.ps1" );
        if ( !File.Exists( script ) ) throw new FileNotFoundException( "Install-Mount.ps1 was not found in this project.", script );
        running = true;
        try
        {
            Log.Info( "S&Box Mount: Building and installing…" );
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe", WorkingDirectory = projectRoot,
                UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach ( string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-EngineRoot", WorkshopGame.EngineRoot } )
                start.ArgumentList.Add( arg );
            using var process = Process.Start( start ) ?? throw new InvalidOperationException( "Could not start the installer." );
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = await stdout;
            string errors = await stderr;
            await MainThread.Wait();
            if ( !string.IsNullOrWhiteSpace( output ) ) Log.Info( output );
            if ( process.ExitCode != 0 ) throw new InvalidOperationException( $"Build/Installation fehlgeschlagen (Exit {process.ExitCode}).\n{errors}\n{output}" );
            if ( !string.IsNullOrWhiteSpace( errors ) ) Log.Warning( errors );
            Log.Info( "S&Box Mount installed. Restart s&box to load the new DLL." );
        }
        finally { running = false; }
    }
}
