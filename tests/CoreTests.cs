using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using AllStarsTextureStudio;

class CoreTests {
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static byte[] Texture(string format,int w,int h,int mips){
        int bytes=128,x=w,y=h;for(int i=0;i<mips;i++){bytes+=format=="BGRA"?x*y*4:Math.Max(1,(x+3)/4)*Math.Max(1,(y+3)/4)*(format=="DXT1"?8:16);x=Math.Max(1,x/2);y=Math.Max(1,y/2);}
        var d=new byte[bytes];Bytes.Put(d,0,0x20534444);Bytes.Put(d,4,124);Bytes.Put(d,8,0x21007);Bytes.Put(d,12,(uint)h);Bytes.Put(d,16,(uint)w);Bytes.Put(d,28,(uint)mips);Bytes.Put(d,76,32);Bytes.Put(d,108,0x401008);
        if(format=="BGRA"){Bytes.Put(d,80,65);Bytes.Put(d,88,32);Bytes.Put(d,92,0xff0000);Bytes.Put(d,96,0xff00);Bytes.Put(d,100,255);Bytes.Put(d,104,0xff000000);}
        else{Bytes.Put(d,80,4);Bytes.Put(d,84,format=="DXT1"?0x31545844u:format=="DXT3"?0x33545844u:0x35545844u);}return d;
    }
    static void Main(string[] args){
        try{
            Directory.CreateDirectory(args[0]);
            foreach(string format in new[]{"BGRA","DXT1","DXT3","DXT5"})foreach(var dim in new[]{new[]{4,4,3},new[]{7,5,3},new[]{1,1,1}}){
                var source=Texture(format,dim[0],dim[1],dim[2]);using(var image=new Bitmap(dim[0],dim[1],PixelFormat.Format32bppArgb)){
                    for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)image.SetPixel(x,y,Color.FromArgb(x%2==0?255:0,220,20,30));
                    var encoded=Dds.Import(source,image);Check(encoded.Length==source.Length,"layout changed");
                    Check(Dds.Recolor(encoded,new ColorTransform()).SequenceEqual(encoded),"identity changed bytes");
                    var edited=Dds.Recolor(encoded,new ColorTransform{Hue=120});
                    for(int mip=0;mip<dim[2];mip++){
                        var before=Dds.Pixels(encoded,mip);var after=Dds.Pixels(edited,mip);
                        Check(before.Select(c=>c>>24).SequenceEqual(after.Select(c=>c>>24)),"recolor alpha changed");
                        Check(after.Any(c=>(c>>24)>0&&(c>>8&255)>(c>>16&255)),"hue did not turn red to green");
                    }
                    var imported=DdsInfo.Read(encoded,0);Check(imported.Compatible(DdsInfo.Read(source,0)),"import format mismatch");
                    File.WriteAllBytes(Path.Combine(args[0],format+"-"+dim[0]+".dds"),encoded);
                    using(var bmp=Dds.Bitmap(encoded))bmp.Save(Path.Combine(args[0],format+"-"+dim[0]+".png"),ImageFormat.Png);
                }
            }
            var range=new ColorTransform{Hue=120,SelectRange=true,SelectedHue=0,Range=15};
            Check(range.Apply(0xff0000ff)==0xff0000ff,"selection touched blue");Check(range.Apply(0xffffffff)==0xffffffff,"selection touched white");Check(range.Apply(0xffff0000)==0xff00ff00,"selection missed red");
            var rand=new Random(5);var raw=new byte[150000];rand.NextBytes(raw);Check(Bytes.DecodeMember(Bytes.EncodeMember(raw)).SequenceEqual(raw),"compression roundtrip");
            var corrupt=Bytes.EncodeMember(raw);corrupt[corrupt.Length-1]^=1;try{Bytes.DecodeMember(corrupt);throw new Exception("corruption admitted");}catch(InvalidDataException){checks++;}
            foreach(int offset in new[]{0,4,76,112}){var d=Texture("DXT1",4,4,1);Bytes.Put(d,offset,0x12345678);try{DdsInfo.Read(d,0);throw new Exception("bad DDS admitted");}catch(InvalidDataException){checks++;}}
            if(args.Length>1){
                var assets=Catalogue.Read(args[1]);Check(assets.Count>60,"incomplete real catalogue");int textures=0;
                foreach(string needle in new[]{"SonicCar.zig","JetSetRadio_Easy.zig","JetSetRadio_Medium.zig"}){
                    var asset=assets.Single(a=>a.ResourceName.EndsWith("/"+needle));var doc=AssetDocument.Load(args[1],asset);
                    Check(doc.Textures.Count>5,"missing real textures");Check(doc.Modified().SequenceEqual(doc.Original),"read changed resource");textures+=doc.Textures.Count;
                    Console.WriteLine(asset.Label+": "+doc.Textures.Count+" textures; sky names: "+String.Join(", ",doc.Textures.Where(t=>t.Name.ToLowerInvariant().Contains("sky")).Select(t=>t.Name)));
                    foreach(var item in doc.Textures){var dds=doc.Dds(item);Check(dds.Length==item.Info.Length,"real DDS range");using(var bitmap=Dds.Bitmap(dds))Check(bitmap.Width==item.Info.Width,"real DDS preview");}
                    var first=doc.Textures[0];var data=doc.Dds(first);var edited=Dds.Recolor(data,new ColorTransform{Hue=80});doc.Set(first,edited);var merged=doc.Modified();
                    Check(merged.Take(first.Offset).SequenceEqual(doc.Original.Take(first.Offset))&&merged.Skip(first.Offset+first.Info.Length).SequenceEqual(doc.Original.Skip(first.Offset+first.Info.Length)),"unrelated resource bytes changed");
                    doc.Set(first,data);Check(doc.Edits.Count==0,"revert did not clear edit");
                    File.WriteAllBytes(Path.Combine(args[0],needle+"-first.dds"),data);using(var bitmap=Dds.Bitmap(data))bitmap.Save(Path.Combine(args[0],needle+"-first.png"));
                }
                Console.WriteLine("Actual textures decoded: "+textures);
            }
            Console.WriteLine("PASS "+checks+" checks. No game launched or installed files changed.");
            ExportTests.Run(args[0]);
        }catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}
