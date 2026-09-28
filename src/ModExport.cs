using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AllStarsTextureStudio {
    public sealed class HashSink : Stream {
        readonly HashAlgorithm hash=SHA256.Create();long length;
        public string Finish(){hash.TransformFinalBlock(new byte[0],0,0);return Bytes.Hex(hash.Hash);}
        public override void Write(byte[] b,int p,int n){hash.TransformBlock(b,p,n,null,0);length+=n;}
        public override bool CanRead{get{return false;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return true;}}
        public override long Length{get{return length;}}public override long Position{get{return length;}set{throw new NotSupportedException();}}
        public override void Flush(){}public override int Read(byte[] b,int p,int n){throw new NotSupportedException();}public override long Seek(long n,SeekOrigin o){throw new NotSupportedException();}public override void SetLength(long n){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(disposing)hash.Dispose();base.Dispose(disposing);}
    }
    public sealed class ModPackage {
        public readonly Dictionary<string,byte[]> Files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Archives=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string,HashSet<string>> MemberKeys=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Resources=new List<string>();
    }
    public static class ModExport {
        public const string Folder="Texture Studio";
        public static byte[] Runtime(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("runtime."+name)){Bytes.Need(s!=null,"The packaged runtime is missing. Use the complete release executable.");return Bytes.Read(s,checked((int)s.Length));}}
        public static ModPackage Build(string game,TextureProject project){
            Bytes.Need(project.Changes.Count>0,"Keep at least one texture edit before exporting a mod.");
            var result=new ModPackage();var groups=new List<object>();
            foreach(var set in project.Changes.GroupBy(c=>c.Archive)){
                string archiveName=set.Key;
                var operations=new List<object>();var replacements=new Dictionary<uint,byte[]>();
                using(var archive=new Archive(Path.Combine(game,"Resource",archiveName))){
                    string originalHash=archive.FileHash();
                    foreach(var resource in set.GroupBy(c=>c.Resource)){
                        TextureProject.ValidateResource(archiveName,resource.Key);
                        var asset=new Asset{ArchiveName=archiveName,ResourceName=resource.Key,Label=resource.Key};var doc=AssetDocument.Load(game,asset);project.ApplyTo(doc);
                        var changed=doc.Modified();var stored=Bytes.EncodeMember(changed);Bytes.Need(Bytes.DecodeMember(stored).SequenceEqual(changed),"Compressed edit verification failed.");
                        string payload="payloads/"+Path.GetFileNameWithoutExtension(archiveName)+"/"+asset.Key.ToString("X8")+".asset";
                        result.Files.Add(payload,stored);replacements.Add(asset.Key,stored);
                        result.Resources.Add(resource.Key);
                        operations.Add(new{key=asset.Key.ToString("X8"),kind="replace",base_stored_sha256=Bytes.Sha(archive.Stored(asset.Key)),payload=payload,payload_sha256=Bytes.Sha(stored),stored_size=stored.Length,decoded_size=changed.Length,codec="zlib",entry_flags=0});
                    }
                    long size;string composed=ComposedHash(archive,replacements,out size);
                    groups.Add(new{path="Resource/"+archiveName,format="xpac",base_sha256=originalHash,base_header_hex=Bytes.Hex(archive.Header),alignment=2048,entry_bytes=20,expected_composed_sha256=composed,expected_composed_size=size,expected_member_count=archive.Entries.Count,operations=operations});
                    result.Archives.Add("Resource/"+archiveName);
                    result.MemberKeys.Add("Resource/"+archiveName,new HashSet<string>(replacements.Keys.Select(k=>k.ToString("X8")),StringComparer.OrdinalIgnoreCase));
                }
            }
            byte[] plan=Encoding.UTF8.GetBytes(TextureProject.Json().Serialize(new{schema=1,mod_id="texture-studio",archive_groups=groups}));
            byte[] runtime=Runtime("TextureRuntime.dll");
            result.Files.Add("plan.json",plan);result.Files.Add("runtime/TextureRuntime.dll",runtime);
            result.Files.Add("mod.ini",Encoding.ASCII.GetBytes("[mod]\r\nschema=1\r\nid=texture-studio\r\nname=Texture Studio\r\nversion=1.0.0\r\nplan=plan.json\r\nplan_sha256="+Bytes.Sha(plan)+"\r\nruntime=runtime/TextureRuntime.dll\r\nruntime_sha256="+Bytes.Sha(runtime)+"\r\n"));
            result.Files.Add("README.txt",Encoding.UTF8.GetBytes("All-Stars Texture Studio texture mod\r\n\r\nTexture replacements only. Original game archives and saves are unchanged.\r\nInstall using Texture Studio, or use the included mods folder with the matching\r\nChaoGarage loader. Close All-Stars before installing or restoring.\r\nMods changing the same resource member need a combined plan; do not overwrite them.\r\nSky texture edits do not change baked lighting, fog, or sky geometry.\r\n"));
            return result;
        }
        private static uint Align(long value){long aligned=(value+2047)&~2047L;Bytes.Need(aligned<=uint.MaxValue,"Edited archive exceeds the format limit.");return (uint)aligned;}
        private static void Zeros(Stream stream,long count){var zeros=new byte[2048];while(count>0){int n=(int)Math.Min(zeros.Length,count);stream.Write(zeros,0,n);count-=n;}}
        public static string ComposedHash(Archive archive,Dictionary<uint,byte[]> changes,out long size){
            byte[] header=(byte[])archive.Header.Clone();uint start=Align(24L+20L*archive.Entries.Count),cursor=start;Bytes.Put(header,8,start);Bytes.Put(header,12,(uint)archive.Entries.Count);
            var rows=new byte[20*archive.Entries.Count];int index=0;
            foreach(var entry in archive.Entries.Values){byte[] replacement;uint n=changes.TryGetValue(entry.Key,out replacement)?(uint)replacement.Length:entry.Length;Bytes.Put(rows,index,entry.Key);Bytes.Put(rows,index+4,cursor);Bytes.Put(rows,index+8,n);Bytes.Put(rows,index+12,n);cursor=Align((long)cursor+n);index+=20;}
            using(var output=new HashSink()){
                output.Write(header,0,24);output.Write(rows,0,rows.Length);Zeros(output,start-24-rows.Length);
                foreach(var entry in archive.Entries.Values){byte[] replacement;long n;if(changes.TryGetValue(entry.Key,out replacement)){n=replacement.Length;output.Write(replacement,0,replacement.Length);}else{n=entry.Length;archive.CopyStored(entry.Key,output);}Zeros(output,Align(n)-n);}
                size=output.Length;Bytes.Need(size==cursor,"Composed archive size mismatch.");return output.Finish();
            }
        }
        public static void Zip(string path,ModPackage package){
            path=Path.GetFullPath(path);SafeFiles.Ordinary(path);string temporary=Path.Combine(Path.GetDirectoryName(path),".texture-export-"+Guid.NewGuid().ToString("N"));
            try{
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){
                    using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){
                        foreach(var file in package.Files)Add(zip,"mods/"+Folder+"/"+file.Key,file.Value);
                        Add(zip,"dinput8.dll",Runtime("dinput8.dll"));Add(zip,"ChaoGarage.dll",Runtime("ChaoGarage.dll"));
                        Add(zip,"START HERE.txt",Encoding.UTF8.GetBytes("All-Stars Texture Studio mod\r\n\r\nBack up your existing mods and loader first. Close the game.\r\nCopy this package beside Sonic & SEGA All-Stars Racing.exe. Do not overwrite\r\nan existing Texture Studio mod or unknown loader; use the editor's Install\r\nand Restore buttons for a checked, reversible installation.\r\nOther mods that replace the same resource member require a combined plan.\r\nNo original archive or save should be deleted or replaced.\r\n"));
                    }output.Flush(true);
                }
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        private static void Add(ZipArchive zip,string name,byte[] data){using(var s=zip.CreateEntry(name,CompressionLevel.Optimal).Open())s.Write(data,0,data.Length);}
    }
}
