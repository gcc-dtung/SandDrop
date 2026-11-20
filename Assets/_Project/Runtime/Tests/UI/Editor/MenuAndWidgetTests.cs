using System.Collections.Generic;
using GameKit.UI.Framework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameKit.Tests.UI
{
    public class MenuNavigatorTests
    {
        private class TestPanel : MenuPanel
        {
            public int Shown;
            public int Hidden;

            protected override void OnShown() => Shown++;
            protected override void OnHidden() => Hidden++;
        }

        private GameObject root;
        private TestPanel[] panels;
        private Button[] tabs;
        private MenuNavigator navigator;

        [SetUp]
        public void Build()
        {
            root = new GameObject("Canvas_Menu", typeof(RectTransform), typeof(Canvas));
            panels = new TestPanel[3];
            tabs = new Button[3];
            for (int i = 0; i < panels.Length; i++)
            {
                var panel = new GameObject("Panel" + i, typeof(RectTransform));
                panel.transform.SetParent(root.transform, false);
                panels[i] = panel.AddComponent<TestPanel>();
                PopupTests.NoAnimation(panels[i]);

                var tab = new GameObject("Tab" + i, typeof(RectTransform), typeof(Image));
                tab.transform.SetParent(root.transform, false);
                tabs[i] = tab.AddComponent<Button>();
            }

            var navGo = new GameObject("Navigator");
            navGo.transform.SetParent(root.transform, false);
            navigator = navGo.AddComponent<MenuNavigator>();

            var so = new SerializedObject(navigator);
            var panelList = so.FindProperty("panels");
            var tabList = so.FindProperty("tabButtons");
            panelList.arraySize = panels.Length;
            tabList.arraySize = tabs.Length;
            for (int i = 0; i < panels.Length; i++)
            {
                panelList.GetArrayElementAtIndex(i).objectReferenceValue = panels[i];
                tabList.GetArrayElementAtIndex(i).objectReferenceValue = tabs[i];
            }
            so.FindProperty("defaultIndex").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            navigator.Initialise();
        }

        [TearDown]
        public void DestroyAll()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Initialise_ShowsOnlyDefaultTab()
        {
            Assert.AreEqual(1, navigator.CurrentIndex);
            Assert.IsFalse(panels[0].gameObject.activeSelf);
            Assert.IsTrue(panels[1].gameObject.activeSelf);
            Assert.IsFalse(panels[2].gameObject.activeSelf);
            Assert.AreEqual(1, panels[1].Shown);
        }

        [Test]
        public void Select_HidesPreviousAndShowsNew_AndRaisesSelectionChanged()
        {
            var changes = new List<int>();
            navigator.SelectionChanged += changes.Add;

            navigator.Select(2);

            Assert.IsFalse(panels[1].gameObject.activeSelf);
            Assert.IsTrue(panels[2].gameObject.activeSelf);
            Assert.AreEqual(1, panels[1].Hidden);
            CollectionAssert.AreEqual(new[] { 2 }, changes);
        }

        [Test]
        public void SelectSameTab_DoesNothing()
        {
            var changes = 0;
            navigator.SelectionChanged += _ => changes++;

            navigator.Select(1);

            Assert.AreEqual(0, changes);
            Assert.AreEqual(1, panels[1].Shown);
        }

        [Test]
        public void TabButtonClick_SelectsMatchingPanel()
        {
            tabs[0].onClick.Invoke();

            Assert.AreEqual(0, navigator.CurrentIndex);
            Assert.IsTrue(panels[0].gameObject.activeSelf);
        }

        [Test]
        public void SelectByType_FindsPanel()
        {
            Assert.IsTrue(navigator.Select<TestPanel>());
            Assert.IsFalse(navigator.Select<OtherPanelType>());
        }

        private class OtherPanelType : MenuPanel
        {
        }
    }

    public class SafeAreaTests
    {
        [Test]
        public void ComputeAnchors_NotchAndHomeBar()
        {
            // 1080x2400 screen, 100 px notch at the top, 60 px home bar at the bottom.
            var safe = new Rect(0, 60, 1080, 2400 - 60 - 100);

            SafeArea.ComputeAnchors(safe, new Vector2(1080, 2400), true, true, out var min, out var max);

            Assert.AreEqual(0f, min.x, 1e-4f);
            Assert.AreEqual(1f, max.x, 1e-4f);
            Assert.AreEqual(60f / 2400f, min.y, 1e-4f);
            Assert.AreEqual(2300f / 2400f, max.y, 1e-4f);
        }

        [Test]
        public void ComputeAnchors_IgnoresDisabledAxis()
        {
            var safe = new Rect(80, 60, 1080 - 160, 2400 - 160);

            SafeArea.ComputeAnchors(safe, new Vector2(1080, 2400), conformX: false, conformY: true, out var min, out var max);

            Assert.AreEqual(0f, min.x);
            Assert.AreEqual(1f, max.x);
            Assert.Greater(min.y, 0f);
        }

        [Test]
        public void ComputeAnchors_ZeroScreen_ReturnsFullRect()
        {
            SafeArea.ComputeAnchors(new Rect(0, 0, 10, 10), Vector2.zero, true, true, out var min, out var max);

            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }
    }
}
