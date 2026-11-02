using System.Text;
using SboxMount;

int assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
void Reject(byte[] input)
{
    try { CompiledResource.Read(input); throw new Exception("Malformed input was accepted"); }
    catch (InvalidDataException) { assertions++; }
    catch (OverflowException) { assertions++; }
}
Reject(new byte[15]);
var malformed = new byte[16];
BitConverter.GetBytes((ushort)12).CopyTo(malformed, 4);
BitConverter.GetBytes(uint.MaxValue).CopyTo(malformed, 8);
Reject(malformed);

// Large, deeply nested mount trees must preserve direct-vs-recursive contents and case-insensitive paths.
const string mountRoot = "mount://sbox_workshop";
var treePaths = Enumerable.Range(0, 10000).Select(i => $"{mountRoot}/Decay/Assets/Models/group_{i / 100}/prop_{i}/model.vmdl").ToList();
treePaths.Add($"{mountRoot}/Decay/Assets/Models/direct.vmdl");
treePaths.Add($"{mountRoot}/RustLegacy/Assets/Models/other.vmdl");
treePaths.Add(treePaths[0].ToLowerInvariant());
treePaths.Add("mount://other/ignored.vmdl");
var index = new MountPathIndex(mountRoot, treePaths);
Check(index.FileCount == 10002, "Index mixed mounts or duplicated case variants");
var modelsFolder = index.Find(mountRoot + "/decay/assets/models/");
Check(modelsFolder != null, "Lowercase restored folder path not found");
Check(modelsFolder.Files.Count == 1, "Folder view included recursive files");
Check(modelsFolder.Children.Count == 100, "Folder view lost subdirectories");
int Traverse(MountPathIndex.Folder f) => f.Files.Count + f.Children.Values.Sum(Traverse);
Check(Traverse(modelsFolder) == 10001, "Flat view lost or duplicated files");
Check(index.Find(mountRoot + "/missing") == null, "Unknown directory should be empty");

// Byte-identical models are only duplicates when their transitive material/texture dependencies also match.
string fixture = Path.Combine(Path.GetTempPath(), "sbox-dedup-" + Guid.NewGuid().ToString("N"));
byte[] Resource(string[] references, byte payload = 0)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
    writer.Write(0u); writer.Write((ushort)12); writer.Write((ushort)0); writer.Write(8u); writer.Write(1u);
    writer.Write(Encoding.ASCII.GetBytes("RERL")); writer.Write(8u); writer.Write(0u);
    writer.Write(8u); writer.Write((uint)references.Length);
    long textOffset = 8 + references.Length * 16L;
    for(int i=0;i<references.Length;i++) { writer.Write((ulong)i); writer.Write(textOffset-(8+i*16L+8)); textOffset+=Encoding.UTF8.GetByteCount(references[i])+1; }
    foreach(string reference in references) { writer.Write(Encoding.UTF8.GetBytes(reference)); writer.Write((byte)0); }
    int size=(int)stream.Length-28;
    writer.Write(payload);
    stream.Position=24; writer.Write((uint)size);
    stream.Position=0; writer.Write((uint)stream.Length);
    return stream.ToArray();
}
void WriteFixture(string game, string relative, byte[] bytes)
{
    string path=Path.Combine(fixture,game,relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,bytes);
}
WorkshopDuplicateScanner.Input Input(string game,string root="models/shared.vmdl_c") => new(
    "mount://sbox_workshop/"+game+"/"+root, game, game, Path.Combine(fixture,game),root,"Model");
try
{
    foreach(var game in new[]{"a","b","c"})
    {
        WriteFixture(game,"models/shared.vmdl_c",Resource(new[]{"materials/shared.vmat"}));
        WriteFixture(game,"materials/shared.vmat_c",Resource(new[]{"textures/shared.vtex"}));
        WriteFixture(game,"textures/shared.vtex_c",Resource(Array.Empty<string>(),game=="b"?(byte)2:(byte)1));
    }
    var duplicates=new WorkshopDuplicateScanner(default).Scan(new[]{Input("a"),Input("b"),Input("c")});
    Check(duplicates.Groups.Length==1,"Unexpected duplicate grouping");
    Check(duplicates.Groups[0].Sources.Select(s=>s.WorkshopId).SequenceEqual(new[]{"a","c"}),"Different transitive texture was collapsed");
    Check(duplicates.HiddenCopies==1,"Incorrect hidden count");
    Check(duplicates.Unverified.Length==0,"Valid dependencies were rejected");
    WriteFixture("c","textures/shared.vtex_c",Resource(Array.Empty<string>(),2));
    var changedStamp=duplicates.Files.Single(s=>s.Path.Replace('\\','/').EndsWith("/c/textures/shared.vtex_c",StringComparison.OrdinalIgnoreCase));
    // Force a different stamp even on filesystems with coarse timestamp resolution.
    File.SetLastWriteTimeUtc(changedStamp.Path,new DateTime(changedStamp.Modified,DateTimeKind.Utc).AddSeconds(3));
    Check(!WorkshopDuplicateScanner.IsCurrent(changedStamp),"Modified dependency report was reused");
    foreach(var game in new[]{"a","b"}) WriteFixture(game,"materials/shared.vmat_c",Resource(new[]{"models/shared.vmdl"}));
    var cyclic=new WorkshopDuplicateScanner(default).Scan(new[]{Input("a"),Input("b")});
    Check(cyclic.Groups.Length==1,"Identical dependency cycles did not terminate and match");
    File.Delete(Path.Combine(fixture,"b","materials/shared.vmat_c"));
    var missing=new WorkshopDuplicateScanner(default).Scan(new[]{Input("a"),Input("b")});
    Check(missing.Groups.Length==0,"Local dependency and unresolved external dependency were merged");
    Check(missing.ExternalPaths.Any(p=>p.Replace('\\','/').EndsWith("/b/materials/shared.vmat_c")),"Missing dependencies were not tracked for cache invalidation");
}
finally { if(Directory.Exists(fixture)) Directory.Delete(fixture,true); }

