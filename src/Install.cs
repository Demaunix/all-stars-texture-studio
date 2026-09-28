using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AllStarsTextureStudio {
    public sealed class InstallReceipt {
        public int Version=1;
        public Dictionary<string,string> Files=new Dictionary<string,string>();
    }
    public static class Install {
        const string Receipt=".texture-studio-install.json";
        public static void GameClosed(){
            foreach(var p in Process.GetProcessesByName("Sonic & SEGA All-Stars Racing"))using(p)Bytes.Need(p.HasExited,"Close All-Stars before installing or removing a texture mod.");
        }
        public static void CheckGame(string game){
            SafeFiles.Ordinary(game);GameClosed();
            string exe=Path.Combine(game,"Sonic & SEGA All-Stars Racing.exe");
            Bytes.Need(File.Exists(exe),"Select the original Windows Steam game's installation folder.");
            using(var s=File.OpenRead(exe))Bytes.Need(Bytes.Sha(s)=="c110b4d3b9729317a93e956f1a49be989c7ad3c2aaba7c54c41319c104381b03","This executable version has not been checked for mod installation. Texture browsing and export remain available.");
        }
        static string Target(string game,string relative){
            bool loader=relative=="dinput8.dll"||relative=="ChaoGarage.dll";
            Bytes.Need(loader||relative.StartsWith("mods/Texture Studio/",StringComparison.Ordinal),"Invalid installation receipt path.");
            Bytes.Need(!relative.Contains("..")&&!relative.Contains("\\")&&!relative.Contains(":"),"Invalid installation path.");
            string path=Path.GetFullPath(Path.Combine(game,relative.Replace('/',Path.DirectorySeparatorChar)));
            SafeFiles.Ordinary(path);Bytes.Need(path.StartsWith(Path.GetFullPath(game).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Installation path escaped the game folder.");return path;
        }
        public static void CheckConflicts(string game,ModPackage package){
            SafeFiles.Ordinary(game);Bytes.Need(!File.Exists(Path.Combine(game,Receipt)),"A Texture Studio mod is already installed. Remove it with Restore first, then install your revised project.");
            Bytes.Need(!Directory.Exists(Path.Combine(game,"mods",ModExport.Folder)),"An existing Texture Studio folder is present. Preserve or move it before installing this project.");
            foreach(string resource in package.Resources)Bytes.Need(!File.Exists(Path.Combine(game,resource)),"A loose extracted file overrides "+resource+". Move that file to a backup outside the game before installing this edit.");
            foreach(string loader in new[]{"dinput8.dll","ChaoGarage.dll"}){
                string file=Target(game,loader);if(File.Exists(file)){
                    string hash=Bytes.Sha(File.ReadAllBytes(file));bool known=hash==Bytes.Sha(ModExport.Runtime(loader))||(loader=="ChaoGarage.dll"&&(hash=="b66b5ab2b0ef5db82448d35f048be7ed56d626b463cf898187bdc98f61fe1952"||hash=="899859d561c49e93d2eb5db17f83549e33b2d205448d781edbb9d8124bebfe35"))||(loader=="dinput8.dll"&&hash=="17a78e1eab0645c63b58ed7b90895787b62a79bbbb9413fb95c4ff54f931a6e2");
                    Bytes.Need(known,"A different "+loader+" is installed. Export your mod instead; Texture Studio will not overwrite an unknown loader.");
                }
            }
            string mods=Path.Combine(game,"mods");SafeFiles.Ordinary(mods);
            if(Directory.Exists(mods))foreach(string folder in Directory.GetDirectories(mods)){
                SafeFiles.Ordinary(folder);string ini=Path.Combine(folder,"mod.ini");if(!File.Exists(ini))continue;
                string planName=File.ReadAllLines(ini).Select(l=>l.Trim()).Where(l=>l.StartsWith("plan=",StringComparison.OrdinalIgnoreCase)).Select(l=>l.Substring(5).Trim()).SingleOrDefault();
                Bytes.Need(planName!=null&&!Path.IsPathRooted(planName)&&!planName.Contains(".."),"An existing mod has an unsupported plan. Export this project instead.");
                string plan=Path.Combine(folder,planName);SafeFiles.Ordinary(plan);Bytes.Need(new FileInfo(plan).Length<=4*1024*1024,"Existing mod plan is too large.");
                var obj=TextureProject.Json().DeserializeObject(File.ReadAllText(plan)) as Dictionary<string,object>;object groups;
                Bytes.Need(obj!=null&&obj.TryGetValue("archive_groups",out groups),"An existing mod has an unsupported plan.");
                groups=obj["archive_groups"];var list=groups as object[];Bytes.Need(list!=null,"An existing mod has an unsupported archive list.");
                foreach(var entry in list){
                    var g=entry as Dictionary<string,object>;object name;Bytes.Need(g!=null&&g.TryGetValue("path",out name)&&name is string,"Invalid existing mod archive.");string resource=((string)g["path"]).Replace('\\','/');if(!package.Archives.Contains(resource))continue;
                    object format,ops;Bytes.Need(g.TryGetValue("format",out format)&&(string)format=="xpac"&&g.TryGetValue("operations",out ops),"Another mod replaces "+resource+". A combined plan is required.");
                    var operations=g["operations"] as object[];Bytes.Need(operations!=null,"Unsupported existing mod operations.");
                    foreach(var operation in operations){var op=operation as Dictionary<string,object>;object key;Bytes.Need(op!=null&&op.TryGetValue("key",out key)&&key is string,"Invalid existing mod operation.");Bytes.Need(!package.MemberKeys[resource].Contains((string)op["key"]),"Another installed mod edits the same character or course resource in "+resource+". A combined plan is required; the other mod was left untouched.");}
                }
            }
        }
        // Transaction tests use an isolated fixture folder. The public UI always
        // calls CheckGame before entering this file-only transaction.
        public static void Apply(string game,ModPackage package){
            CheckConflicts(game,package);GameClosed();
            var files=package.Files.ToDictionary(p=>"mods/"+ModExport.Folder+"/"+p.Key,p=>p.Value);
            foreach(string loader in new[]{"dinput8.dll","ChaoGarage.dll"})if(!File.Exists(Target(game,loader)))files.Add(loader,ModExport.Runtime(loader));
            var receipt=new InstallReceipt();foreach(var p in files){string target=Target(game,p.Key);Bytes.Need(!File.Exists(target),"An installation target already exists.");receipt.Files.Add(p.Key,Bytes.Sha(p.Value));}
            string receiptPath=Path.Combine(game,Receipt);SafeFiles.Ordinary(receiptPath);
            SafeFiles.WriteAtomic(receiptPath,Encoding.UTF8.GetBytes(TextureProject.Json().Serialize(receipt)));
            var written=new List<string>();
            try{foreach(var p in files){string target=Target(game,p.Key);Directory.CreateDirectory(Path.GetDirectoryName(target));using(var stream=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){written.Add(p.Key);stream.Write(p.Value,0,p.Value.Length);stream.Flush(true);}}}
            catch{foreach(string name in written.AsEnumerable().Reverse()){string target=Target(game,name);File.Delete(target);}File.Delete(receiptPath);CleanupEmpty(game);throw;}
        }
        public static void Restore(string game){
            SafeFiles.Ordinary(game);GameClosed();string file=Path.Combine(game,Receipt);SafeFiles.Ordinary(file);
            Bytes.Need(File.Exists(file)&&new FileInfo(file).Length<=1024*1024,"No Texture Studio installation receipt was found.");
            var receipt=TextureProject.Json().Deserialize<InstallReceipt>(File.ReadAllText(file));Bytes.Need(receipt!=null&&receipt.Version==1&&receipt.Files!=null&&receipt.Files.Count>0&&receipt.Files.Count<4096,"Invalid installation receipt.");
            foreach(var p in receipt.Files){string target=Target(game,p.Key);Bytes.Need(p.Value!=null&&p.Value.Length==64,"Invalid installation hash.");if(File.Exists(target))Bytes.Need(Bytes.Sha(File.ReadAllBytes(target))==p.Value,"An installed file has changed: "+p.Key+". Restore stopped to preserve that change.");}
            string mods=Path.Combine(game,"mods");bool otherMods=Directory.Exists(mods)&&Directory.GetDirectories(mods).Any(d=>!Path.GetFileName(d).Equals(ModExport.Folder,StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(d,"mod.ini")));
            // Remove only recorded, unchanged files. Preserve newly added files
            // and loaders now required by another mod.
            foreach(string relative in receipt.Files.Keys){if(otherMods&&!relative.StartsWith("mods/",StringComparison.Ordinal))continue;string target=Target(game,relative);if(File.Exists(target))File.Delete(target);}
            File.Delete(file);CleanupEmpty(game);
        }
        static void CleanupEmpty(string game){
            string root=Path.Combine(game,"mods",ModExport.Folder);SafeFiles.Ordinary(root);if(!Directory.Exists(root))return;
            // Do not traverse junctions, even during cleanup.
            Empty(root);
        }
        static void Empty(string path){SafeFiles.Ordinary(path);foreach(string dir in Directory.GetDirectories(path))Empty(dir);if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);}
    }
}
