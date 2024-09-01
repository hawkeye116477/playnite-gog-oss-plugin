using Playnite.SDK;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace CometLibrary
{
    /// <summary>
    /// Interaction logic for CometLibrarySettingsView.xaml
    /// </summary>
    public partial class CometLibrarySettingsView : UserControl
    {
        private IPlayniteAPI playniteAPI = API.Instance;
        public CometLibrarySettingsView()
        {
            InitializeComponent();
        }

        private void ChooseGamePathBtn_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ChooseLauncherBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{ResourceProvider.GetString(LOC.Comet3P_PlayniteExecutableTitle)}|*.exe");
            if (file != "")
            {
                SelectedLauncherPathTxt.Text = file;
            }
        }
    }
}
