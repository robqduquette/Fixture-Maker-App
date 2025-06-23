using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using HelixToolkit.Wpf;


namespace Fixture_Maker_App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnLoadStlClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "STL Files (*.stl)|*.stl";

            if (dlg.ShowDialog() == true)
            {
                string filePath = dlg.FileName;
                LoadStl(filePath);
                FileNameText.Content = "File \'" + System.IO.Path.GetFileName(filePath) + "\' loaded.";
            }

            Console.WriteLine("Testing");
        }

        private void LoadStl(string filePath)
        {
            var reader = new StLReader();
            var model = reader.Read(filePath);

            var visual = new ModelVisual3D { Content = model };
            MainViewPort.Children.Clear();
            MainViewPort.Children.Add(new DefaultLights());
            MainViewPort.Children.Add(visual);
            MainViewPort.ZoomExtents();
        }
    }
}