using System.Collections.Generic;
using System.IO;

namespace SboxMount;

/// <summary>Uses an indexed location while retaining the engine's mount toolbar, filtering and thumbnails.</summary>
public sealed class WorkshopMountBrowser : MountsAssetBrowser
{
    public WorkshopMountBrowser( Widget parent ) : base( parent, null ) { }

    public void RefreshSubscriptionsView()
    {
        (AssetLocations as MountsAssetLocations)?.SetFilter( "" );
        UpdateAssetList();
    }

    public override void UpdateAssetList()
    {
        if ( CurrentLocation is MountLocation location && SboxWorkshopMount.From( location.Source ) is { } mount )
            CurrentLocation = new IndexedWorkshopLocation( mount, location.Path,
                location.Path.TrimEnd( '/' ).Equals( $"mount://{mount.Ident}", System.StringComparison.OrdinalIgnoreCase )
                && (ShowRecursiveFiles || !Search.IsEmpty)
                && WorkshopDuplicateView.Enabled && !WorkshopAssetCache.MarkerEnabled );
        base.UpdateAssetList();
    }
}

internal sealed record IndexedWorkshopLocation : MountLocation
{
    private readonly SboxWorkshopMount mount;
    private readonly bool combinedView;

    internal IndexedWorkshopLocation( SboxWorkshopMount mount, string path, bool combinedView = false ) : base( mount.Source, path )
    { this.mount = mount; this.combinedView = combinedView; }

    public override IEnumerable<AssetBrowser.Location> GetDirectories()
    {
        var folder = mount.PathIndex.Find( Path );
        if ( folder == null ) yield break;
        foreach ( var child in folder.Children.Values )
            yield return new IndexedWorkshopLocation( mount, child.Path, combinedView );
    }

    public override IEnumerable<FileInfo> GetFiles()
    {
        var folder = mount.PathIndex.Find( Path );
        if ( folder == null ) yield break;
        foreach ( string file in folder.Files )
            if ( !combinedView || !WorkshopDuplicateView.IsHidden( file ) ) yield return new FileInfo( file );
    }
}
