using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Editor.Mcp;

namespace SboxMount;

/// <summary>Only presentation changes. Every resource loader and mount:// alias remains registered.</summary>
internal static class WorkshopDuplicateView
{
    private static HashSet<string> hidden = new( StringComparer.OrdinalIgnoreCase );
    internal static WorkshopDuplicateScanner.Result Result { get; private set; }
    internal static string ReportPath => Path.Combine( Project.Current.GetRootPath(), ".sbox/mount-tests/duplicates.json" );
    internal static bool Enabled { get; set; } = true;
    internal static bool IsHidden( string path ) => Enabled && hidden.Contains( path );

    internal static void Apply( WorkshopDuplicateScanner.Result result )
    {
        Result = result;
        // Publish a complete immutable set, so an in-flight browser query never observes a partial update.
        hidden = result.Groups.SelectMany( g => g.Sources.Skip( 1 ) ).Select( s => s.MountPath )
            .ToHashSet( StringComparer.OrdinalIgnoreCase );
    }

    internal static void Restore( WorkshopDuplicateScanner.Input[] inventory )
    {
        Result = null;
        Enabled = ProjectCookie.Get( "SboxWorkshop.HideDuplicateCopies", true );
        hidden = new( StringComparer.OrdinalIgnoreCase );
        try
        {
            if ( !File.Exists( ReportPath ) ) return;
            var result = JsonSerializer.Deserialize<WorkshopDuplicateScanner.Result>( File.ReadAllText( ReportPath ) );
            if ( result?.Version != 2 || result.Inventory == null || result.Files == null || result.ExternalPaths == null || result.Groups == null ) return;
            if ( !result.Inventory.SequenceEqual( WorkshopDuplicateScanner.InventoryKeys( inventory ) ) ) return;
            if ( !result.Files.All( WorkshopDuplicateScanner.IsCurrent ) ) return;
            if ( result.ExternalPaths.Any( File.Exists ) ) return;
            Apply( result );
        }
        catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException or ArgumentException )
        { Log.Warning( "Duplicate report cannot be reused; scan again: " + e.Message ); }
    }

    internal static void Save( WorkshopDuplicateScanner.Result result, string reportPath )
    {
        System.IO.Directory.CreateDirectory( Path.GetDirectoryName( reportPath ) );
        File.WriteAllText( reportPath, JsonSerializer.Serialize( result, new JsonSerializerOptions { WriteIndented = true } ) );
        var markdown = new StringBuilder( "# Identische Workshop-Assets\n\nNur die flache Gesamtansicht blendet weitere Kopien aus. Alle Mount-Pfade und Originaldateien bleiben erhalten.\n\n" );
        foreach ( var group in result.Groups )
        {
            markdown.Append( "## " ).Append( group.Kind ).Append( ": " ).Append( Path.GetFileName( group.Sources[0].RelativePath ) ).Append( "\n\n" );
            foreach ( var source in group.Sources ) markdown.Append( "- " ).Append( source.Title ).Append( " (`" ).Append( source.WorkshopId ).Append( "`): `" ).Append( source.MountPath ).Append( "`\n" );
            markdown.AppendLine();
        }
        File.WriteAllText( Path.ChangeExtension( reportPath, ".md" ), markdown.ToString() );
    }
}

[McpToolset( "workshop_mount", "Workshop duplicate scanning and browser display settings." )]
public static class WorkshopDuplicateTools
{
    private static bool running;
    private static int completed;
    private static int total;

    /// <summary>Scan original subscribed assets and their transitive local RERL dependencies. Collapse verified identical copies only in the mount's combined flat view. Keeps individual game folders and all aliases intact.</summary>
    [McpTool( "workshop_mount_scan_duplicates" )]
    public static async Task<object> Scan()
    {
        if ( running ) throw new InvalidOperationException( "A duplicate scan is already running; use workshop_mount_duplicate_status." );
        var mount = Sandbox.Mounting.Directory.Get( "sbox_workshop" ) as SboxWorkshopMount;
        if ( mount?.IsMounted != true ) throw new InvalidOperationException( "Open the Workshop mount browser first." );
        var inventory = mount.DuplicateInputs.ToArray();
        Guid revision = mount.InventoryRevision;
        string report = WorkshopDuplicateView.ReportPath;
        running = true;
        completed = total = 0;
        try
        {
            var result = await Task.Run( () =>
            {
                var scan = new WorkshopDuplicateScanner( CancellationToken.None,
                    (done, count) => { Volatile.Write( ref completed, done ); Volatile.Write( ref total, count ); } ).Scan( inventory );
                if ( mount.InventoryRevision != revision ) throw new InvalidOperationException( "Mounted subscriptions changed during scanning. Please scan again." );
                WorkshopDuplicateView.Save( scan, report );
                return scan;
            } );
            await MainThread.Wait();
            if ( mount.InventoryRevision != revision ) throw new InvalidOperationException( "Mounted subscriptions changed during scanning. Please scan again." );
            WorkshopDuplicateView.Apply( result );
            WorkshopMountTools.RefreshBrowserView();
            running = false;
            return Status();
        }
        finally { running = false; }
    }

