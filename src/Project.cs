using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AllStarsTextureStudio {
    public sealed class TextureChange {
        public string Archive,Resource,ResourceSha256,OriginalTextureSha256,Name,Data;
        public int Offset;
    }
    public sealed class TextureProject {
        public int Version=1;public string Title="My texture mod";
        public List<TextureChange> Changes=new List<TextureChange>();
        public static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=128*1024*1024,RecursionLimit=32};}
        public static TextureProject Load(string file){
            Bytes.Need(new FileInfo(file).Length<=96*1024*1024,"Project exceeds the 96 MB limit.");
            var p=Json().Deserialize<TextureProject>(File.ReadAllText(file,Encoding.UTF8));
            Bytes.Need(p!=null&&p.Version==1&&p.Changes!=null&&p.Changes.Count<=4096,"Unsupported texture project.");
            var keys=new HashSet<string>();
            foreach(var c in p.Changes){
                Bytes.Need(c!=null&&c.Offset>=0&&Hash(c.ResourceSha256)&&Hash(c.OriginalTextureSha256)&&c.Data!=null&&c.Data.Length<=96*1024*1024,"Invalid project entry.");
                ValidateResource(c.Archive,c.Resource);
                Bytes.Need(keys.Add(c.Archive+"/"+c.Resource+"/"+c.Offset),"Duplicate project texture.");
                byte[] data=Convert.FromBase64String(c.Data);var d=DdsInfo.Read(data,0);Bytes.Need(d.Length==data.Length,"Invalid saved texture.");
            }return p;
        }
        public static void ValidateResource(string archive,string resource){
            Bytes.Need(new[]{"Racers.xpac","Select.xpac","Tracks.xpac"}.Contains(archive)&&resource!=null&&resource.StartsWith("Resource/"+Path.GetFileNameWithoutExtension(archive)+"/",StringComparison.Ordinal)&&resource.EndsWith(".zig",StringComparison.Ordinal)&&!resource.Contains("..")&&resource.All(c=>c>=32&&c<127&&"\\:*?\"<>|".IndexOf(c)<0),"Invalid project resource path.");
        }
        private static bool Hash(string s){return s!=null&&s.Length==64&&s.All(c=>"0123456789abcdef".Contains(c));}
        public void Save(string file){var text=Json().Serialize(this);byte[] bytes=Encoding.UTF8.GetBytes(text);Bytes.Need(bytes.Length<=96*1024*1024,"Project exceeds the 96 MB limit; split edits into separate projects.");SafeFiles.WriteAtomic(file,bytes);}
        public void Remember(AssetDocument doc){
            Changes.RemoveAll(c=>c.Archive==doc.Asset.ArchiveName&&c.Resource==doc.Asset.ResourceName);
            foreach(var edit in doc.Edits){
                var item=doc.Textures.Single(t=>t.Offset==edit.Key);var original=new byte[item.Info.Length];Buffer.BlockCopy(doc.Original,item.Offset,original,0,original.Length);
                Changes.Add(new TextureChange{Archive=doc.Asset.ArchiveName,Resource=doc.Asset.ResourceName,ResourceSha256=doc.OriginalHash,OriginalTextureSha256=Bytes.Sha(original),Name=item.Name,Offset=item.Offset,Data=Convert.ToBase64String(edit.Value)});
            }
        }
        public void ApplyTo(AssetDocument doc){
            foreach(var change in Changes.Where(c=>c.Archive==doc.Asset.ArchiveName&&c.Resource==doc.Asset.ResourceName)){
                Bytes.Need(doc.OriginalHash==change.ResourceSha256,"The game's model or track changed since this project was saved. Start a new project for the changed files.");
                var item=doc.Textures.SingleOrDefault(t=>t.Offset==change.Offset);Bytes.Need(item!=null&&Bytes.Sha(doc.Dds(item))==change.OriginalTextureSha256,"Saved texture no longer matches the original.");doc.Set(item,Convert.FromBase64String(change.Data));
            }
        }
    }
    public static class SafeFiles {
        public static void ExportDestination(string path,string game,string extension){
            Ordinary(path);string full=Path.GetFullPath(path);Bytes.Need(Path.GetExtension(full).Equals(extension,StringComparison.OrdinalIgnoreCase),"Choose a "+extension+" file.");
            if(game!=null)Bytes.Need(!full.StartsWith(Path.GetFullPath(game).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Save exports outside your game folder. Use Install project to apply edits safely.");
        }
        public static void Ordinary(string path){
            string full=Path.GetFullPath(path);Bytes.Need(!full.StartsWith("\\\\",StringComparison.Ordinal)&&full.Length<240,"Choose a local folder with a shorter path.");
            for(string p=full;p!=null;p=Path.GetDirectoryName(p))if(Directory.Exists(p)||File.Exists(p))Bytes.Need((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Linked folders are not supported.");
        }
        public static void WriteAtomic(string path,byte[] data){
            path=Path.GetFullPath(path);Ordinary(path);string temporary=Path.Combine(Path.GetDirectoryName(path),".texture-write-"+Guid.NewGuid().ToString("N"));
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(data,0,data.Length);file.Flush(true);}
            try{Ordinary(path);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
