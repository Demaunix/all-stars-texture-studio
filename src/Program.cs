using System;
using System.Windows.Forms;

namespace AllStarsTextureStudio {
    static class Program {
        [STAThread] static void Main(string[] args){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException+=(sender,eventArgs)=>MessageBox.Show(eventArgs.Exception.Message,"Texture Studio",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            Application.Run(new StudioForm(args.Length==1?args[0]:null));
        }
    }
}