    [Menu( "Editor", "S&Box Mount/Scan Duplicate Assets" )]
    public static async void ScanMenu()
    {
        try { Log.Info( JsonSerializer.Serialize( await Scan() ) ); }
        catch ( Exception e ) { Log.Error( e ); }
    }

    [Menu( "Editor", "S&Box Mount/Hide Duplicates in Combined Flat View" )]
    public static void Hide() => SetEnabled( true );

    [Menu( "Editor", "S&Box Mount/Show All Copies" )]
    public static void ShowAll() => SetEnabled( false );

    /// <summary>Toggle hiding verified duplicates in the combined flat view. Per-game folders and actual files remain intact.</summary>
    [McpTool( "workshop_mount_hide_duplicates" )]
    public static object SetEnabled( bool enabled )
    {
        ProjectCookie.Set( "SboxWorkshop.HideDuplicateCopies", enabled );
        WorkshopDuplicateView.Enabled = enabled;
        WorkshopMountTools.RefreshBrowserView();
        return Status();
    }

    /// <summary>Read scan progress, counts and a limited list of duplicate groups with their source games. Query filters asset paths or game titles.</summary>
    [McpTool.ReadOnly( "workshop_mount_duplicate_status" )]
    public static object Status( string query = "", int limit = 15 )
    {
        var result = WorkshopDuplicateView.Result;
        var groups = result?.Groups.Where( g => string.IsNullOrWhiteSpace( query ) || g.Sources.Any( s =>
            s.MountPath.Contains( query, StringComparison.OrdinalIgnoreCase ) || s.Title.Contains( query, StringComparison.OrdinalIgnoreCase ) ) ).ToArray()
            ?? Array.Empty<WorkshopDuplicateScanner.Group>();
        return new
        {
            Running = running, CompletedCandidates = completed, TotalCandidates = total,
            Enabled = WorkshopDuplicateView.Enabled,
            PausedForVisualMarker = WorkshopAssetCache.MarkerEnabled,
            Scope = "Combined flat/search view only; per-game views preserve every asset",
            ScannedResources = result?.Total ?? 0, HashedFiles = result?.HashedFiles ?? 0,
            DuplicateGroups = result?.Groups.Length ?? 0, HiddenCopies = result?.HiddenCopies ?? 0,
            Unverified = result?.Unverified.Take( 10 ).ToArray(), UnverifiedCount = result?.Unverified.Length ?? 0,
            MatchingGroups = groups.Length,
            Groups = groups.Take( Math.Clamp( limit, 1, 100 ) ).Select( g => new { g.Kind,
                Asset = g.Sources[0].RelativePath, Retained = g.Sources[0].MountPath,
                Sources = g.Sources.Select( s => new { s.Title, s.WorkshopId, s.MountPath } ).ToArray() } ).ToArray(),
            Report = WorkshopDuplicateView.ReportPath
        };
    }

    /// <summary>Verify that only combined flat traversal is deduplicated, every game keeps its files, and a hidden model alias still loads.</summary>
    [McpTool( "workshop_mount_duplicate_view_test" )]
    public static object TestView()
    {
        var mount = Sandbox.Mounting.Directory.Get( "sbox_workshop" ) as SboxWorkshopMount;
        var result = WorkshopDuplicateView.Result ?? throw new InvalidOperationException( "Scan duplicates first." );
        if ( !WorkshopDuplicateView.Enabled || WorkshopAssetCache.MarkerEnabled ) throw new InvalidOperationException( "Enable duplicate hiding and disable the red marker first." );
        var search = new SearchWidget( null );
        try
        {
            int Count( string path, bool combined ) => search.Filter( new IndexedWorkshopLocation( mount, path, combined ), CancellationToken.None, true ).Count();
            string root = $"mount://{mount.Ident}";
            int all = Count( root, false );
            int combined = Count( root, true );
            var perGame = WorkshopGame.Games.Select( game => new
            {
                game.Title,
                Expected = mount.Resources.Count( r => r.Path.StartsWith( root + "/" + game.FolderName + "/", StringComparison.OrdinalIgnoreCase ) ),
                Actual = Count( root + "/" + game.FolderName, false )
            } ).ToArray();
            var modelGroup = result.Groups.FirstOrDefault( g => g.Kind == "Model" && g.Sources.Any( s => s.RelativePath.Contains( "ammobox_9mm" ) ) )
                ?? result.Groups.FirstOrDefault( g => g.Kind == "Model" );
            string alias = modelGroup?.Sources.Last().MountPath;
            bool aliasLoads = alias == null || Model.Load( alias )?.IsError == false;
            var test = new
            {
                Passed = all == mount.Resources.Count && combined == all - result.HiddenCopies && perGame.All( g => g.Expected == g.Actual ) && aliasLoads,
                RegisteredAssets = mount.Resources.Count, UnfilteredFlat = all, DeduplicatedFlat = combined,
                HiddenCopies = result.HiddenCopies, PerGame = perGame, HiddenModelAlias = alias, HiddenAliasStillLoads = aliasLoads
            };
            File.WriteAllText( Path.Combine( Path.GetDirectoryName( WorkshopDuplicateView.ReportPath ), "duplicate-view-test.json" ),
                JsonSerializer.Serialize( test, new JsonSerializerOptions { WriteIndented = true } ) );
            return test;
        }
        finally { search.Destroy(); }
    }
}
