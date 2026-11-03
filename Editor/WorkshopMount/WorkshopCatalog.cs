using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

#if WORKSHOP_MOUNT_PLUGIN
namespace SboxMount.Runtime;
#else
namespace SboxMount;
#endif

internal sealed record WorkshopGame( string Ident, string Title, string WorkshopId, string Directory = null, string Folder = null, string Type = null )
{
    internal static readonly WorkshopGame[] TestGames =
    {
        new( "conna.decay", "Decay", "3829436870" ),
        new( "redfive.rustlegacy", "RustLegacy", "3828567418" )
    };
    internal static WorkshopGame[] Games => WorkshopCatalog.Games;
    internal static string EngineRoot => Path.GetFullPath( Path.Combine( Path.GetDirectoryName( typeof(Model).Assembly.Location ), "../.." ) );
    internal static string ContentRoot => Environment.GetEnvironmentVariable( "SBOX_WORKSHOP_CONTENT_ROOT" )
        ?? Path.GetFullPath( Path.Combine( EngineRoot, "../../workshop/content/590830" ) );
    internal string InstallPath => Directory ?? Path.Combine( ContentRoot, WorkshopId );
    internal string FolderName => Folder ?? WorkshopIdentity.FolderName( Title, WorkshopId );
    // Preserve the existing two prototype namespaces for already saved scenes.
    internal string Prefix => TestGames.Any( g => g.WorkshopId == WorkshopId )
        ? $"sbox_workshop/v3/{TestGames.Single( g => g.WorkshopId == WorkshopId ).Ident}/"
        : $"sbox_workshop/v3/workshop_{WorkshopId}/";
}

internal static class WorkshopIdentity
{
    internal static string FolderName( string title, string id )
    {
        string folder = new string( (title ?? "").Where( c => char.IsLetterOrDigit( c ) || c is ' ' or '_' or '-' ).ToArray() ).Trim();
        if ( folder.Length > 100 ) folder = folder[..100].TrimEnd();
        return folder.Length > 0 ? folder : "Workshop " + id;
    }
}

/// <summary>Reads Steam's actual subscription set and installation locations. No package installation or assembly loading.</summary>
internal static class WorkshopCatalog
{
    internal static WorkshopGame[] Games { get; private set; } = Array.Empty<WorkshopGame>();
    internal static string[] Unavailable { get; private set; } = Array.Empty<string>();
    internal static int SubscriptionCount { get; private set; }

    internal static WorkshopGame[] Refresh()
    {
        var service = typeof(Model).Assembly.GetType( "Sandbox.Services.Ugc", true );
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var getIds = service.GetMethod( "LocalItemIds", flags )
            ?? throw new NotSupportedException( "This engine no longer exposes local Workshop subscriptions." );
        var getItem = service.GetMethod( "GetItem", flags )
            ?? throw new NotSupportedException( "This engine no longer exposes Workshop installation information." );
        // true = subscribedOnly, confirmed against this installed engine's parameter metadata.
        var ids = getIds.Invoke( null, new object[] { true } ) as ulong[]
            ?? throw new InvalidOperationException( "Steam did not return a subscription list." );
        SubscriptionCount = ids.Length;
        var games = new List<WorkshopGame>();
        var unavailable = new List<string>();
        foreach ( ulong id in ids.Distinct().OrderBy( id => id ) )
        {
            string textId = id.ToString( CultureInfo.InvariantCulture );
            try
            {
                object item = getItem.Invoke( null, new object[] { id } );
                var itemType = item?.GetType();
                bool installed = itemType?.GetProperty( "IsInstalled" )?.GetValue( item ) is true;
                string root = itemType?.GetProperty( "InstallFolder" )?.GetValue( item ) as string;
                string overrideRoot = Environment.GetEnvironmentVariable( "SBOX_WORKSHOP_CONTENT_ROOT" );
                if ( !string.IsNullOrWhiteSpace( overrideRoot ) ) root = Path.Combine( overrideRoot, textId );
                if ( !installed || string.IsNullOrWhiteSpace( root ) || !System.IO.Directory.Exists( root ) )
                {
                    unavailable.Add( $"{textId}: subscribed but not installed; download it through Steam first." );
                    continue;
                }
                string ident = "workshop." + textId;
                string title = "Workshop " + textId;
                string type = null;
                string metadata = new[] { "metadata.json", "item.json" }.Select( name => Path.Combine( root, name ) ).FirstOrDefault( File.Exists );
                if ( metadata != null )
                {
                    try
                    {
                    using var document = JsonDocument.Parse( File.ReadAllText( metadata ) );
                    string Text( string key ) => document.RootElement.TryGetProperty( key, out var value ) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    ident = Text( "PackageIdent" ) ?? ident;
                    title = Text( "Title" ) ?? title;
                    type = Text( "TypeName" );
                    }
                    catch ( Exception e ) when ( e is JsonException or IOException )
                    { unavailable.Add( $"{textId}: metadata could not be read, using the Workshop ID as folder name: {e.Message}" ); }
                }
                games.Add( new WorkshopGame( ident, title, textId, Path.GetFullPath( root ), Type: type ) );
            }
            catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException or ArgumentException )
            { unavailable.Add( $"{textId}: {e.Message}" ); }
        }
        var duplicates = games.GroupBy( g => g.FolderName, StringComparer.OrdinalIgnoreCase )
            .Where( group => group.Count() > 1 ).Select( group => group.Key ).ToHashSet( StringComparer.OrdinalIgnoreCase );
        Games = games.Select( g => duplicates.Contains( g.FolderName ) ? g with { Folder = $"{g.FolderName} ({g.WorkshopId})" } : g )
            .OrderBy( g => g.FolderName, StringComparer.OrdinalIgnoreCase ).ToArray();
        Unavailable = unavailable.ToArray();
        return Games;
    }
}
