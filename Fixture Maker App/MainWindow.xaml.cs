using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Leap71.ShapeKernel;
using Microsoft.Win32;
using PicoGK;



namespace Fixture_Maker_App
{
    public enum BaseplateType { None, Baseplate }

    public partial class MainWindow : Window
    {
        private ModelVisual3D _loadedModel;
        private MeshGeometry3D _savedMesh;
        private string _filename = "";
        private double _zOffset = 10;
        private double _rotation = 0;
        private double _extTol = 0.1;
        private BaseplateType _baseplate = BaseplateType.None;
        private ModelVisual3D _fixtureModel;

        private ModelVisual3D SceneRoot = new ModelVisual3D();
        private ModelVisual3D RingGroup = new ModelVisual3D();
        private ModelVisual3D GroundPlane = new ModelVisual3D();
        private ModelVisual3D XRing, YRing, ZRing;

        private Transform3DGroup modelTransformGroup = new Transform3DGroup();
        private Transform3DGroup rotateTransformGroup = new Transform3DGroup();
        private TranslateTransform3D zOffsetTransform = new TranslateTransform3D(0, 0, 0);
        private Transform3D _centerTransform = Transform3D.Identity;

        private Point lastMousePos;
        private bool rotating = false;
        private Vector3D rotationAxis = new Vector3D();
        private Point rotationCenter2D = new Point(0, 0);
        private ModelVisual3D selectedRing;
        private double cumulativeRotationDegrees = 0;
        private double lastSnappedRotationDegrees = 0;

        public MainWindow()
        {
            InitializeComponent();

            MainViewPort.MouseDown += MainViewPort_MouseDown;
            MainViewPort.MouseMove += MainViewPort_MouseMove;
            MainViewPort.MouseUp += MainViewPort_MouseUp;

            AddRotationHandles();

            MainViewPort.Children.Add(new DefaultLights());
            MainViewPort.Children.Add(SceneRoot);

            RingGroup.Children.Add(XRing);
            RingGroup.Children.Add(YRing);
            RingGroup.Children.Add(ZRing);
            RingGroup.Transform = zOffsetTransform;
            SceneRoot.Children.Add(RingGroup);
            
            this.Loaded += (s, e) => {
                MainViewPort.ZoomExtents();
            };

        }
        private void OnLoadStlClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "STL Files (*.stl)|*.stl" };
            if (dlg.ShowDialog() != true) return;

