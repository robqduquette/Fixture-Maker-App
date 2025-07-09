using System;
using System.Reflection;
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
        Point rotationCenter2D = new Point(0, 0);
        private Transform3DGroup modelTransformGroup = new Transform3DGroup();
        private Transform3DGroup rotateTransformGroup = new Transform3DGroup();
        private TranslateTransform3D zOffsetTransform = new TranslateTransform3D(0, 0, 0);
        private ModelVisual3D RingGroup = new ModelVisual3D(); 
        private double cumulativeRotationDegrees = 0;
        private double lastSnappedRotationDegrees = 0;
        private ModelVisual3D GroundPlane = new ModelVisual3D();
        private MeshGeometry3D _savedMesh;
        private Transform3D _centerTransform = Transform3D.Identity;


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

            // Add rings to scene
            RingGroup = new ModelVisual3D();
            RingGroup.Children.Add(XRing);
            RingGroup.Children.Add(YRing);
            RingGroup.Children.Add(ZRing);

            // Apply same offset as model
            RingGroup.Transform = zOffsetTransform;

            SceneRoot.Children.Add(RingGroup);

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
            _centerTransform = centerTransform;
            // apply to model for viewer
            model.Transform = _centerTransform;

            // Save the mesh for bottom-point calculations
            // Traverse the model group to find the first GeometryModel3D
            if (model is Model3DGroup group)
            {
                foreach (var child in group.Children)
                {
                    if (child is GeometryModel3D geomModel)
                    {
                        _savedMesh = geomModel.Geometry as MeshGeometry3D;
                        break;
                    }
                }
            }

            _loadedModel = new ModelVisual3D { Content = model };
            rotateTransformGroup = new Transform3DGroup();
            zOffsetTransform = new TranslateTransform3D(0, 0, _zOffset);

            modelTransformGroup = new Transform3DGroup();
            modelTransformGroup.Children.Add(rotateTransformGroup);
            modelTransformGroup.Children.Add(zOffsetTransform);

            _loadedModel.Transform = modelTransformGroup;


            // Compute bounds 
            double maxDimension = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            double radius = 0.6 * maxDimension; // slightly larger than object
            double thickness = radius * 0.05;

            // Create/update ground plane (3x bounding box)
            GroundPlane = CreateGroundPlane(3 * maxDimension);
            GroundPlane.Transform = new TranslateTransform3D(0, 0, 0);

            // Only show for BaseplateType.None
            if (BaseplateComboBox.SelectedIndex == (int)BaseplateType.None)
                SceneRoot.Children.Add(GroundPlane);

            // Rebuild rings to fit model
            SceneRoot.Children.Clear();
            AddRotationHandles(radius, thickness);

            SceneRoot.Children.Add(_loadedModel);
            RingGroup = new ModelVisual3D();
            RingGroup.Children.Add(XRing);
            RingGroup.Children.Add(YRing);
            RingGroup.Children.Add(ZRing);
            RingGroup.Transform = zOffsetTransform;
            SceneRoot.Children.Add(RingGroup);

            SceneRoot.Children.Remove(GroundPlane);
            if (BaseplateComboBox.SelectedIndex == (int)BaseplateType.None)
                SceneRoot.Children.Add(GroundPlane);


        }


        private void ApplyTransform()
        {
            if (_loadedModel == null || _savedMesh == null || zOffsetTransform == null)
                return;

            double bottomZ = ComputeBottomZ();
            zOffsetTransform.OffsetZ = _zOffset - bottomZ;
        }


        private void OnTransformChanged(object sender, TextChangedEventArgs e)
        {
            double.TryParse(ZOffsetBox.Text, out _zOffset); 
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
                else if (visual == YRing) { rotationAxis = new Vector3D(0, 1, 0); }
                else if (visual == ZRing) { rotationAxis = new Vector3D(0, 0, 1); }
                else return;

                selectedRing = visual;
                rotating = true;
                lastMousePos = pos;
                Mouse.Capture(MainViewPort);
                // Project origin + offset
                Point3D center = new Point3D(0, 0, _zOffset);
                rotationCenter2D = ProjectToScreen(new Point3D(0, 0, _zOffset));

                // reset rotation snapping state
                cumulativeRotationDegrees = 0;
                lastSnappedRotationDegrees = 0;

            }
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

            // Flip based on view direction
            Vector3D viewDir = GetCameraViewDirection(MainViewPort.Camera);
            double alignment = Vector3D.DotProduct(rotationAxis, viewDir);
            if (alignment < 0) deltaAngle = -deltaAngle;

            // Track total angle
            cumulativeRotationDegrees += deltaAngle;

            double angleToApply;

            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                // Snap cumulative rotation to nearest 15°
                double snapped = Math.Round(cumulativeRotationDegrees / 15.0) * 15.0;

                // Only apply delta from last snapped point
                angleToApply = snapped - lastSnappedRotationDegrees;
                lastSnappedRotationDegrees = snapped;

                if (Math.Abs(angleToApply) < 1e-3) return; // prevent jitter or repeated zero-rotations
            }
            else
            {
                angleToApply = deltaAngle;
            }

            var rotation = new AxisAngleRotation3D(rotationAxis, angleToApply);
            var rotateTransform = new RotateTransform3D(rotation);
            rotateTransformGroup.Children.Add(rotateTransform);
            ApplyTransform();  // Recompute z-translation to maintain bottom alignment


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

        private void OnResetRotationClicked(object sender, RoutedEventArgs e)
        {
            rotateTransformGroup.Children.Clear();
            ApplyTransform();
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
        private ModelVisual3D CreateGroundPlane(double size)
        {
            var builder = new MeshBuilder();
            double half = size / 2;

            // Add a quad at z = 0
            builder.AddQuad(
                new Point3D(-half, -half, 0),
                new Point3D(half, -half, 0),
                new Point3D(half, half, 0),
                new Point3D(-half, half, 0)
            );
            var mesh = builder.ToMesh();

            // Use a brush with alpha transparency (e.g., 120/255 ≈ 47% opacity)
            var transparentBrush = new SolidColorBrush(Color.FromArgb(120, 180, 180, 180));
            transparentBrush.Freeze(); // improves performance

            // Use HelixToolkit's helper to create a transparent material
            var material = MaterialHelper.CreateMaterial(transparentBrush);

            var model = new GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                BackMaterial = material
            };

            return new ModelVisual3D { Content = model };
        }

        private Point ProjectToScreen(Point3D point)
        {
            // Get view * projection matrix
            Matrix3D matrix = GetTotalTransformMatrix(MainViewPort);

            // Convert to homogeneous coordinates
            var point4D = new Point4D(point.X, point.Y, point.Z, 1.0);
            point4D = matrix.Transform(point4D);

            // Handle divide-by-zero or behind-camera situations
            if (Math.Abs(point4D.W) < 1e-6)
                return new Point(MainViewPort.ActualWidth / 2, MainViewPort.ActualHeight / 2);

            // Perspective divide
            double x = point4D.X / point4D.W;
            double y = point4D.Y / point4D.W;

            // Map normalized [-1,1] to screen [0, width/height]
            double screenX = (x + 1) * 0.5 * MainViewPort.ActualWidth;
            double screenY = (-y + 1) * 0.5 * MainViewPort.ActualHeight;

            return new Point(screenX, screenY);
        }

        private Matrix3D GetTotalTransformMatrix(HelixViewport3D viewport)
        {
            var vm = CameraHelper.GetViewMatrix(viewport.Camera);
            var pm = CameraHelper.GetProjectionMatrix(viewport.Camera, viewport.ActualWidth / viewport.ActualHeight);
            return Matrix3D.Multiply(vm, pm);
        }

        private Vector3D GetCameraViewDirection(Camera camera)
        {
            if (camera is ProjectionCamera pc)
            {
                var lookDirection = pc.LookDirection;
                lookDirection.Normalize();
                return lookDirection;
            }

            return new Vector3D(0, 0, -1); // Default fallback
        }

        private double ComputeBottomZ()
        {
            if (_savedMesh == null)
                return 0;

            // Combine center + rotation
            var fullTransform = new MatrixTransform3D(_centerTransform.Value * rotateTransformGroup.Value);

            double minZ = double.MaxValue;
            foreach (var p in _savedMesh.Positions)
            {
                var rotated = fullTransform.Transform(p);
                minZ = Math.Min(minZ, rotated.Z);
            }

            return minZ;
        }



    }
}
