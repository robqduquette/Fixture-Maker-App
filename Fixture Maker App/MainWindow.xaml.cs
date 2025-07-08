using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xaml.Schema;
using HelixToolkit.Wpf;
using Microsoft.Win32;

namespace Fixture_Maker_App
{
    public enum BaseplateType
    {
        None,
        Baseplate1
    }

    public partial class MainWindow : Window
    {
        private ModelVisual3D _loadedModel;
        private string _filename;
        private double _zOffset;
        private double _rotation;
        private double _extTol;
        private BaseplateType _baseplate;

        Point lastMousePos;
        bool rotating = false;
        Vector3D rotationAxis = new Vector3D();
        ModelVisual3D selectedRing;
        private ModelVisual3D XRing, YRing, ZRing;
        private ModelVisual3D SceneRoot = new ModelVisual3D();

        public MainWindow()
        {
            InitializeComponent();
            _filename = "";
            _zOffset = 10;
            _rotation = 0;
            _extTol = 0.1;
            _baseplate = BaseplateType.None;

            MainViewPort.MouseDown += MainViewPort_MouseDown;
            MainViewPort.MouseMove += MainViewPort_MouseMove;
            MainViewPort.MouseUp += MainViewPort_MouseUp;

            AddRotationHandles();

            // Add lights and root container to viewport
            MainViewPort.Children.Add(new DefaultLights());
            MainViewPort.Children.Add(SceneRoot);

            // Add rings to root scene (only once)
            SceneRoot.Children.Add(XRing);
            SceneRoot.Children.Add(YRing);
            SceneRoot.Children.Add(ZRing);
        }

        private void OnLoadStlClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "STL Files (*.stl)|*.stl" };
            if (dlg.ShowDialog() == true)
            {
                _filename = dlg.FileName;
                FileNameText.Content = $"'{System.IO.Path.GetFileName(_filename)}' loaded.";
                LoadStl(_filename);
                ApplyTransform();
            }
        }

        private void LoadStl(string filePath)
        {
            var reader = new StLReader();
            var model = reader.Read(filePath);

            // Center the model at the origin
            Rect3D bounds = model.Bounds;
            Vector3D centerOffset = new Vector3D(
                bounds.X + bounds.SizeX / 2,
                bounds.Y + bounds.SizeY / 2,
                bounds.Z + bounds.SizeZ / 2
            );

            var centerTransform = new TranslateTransform3D(-centerOffset);
            model.Transform = centerTransform;


            _loadedModel = new ModelVisual3D { Content = model };

            // Compute bounds 
            double maxDimension = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            double radius = 0.6 * maxDimension; // slightly larger than object
            double thickness = radius * 0.05;

            // Rebuild rings to fit model
            SceneRoot.Children.Clear();
            AddRotationHandles(radius, thickness);

            SceneRoot.Children.Add(_loadedModel);
            SceneRoot.Children.Add(XRing);
            SceneRoot.Children.Add(YRing);
            SceneRoot.Children.Add(ZRing);
        }


        private void ApplyTransform()
        {
            if (_loadedModel == null) return;

            if (double.TryParse(ZOffset.Text, out _zOffset) &&
                double.TryParse(Rotation.Text, out _rotation))
            {
                var transformGroup = new Transform3DGroup();
                transformGroup.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), _rotation)));
                transformGroup.Children.Add(new TranslateTransform3D(0, 0, _zOffset));
                _loadedModel.Transform = transformGroup;
            }
        }

        private void OnTransformChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTransform();
        }

        private void OnGenerateFixtureClicked(object sender, RoutedEventArgs e)
        {
            if (_filename == "")
            {
                MessageBox.Show("Please load an STL file first.");
                return;
            }

            double.TryParse(ExtTol.Text, out _extTol);
            double.TryParse(FixtureThick.Text, out double fixtureThick);
            _baseplate = (BaseplateType)BaseplateComboBox.SelectedIndex;

            // Connect to backend
            // FixtureGenerator.Generate(_filename, _zOffset, _rotation, _ext_tol, fixtureThick, _baseplate);

            MessageBox.Show("Fixture generation triggered (backend connection goes here).");
        }

        private void MainViewPort_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(MainViewPort);
            var hit = MainViewPort.Viewport.FindHits(pos);
            if (hit.Count > 0)
            {
                var visual = hit[0].Visual as ModelVisual3D;
                if (visual == XRing) { rotationAxis = new Vector3D(1, 0, 0); }
                else if (visual == YRing) { rotationAxis = new Vector3D(0, -1, 0); }
                else if (visual == ZRing) { rotationAxis = new Vector3D(0, 0, -1); }
                else return;

                selectedRing = visual;
                rotating = true;
                lastMousePos = pos;
                Mouse.Capture(MainViewPort);
            }
        }

        private void MainViewPort_MouseMove(object sender, MouseEventArgs e)
        {
            if (!rotating || _loadedModel == null) return;

            Point currentPos = e.GetPosition(MainViewPort);
            double dx = currentPos.X - lastMousePos.X;
            double angle = dx;  // scale this for sensitivity

            var rotation = new AxisAngleRotation3D(rotationAxis, angle);
            var rotateTransform = new RotateTransform3D(rotation);

            var tg = new Transform3DGroup();
            tg.Children.Add(_loadedModel.Transform);
            tg.Children.Add(rotateTransform);

            _loadedModel.Transform = tg;

            lastMousePos = currentPos;
        }

        private void MainViewPort_MouseUp(object sender, MouseButtonEventArgs e)
        {
            rotating = false;
            Mouse.Capture(null);
        }

        private void AddRotationHandles(double radius = 30, double thickness = 1)
        {
            XRing = CreateRing(radius, thickness, Brushes.Red, new Vector3D(1, 0, 0));
            YRing = CreateRing(radius, thickness, Brushes.Green, new Vector3D(0, 1, 0));
            ZRing = CreateRing(radius, thickness, Brushes.Blue, new Vector3D(0, 0, 1));
        }


        private ModelVisual3D CreateRing(double radius, double thickness, Brush color, Vector3D axis)
        {
            var builder = new MeshBuilder(true, false); // flip normals for inside/outside visibility
            builder.AddTorus(2 * radius, thickness, 64, 8);
            var mesh = builder.ToMesh();
            var material = new MaterialGroup
            {
                Children = new MaterialCollection
                {
                    new EmissiveMaterial(color),
                    new DiffuseMaterial(color)
                }
            };
            var model = new GeometryModel3D(mesh, material);

            Transform3D transform = Transform3D.Identity;

            if (axis == new Vector3D(1, 0, 0))
                transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 90));
            else if (axis == new Vector3D(0, 1, 0))
                transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90));

            model.Transform = transform;
            return new ModelVisual3D { Content = model };
        }

    }
}
