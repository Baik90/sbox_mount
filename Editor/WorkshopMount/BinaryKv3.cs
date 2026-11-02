using System;
using System.IO;
using System.Text;

namespace SboxMount;

/// <summary>
/// Narrow KV3 v2-v4 string-table rebaser. Does not deserialize or instantiate any game objects.
/// Header layout reference: ValveResourceFormat/Resource/ResourceTypes/BinaryKV3.cs (MIT).
/// Unsupported encodings fail rather than silently using global dependency paths.
/// </summary>
internal static class BinaryKv3
{
    internal static byte[] Rebase( byte[] block, IReadOnlyDictionary<string, string> paths )
    {
        int version = block[0];
        if ( version is < 2 or > 4 ) throw new NotSupportedException( $"KV3 v{version} is not supported by the prototype." );
        int header = version >= 4 ? 72 : 64;
        if ( block.Length < header ) throw new InvalidDataException( "Truncated KV3 header." );
        int compression = BitConverter.ToInt32( block, 20 );
        if ( compression is not (0 or 1) ) throw new NotSupportedException( $"KV3 compression {compression} is not supported." );
        if ( BitConverter.ToUInt16( block, 24 ) != 0 ) throw new NotSupportedException( "KV3 dictionary compression is not supported." );
        int rawLength = BitConverter.ToInt32( block, 48 );
        int storedLength = BitConverter.ToInt32( block, 52 );
        if ( rawLength < 0 || rawLength > 128 * 1024 * 1024 || storedLength < 0 || (long)header + storedLength > block.Length )
            throw new InvalidDataException( "Invalid KV3 buffer length." );
        byte[] raw = compression == 0 ? block.AsSpan( header, rawLength ).ToArray()
            : DecodeLz4( block.AsSpan( header, storedLength ), rawLength );
        int byteCount = BitConverter.ToInt32( block, 28 );
        int words = BitConverter.ToInt32( block, 32 );
        int longs = BitConverter.ToInt32( block, 36 );
        int shortCount = version >= 4 ? BitConverter.ToInt32( block, 64 ) : 0;
        int wordStart = byteCount;
        if ( shortCount > 0 ) wordStart = checked( Align( wordStart, 2 ) + shortCount * 2 );
        if ( words > 0 ) wordStart = Align( wordStart, 4 );
        if ( wordStart < 0 || words < 1 || wordStart > raw.Length - 4 || longs < 0 ) throw new InvalidDataException( "Invalid KV3 lanes." );
        int stringCount = BitConverter.ToInt32( raw, wordStart );
        int stringsStart = checked( Align( checked( wordStart + words * 4 ), 8 ) + longs * 8 );
        if ( stringsStart < 0 || stringsStart > raw.Length || stringCount < 0 || stringCount > raw.Length )
            throw new InvalidDataException( "Invalid KV3 string table." );
        int cursor = stringsStart;
        bool anyChanged = false;
        using var output = new MemoryStream();
        output.Write( raw, 0, stringsStart );
        for ( int i = 0; i < stringCount; i++ )
        {
            int end = Array.IndexOf( raw, (byte)0, cursor );
            if ( end < 0 ) throw new InvalidDataException( "Unterminated KV3 string." );
            string value = Encoding.UTF8.GetString( raw, cursor, end - cursor );
            string replacement = paths.TryGetValue( value, out var mapped ) ? mapped : value;
            anyChanged |= replacement != value;
            output.Write( Encoding.UTF8.GetBytes( replacement ) );
            output.WriteByte( 0 );
            cursor = end + 1;
        }
        if ( !anyChanged ) return block;
        int delta = checked( (int)output.Length - cursor );
        output.Write( raw, cursor, raw.Length - cursor );
        byte[] rewritten = output.ToArray();
        // Literal-only LZ4 retains the original compression mode and any separately compressed blob frames.
        byte[] stored = compression == 0 ? rewritten : EncodeLiterals( rewritten );
        using var result = new MemoryStream();
        result.Write( block, 0, header );
        result.Write( stored );
        result.Write( block, header + storedLength, block.Length - header - storedLength );
        byte[] bytes = result.ToArray();
        BitConverter.GetBytes( checked( BitConverter.ToInt32( block, 40 ) + delta ) ).CopyTo( bytes, 40 );
        BitConverter.GetBytes( rewritten.Length ).CopyTo( bytes, 48 );
        BitConverter.GetBytes( stored.Length ).CopyTo( bytes, 52 );
        return bytes;
    }

    private static int Align( int value, int alignment ) => checked( (value + alignment - 1) / alignment * alignment );

    private static byte[] DecodeLz4( ReadOnlySpan<byte> input, int length )
    {
        var output = new byte[length];
        int source = 0, target = 0;
        while ( source < input.Length )
        {
            byte token = input[source++];
            int literals = token >> 4;
            if ( literals == 15 ) { byte n; do { if ( source >= input.Length ) throw new InvalidDataException( "Invalid LZ4 literal length." ); n = input[source++]; literals = checked( literals + n ); } while ( n == 255 ); }
            if ( literals > input.Length - source || literals > output.Length - target ) throw new InvalidDataException( "Invalid LZ4 literal run." );
            input.Slice( source, literals ).CopyTo( output.AsSpan( target ) );
            source += literals; target += literals;
            if ( source == input.Length ) break;
            if ( source > input.Length - 2 ) throw new InvalidDataException( "Invalid LZ4 match." );
            int distance = input[source] | (input[source + 1] << 8); source += 2;
            int match = token & 15;
            if ( match == 15 ) { byte n; do { if ( source >= input.Length ) throw new InvalidDataException( "Invalid LZ4 match length." ); n = input[source++]; match = checked( match + n ); } while ( n == 255 ); }
            match = checked( match + 4 );
            if ( distance == 0 || distance > target || match > output.Length - target ) throw new InvalidDataException( "Invalid LZ4 back reference." );
            for ( int i = 0; i < match; i++ ) { output[target] = output[target - distance]; target++; }
        }
        if ( target != length ) throw new InvalidDataException( "LZ4 decoded length mismatch." );
        return output;
    }

    private static byte[] EncodeLiterals( byte[] bytes )
    {
        using var output = new MemoryStream();
        output.WriteByte( (byte)(Math.Min( bytes.Length, 15 ) << 4) );
        if ( bytes.Length >= 15 )
        {
            int extra = bytes.Length - 15;
            while ( extra >= 255 ) { output.WriteByte( 255 ); extra -= 255; }
            output.WriteByte( (byte)extra );
        }
        output.Write( bytes );
        return output.ToArray();
    }
}
