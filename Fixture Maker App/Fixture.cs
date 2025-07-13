
using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

//public class FixtureMakerApp
//{
//    string m_strFileName;
//    string m_strDirName = "";

//    public FixtureMakerApp(string strSTLFile)
//    {
//        if (strSTLFile == null)
//            throw new Exception("STL file must be a valid path.");
//        m_strFileName = Path.GetFileName(strSTLFile);
//        m_strDirName = Path.GetDirectoryName(strSTLFile);

//    }
//    public void MakeFixture()
//    {
//        string strOutputFilename = Path.GetFileNameWithoutExtension(m_strFileName);
//        Fixture.BasePlate oBase = new();
//        Mesh mshObject = Mesh.mshFromStlFile(Path.Combine(m_strDirName, m_strFileName));

//        mshObject = MeshUtility.mshApplyTransformation(mshObject, new Rotator(new Vector3(0.3f, 1f, 0f), 1.5f).Rotate);

//        Fixture.Object oObject = new(mshObject, 5f, 25f, 3f, 5f, 3f);

//        Library.Log("Creating Fixture");
//        Fixture oFixture = new(oBase, oObject);
//        Library.Log("Fixture generated.");

//        if (strOutputFilename != "")
//        {
//            string strFile = Path.Combine(Utils.strProjectRootFolder(), strOutputFilename + "_fixture.stl");
//            Library.Log("Saving fixture to " + m_strDirName);
//            Library.Log("as " + strOutputFilename + "_fixture.stl");
//            Sh.ExportVoxelsToSTLFile(oFixture.asVoxels(), strFile);
//        }

//        Library.Log("Done.");

//    }

//    public class Rotator
//    {
//        public Rotator(Vector3 vecAxis, float fRotation)
//        {
//            m_vecAxis = vecAxis;
//            m_fRotation = fRotation;
//        }

//        public Vector3 Rotate(Vector3 vecPt)
//        {
//            return VecOperations.vecRotateAroundAxis(vecPt, m_fRotation, m_vecAxis);
//        }

//        Vector3 m_vecAxis;
//        float m_fRotation;
//    }

//}

public class Fixture
{
    public class BasePlate
    {
        // implement code here
    }
    public class Object
    {
        public Object(Mesh msh,
                        float fObjBottomOffsetMM,
                        float fSleeveHtMM,
                        float fWallThickMM,
                        float fFlangeWideMM,
                        float fFlangeThickMM,
                        float fToleranceMM = 0.1f,
                        float fFeatureSizeCutoffMM = 1f)
        {
            { // Parameter checks
                if (fObjBottomOffsetMM <= 0)
                    throw new Exception("Object must be above base.");
                if (fSleeveHtMM <= 0)
                    throw new Exception("Object sleeve thickness must be a positive number.");
                if (fWallThickMM <= 0)
                    throw new Exception("Wall thickness must be a positive value.");
                if (fFlangeWideMM <= 0)
                    throw new Exception("Flange width must be a positive value.");
                if (fFlangeThickMM <= 0)
                    throw new Exception("Flange thickness must be a positive value.");
                if (fToleranceMM <= 0)
                    throw new Exception("Tolerance must a non-negative value.");
                if (fFeatureSizeCutoffMM >= fWallThickMM / 2.0)
                    throw new Exception("Feature cutoff size must be smaller than half of wall thickness.");
                if (fFeatureSizeCutoffMM >= fFlangeThickMM / 2.0)
                    throw new Exception("Feature cutoff size must be smaller than half of flange thickness.");
            }

            // Position the object
            BBox3 oObjectBounds = msh.oBoundingBox();
            Vector3 vecOffset = new Vector3(
                -oObjectBounds.vecCenter().X,
                -oObjectBounds.vecCenter().Y,
                -oObjectBounds.vecMin.Z + fObjBottomOffsetMM);

            m_voxFixture = new Voxels(msh.mshCreateTransformed(Vector3.One, vecOffset));
            m_fObjectBottom = fObjBottomOffsetMM;
            m_fSleeve = fSleeveHtMM;
            m_fWall = fWallThickMM;
            m_fFlangeWide = fFlangeWideMM;
            m_fFlangeThick = fFlangeThickMM;
            m_fTolerance = fToleranceMM;
            m_fFeatureCutoff = fFeatureSizeCutoffMM;
        }

        public Voxels VoxObject()
        {
            return m_voxFixture;
        }
        public float fBottomOffset()
        {
            return m_fObjectBottom;
        }
        public float fSleeveHt()
        {
            return m_fSleeve;
        }
        public float fWallThick()
        {
            return m_fWall;
        }
        public float fFlangeWide()
        {
            return m_fFlangeWide;
        }
        public float fFlangeThick()
        {
            return m_fFlangeThick;
        }
        public float fTolerance()
        {
            return m_fTolerance;
        }
        public float fFeatureCutoff()
        {
            return m_fFeatureCutoff;
        }
        Voxels m_voxFixture;
        float m_fObjectBottom;
        float m_fSleeve;
        float m_fWall;
        float m_fFlangeWide;
        float m_fFlangeThick;
        float m_fTolerance;
        float m_fFeatureCutoff;
    }

    public Fixture(BasePlate oPlate,
                    Object oObject)
    {
        Voxels voxObj = new(oObject.VoxObject());

        // Add tolerance
        voxObj.Offset(oObject.fTolerance());

        // Create a wall around the object
        Voxels voxFixture = new(voxObj);
        voxFixture.Offset(oObject.fWallThick());

        //Project down to the base
        BBox3 oFixtureBounds = voxFixture.oCalculateBoundingBox();
        oFixtureBounds.vecMin.Z = -oObject.fFlangeThick();
        oFixtureBounds.vecMax.Z = oObject.fSleeveHt() + oObject.fBottomOffset();
        voxFixture.ProjectZSlice(oFixtureBounds.vecMax.Z, oFixtureBounds.vecMin.Z);

        // Create a box to intersect the fixture with and cut off the top
        Mesh mshBoxIntersect = Utils.mshCreateCube(oFixtureBounds);
        voxFixture.BoolIntersect(new Voxels(mshBoxIntersect));

        // Cut vertical relief
        Voxels voxVerticalCut = new(voxObj);
        voxVerticalCut.ProjectZSlice(oObject.fBottomOffset(), voxFixture.oCalculateBoundingBox().vecMax.Z);
        voxFixture.BoolSubtract(voxVerticalCut);

        // Remove small features
        voxFixture.OverOffset(-oObject.fFeatureCutoff());

        // Create the flange
        Voxels voxFlange = new(voxFixture);
        voxFlange.Offset(oObject.fFlangeWide());
        BBox3 oFlangeBounds = voxFlange.oCalculateBoundingBox();
        oFlangeBounds.vecMin.Z = 0;
        oFlangeBounds.vecMax.Z = -oObject.fFlangeThick();
        Mesh mshFlangeIntersect = Utils.mshCreateCube(oFlangeBounds);
        voxFlange.BoolIntersect(new Voxels(mshFlangeIntersect));
        voxFlange.ProjectZSlice(0, -oObject.fFlangeThick());

        // Add the flange
        voxFixture.BoolAdd(voxFlange);

        m_voxFixture = voxFixture;
    }

    public Voxels asVoxels()
    {
        return m_voxFixture;
    }

    Voxels m_voxFixture;
}