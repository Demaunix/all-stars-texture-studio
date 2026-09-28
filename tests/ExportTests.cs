using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AllStarsTextureStudio;

static class ExportTests {
    static int count;
    static void Check(bool result,string name){count++;if(!result)throw new Exception(name);}
    static void Refuses(Action action,string name){try{action();}catch(InvalidDataException){count++;return;}throw new Exception("Did not refuse "+name);}
    public static void Run(string output){
        string root=Path.Combine(Path.GetFullPath(output),"transaction-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Resource"));
        var dds=File.ReadAllBytes(Path.Combine(output,"DXT5-4.dds"));var model=new byte[16+dds.Length];Buffer.BlockCopy(dds,0,model,16,dds.Length);var stored=Bytes.EncodeMember(model);
        var asset=new Asset{ArchiveName="Racers.xpac",ResourceName="Resource/Racers/SonicCar.zig",Label="Synthetic Sonic fixture"};
        var archive=new byte[4096];Bytes.Put(archive,8,2048);Bytes.Put(archive,12,1);Bytes.Put(archive,24,asset.Key);Bytes.Put(archive,28,2048);Bytes.Put(archive,32,(uint)stored.Length);Bytes.Put(archive,36,(uint)stored.Length);Buffer.BlockCopy(stored,0,archive,2048,stored.Length);File.WriteAllBytes(Path.Combine(root,"Resource/Racers.xpac"),archive);
        var doc=AssetDocument.Load(root,asset);Check(doc.Textures.Count==1,"fixture discovery");var item=doc.Textures[0];var edited=Dds.Recolor(doc.Dds(item),new ColorTransform{Hue=90});doc.Set(item,edited);var project=new TextureProject();project.Remember(doc);
        string saved=Path.Combine(output,"roundtrip.textureproject");project.Save(saved);project=TextureProject.Load(saved);var fresh=AssetDocument.Load(root,asset);project.ApplyTo(fresh);Check(fresh.Dds(fresh.Textures[0]).SequenceEqual(edited),"project lost edits");
        var bad=TextureProject.Load(saved);bad.Changes[0].ResourceSha256=new string('0',64);Refuses(()=>bad.ApplyTo(AssetDocument.Load(root,asset)),"mismatched base");
        var package=ModExport.Build(root,project);Check(package.Files.Count==5,"unexpected package members");Check(package.Files.Keys.All(k=>!k.EndsWith(".bin")),"bin cache shipped");
        string zip=Path.Combine(output,"fixture-mod.zip");ModExport.Zip(zip,package);using(var reader=ZipFile.OpenRead(zip)){Check(reader.Entries.Count==8,"ZIP members");foreach(var entry in reader.Entries)using(var s=entry.Open()){var bytes=Bytes.Read(s,(int)entry.Length);Check(bytes.Length==entry.Length,"ZIP corrupt");}}
        File.WriteAllText(Path.Combine(root,"save.dat"),"KEEP SAVE");File.WriteAllText(Path.Combine(root,"settings.ini"),"KEEP SETTINGS");
        Install.Apply(root,package);Check(File.Exists(Path.Combine(root,"mods/Texture Studio/mod.ini")),"install missing");Refuses(()=>Install.Apply(root,package),"overwriting installed mod");
        string ini=Path.Combine(root,"mods/Texture Studio/mod.ini");byte[] correct=File.ReadAllBytes(ini);File.AppendAllText(ini,"changed");Refuses(()=>Install.Restore(root),"deleting changed file");File.WriteAllBytes(ini,correct);
        Install.Restore(root);Check(!File.Exists(ini)&&!File.Exists(Path.Combine(root,"dinput8.dll")),"restore left owned files");Check(File.ReadAllText(Path.Combine(root,"save.dat"))=="KEEP SAVE"&&File.ReadAllText(Path.Combine(root,"settings.ini"))=="KEEP SETTINGS","save/settings changed");Check(File.ReadAllBytes(Path.Combine(root,"Resource/Racers.xpac")).SequenceEqual(archive),"archive changed");
        File.WriteAllText(Path.Combine(root,"dinput8.dll"),"unknown loader");Refuses(()=>Install.Apply(root,package),"unknown loader overwrite");File.Delete(Path.Combine(root,"dinput8.dll"));
        string other=Path.Combine(root,"mods/Other");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"mod.ini"),"[mod]\nplan=plan.json");File.WriteAllText(Path.Combine(other,"plan.json"),"{\"archive_groups\":[{\"path\":\"Resource/Racers.xpac\"}]}");Refuses(()=>Install.Apply(root,package),"archive conflict");
        Refuses(()=>SafeFiles.ExportDestination(Path.Combine(root,"save.dat"),root,".textureproject"),"wrong extension");Refuses(()=>SafeFiles.ExportDestination(Path.Combine(root,"oops.zip"),root,".zip"),"export inside game");
        Refuses(()=>TextureProject.ValidateResource("Racers.xpac","Resource/Racers/../evil.zig"),"traversal");
        File.WriteAllText(Path.Combine(other,"plan.json"),"{\"archive_groups\":[{\"path\":\"Resource/Racers.xpac\",\"format\":\"xpac\",\"operations\":[{\"key\":\"00000001\"}]}]}");
        Install.CheckConflicts(root,package);Check(true,"independent members should coexist");
        File.WriteAllText(Path.Combine(other,"plan.json"),"{\"archive_groups\":[{\"path\":\"Resource/Racers.xpac\",\"format\":\"xpac\",\"operations\":[{\"key\":\""+asset.Key.ToString("X8")+"\"}]}]}");Refuses(()=>Install.CheckConflicts(root,package),"overlapping member");
        File.WriteAllText(Path.Combine(other,"plan.json"),"{\"archive_groups\":[]}");
        Install.Apply(root,package);Install.Restore(root);Check(File.Exists(Path.Combine(root,"dinput8.dll"))&&File.Exists(Path.Combine(root,"ChaoGarage.dll")),"other mod lost its loader");
        string loose=Path.Combine(root,asset.ResourceName);Directory.CreateDirectory(Path.GetDirectoryName(loose));File.WriteAllBytes(loose,model);Refuses(()=>Install.Apply(root,package),"loose resource override");
        // Retain a standalone package for independent native-loader validation.
        string staged=Path.Combine(root,"staged");foreach(var f in package.Files){string path=Path.Combine(staged,f.Key);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,f.Value);}
        File.WriteAllText(Path.Combine(output,"fixture-root.txt"),root);
        Console.WriteLine("PASS "+count+" export, project and installation checks. Fixture only.");
    }
}
