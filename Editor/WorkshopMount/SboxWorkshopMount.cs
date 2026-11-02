using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Sandbox.Mounting;
using Editor.Mcp;

namespace SboxMount;

/// <summary>Asset-only editor mount for all locally installed Steam Workshop subscriptions.</summary>
public sealed class SboxWorkshopMount : BaseGameMount
{
    public override string Ident => "sbox_workshop";
    public override string Title => "S&Box";
    private readonly Dictionary<string, WorkshopAssetCache> caches = new();
    private WorkshopGame[] mountedGames = Array.Empty<WorkshopGame>();
    internal readonly List<object> SourceStats = new();
    internal readonly List<string> ScanErrors = new();
    internal readonly List<WorkshopDuplicateScanner.Input> DuplicateInputs = new();
    internal Guid InventoryRevision { get; private set; }
    public int SceneCount { get; private set; }
    private MountPathIndex pathIndex;
    internal MountPathIndex PathIndex => pathIndex ??= new MountPathIndex( $"mount://{Ident}", Resources.Select( r => r.Path ) );

    protected override void Initialize( InitializeContext context )
    {
        IsInstalled = context.IsAppInstalled( 590830 );
    }

    protected override Task Mount( MountContext context )
    {
        SceneCount = 0;
        caches.Clear();
        SourceStats.Clear();
        ScanErrors.Clear();
        DuplicateInputs.Clear();
        InventoryRevision = Guid.NewGuid();
        mountedGames = WorkshopCatalog.Refresh();
        foreach ( var game in mountedGames )
        {
            if ( !System.IO.Directory.Exists( game.InstallPath ) ) continue;
            var cache = new WorkshopAssetCache( game );
            caches[game.WorkshopId] = cache;
            int models = 0, materials = 0, textures = 0, sounds = 0, scenes = 0, prefabs = 0;
            try
            {
            foreach ( string file in System.IO.Directory.EnumerateFiles( game.InstallPath, "*", SearchOption.AllDirectories ) )
            {
                string relative = Path.GetRelativePath( game.InstallPath, file ).Replace( '\\', '/' );
                if ( relative.StartsWith( ".bin/", StringComparison.OrdinalIgnoreCase ) ) continue;
                string extension = Path.GetExtension( relative ).ToLowerInvariant();
                var type = extension switch
                {
                    ".vmdl_c" => ResourceType.Model,
                    ".vmat_c" => ResourceType.Material,
                    ".vtex_c" => ResourceType.Texture,
                    ".vsnd_c" => ResourceType.Sound,
                    _ => ResourceType.None
                };
                if ( extension == ".scene_c" ) { SceneCount++; scenes++; continue; }
                if ( extension == ".prefab_c" ) { prefabs++; continue; }
                if ( type == ResourceType.None ) continue;
                string category = type switch { ResourceType.Model => "Models", ResourceType.Material => "Materials", ResourceType.Sound => "Sounds", _ => "Textures" };
                string path = relative[..^2];
                string display = path.StartsWith( category + "/", StringComparison.OrdinalIgnoreCase ) ? path[(category.Length + 1)..] : path;
                var loader = new NativeAssetLoader( cache, path, type );
                context.Add( type, $"{game.FolderName}/Assets/{category}/{display}", loader );
                DuplicateInputs.Add( new WorkshopDuplicateScanner.Input( loader.Path, game.WorkshopId, game.Title.Trim(), game.InstallPath, relative, type.ToString() ) );
                switch ( type ) { case ResourceType.Model: models++; break; case ResourceType.Material: materials++; break; case ResourceType.Texture: textures++; break; case ResourceType.Sound: sounds++; break; }
            }
            }
            catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException ) { ScanErrors.Add( $"{game.Title} ({game.WorkshopId}): {e.Message}" ); }
            SourceStats.Add( new { game.Ident, game.Title, game.WorkshopId, game.FolderName, Models = models, Materials = materials, Textures = textures, Sounds = sounds, ScenesDeferred = scenes, PrefabsDeferred = prefabs } );
        }
        pathIndex = new MountPathIndex( $"mount://{Ident}", Resources.Select( r => r.Path ) );
        WorkshopDuplicateView.Restore( DuplicateInputs.ToArray() );
        IsMounted = true;
        return Task.CompletedTask;
    }

    private WorkshopAssetCache GetCache( string identOrId )
    {
        if ( caches.TryGetValue( identOrId, out var cache ) ) return cache;
        var matches = mountedGames.Where( g => g.Ident == identOrId ).ToArray();
        if ( matches.Length == 1 ) return caches[matches[0].WorkshopId];
        throw new InvalidOperationException( $"Package {identOrId} is missing or ambiguous; use its Workshop ID." );
    }
    internal string Prepare( string ident, string path ) => GetCache( ident ).Prepare( path );

    internal string Reprepare( string ident, string path ) => GetCache( ident ).Reprepare( path );

    protected override void Shutdown() { caches.Clear(); pathIndex = null; IsMounted = false; }

    private sealed class NativeAssetLoader : ResourceLoader<SboxWorkshopMount>
    {
        private readonly WorkshopAssetCache cache;
        private readonly string source;
        private readonly ResourceType type;
        internal NativeAssetLoader( WorkshopAssetCache cache, string source, ResourceType type )
        { this.cache = cache; this.source = source; this.type = type; }

        protected override object Load()
        {
            string path = cache.Prepare( source );
            return type switch
            {
                ResourceType.Model => Model.Load( path ),
                ResourceType.Material => Material.Load( path ),
                ResourceType.Texture => Texture.Load( path ),
                ResourceType.Sound => SoundFile.Load( path ),
                _ => throw new NotSupportedException()
            };
        }
    }
}

