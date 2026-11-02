using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Editor.Mcp;

namespace SboxMount;

[McpToolset( "workshop_mount", "Asset-only Workshop mount tests." )]
public static class WorkshopVisualTest
{
    private const string BandageModel = "weapons/rust_bandage/v_bandage.vmdl";

    [Menu( "Editor", "S&Box Mount/Enable Red Bandage Test" )]
    public static void EnableMenu()
    {
        try { SetMarker( true ); Log.Info( "Visual test enabled: Decay bandage is red; RustLegacy keeps its original texture." ); }
        catch ( Exception e ) { Log.Error( e ); }
    }

    [Menu( "Editor", "S&Box Mount/Disable Red Bandage Test" )]
    public static void DisableMenu()
    {
        try { SetMarker( false ); Log.Info( "Visual test disabled: Decay's original bandage texture restored from Workshop." ); }
        catch ( Exception e ) { Log.Error( e ); }
    }

    /// <summary>Enable/disable a solid red albedo in Decay's prepared bandage texture only. Writes only the local test cache; keeps Workshop originals and RustLegacy untouched. Persists until disabled.</summary>
    [McpTool( "workshop_mount_visual_marker" )]
    public static object SetMarker( bool enabled = true )
    {
        var mount = GetMount();
        WorkshopAssetCache.Attach( Project.Current.GetRootPath() );
        var before = SourceHashes();
        var otherGame = WorkshopGame.TestGames.Single( g => g.Ident == "redfive.rustlegacy" );
        string otherPath = mount.Prepare( otherGame.Ident, WorkshopAssetCache.BandageColorTexture );
        string otherFile = Path.Combine( WorkshopAssetCache.CacheRoot, otherPath + "_c" );
        string otherBefore = Hash( otherFile );
        if ( enabled )
        {
            System.IO.Directory.CreateDirectory( Path.GetDirectoryName( WorkshopAssetCache.MarkerTexturePath ) );
            byte[] rgba = Enumerable.Range( 0, 16 ).SelectMany( _ => new byte[] { 255, 12, 12, 255 } ).ToArray();
            using var texture = Texture.Create( 4, 4, ImageFormat.RGBA8888 ).WithData( rgba ).Finish();
            File.WriteAllBytes( WorkshopAssetCache.MarkerTexturePath, texture.SaveToVtex() );
            File.WriteAllText( WorkshopAssetCache.MarkerFlagPath, "Decay bandage albedo only. Disable with S&Box Mount/Disable Red Bandage Test." );
        }
        else
        {
            File.Delete( WorkshopAssetCache.MarkerFlagPath );
        }
        string native = mount.Reprepare( "conna.decay", WorkshopAssetCache.BandageColorTexture );
        Reload( native );
        WorkshopMountTools.RefreshBrowserView();
        return new
        {
            Enabled = WorkshopAssetCache.MarkerEnabled,
            DecayTexture = native,
            RustLegacyTexture = otherPath,
            DecayCacheMatchesWorkshop = Hash( Path.Combine( WorkshopAssetCache.CacheRoot, native + "_c" ) )
                == Hash( Path.Combine( WorkshopGame.TestGames.Single( g => g.Ident == "conna.decay" ).InstallPath, WorkshopAssetCache.BandageColorTexture + "_c" ) ),
            WorkshopSourcesUnchanged = before.SequenceEqual( SourceHashes() ),
            RustLegacyCacheUnchanged = otherBefore == Hash( otherFile )
        };
    }

