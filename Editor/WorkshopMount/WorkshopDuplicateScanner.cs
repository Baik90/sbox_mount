using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

#if WORKSHOP_MOUNT_PLUGIN
namespace SboxMount.Runtime;
#else
namespace SboxMount;
#endif

/// <summary>Conservative equality: original compiled bytes and all locally resolved RERL dependencies must match.</summary>
internal sealed class WorkshopDuplicateScanner
{
    internal sealed record Input( string MountPath, string WorkshopId, string Title, string Root, string RelativePath, string Kind );
    internal sealed record Stamp( string Path, long Length, long Modified );
    internal sealed record Group( string Fingerprint, string Kind, Input[] Sources );
    internal sealed class Result
    {
        public int Version { get; set; } = 3;
        public string[] Inventory { get; set; }
        public Stamp[] Files { get; set; }
        public string[] ExternalPaths { get; set; }
        public Group[] Groups { get; set; }
        public string[] Unverified { get; set; }
        public int Total { get; set; }
        public int HashedFiles { get; set; }
        public int HiddenCopies => Groups.Sum( g => g.Sources.Length - 1 );
    }

    private sealed record FileData( Stamp Stamp, string Hash, string[] References );
    private readonly Dictionary<string, FileData> files = new( StringComparer.OrdinalIgnoreCase );
    private readonly Dictionary<string, Stamp> stamps = new( StringComparer.OrdinalIgnoreCase );
    private readonly List<string> warnings = new();
    private readonly HashSet<string> externalPaths = new( StringComparer.OrdinalIgnoreCase );
    private readonly CancellationToken cancellation;
    private readonly Action<int, int> progress;

    internal WorkshopDuplicateScanner( CancellationToken cancellation, Action<int, int> progress = null )
    { this.cancellation = cancellation; this.progress = progress; }

