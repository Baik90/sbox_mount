using System;
using System.Reflection;
using Sandbox.Mounting;

namespace SboxMount;

/// <summary>Editor bridge to the independently installed native mount.</summary>
public sealed class SboxWorkshopMount
{
    public BaseGameMount Source { get; }
    private static SboxWorkshopMount current;
    private Guid revision;
    private MountPathIndex index;
    private List<WorkshopDuplicateScanner.Input> inputs;
    private SboxWorkshopMount( BaseGameMount source ) { Source = source; }
    public static SboxWorkshopMount Get() => From( Sandbox.Mounting.Directory.Get( "sbox_workshop" ) );
    public static SboxWorkshopMount From( BaseGameMount source )
    {
        if ( source?.Ident != "sbox_workshop" || source.GetType().GetMethod( "GetAssetManifest" ) == null ) return null;
        if ( current?.Source != source ) current = new( source );
        return current;
    }
    public string Ident => Source.Ident;
    public bool IsMounted => Source.IsMounted;
    public bool IsInstalled => Source.IsInstalled;
    public string Title => Source.Title;
    public IReadOnlyCollection<ResourceLoader> Resources => Source.Resources;
    public IReadOnlyCollection<ResourceLoader> GetAll( ResourceType type ) => Source.GetAll( type );
    private object Read( string name ) => Source.GetType().GetProperty( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( Source )
        ?? Source.GetType().GetField( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( Source );
    internal Guid InventoryRevision => (Guid)Read( "InventoryRevision" );
    internal IEnumerable<object> SourceStats => (IEnumerable<object>)Read( "SourceStats" );
    internal IEnumerable<string> ScanErrors => (IEnumerable<string>)Read( "ScanErrors" );
    public int SceneCount => (int)Read( "SceneCount" );
    private void Sync()
    {
        if ( index != null && revision == InventoryRevision ) return;
        revision = InventoryRevision;
        index = new MountPathIndex( $"mount://{Ident}", Resources.Select( r => r.Path ) );
        var rows = (string[][])Source.GetType().GetMethod( "GetAssetManifest" ).Invoke( Source, null );
        inputs = rows.Select( r => new WorkshopDuplicateScanner.Input( r[0], r[1], r[2], r[3], r[4], r[5] ) ).ToList();
        WorkshopCatalog.Refresh();
        WorkshopDuplicateView.Restore( inputs.ToArray() );
    }
    internal MountPathIndex PathIndex { get { Sync(); return index; } }
    internal List<WorkshopDuplicateScanner.Input> DuplicateInputs { get { Sync(); return inputs; } }
    private string Invoke( string method, string ident, string path )
    {
        try { return (string)Source.GetType().GetMethod( method, BindingFlags.Instance | BindingFlags.NonPublic ).Invoke( Source, new object[] { ident, path } ); }
        catch ( TargetInvocationException e ) { throw e.InnerException ?? e; }
    }
    internal string Prepare( string ident, string path ) => Invoke( "Prepare", ident, path );
    internal string Reprepare( string ident, string path ) => Invoke( "Reprepare", ident, path );
}