[McpToolset( "workshop_mount", "Asset-only Workshop mount prototype: enable, inspect and test isolated resources." )]
public static class WorkshopMountTools
{
    private static Window browserWindow;
    private static bool storageStartupFailed;
    private static bool restoreAfterStartup = true;

    [EditorEvent.Frame]
    private static void InitializeStorageAfterRestart()
    {
        if ( storageStartupFailed || Project.Current == null ) return;
        try
        {
            if ( WorkshopAssetCache.CacheRoot == null )
            {
                WorkshopAssetCache.Attach( Project.Current.GetRootPath() );
                restoreAfterStartup = true;
            }
            if ( restoreAfterStartup && SceneEditorSession.Active?.Scene != null
                && Sandbox.Mounting.Directory.Get( "sbox_workshop" )?.IsMounted == true )
            {
                restoreAfterStartup = false;
                RepairFailedModels();
            }
        }
        catch ( Exception e ) { storageStartupFailed = true; Log.Error( e ); }
    }

    /// <summary>Retry only failed Workshop model references in the open scene. Preserves objects and their other component values; does not save the scene.</summary>
    [McpTool( "workshop_mount_repair_models" )]
    public static object RepairFailedModels()
    {
        int repaired = 0;
        var failed = new List<string>();
        var scene = SceneEditorSession.Active?.Scene;
        if ( scene == null ) return new { Repaired = 0, Failed = Array.Empty<string>() };
        foreach ( var renderer in scene.GetAllComponents<ModelRenderer>() )
        {
            if ( renderer.Model?.IsError != true ) continue;
            string path = renderer.Model.ResourcePath;
            var replacement = ResolveFailedModel( renderer.Model );
            if ( replacement == null ) continue;
            if ( replacement?.IsError != false ) { failed.Add( path ); continue; }
            renderer.Model = replacement;
            repaired++;
        }
        return new { Repaired = repaired, Failed = failed.ToArray() };
    }

    internal static Model ResolveFailedModel( Model failed )
    {
        string path = failed.ResourcePath;
        if ( path.StartsWith( "mount://sbox_workshop/", StringComparison.OrdinalIgnoreCase ) )
            return Model.Load( path );
        var game = WorkshopGame.Games.FirstOrDefault( g => path.StartsWith( g.Prefix, StringComparison.OrdinalIgnoreCase ) );
        if ( game == null ) return null;
        var mount = Sandbox.Mounting.Directory.Get( "sbox_workshop" ) as SboxWorkshopMount;
        path = mount.Prepare( game.Ident, path[game.Prefix.Length..] );
        // Saved scenes may contain the native cache path rather than mount://.
        // Drop a previously failed managed lookup and retry the corresponding native resource.
        var resources = typeof(Game).GetProperty( "Resources", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic ).GetValue( null );
        typeof(ResourceSystem).GetMethod( "Unregister", BindingFlags.Instance | BindingFlags.NonPublic )
            ?.Invoke( resources, new object[] { failed } );
        typeof(Model).Assembly.GetType( "NativeEngine.g_pResourceSystem", true )
            .GetMethod( "ReloadResource", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )
            .Invoke( null, new object[] { path } );
        return Model.Load( path );
    }

