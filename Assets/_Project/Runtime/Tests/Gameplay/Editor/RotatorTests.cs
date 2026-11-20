using GameKit.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameKit.Tests.Gameplay
{
    public class RotatorTests
    {
        private const float AngleTolerance = 0.01f;

        private GameObject root;
        private GameObject parent;
        private Rotator rotator;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("RotatorTest");
            rotator = root.AddComponent<Rotator>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            if (parent != null)
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Step_DefaultSpeedAroundY_RotatesNinetyDegrees()
        {
            rotator.Step(1f);

            Assert.Less(Quaternion.Angle(root.transform.rotation, Quaternion.Euler(0f, 90f, 0f)), AngleTolerance);
        }

        [Test]
        public void Step_ZeroSpeed_KeepsRotation()
        {
            rotator.DegreesPerSecond = 0f;

            rotator.Step(1f);

            Assert.Less(Quaternion.Angle(root.transform.rotation, Quaternion.identity), AngleTolerance);
        }

        [Test]
        public void Step_NegativeSpeed_RotatesBackwards()
        {
            rotator.DegreesPerSecond = -90f;

            rotator.Step(1f);

            Assert.Less(Quaternion.Angle(root.transform.rotation, Quaternion.Euler(0f, -90f, 0f)), AngleTolerance);
        }

        [Test]
        public void Step_SpeedChangedAtRuntime_UsesNewSpeedOnNextStep()
        {
            rotator.Step(1f);
            rotator.DegreesPerSecond = 45f;

            rotator.Step(1f);

            Assert.Less(Quaternion.Angle(root.transform.rotation, Quaternion.Euler(0f, 135f, 0f)), AngleTolerance);
        }

        [Test]
        public void Step_ZeroAxis_KeepsRotationAndWarnsOnce()
        {
            rotator.Axis = Vector3.zero;
            LogAssert.Expect(LogType.Warning, "[Rotator] Axis is zero, not rotating.");

            rotator.Step(1f);
            rotator.Step(1f);

            Assert.Less(Quaternion.Angle(root.transform.rotation, Quaternion.identity), AngleTolerance);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Step_WorldSpaceWithRotatedParent_RotatesAroundWorldAxis()
        {
            parent = new GameObject("RotatorTestParent");
            parent.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            root.transform.SetParent(parent.transform, false);

            var serialized = new SerializedObject(rotator);
            serialized.FindProperty("space").enumValueIndex = (int)Space.World;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Quaternion before = root.transform.rotation;
            rotator.Step(1f);

            Quaternion expected = Quaternion.AngleAxis(90f, Vector3.up) * before;
            Assert.Less(Quaternion.Angle(root.transform.rotation, expected), AngleTolerance);
        }
    }
}
