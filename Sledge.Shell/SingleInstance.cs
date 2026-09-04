using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Microsoft.VisualBasic.ApplicationServices;

namespace Sledge.Shell
{
    public class SingleInstance : WindowsFormsApplicationBase
    {
        public SingleInstance(Form form)
        {
            IsSingleInstance = true;
            MainForm = form;
        }

        protected override void OnStartupNextInstance(StartupNextInstanceEventArgs e)
        {
            e.BringToForeground = false;
            base.OnStartupNextInstance(e);
            MainForm.Invoke(() =>
            {
                if (MainForm.WindowState == FormWindowState.Minimized)
                {
                    MainForm.WindowState = FormWindowState.Normal;
                }
                MainForm.Activate();
            });
            Oy.Publish("Shell:InstanceOpened", e.CommandLine.ToList());
        }
    }
}