    /// <summary>Check folder/flat traversal through the browser's actual search filter for both games, including lowercase restored paths.</summary>
    [McpTool( "workshop_mount_browser_test" )]
    public static object BrowserTest()
    {
        var mount = Sandbox.Mounting.Directory.Get( "sbox_workshop" ) as SboxWorkshopMount;
        if ( mount?.IsMounted != true ) throw new InvalidOperationException( "Enable the mount first." );
        var search = new SearchWidget( null );
        try
        {
            var results = new List<object>();
            bool passed = true;
            foreach ( var game in WorkshopGame.Games )
            {
                string path = $"mount://{mount.Ident}/{game.FolderName}/Assets/Models";
                var location = new IndexedWorkshopLocation( mount, path.ToLowerInvariant() );
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var flat = search.Filter( location, System.Threading.CancellationToken.None, true ).Select( f => f.ToString() ).ToArray();
                long milliseconds = timer.ElapsedMilliseconds;
                int directFiles = search.Filter( location, System.Threading.CancellationToken.None, false ).Count();
                var directories = location.GetDirectories().Select( d => d.Name ).ToArray();
                int expected = mount.GetAll( ResourceType.Model ).Count( r => r.Path.StartsWith( path + "/", StringComparison.OrdinalIgnoreCase ) );
                passed &= flat.Length == expected && flat.Distinct( StringComparer.OrdinalIgnoreCase ).Count() == expected;
                results.Add( new { game.Ident, FlatFiles = flat.Length, Expected = expected, FolderFiles = directFiles, Folders = directories.Length, Milliseconds = milliseconds } );
            }
            return new { Passed = passed, IndexFolders = mount.PathIndex.FolderCount, Games = results };
        }
        finally { search.Destroy(); }
    }

    [Menu( "Editor", "S&Box Mount/Open Browser" )]
    public static async void OpenBrowser()
    {
        try
        {
            await Enable();
            if ( browserWindow.IsValid() )
            {
                if ( browserWindow.Canvas is not WorkshopMountBrowser )
                {
                    var oldBrowser = browserWindow.Canvas as AssetBrowser;
                    string oldPath = oldBrowser?.CurrentLocation?.Path;
                    bool recursive = oldBrowser?.ShowRecursiveFiles ?? false;
                    var replacement = new WorkshopMountBrowser( browserWindow );
                    browserWindow.Canvas = replacement;
                    if ( oldPath != null ) replacement.NavigateTo( oldPath );
                    replacement.ShowRecursiveFiles = recursive;
                    oldBrowser?.Destroy();
                }
                browserWindow.Show();
                return;
            }
            browserWindow = new Window { Title = "S&Box Workshop Assets", Size = new Vector2( 1100, 760 ), DeleteOnClose = true };
            browserWindow.Canvas = new WorkshopMountBrowser( browserWindow );
            browserWindow.Show();
        }
        catch ( Exception e ) { Log.Error( e ); }
    }

    /// <summary>Render a mounted model in a temporary preview world, without changing or saving the user's scene.</summary>
    [McpTool( "workshop_mount_preview" )]
    public static object Preview( string game = "conna.decay", string modelPath = "models/rust_animals/bear/bear.vmdl" )
    {
        var mount = Sandbox.Mounting.Directory.Get( "sbox_workshop" ) as SboxWorkshopMount;
        if ( mount?.IsMounted != true ) throw new InvalidOperationException( "Enable the mount first." );
        using var streaming = EditorUtility.DisableTextureStreaming();
        var model = Model.Load( mount.Prepare( game, modelPath ) );
        if ( model.IsError ) throw new InvalidOperationException( $"Could not load {modelPath}." );
        var bitmap = new Bitmap( 768, 768 );
        SceneUtility.RenderModelBitmap( model, bitmap );
        string directory = Path.Combine( Project.Current.GetRootPath(), ".sbox/mount-tests" );
        System.IO.Directory.CreateDirectory( directory );
        File.WriteAllBytes( Path.Combine( directory, game + "-preview.png" ), bitmap.ToPng() );
        return bitmap;
    }

