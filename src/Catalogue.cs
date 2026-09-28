using System;
using System.Collections.Generic;
using System.IO;

namespace AllStarsTextureStudio {
    public static class Catalogue {
        public static List<Asset> Read(string game){
            var result=new List<Asset>();
            string[] racers={"SonicCar","Tails","Knuckles","Amy","Shadow","Eggman","BigTheCat","AiAi","Samba","BillyHatcher","Beat","Ulala","BDJoe","RyoBike","RyoForklift","JackyBryant","HouseOfTheDead","BonanzaBrothers","OpaOpa","AlexKidd","ChuChuRocket","Banjo","MechaSonic","Avatar","Mii"};
            string[] effects={"SonicCar","Tails","Knuckles","Amy","Shadow","Eggman","BigTheCat","AiAi","Samba","BillyHatcher","Beat","Ulala","BDJoe","Ryo","Jacky","Zobio","Bonanza","Alex","ChuChu","Common"};
            var names=new Dictionary<string,List<string>>();
            names["Racers"]=new List<string>();names["Select"]=new List<string>();names["Tracks"]=new List<string>();
            foreach(string racer in racers){names["Racers"].Add(racer);names["Select"].Add(racer=="SonicCar"?"SonicSelect":racer+"Select");}
            foreach(string effect in effects)names["Racers"].Add("FX/"+effect+"_FX");
            names["Racers"].AddRange(new[]{"FX/Common_Trail","FX/TrackMarks","FX/Zobio_VFX"});names["Select"].Add("SonicAttract");
            foreach(string world in new[]{"SeasideHill","CasinoPark","FinalFortress","Samba","BillyHatcher","JetSetRadio","HouseOfTheDead","SMB"})
                foreach(string grade in new[]{"Easy","Medium","Hard","Arena"})names["Tracks"].Add(world+"_"+grade);
            names["Tracks"].AddRange(new[]{"JSR_Traffic","ViewerEnvironment","FX/Snowflake"});
            foreach(var archive in names){
                string file=Path.Combine(game,"Resource",archive.Key+".xpac");if(!File.Exists(file))continue;
                using(var index=new Archive(file))foreach(string name in archive.Value){
                    string resource="Resource/"+archive.Key+"/"+name+".zig";
                    var asset=new Asset{ArchiveName=archive.Key+".xpac",ResourceName=resource,Label=archive.Key+" / "+Nice(name)};
                    if(index.Entries.ContainsKey(asset.Key))result.Add(asset);
                }
            }
            return result;
        }
        private static string Nice(string name){switch(name){
            case "JetSetRadio_Easy":return "Shibuya Downtown";
            case "JetSetRadio_Medium":return "Rokkaku Hill";
            case "JetSetRadio_Hard":return "Shibuya Highway";
            case "SonicCar":return "Sonic";
            case "HouseOfTheDead":return "Zobio & Zobiko";
            case "BigTheCat":return "Big the Cat";
            case "OpaOpa":return "Opa-Opa";
            default:return name.Replace('_',' ');
        }}
    }
}
