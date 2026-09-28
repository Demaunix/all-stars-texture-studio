using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AllStarsTextureStudio {
    public sealed class StudioForm : Form {
        private string game;private List<Asset> catalogue=new List<Asset>();private AssetDocument document;private TextureItem selected;
        private TextureProject project=new TextureProject();private byte[] basis,preview,clipboard;
        private bool busy,resetting,dirty;private Control workspace;private readonly ListBox assets=new ListBox(),textures=new ListBox();
        private readonly TextBox assetSearch=new TextBox(),textureSearch=new TextBox();
        private readonly CheckBox skies=new CheckBox{Text="Sky and cloud textures",AutoSize=true},range=new CheckBox{Text="Only the selected color range",AutoSize=true};
        private readonly PictureBox before=new PictureBox(),after=new PictureBox();
        private readonly TrackBar hue=Slider(-180,180,0),saturation=Slider(0,200,100),brightness=Slider(0,200,100),tolerance=Slider(1,90,25);
        private readonly Label status=new Label{AutoSize=false,Dock=DockStyle.Fill,Text="Open your game folder to begin.",Padding=new Padding(10)},details=new Label{AutoSize=true},values=new Label{AutoSize=true};
        private readonly Button keep=new Button{Text="Keep this edit",AutoSize=true},pick=new Button{Text="Choose color…",AutoSize=true};
        private double selectedHue;private readonly Timer debounce=new Timer{Interval=200};
        public StudioForm(string initialGame){
            Text="All-Stars Texture Studio";Width=1420;Height=900;MinimumSize=new Size(1050,720);Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;BackColor=Color.FromArgb(245,247,250);
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};workspace=root;root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Controls.Add(root);
            var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(10),WrapContents=true};root.Controls.Add(toolbar,0,0);
            Add(toolbar,"Open game…",OpenGame);Add(toolbar,"New project",NewProject);Add(toolbar,"Open project…",OpenProject);Add(toolbar,"Save project…",SaveProject);Add(toolbar,"Export mod…",ExportMod);Add(toolbar,"Help",()=>MessageBox.Show("1. Open the original PC game's folder.\n2. Choose a character or track, then a texture.\n3. Adjust colors or import an image. Click Keep this edit.\n4. Save your project, or export it as a texture mod.\n\nCopy/Paste texture lets you reuse another character or track texture. Images are fitted to the target dimensions and all mipmaps are rebuilt.\n\nChanging the sky texture does not change baked lighting, fog, shadows or sky geometry. Day/night scenes may require editing several related textures.\n\nThe editor reads original archives directly; no manual unpacking is needed.","Texture Studio help"));
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new Padding(8)};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,255));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,330));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.Controls.Add(layout,0,1);
            var assetPanel=Panel("CHARACTERS / COURSES",assetSearch,assets);layout.Controls.Add(assetPanel,0,0);
            var texturePanel=Panel("TEXTURES",textureSearch,textures);texturePanel.Controls.Add(skies,0,3);texturePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.Controls.Add(texturePanel,1,0);
            assets.Dock=textures.Dock=DockStyle.Fill;assets.IntegralHeight=textures.IntegralHeight=false;assets.HorizontalScrollbar=textures.HorizontalScrollbar=true;
            var editor=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(12,0,0,0)};editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));editor.RowStyles.Add(new RowStyle(SizeType.Percent,100));editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.Controls.Add(editor,2,0);
            editor.Controls.Add(details,0,0);var pictures=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};pictures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));pictures.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));pictures.RowStyles.Add(new RowStyle(SizeType.AutoSize));pictures.RowStyles.Add(new RowStyle(SizeType.Percent,100));pictures.Controls.Add(new Label{Text="Original",AutoSize=true},0,0);pictures.Controls.Add(new Label{Text="Edited preview",AutoSize=true},1,0);ConfigurePicture(before);ConfigurePicture(after);pictures.Controls.Add(before,0,1);pictures.Controls.Add(after,1,1);editor.Controls.Add(pictures,0,1);
            var sliders=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2};sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,92));sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));SliderRow(sliders,"Hue",hue,0);SliderRow(sliders,"Saturation",saturation,1);SliderRow(sliders,"Brightness",brightness,2);SliderRow(sliders,"Color range",tolerance,3);editor.Controls.Add(sliders,0,2);
            var selection=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};selection.Controls.Add(range);selection.Controls.Add(pick);selection.Controls.Add(values);editor.Controls.Add(selection,0,3);
            var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};buttons.Controls.Add(keep);Add(buttons,"Reset controls",ResetControls);Add(buttons,"Revert texture",Revert);Add(buttons,"Import image…",Import);Add(buttons,"Export PNG…",ExportImage);Add(buttons,"Copy texture",Copy);Add(buttons,"Paste texture",Paste);editor.Controls.Add(buttons,0,4);root.Controls.Add(status,0,2);
            assetSearch.TextChanged+=(s,e)=>FilterAssets();textureSearch.TextChanged+=(s,e)=>FilterTextures();skies.CheckedChanged+=(s,e)=>FilterTextures();assets.SelectedIndexChanged+=async(s,e)=>await SelectAsset();textures.SelectedIndexChanged+=(s,e)=>SelectTexture();
            foreach(var slider in new[]{hue,saturation,brightness,tolerance})slider.ValueChanged+=(s,e)=>SchedulePreview();range.CheckedChanged+=(s,e)=>SchedulePreview();
            pick.Click+=(s,e)=>{using(var dialog=new ColorDialog{FullOpen=true})if(dialog.ShowDialog(this)==DialogResult.OK){selectedHue=dialog.Color.GetHue();pick.BackColor=dialog.Color;range.Checked=true;SchedulePreview();}};
            Add(toolbar,"Install project",InstallProject);Add(toolbar,"Restore original look",RestoreInstalled);
            keep.Click+=(s,e)=>Commit();debounce.Tick+=async(s,e)=>{debounce.Stop();await UpdatePreview();};
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;return;}if(!DiscardProject())e.Cancel=true;};
            if(!String.IsNullOrEmpty(initialGame))Shown+=async(s,e)=>await LoadGame(initialGame);
        }
        private static TrackBar Slider(int min,int max,int value){return new TrackBar{Minimum=min,Maximum=max,Value=value,TickStyle=TickStyle.None,Dock=DockStyle.Fill,Height=32};}
        private static void SliderRow(TableLayoutPanel panel,string text,TrackBar slider,int row){panel.RowStyles.Add(new RowStyle(SizeType.Absolute,35));panel.Controls.Add(new Label{Text=text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);panel.Controls.Add(slider,1,row);}
        private static TableLayoutPanel Panel(string title,TextBox search,ListBox list){var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(4)};p.RowStyles.Add(new RowStyle(SizeType.AutoSize));p.RowStyles.Add(new RowStyle(SizeType.Absolute,36));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));p.Controls.Add(new Label{Text=title,AutoSize=true,Font=new Font("Segoe UI",10,FontStyle.Bold)},0,0);search.Dock=DockStyle.Fill;p.Controls.Add(search,0,1);p.Controls.Add(list,0,2);return p;}
        private void Add(Control panel,string text,Action action){var b=new Button{Text=text,AutoSize=true,Margin=new Padding(3)};b.Click+=(s,e)=>{if(!busy)Try(action);};panel.Controls.Add(b);}
        private static void ConfigurePicture(PictureBox p){p.Dock=DockStyle.Fill;p.SizeMode=PictureBoxSizeMode.Zoom;p.BackColor=Color.FromArgb(64,67,74);p.BorderStyle=BorderStyle.FixedSingle;}
        private static void SetImage(PictureBox p,Image image){var old=p.Image;p.Image=image;if(old!=null)old.Dispose();}
        private void SetBusy(bool value){busy=value;workspace.Enabled=!value;UseWaitCursor=value;}
        private void ClearTexture(){selected=null;basis=preview=null;debounce.Stop();SetImage(before,null);SetImage(after,null);details.Text="Choose a texture to preview and edit.";}
        private void Try(Action action){try{action();}catch(Exception e){MessageBox.Show(this,e.Message,"Texture Studio",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
        private void OpenGame(){if(!DiscardProject())return;using(var dialog=new OpenFileDialog{Title="Select All-Stars in its Steam installation folder",Filter="All-Stars executable|Sonic & SEGA All-Stars Racing.exe",FileName="Sonic & SEGA All-Stars Racing.exe"})if(dialog.ShowDialog(this)==DialogResult.OK){var ignored=LoadGame(Path.GetDirectoryName(dialog.FileName));}}
        private async Task LoadGame(string path){
            if(busy)return;SetBusy(true);status.Text="Reading archive indexes…";
            try{SafeFiles.Ordinary(path);var found=await Task.Run(()=>Catalogue.Read(path));Bytes.Need(found.Count>0,"No supported All-Stars texture archives were found in this folder.");game=Path.GetFullPath(path);catalogue=found;project=new TextureProject();dirty=false;document=null;ClearTexture();FilterAssets();FilterTextures();status.Text="Loaded "+found.Count+" assets. Select one to browse its textures.";}catch(Exception e){MessageBox.Show(this,e.Message,"Open game");}finally{SetBusy(false);}
        }
        private void FilterAssets(){resetting=true;var old=assets.SelectedItem;assets.BeginUpdate();assets.Items.Clear();foreach(var a in catalogue.Where(a=>a.Label.IndexOf(assetSearch.Text,StringComparison.OrdinalIgnoreCase)>=0))assets.Items.Add(a);assets.SelectedItem=old;assets.EndUpdate();resetting=false;}
        private async Task SelectAsset(){
            if(busy||resetting||assets.SelectedItem==null)return;var asset=(Asset)assets.SelectedItem;if(document!=null&&asset.ResourceName==document.Asset.ResourceName)return;
            if(!DiscardPreview()){resetting=true;assets.SelectedItem=document==null?null:document.Asset;resetting=false;return;}SetBusy(true);status.Text="Reading "+asset.Label+" textures…";ClearTexture();document=null;FilterTextures();
            try{var doc=await Task.Run(()=>{var d=AssetDocument.Load(game,asset);project.ApplyTo(d);return d;});document=doc;FilterTextures();status.Text=doc.Textures.Count+" textures loaded directly from the archive. "+project.Changes.Count+" saved edits in this project.";}
            catch(Exception e){MessageBox.Show(this,e.Message,"Open asset");}finally{SetBusy(false);}
        }
        private void FilterTextures(){
            resetting=true;textures.BeginUpdate();var old=selected;textures.Items.Clear();if(document!=null)foreach(var t in document.Textures.Where(t=>t.Name.IndexOf(textureSearch.Text,StringComparison.OrdinalIgnoreCase)>=0&&(!skies.Checked||SkyName(t.Name))))textures.Items.Add(t);textures.EndUpdate();if(old!=null&&textures.Items.Contains(old))textures.SelectedItem=old;resetting=false;
        }
        private static bool SkyName(string text){text=text.ToLowerInvariant();return (text.Contains("sky")&&!text.Contains("skyscr"))||text.Contains("cloud");}
        private void SelectTexture(){if(busy||resetting||textures.SelectedItem==null)return;var next=(TextureItem)textures.SelectedItem;if(next==selected)return;if(!DiscardPreview()){resetting=true;textures.SelectedItem=selected;resetting=false;return;}selected=next;basis=document.Dds(selected);preview=null;ResetControls();var original=new byte[selected.Info.Length];Buffer.BlockCopy(document.Original,selected.Offset,original,0,original.Length);SetImage(before,Dds.Bitmap(original));SetImage(after,Dds.Bitmap(basis));details.Text=selected.Name+"\n"+selected.Info.Width+" × "+selected.Info.Height+" · "+selected.Info.Format+" · "+selected.Info.Mips+" mip levels";}
        private void ResetControls(){resetting=true;debounce.Stop();hue.Value=0;saturation.Value=brightness.Value=100;tolerance.Value=25;range.Checked=false;resetting=false;if(basis!=null){preview=null;SetImage(after,Dds.Bitmap(basis));}values.Text="";}
        private void SchedulePreview(){if(resetting||basis==null||busy)return;debounce.Stop();debounce.Start();values.Text="Hue "+hue.Value+"° · Sat "+saturation.Value+"% · Light "+brightness.Value+"%";}
        private async Task UpdatePreview(){if(basis==null||busy)return;SetBusy(true);try{var transform=new ColorTransform{Hue=hue.Value,Saturation=saturation.Value/100.0,Brightness=brightness.Value/100.0,SelectRange=range.Checked,SelectedHue=selectedHue,Range=tolerance.Value};preview=await Task.Run(()=>transform.IsIdentity?null:Dds.Recolor(basis,transform));SetImage(after,Dds.Bitmap(preview??basis));}catch(Exception e){MessageBox.Show(this,e.Message,"Texture preview");}finally{SetBusy(false);}}
        private bool DiscardPreview(){if(preview==null&&!debounce.Enabled)return true;if(MessageBox.Show(this,"Discard the preview that has not been kept?","Uncommitted preview",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return false;ResetControls();return true;}
        private async void Commit(){if(selected==null||busy)return;bool pending=debounce.Enabled;debounce.Stop();if(pending)await UpdatePreview();if(preview==null)return;Try(()=>{document.Set(selected,preview);project.Remember(document);basis=document.Dds(selected);preview=null;dirty=true;ResetControls();status.Text="Edit kept. "+project.Changes.Count+" texture changes in the project.";});}
        private void Revert(){if(selected==null)return;document.Edits.Remove(selected.Offset);project.Remember(document);basis=document.Dds(selected);preview=null;dirty=true;ResetControls();status.Text="Original texture restored in the project.";}
        private void Import(){if(selected==null)return;using(var dialog=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.dds"})if(dialog.ShowDialog(this)==DialogResult.OK){Bytes.Need(new FileInfo(dialog.FileName).Length<=64*1024*1024,"Image file exceeds 64 MB.");byte[] data=File.ReadAllBytes(dialog.FileName);ResetControls();if(Path.GetExtension(dialog.FileName).Equals(".dds",StringComparison.OrdinalIgnoreCase)){var info=DdsInfo.Read(data,0);if(info.Compatible(selected.Info)&&info.Length==data.Length)preview=data;else using(var bmp=Dds.Bitmap(data))preview=Dds.Import(basis,bmp);}else using(var stream=new MemoryStream(data))using(var source=Image.FromStream(stream)){Bytes.Need(source.Width<=8192&&source.Height<=8192&&(long)source.Width*source.Height<=16777216,"Imported image dimensions are too large.");using(var bmp=new Bitmap(source))preview=Dds.Import(basis,bmp);}SetImage(after,Dds.Bitmap(preview));status.Text="Image imported and fitted to this texture. Click Keep this edit.";}}
        private async Task FlushPreview(){if(debounce.Enabled){debounce.Stop();await UpdatePreview();}}
        private async void ExportImage(){if(selected==null||busy)return;await FlushPreview();Try(()=>{using(var dialog=new SaveFileDialog{Filter="PNG image|*.png",FileName=Path.GetFileNameWithoutExtension(selected.Name)+".png"})if(dialog.ShowDialog(this)==DialogResult.OK)using(var bitmap=Dds.Bitmap(preview??basis))using(var buffer=new MemoryStream()){SafeFiles.ExportDestination(dialog.FileName,game,".png");bitmap.Save(buffer,ImageFormat.Png);SafeFiles.WriteAtomic(dialog.FileName,buffer.ToArray());}});}
        private async void Copy(){if(basis==null||busy)return;await FlushPreview();clipboard=(byte[])(preview??basis).Clone();status.Text="Texture copied. Choose another asset or texture, then Paste texture.";}
        private void Paste(){if(selected==null||clipboard==null)return;ResetControls();var info=DdsInfo.Read(clipboard,0);if(info.Compatible(selected.Info))preview=(byte[])clipboard.Clone();else using(var bitmap=Dds.Bitmap(clipboard))preview=Dds.Import(basis,bitmap);SetImage(after,Dds.Bitmap(preview));status.Text="Copied texture fitted to the target. Click Keep this edit.";}
        private bool DiscardProject(){return DiscardPreview()&&(!dirty||MessageBox.Show(this,"Discard the project changes that have not been saved?","Unsaved project",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes);}
        private void NewProject(){if(!DiscardProject())return;project=new TextureProject();dirty=false;document=null;ClearTexture();assets.ClearSelected();FilterTextures();status.Text="New project. Choose an asset.";}
        private void SaveProject(){if(!DiscardPreview())return;using(var dialog=new SaveFileDialog{Filter="Texture Studio project|*.textureproject",FileName="My texture mod.textureproject"})if(dialog.ShowDialog(this)==DialogResult.OK){SafeFiles.ExportDestination(dialog.FileName,game,".textureproject");project.Title=Path.GetFileNameWithoutExtension(dialog.FileName);project.Save(dialog.FileName);dirty=false;status.Text="Project saved.";}}
        private void OpenProject(){if(game==null){MessageBox.Show(this,"Open your game folder first.");return;}if(!DiscardProject())return;using(var dialog=new OpenFileDialog{Filter="Texture Studio project|*.textureproject"})if(dialog.ShowDialog(this)==DialogResult.OK){var loaded=TextureProject.Load(dialog.FileName);project=loaded;document=null;ClearTexture();assets.ClearSelected();dirty=false;FilterTextures();status.Text="Project opened: "+project.Changes.Count+" texture edits. Choose an asset to preview.";}}
        private async void ExportMod(){
            if(game==null||!DiscardPreview())return;
            using(var dialog=new SaveFileDialog{Filter="Texture mod ZIP|*.zip",FileName="My texture mod.zip"})if(dialog.ShowDialog(this)==DialogResult.OK){
                try{SafeFiles.ExportDestination(dialog.FileName,game,".zip");SetBusy(true);status.Text="Building and checking your texture mod…";await Task.Run(()=>ModExport.Zip(dialog.FileName,ModExport.Build(game,project)));status.Text="Texture mod exported. Your original archives are unchanged.";}
                catch(Exception e){MessageBox.Show(this,e.Message,"Export mod");}finally{SetBusy(false);}
            }
        }
        private async void InstallProject(){
            if(game==null||!DiscardPreview())return;
            try{Install.CheckGame(game);SetBusy(true);status.Text="Checking and installing your texture project…";await Task.Run(()=>Install.Apply(game,ModExport.Build(game,project)));status.Text="Installed. Launch All-Stars through Steam to see your textures. Restore original look removes this mod.";}
            catch(Exception e){MessageBox.Show(this,e.Message,"Install texture mod");}finally{SetBusy(false);}
        }
        private void RestoreInstalled(){if(game==null)return;Install.CheckGame(game);Install.Restore(game);status.Text="Texture Studio mod removed. Original archives, other mods and saves are unchanged.";}
        protected override void Dispose(bool disposing){if(disposing){debounce.Dispose();if(before.Image!=null)before.Image.Dispose();if(after.Image!=null)after.Image.Dispose();}base.Dispose(disposing);}
    }
}