    /// <summary>Enable all installed Steam subscriptions as assets, without loading their code. Scenes and prefabs remain deferred.</summary>
    [McpTool( "workshop_mount_enable" )]
    public static async Task<object> Enable()
    {
        string projectRoot = Project.Current.GetRootPath();
        WorkshopAssetCache.Attach( projectRoot );
        if ( Sandbox.Mounting.Directory.Get( "sbox_workshop" ) == null )
        {
            var add = typeof(Sandbox.Mounting.Directory).GetMethod( "AddAssembly", BindingFlags.Static | BindingFlags.NonPublic );
            if ( add == null ) throw new NotSupportedException( "This engine no longer exposes mount registration." );
            add.Invoke( null, new object[] { typeof(SboxWorkshopMount).Assembly } );
        }
        await EditorUtility.Mounting.SetMounted( "sbox_workshop", true );
        return Status();
    }

    /// <summary>Re-read Steam's subscribed items and rebuild the mount. No subscriptions or downloads are changed.</summary>
    [McpTool( "workshop_mount_refresh" )]
    public static async Task<object> Refresh()
    {
        await Enable();
        await EditorUtility.Mounting.Refresh( "sbox_workshop" );
        if ( browserWindow.IsValid() && browserWindow.Canvas is WorkshopMountBrowser browser ) browser.RefreshSubscriptionsView();
        return Status();
    }

    [Menu( "Editor", "S&Box Mount/Refresh Subscribed Content" )]
    public static async void RefreshMenu()
    {
        try { await Refresh(); Log.Info( "Subscribed Workshop content refreshed." ); }
        catch ( Exception e ) { Log.Error( e ); }
    }

    [Menu( "Editor", "S&Box Mount/Enable Prototype" )]
    public static async void EnableMenu()
    {
        try { await Enable(); Log.Info( "S&Box Workshop mount enabled. Open the Mounts asset browser." ); }
        catch ( Exception e ) { Log.Error( e ); }
    }

    [Menu( "Editor", "S&Box Mount/Run Isolation Test" )]
    public static void TestMenu()
    {
        try { Log.Info( System.Text.Json.JsonSerializer.Serialize( Test() ) ); }
        catch ( Exception e ) { Log.Error( e ); }
    }

    /// <summary>Read the current mount state, resource counts and existing foreign assemblies. Does not load packages.</summary>
    [McpTool.ReadOnly( "workshop_mount_status" )]
    public static object Status()
    {
        var mount = Sandbox.Mounting.Directory.Get( "sbox_workshop" ) as SboxWorkshopMount;
        return new
        {
            Mounted = mount?.IsMounted ?? false,
            Resources = mount?.Resources.Count ?? 0,
            Models = mount?.GetAll( ResourceType.Model ).Count ?? 0,
            Materials = mount?.GetAll( ResourceType.Material ).Count ?? 0,
            Textures = mount?.GetAll( ResourceType.Texture ).Count ?? 0,
            Sounds = mount?.GetAll( ResourceType.Sound ).Count ?? 0,
            ScenesDeferred = mount?.SceneCount ?? 0,
            Cache = WorkshopAssetCache.CacheRoot,
            RedBandageTestEnabled = WorkshopAssetCache.MarkerEnabled,
            DuplicateGroups = WorkshopDuplicateView.Result?.Groups.Length ?? 0,
            DuplicateCopiesInCombinedView = WorkshopDuplicateView.Result?.HiddenCopies ?? 0,
            HideDuplicatesInCombinedView = WorkshopDuplicateView.Enabled,
            Subscriptions = WorkshopCatalog.SubscriptionCount,
            Games = mount?.SourceStats.ToArray(),
            UnavailableSubscriptions = WorkshopCatalog.Unavailable,
            ScanErrors = mount?.ScanErrors.ToArray(),
            ForeignAssemblies = ForeignAssemblies()
        };
    }

    internal static void RefreshBrowserView()
    {
        if ( browserWindow.IsValid() && browserWindow.Canvas is WorkshopMountBrowser browser ) browser.UpdateAssetList();
    }

    internal static string[] ForeignAssemblies() => AppDomain.CurrentDomain.GetAssemblies()
        .Select( a => a.GetName().Name ).Where( n => n.StartsWith( "package.", StringComparison.OrdinalIgnoreCase )
            && !n.StartsWith( "package.local.", StringComparison.OrdinalIgnoreCase )
            && !n.Equals( "package.toolbase", StringComparison.OrdinalIgnoreCase ) ).Distinct().OrderBy( n => n ).ToArray();