    /// <summary>Render both actual mounted bandage models, reload in reverse order, and compare the rendered pixels. Writes labeled-by-filename PNGs and a JSON result to .sbox/mount-tests/visual-marker. Does not change the editor scene.</summary>
    [McpTool( "workshop_mount_visual_test" )]
    public static object Test()
    {
        var mount = GetMount();
        if ( !WorkshopAssetCache.MarkerEnabled ) throw new InvalidOperationException( "Enable the red marker first." );
        using var streaming = EditorUtility.DisableTextureStreaming();
        string directory = Path.Combine( Project.Current.GetRootPath(), ".sbox/mount-tests/visual-marker" );
        System.IO.Directory.CreateDirectory( directory );
        var before = SourceHashes();
        var models = new Dictionary<string, Model>();
        var images = new Dictionary<string, byte[]>();
        var pixels = new Dictionary<string, Color[]>();
        var redCounts = new Dictionary<string, int>();
        using var comparison = new Bitmap( 1536, 768 );
        comparison.Clear( new Color( 0.05f, 0.06f, 0.08f ) );
        int panel = 0;
        foreach ( var game in WorkshopGame.TestGames )
        {
            var model = LoadBandage( mount, game );
            models.Add( game.Ident, model );
            using var bitmap = new Bitmap( 768, 768 );
            SceneUtility.RenderModelBitmap( model, bitmap );
            images.Add( game.Ident, bitmap.ToPng() );
            pixels.Add( game.Ident, bitmap.GetPixels().ToArray() );
            redCounts.Add( game.Ident, RedPixels( pixels[game.Ident] ) );
            comparison.DrawBitmap( bitmap, new Rect( panel++ * 768, 0, 768, 768 ) );
            File.WriteAllBytes( Path.Combine( directory, game.Ident + ".png" ), images[game.Ident] );
        }
        File.WriteAllBytes( Path.Combine( directory, "comparison-decay-left-rustlegacy-right.png" ), comparison.ToPng() );
        bool sameObjects = true;
        bool exactImages = true;
        bool stableRendering = true;
        var reverseChecks = new List<object>();
        foreach ( var game in WorkshopGame.TestGames.Reverse() )
        {
            var model = LoadBandage( mount, game );
            sameObjects &= ReferenceEquals( model, models[game.Ident] );
            using var bitmap = new Bitmap( 768, 768 );
            SceneUtility.RenderModelBitmap( model, bitmap );
            byte[] bytes = bitmap.ToPng();
            exactImages &= images[game.Ident].SequenceEqual( bytes );
            var afterPixels = bitmap.GetPixels().ToArray();
            var firstPixels = pixels[game.Ident];
            double meanDelta = firstPixels.Zip( afterPixels, (a, b) =>
                Math.Abs( a.r - b.r ) + Math.Abs( a.g - b.g ) + Math.Abs( a.b - b.b ) ).Sum( x => (double)x ) * 255.0 / (firstPixels.Length * 3.0);
            // Thumbnail antialiasing/render noise can change bytes between frames.
            // Compare actual colors with a small tolerance rather than requiring identical PNGs.
            int reverseRed = RedPixels( afterPixels );
            bool expectedColor = game.Ident == "conna.decay" ? reverseRed > 1000 : reverseRed < redCounts["conna.decay"] / 10;
            stableRendering &= meanDelta <= 1.0 && expectedColor;
            reverseChecks.Add( new { game.Ident, RedPixels = reverseRed, MeanChannelDelta = meanDelta, ExpectedColor = expectedColor } );
            File.WriteAllBytes( Path.Combine( directory, game.Ident + "-reverse.png" ), bytes );
        }
        var red = Texture.Load( mount.Prepare( "conna.decay", WorkshopAssetCache.BandageColorTexture ) );
        var original = Texture.Load( mount.Prepare( "redfive.rustlegacy", WorkshopAssetCache.BandageColorTexture ) );
        bool sourcesUnchanged = before.SequenceEqual( SourceHashes() );
        bool colorSeparation = redCounts["conna.decay"] > 1000 && redCounts["redfive.rustlegacy"] < redCounts["conna.decay"] / 10;
        bool noCode = WorkshopMountTools.ForeignAssemblies().Length == 0;
        bool separateModels = !ReferenceEquals( models.Values.First(), models.Values.Last() );
        bool separateMaterials = !ReferenceEquals( models.Values.First().Materials[0], models.Values.Last().Materials[0] );
        var result = new
        {
            Passed = separateModels && separateMaterials && colorSeparation && stableRendering && sameObjects && sourcesUnchanged && noCode && !red.IsError && !original.IsError,
            MarkerEnabled = true,
            SeparateModels = separateModels,
            SeparateMaterials = separateMaterials,
            ImagesDiffer = !images.Values.First().SequenceEqual( images.Values.Last() ),
            SameModelsAfterReverseLoad = sameObjects,
            ExactImagesAfterReverseLoad = exactImages,
            StableAppearanceAfterReverseLoad = stableRendering,
            ColorSeparation = colorSeparation,
            RedPixels = redCounts,
            ReverseChecks = reverseChecks,
            Textures = new[] { red, original }.Select( t => new { t.ResourcePath, t.Width, t.Height, t.IsError } ).ToArray(),
            WorkshopSourcesUnchanged = sourcesUnchanged,
            NoForeignAssembliesLoaded = noCode,
            Directory = directory
        };
        File.WriteAllText( Path.Combine( directory, "result.json" ), System.Text.Json.JsonSerializer.Serialize( result,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
        return result;
    }

    private static int RedPixels( Color[] pixels ) => pixels.Count( p => p.r > 0.35f && p.r > p.g * 2 && p.r > p.b * 2 );

    private static Model LoadBandage( SboxWorkshopMount mount, WorkshopGame game )
    {
        string path = $"mount://{mount.Ident}/{game.FolderName}/Assets/Models/{BandageModel}";
        var model = Model.Load( path );
        if ( model?.IsError != false ) throw new InvalidOperationException( $"Could not load {path}." );
        return model;
    }

    private static SboxWorkshopMount GetMount() => Sandbox.Mounting.Directory.Get( "sbox_workshop" ) is SboxWorkshopMount { IsMounted: true } mount
        ? mount : throw new InvalidOperationException( "Open the S&Box Mount browser first." );

    private static string Hash( string path ) => Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( path ) ) );
    private static string[] SourceHashes() => WorkshopGame.TestGames.SelectMany( g => new[]
    {
        Hash( Path.Combine( g.InstallPath, BandageModel + "_c" ) ),
        Hash( Path.Combine( g.InstallPath, "weapons/rust_bandage/rust_bandage.vmat_c" ) ),
        Hash( Path.Combine( g.InstallPath, WorkshopAssetCache.BandageColorTexture + "_c" ) )
    } ).ToArray();

    private static void Reload( string path ) => typeof(Model).Assembly.GetType( "NativeEngine.g_pResourceSystem", true )
        .GetMethod( "ReloadResource", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )
        .Invoke( null, new object[] { path } );
}
