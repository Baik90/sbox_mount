using System;
using System.IO;
using System.Reflection;

#if WORKSHOP_MOUNT_PLUGIN
namespace SboxMount.Runtime;
#else
namespace SboxMount;
#endif

/// <summary>Local, lazy asset cache. Never installs packages or opens .bin archives.</summary>
internal sealed class WorkshopAssetCache
{
    internal const string BandageColorTexture = "weapons/rust_bandage/textures/bandages_bc_tga_9436aadb.generated.vtex";
    internal static string MarkerTexturePath => Path.Combine( CacheRoot, "visual-test/decay-bandage-red.vtex_c" );
    internal static string MarkerFlagPath => Path.Combine( CacheRoot, "visual-test/enabled.txt" );
    internal static bool MarkerEnabled => CacheRoot != null && File.Exists( MarkerFlagPath );
    private readonly WorkshopGame game;
    private readonly HashSet<string> prepared = new( StringComparer.OrdinalIgnoreCase );
    internal static string CacheRoot { get; private set; }
    private static readonly Func<string, ulong> ResourceHash = typeof(Model).Assembly.GetType( "Sandbox.Resources.ResourceWriter", true )
        .GetMethod( "MurmurHash64", BindingFlags.Static | BindingFlags.NonPublic )?.CreateDelegate<Func<string, ulong>>()
        ?? throw new NotSupportedException( "This engine no longer exposes the native resource hash." );

    internal WorkshopAssetCache( WorkshopGame game ) { this.game = game; }

    internal string Reprepare( string path )
    {
        prepared.Remove( Normalize( path ) + "_c" );
        return Prepare( path );
    }

    internal static void Attach( string projectRoot )
    {
        string root = Path.Combine( WorkshopGame.EngineRoot, "mount/sbox_workshop/Assets" );
        if ( CacheRoot == root ) return;
        System.IO.Directory.CreateDirectory( root );
        CacheRoot = root;
    }

    internal string Prepare( string resourcePath )
    {
        // Mounts are also activated by project settings/browser controls after restart,
        // without passing through WorkshopMountTools.Enable. Native loads must be self-sufficient.
        if ( CacheRoot == null ) Attach( null );
        string path = Normalize( resourcePath );
        string compiled = path.EndsWith( "_c", StringComparison.Ordinal ) ? path : path + "_c";
        string nativePath = game.Prefix + (path.EndsWith( "_c", StringComparison.Ordinal ) ? path[..^2] : path);
        if ( prepared.Contains( compiled ) ) return nativePath;
        string source = Within( game.InstallPath, compiled );
        if ( !File.Exists( source ) ) throw new FileNotFoundException( $"{game.Title}: missing asset {compiled}", source );
        prepared.Add( compiled );
        try
        {
            byte[] bytes = File.ReadAllBytes( source );
            if ( game.Ident == "conna.decay" && compiled == BandageColorTexture + "_c" && MarkerEnabled )
                bytes = File.ReadAllBytes( MarkerTexturePath );
            var references = CompiledResource.Read( bytes );
            bytes = CompiledResource.Remap( bytes, references, reference =>
            {
                string dependency = Normalize( reference );
                // Engine dependencies stay in the engine. Only this game's own files are rebased.
                if ( !File.Exists( Within( game.InstallPath, dependency.EndsWith( "_c", StringComparison.Ordinal ) ? dependency : dependency + "_c" ) ) ) return reference;
                return Prepare( dependency );
            }, ResourceHash, path.EndsWith( "_c", StringComparison.Ordinal ) ? path[..^2] : path, nativePath );
            string destination = Within( CacheRoot, game.Prefix + compiled );
            System.IO.Directory.CreateDirectory( Path.GetDirectoryName( destination ) );
            File.WriteAllBytes( destination, bytes );
            return nativePath;
        }
        catch { prepared.Remove( compiled ); throw; }
    }

    internal static string Normalize( string path )
    {
        path = path.Replace( '\\', '/' ).ToLowerInvariant();
        if ( string.IsNullOrWhiteSpace( path ) || path.StartsWith( '/' ) || path.Contains( ':' )
            || path.Split( '/' ).Any( p => p is ".." or "." or ".bin" || p.Length == 0 ) )
            throw new InvalidDataException( $"Unsafe asset path: {path}" );
        return path;
    }

    private static string Within( string root, string relative )
    {
        string fullRoot = Path.GetFullPath( root ).TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath( Path.Combine( fullRoot, Normalize( relative ) ) );
        if ( !full.StartsWith( fullRoot, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidDataException( "Asset path escapes its root." );
        return full;
    }
}