// Minimal literal-only LZ4 KV3 v2, containing one resource string.
const string oldPath = "materials/example.vmat";
const string newPath = "sbox_workshop/test/materials/example.vmat";
byte[] name = Encoding.UTF8.GetBytes(oldPath + "\0");
byte[] raw = new byte[8 + name.Length + 4];
BitConverter.GetBytes(1).CopyTo(raw, 0); // 4-byte lane: string count
name.CopyTo(raw, 8); // empty 8-byte lane is aligned
BitConverter.GetBytes(0xffeedd00u).CopyTo(raw, 8 + name.Length);
byte[] block = new byte[64 + 2 + raw.Length];
BitConverter.GetBytes(0x4b563302u).CopyTo(block, 0);
BitConverter.GetBytes(1).CopyTo(block, 20);
BitConverter.GetBytes((ushort)16384).CopyTo(block, 26);
BitConverter.GetBytes(1).CopyTo(block, 32);
BitConverter.GetBytes(name.Length).CopyTo(block, 40);
BitConverter.GetBytes(raw.Length).CopyTo(block, 48);
BitConverter.GetBytes(raw.Length + 2).CopyTo(block, 52);
block[64] = 0xf0; block[65] = (byte)(raw.Length - 15);
raw.CopyTo(block, 66);
var changed = BinaryKv3.Rebase(block, new Dictionary<string, string> { [oldPath] = newPath });
Check(Encoding.UTF8.GetString(changed).Contains(newPath + "\0"), "KV3 resource string was not replaced");
Check(BitConverter.ToInt32(changed, 40) == name.Length + newPath.Length - oldPath.Length, "KV3 types/string size not updated");
Check(BitConverter.ToInt32(changed, 48) == raw.Length + newPath.Length - oldPath.Length, "KV3 raw size not updated");
Check(BitConverter.ToInt32(changed, 52) == changed.Length - 64, "KV3 compressed size not updated");
Check(BinaryKv3.Rebase(block, new Dictionary<string, string>()).SequenceEqual(block), "Unchanged KV3 payload changed");

if (args.Length > 0)
{
    foreach (string id in new[] { "3829436870", "3828567418" })
    foreach (string asset in new[] { "weapons/rust_bandage/v_bandage.vmdl", "weapons/rust_bandage/rust_bandage.vmat" })
    {
        var original = File.ReadAllBytes(Path.Combine(args[0], id, asset + "_c"));
        var refs = CompiledResource.Read(original);
        string Map(string path) => "sbox_workshop/test/" + id + "/" + path;
        var rebased = CompiledResource.Remap(original, refs, Map, _ => 42UL, asset, Map(asset));
        var rebasedRefs = CompiledResource.Read(rebased);
        Check(rebasedRefs.References.Select(r => r.Path).SequenceEqual(refs.References.Select(r => Map(r.Path))), "RERL paths differ from requested mapping");
        Check(rebasedRefs.References.All(r => r.Id == 42UL), "RERL identity not updated");
        Check(BitConverter.ToUInt32(rebased, 0) == rebased.Length, "Resource file size not updated");
        // The entire original payload remains byte-for-byte intact except the resource header/block table.
        int endOfTable = 8 + (int)BitConverter.ToUInt32(original, 8) + (int)BitConverter.ToUInt32(original, 12) * 12;
        Check(original.AsSpan(endOfTable).SequenceEqual(rebased.AsSpan(endOfTable, original.Length - endOfTable)), "Original mesh/physics/streaming payload changed");
        Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(args[0], id, asset + "_c"))), "Workshop source was modified");
        Console.WriteLine($"PASS {id}: {asset}");
    }
}
Console.WriteLine($"PASS {assertions} assertions; no engine or foreign game assemblies loaded.");
