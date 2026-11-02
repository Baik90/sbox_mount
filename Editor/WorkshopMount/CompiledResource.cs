using System;
using System.IO;
using System.Text;

namespace SboxMount;

/// <summary>Rebases Source 2 v12 RERL and supported binary KV3 string tables.</summary>
internal static class CompiledResource
{
    internal sealed record Reference( ulong Id, string Path );
    internal sealed record ReferenceBlock( int Header, Reference[] References );

    internal static ReferenceBlock Read( byte[] bytes )
    {
        if ( bytes.Length < 16 || BitConverter.ToUInt16( bytes, 4 ) != 12 )
            throw new InvalidDataException( "Unsupported compiled resource header (expected Source 2 v12)." );
        int table = checked( 8 + (int)BitConverter.ToUInt32( bytes, 8 ) );
        int count = checked( (int)BitConverter.ToUInt32( bytes, 12 ) );
        Check( bytes, table, checked( count * 12 ) );
        for ( int i = 0; i < count; i++ )
        {
            int header = table + i * 12;
            if ( Encoding.ASCII.GetString( bytes, header, 4 ) != "RERL" ) continue;
            int start = checked( header + 4 + (int)BitConverter.ToUInt32( bytes, header + 4 ) );
            int size = checked( (int)BitConverter.ToUInt32( bytes, header + 8 ) );
            Check( bytes, start, size );
            if ( size < 8 ) throw new InvalidDataException( "Truncated RERL header." );
            int entries = checked( start + (int)BitConverter.ToUInt32( bytes, start ) );
            int entriesCount = checked( (int)BitConverter.ToUInt32( bytes, start + 4 ) );
            Check( bytes, entries, checked( entriesCount * 16 ) );
            if ( entries < start || (long)entries + entriesCount * 16L > start + size )
                throw new InvalidDataException( "RERL table is outside its block." );
            var refs = new Reference[entriesCount];
            for ( int j = 0; j < entriesCount; j++ )
            {
                int entry = entries + j * 16;
                int name = checked( entry + 8 + (int)BitConverter.ToInt64( bytes, entry + 8 ) );
                if ( name < start || name >= start + size ) throw new InvalidDataException( "Invalid RERL string offset." );
                int end = Array.IndexOf( bytes, (byte)0, name, start + size - name );
                if ( end < 0 ) throw new InvalidDataException( "Unterminated RERL string." );
                refs[j] = new Reference( BitConverter.ToUInt64( bytes, entry ), Encoding.UTF8.GetString( bytes, name, end - name ) );
            }
            return new ReferenceBlock( header, refs );
        }
        return new ReferenceBlock( -1, Array.Empty<Reference>() );
    }

    internal static byte[] Remap( byte[] original, ReferenceBlock block, Func<string, string> map, Func<string, ulong> hash, string self, string mappedSelf )
    {
        if ( block.Header < 0 ) return original;
        var paths = block.References.Select( r => map( r.Path ) ).ToArray();
        var replacements = block.References.Select( (r, i) => (r.Path, Mapped: paths[i]) )
            .Where( r => r.Path != r.Mapped ).ToDictionary( r => r.Path, r => r.Mapped, StringComparer.Ordinal );
        replacements[self] = mappedSelf;
        // Append replacements: unchanged block-relative offsets and streaming payload stay intact.
        using var stream = new MemoryStream();
        stream.Write( original );
        while ( stream.Position % 8 != 0 ) stream.WriteByte( 0 );
        int start = checked( (int)stream.Position );
        using var writer = new BinaryWriter( stream, Encoding.UTF8, true );
        writer.Write( 8u );
        writer.Write( (uint)paths.Length );
        long strings = 8 + paths.Length * 16L;
        for ( int i = 0; i < paths.Length; i++ )
        {
            writer.Write( paths[i] == block.References[i].Path ? block.References[i].Id : hash( paths[i] ) );
            writer.Write( strings - (8 + i * 16L + 8) );
            strings += Encoding.UTF8.GetByteCount( paths[i] ) + 1;
        }
        foreach ( var path in paths ) { writer.Write( Encoding.UTF8.GetBytes( path ) ); writer.Write( (byte)0 ); }
        int size = checked( (int)stream.Position - start );
        stream.Position = block.Header + 4;
        writer.Write( checked( (uint)(start - block.Header - 4) ) );
        writer.Write( (uint)size );
        int table = 8 + (int)BitConverter.ToUInt32( original, 8 );
        int count = (int)BitConverter.ToUInt32( original, 12 );
        for ( int i = 0; i < count; i++ )
        {
            int header = table + i * 12;
            if ( header == block.Header ) continue;
            int data = checked( header + 4 + (int)BitConverter.ToUInt32( original, header + 4 ) );
            int length = checked( (int)BitConverter.ToUInt32( original, header + 8 ) );
            Check( original, data, length );
            if ( length < 4 || (BitConverter.ToUInt32( original, data ) & 0xffffff00) != 0x4b563300 ) continue;
            byte[] changed = BinaryKv3.Rebase( original.AsSpan( data, length ).ToArray(), replacements );
            stream.Position = stream.Length;
            while ( stream.Position % 8 != 0 ) stream.WriteByte( 0 );
            int changedStart = checked( (int)stream.Position );
            writer.Write( changed );
            stream.Position = header + 4;
            writer.Write( checked( (uint)(changedStart - header - 4) ) );
            writer.Write( (uint)changed.Length );
        }
        stream.Position = 0;
        writer.Write( checked( (uint)stream.Length ) );
        return stream.ToArray();
    }

    private static void Check( byte[] bytes, int offset, int size )
    {
        if ( offset < 0 || size < 0 || (long)offset + size > bytes.Length )
            throw new InvalidDataException( "Compiled resource block is outside the file." );
    }
}
