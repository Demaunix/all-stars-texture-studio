using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace AllStarsTextureStudio {
    public sealed class DdsInfo {
        public int Width,Height,Mips,Length;public string Format;public uint Red,Green,Blue,Alpha;public int Bits;
        public int LevelSize(int w,int h){return Format=="BGRA"?checked(w*h*4):checked(Math.Max(1,(w+3)/4)*Math.Max(1,(h+3)/4)*(Format=="DXT1"?8:16));}
        public bool Compatible(DdsInfo b){return Width==b.Width&&Height==b.Height&&Mips==b.Mips&&Format==b.Format&&Red==b.Red&&Green==b.Green&&Blue==b.Blue&&Alpha==b.Alpha;}
        public static DdsInfo Read(byte[] b,int p) {
            Bytes.Need(p>=0&&p<=b.Length-128&&Bytes.U32(b,p)==0x20534444&&Bytes.U32(b,p+4)==124&&Bytes.U32(b,p+76)==32,"Not a supported DDS image.");
            var d=new DdsInfo{Height=checked((int)Bytes.U32(b,p+12)),Width=checked((int)Bytes.U32(b,p+16)),Mips=Math.Max(1,checked((int)Bytes.U32(b,p+28))),Bits=checked((int)Bytes.U32(b,p+88)),Red=Bytes.U32(b,p+92),Green=Bytes.U32(b,p+96),Blue=Bytes.U32(b,p+100),Alpha=Bytes.U32(b,p+104)};
            Bytes.Need(d.Width>0&&d.Height>0&&d.Width<=8192&&d.Height<=8192&&(long)d.Width*d.Height<=16777216,"Unsupported texture dimensions.");
            int levels=1;for(int n=Math.Max(d.Width,d.Height);n>1;n>>=1)levels++;
            Bytes.Need(d.Mips<=levels&&Bytes.U32(b,p+112)==0&&Bytes.U32(b,p+24)<=1,"Unsupported cube, volume, or mip layout.");
            uint flags=Bytes.U32(b,p+80),fourcc=Bytes.U32(b,p+84);
            if((flags&4)!=0){d.Format=fourcc==0x31545844?"DXT1":fourcc==0x33545844?"DXT3":fourcc==0x35545844?"DXT5":null;Bytes.Need(d.Format!=null,"This DDS compression format is not supported.");}
            else{
                Bytes.Need((flags&64)!=0&&d.Bits==32&&d.Green==0xff00&&((d.Red==0xff0000&&d.Blue==255)||(d.Red==255&&d.Blue==0xff0000))&&(d.Alpha==0||d.Alpha==0xff000000),"Unsupported uncompressed DDS pixel layout.");d.Format="BGRA";
            }
            long length=128;int w=d.Width,h=d.Height;
            for(int n=0;n<d.Mips;n++){length+=d.LevelSize(w,h);w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
            Bytes.Need(length<=b.Length-p,"Truncated DDS pixels or mipmaps.");d.Length=(int)length;return d;
        }
    }
    public static class Dds {
        private static uint Rgb(int r,int g,int b,int a){return (uint)(a<<24|r<<16|g<<8|b);}
        private static uint C565(int v){return Rgb(((v>>11)&31)*255/31,((v>>5)&63)*255/63,(v&31)*255/31,255);}
        private static int V565(uint c){return (int)((((c>>16)&255)*31+127)/255)<<11|(int)((((c>>8)&255)*63+127)/255)<<5|(int)(((c&255)*31+127)/255);}
        private static uint Mix(uint a,uint b,int x,int y,int den){return Rgb((int)(((a>>16&255)*x+(b>>16&255)*y)/den),(int)(((a>>8&255)*x+(b>>8&255)*y)/den),(int)(((a&255)*x+(b&255)*y)/den),255);}
        private static uint[] Palette(int a,int b,bool bc1){uint x=C565(a),y=C565(b);return bc1&&a<=b?new[]{x,y,Mix(x,y,1,1,2),0u}:new[]{x,y,Mix(x,y,2,1,3),Mix(x,y,1,2,3)};}
        private static byte[] AlphaPalette(int a,int b){var p=new byte[8];p[0]=(byte)a;p[1]=(byte)b;if(a>b){for(int n=1;n<=6;n++)p[n+1]=(byte)(((7-n)*a+n*b)/7);}else{for(int n=1;n<=4;n++)p[n+1]=(byte)(((5-n)*a+n*b)/5);p[6]=0;p[7]=255;}return p;}
        private static int Start(DdsInfo d,int level){int at=128,w=d.Width,h=d.Height;for(int n=0;n<level;n++){at+=d.LevelSize(w,h);w=Math.Max(1,w/2);h=Math.Max(1,h/2);}return at;}
        public static uint[] Pixels(byte[] b,int level) {
            var d=DdsInfo.Read(b,0);Bytes.Need(level>=0&&level<d.Mips,"Invalid mip level.");int w=Math.Max(1,d.Width>>level),h=Math.Max(1,d.Height>>level),at=Start(d,level);var pixels=new uint[w*h];
            if(d.Format=="BGRA"){
                for(int i=0;i<pixels.Length;i++){uint c=Bytes.U32(b,at+i*4);if(d.Red==255)c=(c&0xff00ff00)|((c&255)<<16)|(c>>16&255);if(d.Alpha==0)c|=0xff000000;pixels[i]=c;}return pixels;
            }
            int size=d.Format=="DXT1"?8:16;
            for(int by=0;by<(h+3)/4;by++)for(int bx=0;bx<(w+3)/4;bx++){
                int pos=at+(by*((w+3)/4)+bx)*size,cp=pos+(size==16?8:0);int a=b[cp]|b[cp+1]<<8,bb=b[cp+2]|b[cp+3]<<8;uint codes=Bytes.U32(b,cp+4);var palette=Palette(a,bb,size==8);
                ulong alpha=0;byte[] ap=null;if(d.Format=="DXT3"){for(int k=0;k<8;k++)alpha|=(ulong)b[pos+k]<<(8*k);}else if(d.Format=="DXT5"){for(int k=0;k<6;k++)alpha|=(ulong)b[pos+2+k]<<(8*k);ap=AlphaPalette(b[pos],b[pos+1]);}
                for(int n=0;n<16;n++){
                    int x=bx*4+n%4,y=by*4+n/4;if(x>=w||y>=h)continue;uint c=palette[(codes>>(n*2))&3];
                    if(d.Format=="DXT3")c=(c&0xffffff)|((uint)((alpha>>(n*4))&15)*17<<24);
                    if(d.Format=="DXT5")c=(c&0xffffff)|((uint)ap[(alpha>>(n*3))&7]<<24);pixels[y*w+x]=c;
                }
            }return pixels;
        }
        public static Bitmap Bitmap(byte[] data){var d=DdsInfo.Read(data,0);return FromPixels(Pixels(data,0),d.Width,d.Height);}
        public static Bitmap FromPixels(uint[] pixels,int width,int height){
            var b=new Bitmap(width,height,PixelFormat.Format32bppArgb);var locked=b.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try{int[] row=new int[width];for(int y=0;y<height;y++){for(int x=0;x<width;x++)row[x]=unchecked((int)pixels[y*width+x]);Marshal.Copy(row,0,IntPtr.Add(locked.Scan0,y*locked.Stride),width);}}finally{b.UnlockBits(locked);}return b;
        }
        public static uint[] FromBitmap(Bitmap b){
            var result=new uint[b.Width*b.Height];var locked=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{int[] row=new int[b.Width];for(int y=0;y<b.Height;y++){Marshal.Copy(IntPtr.Add(locked.Scan0,y*locked.Stride),row,0,b.Width);for(int x=0;x<b.Width;x++)result[y*b.Width+x]=unchecked((uint)row[x]);}}finally{b.UnlockBits(locked);}return result;
        }
        private static long Distance(uint a,uint b){int r=(int)(a>>16&255)-(int)(b>>16&255),g=(int)(a>>8&255)-(int)(b>>8&255),c=(int)(a&255)-(int)(b&255);return r*r*3+g*g*6+c*c*2;}
        private static void EncodeColors(byte[] target,int at,uint[] block,bool bc1){
            int r0=255,g0=255,b0=255,r1=0,g1=0,b1=0;bool transparent=false;int count=0;
            foreach(uint c in block){if(bc1&&(c>>24)<128){transparent=true;continue;}count++;int r=(int)(c>>16&255),g=(int)(c>>8&255),b=(int)(c&255);r0=Math.Min(r0,r);g0=Math.Min(g0,g);b0=Math.Min(b0,b);r1=Math.Max(r1,r);g1=Math.Max(g1,g);b1=Math.Max(b1,b);}
            if(count==0){r0=g0=b0=r1=g1=b1=0;}
            int a=V565(Rgb(r1,g1,b1,255)),b2=V565(Rgb(r0,g0,b0,255));
            if(transparent){if(a>b2){int t=a;a=b2;b2=t;}}
            else{if(a<b2){int t=a;a=b2;b2=t;}if(a==b2){if(a<65535)a++;else b2--;}}
            var palette=Palette(a,b2,bc1);uint indices=0;
            for(int n=0;n<16;n++){
                int best=0;if(transparent&&(block[n]>>24)<128)best=3;
                else{long error=long.MaxValue;for(int k=0;k<(transparent?3:4);k++){long distance=Distance(block[n],palette[k]);if(distance<error){error=distance;best=k;}}}indices|=(uint)best<<(2*n);
            }
            target[at]=(byte)a;target[at+1]=(byte)(a>>8);target[at+2]=(byte)b2;target[at+3]=(byte)(b2>>8);Bytes.Put(target,at+4,indices);
        }
        private static void EncodeLevel(byte[] target,DdsInfo d,int level,uint[] pixels,bool preserveAlpha,uint[] originalPixels=null){
            int w=Math.Max(1,d.Width>>level),h=Math.Max(1,d.Height>>level),at=Start(d,level);
            Bytes.Need(pixels.Length==w*h,"Wrong image dimensions.");
            if(d.Format=="BGRA"){
                for(int n=0;n<pixels.Length;n++){
                    uint c=pixels[n];if(d.Red==255)c=(c&0xff00ff00)|((c&255)<<16)|(c>>16&255);
                    if(preserveAlpha||d.Alpha==0)c=(c&0xffffff)|(Bytes.U32(target,at+n*4)&0xff000000);Bytes.Put(target,at+n*4,c);
                }return;
            }
            int size=d.Format=="DXT1"?8:16;var block=new uint[16];
            for(int by=0;by<(h+3)/4;by++)for(int bx=0;bx<(w+3)/4;bx++){
                if(originalPixels!=null){bool changed=false;for(int n=0;n<16;n++){int index=Math.Min(h-1,by*4+n/4)*w+Math.Min(w-1,bx*4+n%4);if(pixels[index]!=originalPixels[index]){changed=true;break;}}if(!changed)continue;}
                for(int n=0;n<16;n++)block[n]=pixels[Math.Min(h-1,by*4+n/4)*w+Math.Min(w-1,bx*4+n%4)];
                int pos=at+(by*((w+3)/4)+bx)*size;EncodeColors(target,pos+(size==16?8:0),block,size==8);
                if(preserveAlpha||size==8)continue;
                if(d.Format=="DXT3")for(int n=0;n<8;n++)target[pos+n]=(byte)((((block[n*2]>>24)+8)/17)|((((block[n*2+1]>>24)+8)/17)<<4));
                else{
                    int min=255,max=0;foreach(uint c in block){min=Math.Min(min,(int)(c>>24));max=Math.Max(max,(int)(c>>24));}target[pos]=(byte)max;target[pos+1]=(byte)min;var ap=AlphaPalette(max,min);ulong bits=0;
                    for(int n=0;n<16;n++){int best=0,error=1000;for(int k=0;k<8;k++){int delta=Math.Abs((int)(block[n]>>24)-ap[k]);if(delta<error){error=delta;best=k;}}bits|=(ulong)best<<(3*n);}for(int k=0;k<6;k++)target[pos+2+k]=(byte)(bits>>(k*8));
                }
            }
        }
        public static byte[] Recolor(byte[] original,ColorTransform transform){
            var d=DdsInfo.Read(original,0);var result=(byte[])original.Clone();if(transform.IsIdentity)return result;
            for(int level=0;level<d.Mips;level++){var pixels=Pixels(original,level);var previous=(uint[])pixels.Clone();for(int n=0;n<pixels.Length;n++)pixels[n]=transform.Apply(pixels[n]);EncodeLevel(result,d,level,pixels,true,previous);}return result;
        }
        public static byte[] Import(byte[] template,Bitmap image){
            var d=DdsInfo.Read(template,0);var result=(byte[])template.Clone();
            for(int level=0;level<d.Mips;level++){
                int w=Math.Max(1,d.Width>>level),h=Math.Max(1,d.Height>>level);
                using(var scaled=new Bitmap(w,h,PixelFormat.Format32bppArgb)){
                    using(var g=Graphics.FromImage(scaled)){g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;using(var attrs=new ImageAttributes()){attrs.SetWrapMode(WrapMode.TileFlipXY);g.DrawImage(image,new Rectangle(0,0,w,h),0,0,image.Width,image.Height,GraphicsUnit.Pixel,attrs);}}
                    EncodeLevel(result,d,level,FromBitmap(scaled),false);
                }
            }return result;
        }
    }
    public sealed class ColorTransform {
        public double Hue,Saturation=1,Brightness=1;public bool SelectRange;public double SelectedHue,Range=25;
        public bool IsIdentity{get{return Hue==0&&Saturation==1&&Brightness==1;}}
        public uint Apply(uint pixel){
            double r=(pixel>>16&255)/255.0,g=(pixel>>8&255)/255.0,b=(pixel&255)/255.0,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
            double hue=delta==0?0:max==r?60*((g-b)/delta%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);if(hue<0)hue+=360;
            double sat=max==0?0:delta/max;
            if(SelectRange){double distance=Math.Abs(hue-SelectedHue);distance=Math.Min(distance,360-distance);if(sat<0.12||distance>Range)return pixel;}
            hue=(hue+Hue)%360;if(hue<0)hue+=360;sat=Math.Max(0,Math.Min(1,sat*Saturation));max=Math.Max(0,Math.Min(1,max*Brightness));
            double c=max*sat,x=c*(1-Math.Abs(hue/60%2-1)),m=max-c;double rr,gg,bb;
            if(hue<60){rr=c;gg=x;bb=0;}else if(hue<120){rr=x;gg=c;bb=0;}else if(hue<180){rr=0;gg=c;bb=x;}else if(hue<240){rr=0;gg=x;bb=c;}else if(hue<300){rr=x;gg=0;bb=c;}else{rr=c;gg=0;bb=x;}
            return (pixel&0xff000000)|((uint)Math.Round((rr+m)*255)<<16)|((uint)Math.Round((gg+m)*255)<<8)|(uint)Math.Round((bb+m)*255);
        }
    }
}