            _filename = dlg.FileName;
            FileNameText.Content = $"'{System.IO.Path.GetFileName(_filename)}' loaded.";
            LoadStl(_filename);
            ApplyTransform();
        }
        private MeshGeometry3D GetMeshGeometry(Model3DGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is GeometryModel3D geomModel)
                {
                    MeshGeometry3D mesh = (MeshGeometry3D)geomModel.Geometry;
                    return mesh;
                }
            }
            return new MeshGeometry3D();
        }
        private void LoadStl(string filePath)
        {
            var reader = new StLReader();
            var model = reader.Read(filePath);
            Rect3D bounds = model.Bounds;

            var centerOffset = new Vector3D(
                bounds.X + bounds.SizeX / 2,
                bounds.Y + bounds.SizeY / 2,
                bounds.Z + bounds.SizeZ / 2);
            _centerTransform = new TranslateTransform3D(-centerOffset);
            model.Transform = _centerTransform;

            _savedMesh = GetMeshGeometry(model);

            _loadedModel = new ModelVisual3D { Content = model };

            rotateTransformGroup = new Transform3DGroup();
            zOffsetTransform = new TranslateTransform3D(0, 0, _zOffset);

            modelTransformGroup = new Transform3DGroup();
            modelTransformGroup.Children.Add(rotateTransformGroup);
            modelTransformGroup.Children.Add(zOffsetTransform);
            _loadedModel.Transform = modelTransformGroup;

            double maxDimension = Math.Sqrt(bounds.SizeX*bounds.SizeX + bounds.SizeY*bounds.SizeY + bounds.SizeZ*bounds.SizeZ);
            double radius = 0.6 * maxDimension;
            double thickness = radius * 0.05;

            GroundPlane = CreateGroundPlane(1.5 * maxDimension);
            GroundPlane.Transform = new TranslateTransform3D(0, 0, 0);

            SceneRoot.Children.Clear();
            AddRotationHandles(radius, thickness);

            SceneRoot.Children.Add(_loadedModel);

            RingGroup = new ModelVisual3D();
            RingGroup.Children.Add(XRing);
            RingGroup.Children.Add(YRing);
            RingGroup.Children.Add(ZRing);
            RingGroup.Transform = zOffsetTransform;
            SceneRoot.Children.Add(RingGroup);

            if (BaseplateComboBox.SelectedIndex == (int)BaseplateType.None)
                SceneRoot.Children.Add(GroundPlane);

            MainViewPort.ZoomExtents();
        }
         private void ApplyTransform()
        {
            if (_loadedModel == null || _savedMesh == null) return;
            double bottomZ = ComputeBottomZ();
            zOffsetTransform.OffsetZ = _zOffset - bottomZ;
        }
        private void OnTransformChanged(object sender, TextChangedEventArgs e)
        {
            double.TryParse(ZOffsetBox.Text, out _zOffset);
            ApplyTransform();
        }
        private void OnResetRotationClicked(object sender, RoutedEventArgs e)
        {
            rotateTransformGroup.Children.Clear();
            ApplyTransform();
        }
        private void OnGenerateFixtureClicked(object sender, RoutedEventArgs e)
        {
            if (_filename == "")
            {
                MessageBox.Show("Please load an STL file first.");
                return;
            }

            MessageBox.Show("Fixture generation triggered (backend connection goes here).");


            double.TryParse(ExtTol.Text, out _extTol);
            double.TryParse(FixtureThick.Text, out double fixtureThick);
            _baseplate = (BaseplateType)BaseplateComboBox.SelectedIndex;

            // export the loaded and transformed model to STL for PicoGK upload
            var exporter = new StlExporter();
            using (var stream = File.Create("temp.stl"))
            {
                exporter.Export(_loadedModel, stream);
            }

            try
            {
                using PicoGK.Library oLibrary = new(0.5f);
                {
                    Mesh mesh = Mesh.mshFromStlFile("temp.stl");

                    // generate the fixture objects
                    Fixture.BasePlate oBasePlate = new Fixture.BasePlate();
                    Fixture.Object oObject = new Fixture.Object(
                        mesh,
                        (float)_zOffset,
                        10f,
                        3f,
                        5,
                        3f
                    );

                    Fixture oFixture = new Fixture(oBasePlate, oObject);

                    // save fixture to STL temporarily
                    string tempPath = Path.Combine(Path.GetTempPath(), "temp_fixture.stl");
                    oFixture.asVoxels().mshAsMesh().SaveToStlFile(tempPath);

                    StLReader reader = new StLReader();
                    Model3D model = reader.Read(tempPath);
                    _fixtureModel = new ModelVisual3D { Content = model };

                    //GeometryModel3D geoModel = new GeometryModel3D
                    //{
                    //    Geometry = _fixtureModel,
                    //    Material = MaterialHelper.CreateMaterial(Brushes.Red)
                    //};
                    SceneRoot.Children.Clear();
                    SceneRoot.Children.Add(_loadedModel);
                    SceneRoot.Children.Add(_fixtureModel);

                    // cleanup temp file
                    if (File.Exists(tempPath))
                            File.Delete(tempPath);
                }
                MessageBox.Show("Generated Fixture successfully.");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                MessageBox.Show("Fixture generation failed.");

            }



        }
        private void MainViewPort_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(MainViewPort);
            var hit = MainViewPort.Viewport.FindHits(pos);
            if (hit.Count == 0) return;

            var visual = hit[0].Visual as ModelVisual3D;
            if (visual == XRing) rotationAxis = new Vector3D(1, 0, 0);
            else if (visual == YRing) rotationAxis = new Vector3D(0, 1, 0);
            else if (visual == ZRing) rotationAxis = new Vector3D(0, 0, 1);
            else return;

            selectedRing = visual;
            rotating = true;
            lastMousePos = pos;
            Mouse.Capture(MainViewPort);
            rotationCenter2D = ProjectToScreen(new Point3D(0, 0, _zOffset));

            cumulativeRotationDegrees = 0;
            lastSnappedRotationDegrees = 0;
        }
        private void MainViewPort_MouseMove(object sender, MouseEventArgs e)
        {
            if (!rotating || _loadedModel == null) return;

            Point currentPos = e.GetPosition(MainViewPort);
            Vector v1 = lastMousePos - rotationCenter2D;
            Vector v2 = currentPos - rotationCenter2D;

            double angle1 = Math.Atan2(v1.Y, v1.X);
            double angle2 = Math.Atan2(v2.Y, v2.X);
            double deltaAngle = (angle2 - angle1) * 180 / Math.PI;

            Vector3D viewDir = GetCameraViewDirection(MainViewPort.Camera);
            if (Vector3D.DotProduct(rotationAxis, viewDir) < 0)
                deltaAngle = -deltaAngle;

            cumulativeRotationDegrees += deltaAngle;
            double angleToApply = deltaAngle;

            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                double snapped = Math.Round(cumulativeRotationDegrees / 15.0) * 15.0;
                angleToApply = snapped - lastSnappedRotationDegrees;
                lastSnappedRotationDegrees = snapped;
                if (Math.Abs(angleToApply) < 1e-3) return;
            }

            var rotation = new AxisAngleRotation3D(rotationAxis, angleToApply);
            rotateTransformGroup.Children.Add(new RotateTransform3D(rotation));
            ApplyTransform();
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
            var builder = new MeshBuilder(true, false);
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

            var model = new GeometryModel3D(mesh, material)
            {
                Transform = axis switch
                {
                    var v when v == new Vector3D(1, 0, 0) => new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 90)),
                    var v when v == new Vector3D(0, 1, 0) => new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90)),
                    _ => Transform3D.Identity
                }
            };

            return new ModelVisual3D { Content = model };
        }
        private ModelVisual3D CreateGroundPlane(double size)
        {
            var builder = new MeshBuilder();
            double half = size / 2;

            builder.AddQuad(
                new Point3D(-half, -half, 0),
                new Point3D(half, -half, 0),
                new Point3D(half, half, 0),
                new Point3D(-half, half, 0));

            var transparentBrush = new SolidColorBrush(Color.FromArgb(120, 180, 180, 180));
            transparentBrush.Freeze();
            var material = MaterialHelper.CreateMaterial(transparentBrush);

            return new ModelVisual3D
            {
                Content = new GeometryModel3D
                {
                    Geometry = builder.ToMesh(),
                    Material = material,
                    BackMaterial = material
                }
            };
        }
        private double ComputeBottomZ()
        {
            if (_savedMesh == null) return 0;
            var fullTransform = new MatrixTransform3D(_centerTransform.Value * rotateTransformGroup.Value);

            double minZ = double.MaxValue;
            foreach (var p in _savedMesh.Positions)
                minZ = Math.Min(minZ, fullTransform.Transform(p).Z);

            return minZ;
        }
        private Point ProjectToScreen(Point3D point)
        {
            Matrix3D matrix = GetTotalTransformMatrix(MainViewPort);
            var point4D = new Point4D(point.X, point.Y, point.Z, 1.0);
            point4D = matrix.Transform(point4D);

            if (Math.Abs(point4D.W) < 1e-6)
                return new Point(MainViewPort.ActualWidth / 2, MainViewPort.ActualHeight / 2);

            double x = point4D.X / point4D.W;
            double y = point4D.Y / point4D.W;

            return new Point(
                (x + 1) * 0.5 * MainViewPort.ActualWidth,
                (-y + 1) * 0.5 * MainViewPort.ActualHeight);
        }
        private Matrix3D GetTotalTransformMatrix(HelixViewport3D viewport)
        {
            var vm = CameraHelper.GetViewMatrix(viewport.Camera);
            var pm = CameraHelper.GetProjectionMatrix(viewport.Camera, viewport.ActualWidth / viewport.ActualHeight);
            return vm * pm;
        }
        private Vector3D GetCameraViewDirection(Camera camera)
        {
            return camera is ProjectionCamera pc ? pc.LookDirection : new Vector3D(0, 0, -1);
        }
    }
}
