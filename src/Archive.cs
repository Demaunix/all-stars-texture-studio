using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AllStarsTextureStudio {
    public static class Bytes {
        public static uint U32(byte[] b, int p) { Need(p>=0 && p<=b.Length-4,"Truncated binary data."); return BitConverter.ToUInt32(b,p); }
        public static void Put(byte[] b,int p,uint v) { Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,p,4); }
        public static void Need(bool ok,string message) { if(!ok) throw new InvalidDataException(message); }
        public static string Sha(byte[] b) { using(var h=SHA256.Create())return Hex(h.ComputeHash(b)); }
        public static string Sha(Stream s) { using(var h=SHA256.Create())return Hex(h.ComputeHash(s)); }
        public static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-","").ToLowerInvariant(); }
        public static byte[] Read(Stream s,int n) { var b=new byte[n]; int at=0; while(at<n){int k=s.Read(b,at,n-at); if(k==0)throw new EndOfStreamException();at+=k;}return b; }
        public static uint NameHash(string name) { uint h=0;string s=name.Replace('/','\\').ToUpperInvariant();unchecked{for(int i=s.Length-1;i>=0;i--)h=h*131+s[i];}return h; }
        public static uint Adler(byte[] bytes) { uint a=1,b=0; foreach(byte c in bytes){a=(a+c)%65521;b=(b+a)%65521;}return (b<<16)|a; }
        public static byte[] DecodeMember(byte[] data) {
            if(data.Length<2 || (data[0]&15)!=8 || ((data[0]<<8)+data[1])%31!=0)return data;
            Need(data.Length>=6 && (data[1]&32)==0,"Unsupported compressed archive entry.");
            using(var input=new MemoryStream(data,2,data.Length-6,false))
            using(var deflate=new DeflateStream(input,CompressionMode.Decompress))
            using(var output=new MemoryStream()){
                var block=new byte[65536];int n;
                while((n=deflate.Read(block,0,block.Length))>0){Need(output.Length+n<=512L*1024*1024,"Archive entry exceeds the 512 MB limit.");output.Write(block,0,n);}
                byte[] result=output.ToArray();uint adler=(uint)(data[data.Length-4]<<24|data[data.Length-3]<<16|data[data.Length-2]<<8|data[data.Length-1]);
                Need(Adler(result)==adler,"Archive entry checksum failed.");return result;
            }
        }
        public static byte[] EncodeMember(byte[] data) {
            using(var output=new MemoryStream()){
                output.WriteByte(0x78);output.WriteByte(0x9c);
                using(var deflate=new DeflateStream(output,CompressionLevel.Optimal,true))deflate.Write(data,0,data.Length);
                uint a=Adler(data);for(int i=3;i>=0;i--)output.WriteByte((byte)(a>>(i*8)));return output.ToArray();
            }
        }
    }
    public sealed class ArchiveEntry { public uint Key,Offset,Length; }
    public sealed class Archive : IDisposable {
        public readonly string PathName; public readonly byte[] Header;
        public readonly SortedDictionary<uint,ArchiveEntry> Entries=new SortedDictionary<uint,ArchiveEntry>();
        private readonly FileStream stream;
        public Archive(string path) {
            PathName=Path.GetFullPath(path);
            stream=new FileStream(PathName,FileMode.Open,FileAccess.Read,FileShare.Read);
            try{
                Bytes.Need(stream.Length>=24 && stream.Length<=uint.MaxValue,"Unsupported archive size.");
                Header=Bytes.Read(stream,24);uint start=Bytes.U32(Header,8),count=Bytes.U32(Header,12);
                Bytes.Need(Bytes.U32(Header,0)==0&&Bytes.U32(Header,4)==0&&Bytes.U32(Header,16)==0&&Bytes.U32(Header,20)==0,"Not a PC All-Stars archive.");
                Bytes.Need(count>0&&count<1000000&&24L+20L*count<=start&&start<=stream.Length,"Invalid archive index.");
                var rows=Bytes.Read(stream,checked((int)count*20));
                for(int i=0;i<count;i++){
                    int p=i*20;uint key=Bytes.U32(rows,p),off=Bytes.U32(rows,p+4),size=Bytes.U32(rows,p+12);
                    Bytes.Need(Bytes.U32(rows,p+8)==size&&Bytes.U32(rows,p+16)==0,"Unsupported archive record.");
                    Bytes.Need(off>=start&&(long)off+size<=stream.Length&&!Entries.ContainsKey(key),"Invalid archive entry bounds or duplicate key.");
                    Entries.Add(key,new ArchiveEntry{Key=key,Offset=off,Length=size});
                }
                long end=start;foreach(var e in Entries.Values.OrderBy(e=>e.Offset)){
                    Bytes.Need(e.Offset>=end,"Overlapping archive entries.");end=(long)e.Offset+e.Length;
                }
            }catch{stream.Dispose();throw;}
        }
        public byte[] Stored(uint key) { var e=Entries[key];Bytes.Need(e.Length<=512*1024*1024,"Selected entry is too large.");lock(stream){stream.Position=e.Offset;return Bytes.Read(stream,(int)e.Length);} }
        public byte[] Decoded(uint key) { return Bytes.DecodeMember(Stored(key)); }
        public string FileHash() { lock(stream){stream.Position=0;return Bytes.Sha(stream);} }
        public void CopyStored(uint key,Stream output) {
            var e=Entries[key];lock(stream){stream.Position=e.Offset;var block=new byte[1<<20];long remain=e.Length;while(remain>0){int n=stream.Read(block,0,(int)Math.Min(remain,block.Length));if(n==0)throw new EndOfStreamException();output.Write(block,0,n);remain-=n;}}
        }
        public void Dispose(){stream.Dispose();}
    }
    public sealed class Asset {
        public string ArchiveName,ResourceName,Label;
        public uint Key {get{return Bytes.NameHash(".\\"+ResourceName);}}
        public override string ToString(){return Label;}
    }
    public sealed class TextureItem {
        public int Offset; public string Name; public DdsInfo Info;
        public override string ToString(){return Name+"  ·  "+Info.Width+" × "+Info.Height+"  "+Info.Format;}
    }
    public sealed class AssetDocument {
        public Asset Asset; public byte[] Original; public readonly List<TextureItem> Textures=new List<TextureItem>();
        public readonly Dictionary<int,byte[]> Edits=new Dictionary<int,byte[]>();
        public string OriginalHash;
        public static AssetDocument Load(string game,Asset asset) {
            var doc=new AssetDocument{Asset=asset};
            using(var archive=new Archive(System.IO.Path.Combine(game,"Resource",asset.ArchiveName))){
                doc.Original=archive.Decoded(asset.Key);doc.OriginalHash=Bytes.Sha(doc.Original);
                var labels=new Dictionary<int,string>();
                uint metaKey=Bytes.NameHash(".\\"+System.IO.Path.ChangeExtension(asset.ResourceName,"zif"));
                if(archive.Entries.ContainsKey(metaKey)) {
                    byte[] meta=archive.Decoded(metaKey);
                    try{labels=Labels(meta,doc.Original);}catch(InvalidDataException){/* Raw DDS browsing remains available. */}catch(OverflowException){/* Invalid optional metadata does not expose unsafe DDS ranges. */}
                }
                for(int at=0;at<=doc.Original.Length-128;at++){
                    if(Bytes.U32(doc.Original,at)!=0x20534444)continue;
                    DdsInfo info;try{info=DdsInfo.Read(doc.Original,at);}catch(InvalidDataException){continue;}
                    string name; if(!labels.TryGetValue(at,out name))name="Texture "+(doc.Textures.Count+1);
                    doc.Textures.Add(new TextureItem{Offset=at,Name=name,Info=info});at+=info.Length-1;
                }
            }
            return doc;
        }
        private static string Text(byte[] b,int at){Bytes.Need(at>=0&&at<b.Length,"Invalid texture name.");int end=at;while(end<b.Length&&end-at<1024&&b[end]!=0)end++;Bytes.Need(end<b.Length&&b[end]==0,"Unterminated texture name.");return Encoding.ASCII.GetString(b,at,end-at);}
        private static Dictionary<int,string> Labels(byte[] b,byte[] gpu) {
            var result=new Dictionary<int,string>();int chunk=4;
            while(chunk<=b.Length-16){int size=checked((int)Bytes.U32(b,chunk+4));Bytes.Need(size>=16&&size<=b.Length-chunk&&Bytes.U32(b,chunk+12)==0x44332211,"Invalid model chunk.");if(Bytes.U32(b,chunk)==0x45524f46)break;chunk+=size;}
            Bytes.Need(chunk<=b.Length-20&&Bytes.U32(b,chunk)==0x45524f46,"Model texture metadata is unavailable.");
            int forests=checked((int)Bytes.U32(b,chunk+16));Bytes.Need(forests>=0&&forests<4096,"Invalid model header.");
            for(int f=0;f<forests;f++){
                int p=chunk+20+f*16;int basis=checked(chunk+16+(int)Bytes.U32(b,p+8)),gpuBase=checked(4+(int)Bytes.U32(b,p+12));
                int count=checked((int)Bytes.U32(b,basis+8)),table=checked(basis+(int)Bytes.U32(b,basis+12));Bytes.Need(count>=0&&count<100000,"Invalid texture table.");
                for(int n=0;n<count;n++){
                    int pos=checked(basis+(int)Bytes.U32(b,table+n*4));int off=checked(gpuBase+(int)Bytes.U32(b,pos+8));
                    if(off>=0&&off<=gpu.Length-128&&Bytes.U32(gpu,off)==0x20534444)result[off]=Text(b,checked(basis+(int)Bytes.U32(b,pos)));
                }
            }return result;
        }
        public byte[] Dds(TextureItem item){byte[] edit;if(Edits.TryGetValue(item.Offset,out edit))return (byte[])edit.Clone();var b=new byte[item.Info.Length];Buffer.BlockCopy(Original,item.Offset,b,0,b.Length);return b;}
        public byte[] Modified(){var b=(byte[])Original.Clone();foreach(var pair in Edits){var item=Textures.Single(t=>t.Offset==pair.Key);Bytes.Need(pair.Value.Length==item.Info.Length,"Edited texture size changed.");Buffer.BlockCopy(pair.Value,0,b,pair.Key,pair.Value.Length);}return b;}
        public void Set(TextureItem item,byte[] data){var info=DdsInfo.Read(data,0);Bytes.Need(info.Length==data.Length&&info.Compatible(item.Info),"Texture dimensions, format or mip levels differ.");if(Original.Skip(item.Offset).Take(data.Length).SequenceEqual(data))Edits.Remove(item.Offset);else Edits[item.Offset]=(byte[])data.Clone();}
    }
}