    /// <summary>Load test models and both variants of a colliding texture. Returns native paths, material dependencies, cache identity and assembly changes. Does not modify a scene.</summary>
    [McpTool( "workshop_mount_test" )]
    public static object Test()
    {
        var mount = Sandbox.Mounting.Directory.Get( "sbox_workshop" ) as SboxWorkshopMount;
        if ( mount?.IsMounted != true ) throw new InvalidOperationException( "Call workshop_mount_enable first." );
        var before = ForeignAssemblies();
        var models = new List<object>();
        var textures = new List<Texture>();
        var bandages = new List<Model>();
        var loadedModels = new Dictionary<string, Model>();
        bool isolatedMaterials = true;
        const string texturePath = "materials/default/default_skin_diffusewarp_tga_4d3cb601.generated.vtex";
        foreach ( var game in WorkshopGame.TestGames )
        {
            foreach ( var original in new[] { "weapons/rust_bandage/v_bandage.vmdl", game.Ident == "conna.decay" ? "models/rust_animals/bear/bear.vmdl" : "models/legacy/zombie/zombie.vmdl" } )
            {
                string path = mount.Prepare( game.Ident, original );
                string display = original.StartsWith( "models/", StringComparison.OrdinalIgnoreCase ) ? original[7..] : original;
                var entry = mount.GetAll( ResourceType.Model ).Single( r => r.RelativePath.Equals( $"{game.FolderName}/Assets/Models/{display}", StringComparison.OrdinalIgnoreCase ) );
                var model = Model.Load( entry.Path );
                loadedModels[entry.Path] = model;
                if ( original.Contains( "v_bandage" ) ) bandages.Add( model );
                isolatedMaterials &= !model.IsError && model.Materials.All( m => m.Name.StartsWith( game.Prefix, StringComparison.Ordinal ) );
                models.Add( new { game.Ident, Original = original, MountPath = entry.Path, Path = path, model.IsValid, model.IsError,
                    Materials = model.Materials.Select( m => new { m.Name, m.IsValid, m.ShaderName, Texture = m.FirstTexture?.ResourcePath } ).ToArray() } );
            }
            textures.Add( Texture.Load( mount.Prepare( game.Ident, texturePath ) ) );
        }
        var after = ForeignAssemblies();
        bool stableModels = loadedModels.Reverse().All( pair => ReferenceEquals( pair.Value, Model.Load( pair.Key ) ) );
        bool stableTextures = WorkshopGame.TestGames.Reverse().All( g => ReferenceEquals(
            textures[Array.IndexOf( WorkshopGame.TestGames, g )], Texture.Load( mount.Prepare( g.Ident, texturePath ) ) ) );
        bool separateModels = !ReferenceEquals( bandages[0], bandages[1] );
        bool separateMaterials = !ReferenceEquals( bandages[0].Materials[0], bandages[1].Materials[0] );
        bool separateTextures = !ReferenceEquals( textures[0], textures[1] );
        var result = new
        {
            Passed = isolatedMaterials && separateModels && separateMaterials && separateTextures && stableModels && stableTextures
                && textures.All( t => t.IsValid && !t.IsError ) && after.Length == 0,
            Models = models,
            MaterialPathsAreIsolated = isolatedMaterials,
            SeparateBandageModels = separateModels,
            SeparateBandageMaterials = separateMaterials,
            Textures = textures.Select( t => new { t.ResourcePath, t.IsValid, t.IsError, t.IsLoaded, t.Width, t.Height } ).ToArray(),
            SeparateTextureObjects = separateTextures,
            StableModelsAfterReverseLoad = stableModels,
            StableTexturesAfterReverseLoad = stableTextures,
            ForeignAssembliesBefore = before,
            ForeignAssembliesAfter = after,
            NoForeignAssembliesLoaded = after.Length == 0,
            NoForeignAssembliesAdded = before.SequenceEqual( after )
        };
        string directory = Path.Combine( Project.Current.GetRootPath(), ".sbox/mount-tests" );
        System.IO.Directory.CreateDirectory( directory );
        File.WriteAllText( Path.Combine( directory, "prototype-runtime.json" ),
            System.Text.Json.JsonSerializer.Serialize( result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
        return result;
    }
}
