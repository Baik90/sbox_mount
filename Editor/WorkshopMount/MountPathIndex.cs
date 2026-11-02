using System;
using System.Collections.Generic;
using System.Linq;

namespace SboxMount;

/// <summary>Immutable-after-construction directory index. Traversal never rescans the mount's resource list.</summary>
internal sealed class MountPathIndex
{
    internal sealed class Folder
    {
        internal string Path { get; }
        internal Dictionary<string, Folder> Children { get; } = new( StringComparer.OrdinalIgnoreCase );
        internal List<string> Files { get; } = new();
        internal Folder( string path ) { Path = path; }
    }

    private readonly Dictionary<string, Folder> folders = new( StringComparer.OrdinalIgnoreCase );
    internal Folder Root { get; }
    internal int FolderCount => folders.Count;
    internal int FileCount { get; private set; }

    internal MountPathIndex( string root, IEnumerable<string> paths )
    {
        Root = new Folder( root.TrimEnd( '/' ) );
        folders.Add( Root.Path, Root );
        string prefix = Root.Path + "/";
        var unique = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        foreach ( string path in paths )
        {
            if ( !path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) || !unique.Add( path ) ) continue;
            string[] parts = path[prefix.Length..].Split( '/' );
            var folder = Root;
            for ( int i = 0; i < parts.Length - 1; i++ )
            {
                if ( !folder.Children.TryGetValue( parts[i], out var child ) )
                {
                    child = new Folder( folder.Path + "/" + parts[i] );
                    folder.Children.Add( parts[i], child );
                    folders.Add( child.Path, child );
                }
                folder = child;
            }
            folder.Files.Add( path );
            FileCount++;
        }
    }

    internal Folder Find( string path ) => folders.GetValueOrDefault( path.TrimEnd( '/' ) );
}