    internal Result Scan( Input[] inventory )
    {
        var available = new List<(Input Item, Stamp Stamp)>();
        foreach ( var input in inventory )
        {
            cancellation.ThrowIfCancellationRequested();
            try { available.Add( (input, Stat( LocalPath( input.Root, input.RelativePath ) )) ); }
            catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or ArgumentException )
            { warnings.Add( input.MountPath + ": " + e.Message ); }
        }
        var candidates = available.GroupBy( x => (x.Item.Kind, x.Stamp.Length) ).Where( g => g.Count() > 1 ).SelectMany( g => g ).ToArray();
        var roots = new List<(Input Item, string Hash)>();
        int completed = 0;
        foreach ( var candidate in candidates )
        {
            cancellation.ThrowIfCancellationRequested();
            try { roots.Add( (candidate.Item, Read( candidate.Stamp.Path ).Hash) ); }
            catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException )
            { warnings.Add( candidate.Item.MountPath + ": " + e.Message ); }
            progress?.Invoke( ++completed, candidates.Length );
        }
        var groups = new List<Group>();
        foreach ( var rawGroup in roots.GroupBy( x => (x.Item.Kind, x.Hash) ).Where( g => g.Count() > 1 ) )
        {
            var verified = new List<(Input Item, string Fingerprint)>();
            foreach ( var item in rawGroup )
            {
                try { verified.Add( (item.Item, Fingerprint( item.Item )) ); }
                catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException )
                { warnings.Add( item.Item.MountPath + ": " + e.Message ); }
            }
            foreach ( var group in verified.GroupBy( x => x.Fingerprint ).Where( g => g.Count() > 1 ) )
                groups.Add( new Group( group.Key, rawGroup.Key.Kind, group.Select( x => x.Item )
                    .OrderBy( x => x.WorkshopId == "core" ? 0 : 1 ).ThenBy( x => x.WorkshopId, StringComparer.Ordinal ).ThenBy( x => x.MountPath, StringComparer.Ordinal ).ToArray() ) );
        }
        // A Steam update during the scan invalidates its result rather than hiding uncertain variants.
        if ( stamps.Values.Any( s => !IsCurrent( s ) ) || externalPaths.Any( File.Exists ) ) throw new IOException( "Workshop files changed during scanning. Please scan again after Steam finishes updating." );
        return new Result
        {
            Inventory = InventoryKeys( inventory ), Files = stamps.Values.ToArray(), ExternalPaths = externalPaths.ToArray(), Groups = groups.ToArray(),
            Unverified = warnings.ToArray(), Total = inventory.Length, HashedFiles = files.Count
        };
    }

    internal static string[] InventoryKeys( IEnumerable<Input> inputs ) => inputs.Select( i => i.MountPath + "|" + i.Root + "|" + i.RelativePath )
        .OrderBy( p => p, StringComparer.Ordinal ).ToArray();

    internal static bool IsCurrent( Stamp stamp )
    {
        var info = new FileInfo( stamp.Path );
        return info.Exists && info.Length == stamp.Length && info.LastWriteTimeUtc.Ticks == stamp.Modified;
    }

    private Stamp Stat( string path )
    {
        if ( stamps.TryGetValue( path, out var cached ) ) return cached;
        var info = new FileInfo( path );
        if ( !info.Exists ) throw new FileNotFoundException( "Asset is missing", path );
        var stamp = new Stamp( info.FullName, info.Length, info.LastWriteTimeUtc.Ticks );
        stamps.Add( path, stamp );
        return stamp;
    }

    private FileData Read( string path )
    {
        if ( files.TryGetValue( path, out var cached ) ) return cached;
        var stamp = Stat( path );
        using var stream = File.OpenRead( path );
        string hash = Convert.ToHexString( SHA256.HashData( stream ) );
        stream.Position = 0;
        string[] references = ReadReferences( stream );
        var data = new FileData( stamp, hash, references );
        files.Add( path, data );
        return data;
    }

    private string Fingerprint( Input input )
    {
        var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        var nodes = new SortedDictionary<string, string>( StringComparer.Ordinal );
        var pending = new Stack<string>();
        pending.Push( input.RelativePath );
        while ( pending.Count > 0 )
        {
            cancellation.ThrowIfCancellationRequested();
            string relative = pending.Pop().Replace( '\\', '/' ).ToLowerInvariant();
            if ( !visited.Add( relative ) ) continue;
            var node = Read( LocalPath( input.Root, relative ) );
            nodes[relative == input.RelativePath.ToLowerInvariant() ? "$root" : relative] = node.Hash;
            foreach ( var reference in node.References )
            {
                string dependency = reference.Replace( '\\', '/' ).ToLowerInvariant();
                if ( !dependency.EndsWith( "_c", StringComparison.Ordinal ) ) dependency += "_c";
                string local = LocalPath( input.Root, dependency );
                if ( File.Exists( local ) ) pending.Push( dependency );
                else
                {
                    externalPaths.Add( local );
                    nodes["$external/" + dependency] = "shared-engine-path";
                }
            }
        }
        var text = new StringBuilder();
        foreach ( var node in nodes ) text.Append( node.Key ).Append( '\0' ).Append( node.Value ).Append( '\n' );
        return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text.ToString() ) ) );
    }

    private static string LocalPath( string root, string relative )
    {
        relative = relative.Replace( '\\', '/' );
        if ( string.IsNullOrWhiteSpace( relative ) || relative.StartsWith( '/' ) || relative.Contains( ':' )
            || relative.Split( '/' ).Any( p => p is "." or ".." or ".bin" || p.Length == 0 ) ) throw new InvalidDataException( "Unsupported resource reference: " + relative );
        string fullRoot = Path.GetFullPath( root ).TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath( Path.Combine( fullRoot, relative ) );
        if ( !path.StartsWith( fullRoot, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidDataException( "Resource reference escapes its source." );
        return path;
    }

    private static string[] ReadReferences( Stream stream )
    {
        using var reader = new BinaryReader( stream, Encoding.UTF8, true );
        if ( stream.Length < 16 ) throw new InvalidDataException( "Truncated resource header" );
        reader.ReadUInt32();
        if ( reader.ReadUInt16() != 12 ) throw new InvalidDataException( "Resource header cannot be verified" );
        reader.ReadUInt16();
        long table = 8L + reader.ReadUInt32();
        uint count = reader.ReadUInt32();
        if ( count > 4096 || table + count * 12L > stream.Length ) throw new InvalidDataException( "Invalid resource block table" );
        for ( int i = 0; i < count; i++ )
        {
            stream.Position = table + i * 12L;
            string kind = Encoding.ASCII.GetString( reader.ReadBytes( 4 ) );
            long start = stream.Position + reader.ReadUInt32();
            uint length = reader.ReadUInt32();
            if ( kind != "RERL" ) continue;
            if ( length > 8 * 1024 * 1024 || start + length > stream.Length ) throw new InvalidDataException( "Invalid RERL block" );
            stream.Position = start;
            // Reuse the bounds-checked RERL parser without reading mesh/texture streaming payloads into memory.
            byte[] minimal = new byte[checked( 28 + (int)length )];
            BitConverter.GetBytes( (uint)minimal.Length ).CopyTo( minimal, 0 );
            BitConverter.GetBytes( (ushort)12 ).CopyTo( minimal, 4 );
            BitConverter.GetBytes( 8u ).CopyTo( minimal, 8 );
            BitConverter.GetBytes( 1u ).CopyTo( minimal, 12 );
            Encoding.ASCII.GetBytes( "RERL" ).CopyTo( minimal, 16 );
            BitConverter.GetBytes( 8u ).CopyTo( minimal, 20 );
            BitConverter.GetBytes( length ).CopyTo( minimal, 24 );
            stream.ReadExactly( minimal.AsSpan( 28 ) );
            return CompiledResource.Read( minimal ).References.Select( r => r.Path ).ToArray();
        }
        return Array.Empty<string>();
    }
}
