using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Events;
using GameKit.UI.Framework;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameKit.Tests.UI
{
    public class PopupTests
    {
        private class TestPopup : PopupView
        {
            public int OpenedCalls;
            public int ClosedCalls;
            public bool AllowBack = true;

            public override bool CanCloseWithBack => AllowBack;
            protected override void OnOpened() => OpenedCalls++;
            protected override void OnClosed() => ClosedCalls++;
        }

        private class OtherPopup : PopupView
        {
        }

        private GameObject root;
        private PopupManager manager;

        [SetUp]
        public void CreateManager()
        {
            EventBus<PopupOpened>.Clear();
            EventBus<PopupClosed>.Clear();

            root = new GameObject("Canvas_Popup", typeof(RectTransform), typeof(Canvas));
            var popups = new GameObject("Popups", typeof(RectTransform));
            popups.transform.SetParent(root.transform, false);
            manager = popups.AddComponent<PopupManager>();
        }

        [TearDown]
        public void DestroyAll()
        {
            Object.DestroyImmediate(root);
            EventBus<PopupOpened>.Clear();
            EventBus<PopupClosed>.Clear();
        }

        private T AddPopup<T>(string popupName) where T : PopupView
        {
            var go = new GameObject(popupName, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(manager.transform, false);
            var popup = go.AddComponent<T>();
            NoAnimation(popup);
            return popup;
        }

        internal static void NoAnimation(Object popupOrPanel)
        {
            var so = new SerializedObject(popupOrPanel);
            foreach (var field in new[] { "openDuration", "closeDuration", "fadeDuration" })
            {
                var property = so.FindProperty(field);
                if (property != null) property.floatValue = 0f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void Register(params PopupView[] popups)
        {
            var so = new SerializedObject(manager);
            var list = so.FindProperty("popups");
            list.arraySize = popups.Length;
            for (int i = 0; i < popups.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = popups[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            manager.Init(null);
        }

        [Test]
        public void Open_ActivatesPopup_CallsOnOpened_AndPushesOnStack()
        {
            var popup = AddPopup<TestPopup>("A");
            Register(popup);

            var opened = manager.Open<TestPopup>();

            Assert.AreSame(popup, opened);
            Assert.IsTrue(popup.gameObject.activeSelf);
            Assert.IsTrue(popup.IsOpen);
            Assert.AreEqual(1, popup.OpenedCalls);
            Assert.AreSame(popup, manager.Top);
            Assert.IsTrue(popup.GetComponent<CanvasGroup>().interactable);
        }

        [Test]
        public void SecondPopup_IsSortedAboveFirst()
        {
            var a = AddPopup<TestPopup>("A");
            var b = AddPopup<OtherPopup>("B");
            Register(a, b);

            a.Open();
            b.Open();

            var orderA = a.GetComponent<Canvas>().sortingOrder;
            var orderB = b.GetComponent<Canvas>().sortingOrder;
            Assert.Greater(orderB, orderA);
            Assert.IsTrue(b.GetComponent<Canvas>().overrideSorting);
            Assert.AreSame(b, manager.Top);
            Assert.AreEqual(2, manager.OpenCount);
        }

        [Test]
        public void Close_DeactivatesPopup_AndPopsStack()
        {
            var popup = AddPopup<TestPopup>("A");
            Register(popup);
            popup.Open();

            popup.Close();

            Assert.IsFalse(popup.gameObject.activeSelf);
            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual(1, popup.ClosedCalls);
            Assert.AreEqual(0, manager.OpenCount);
        }

        [Test]
        public void OpenTwice_DoesNotDuplicate()
        {
            var popup = AddPopup<TestPopup>("A");
            Register(popup);

            popup.Open();
            popup.Open();

            Assert.AreEqual(1, manager.OpenCount);
            Assert.AreEqual(1, popup.OpenedCalls);
        }

        [Test]
        public void HandleBack_ClosesOnlyTopPopup()
        {
            var a = AddPopup<TestPopup>("A");
            var b = AddPopup<OtherPopup>("B");
            Register(a, b);
            a.Open();
            b.Open();

            Assert.IsTrue(manager.HandleBack());

            Assert.IsFalse(b.IsOpen);
            Assert.IsTrue(a.IsOpen);
            Assert.AreSame(a, manager.Top);
        }

        [Test]
        public void HandleBack_RespectsCanCloseWithBack()
        {
            var popup = AddPopup<TestPopup>("A");
            popup.AllowBack = false;
            Register(popup);
            popup.Open();

            Assert.IsFalse(manager.HandleBack());
            Assert.IsTrue(popup.IsOpen);
        }

        [Test]
        public void HandleBack_WithNoPopup_ReturnsFalse()
        {
            Register();
            Assert.IsFalse(manager.HandleBack());
        }

        [Test]
        public void OpenAndClose_RaiseEvents()
        {
            var popup = AddPopup<TestPopup>("A");
            Register(popup);
            var events = new List<string>();
            EventBus<PopupOpened>.Subscribe(e => events.Add("open:" + e.PopupType.Name));
            EventBus<PopupClosed>.Subscribe(e => events.Add("close:" + e.PopupType.Name));

            popup.Open();
            popup.Close();

            CollectionAssert.AreEqual(new[] { "open:TestPopup", "close:TestPopup" }, events);
        }

        [Test]
        public void WaitUntilClosed_CompletesOnClose()
        {
            var popup = AddPopup<TestPopup>("A");
            Register(popup);
            popup.Open();

            var wait = popup.WaitUntilClosed();
            Assert.AreEqual(UniTaskStatus.Pending, wait.Status);

            popup.Close();
            Assert.AreEqual(UniTaskStatus.Succeeded, wait.Status);
        }

        [Test]
        public void CloseAll_ClosesEveryPopup()
        {
            var a = AddPopup<TestPopup>("A");
            var b = AddPopup<OtherPopup>("B");
            Register(a, b);
            a.Open();
            b.Open();

            manager.CloseAll();

            Assert.AreEqual(0, manager.OpenCount);
            Assert.IsFalse(a.IsOpen);
            Assert.IsFalse(b.IsOpen);
        }

        // ---- ConfirmPopup ----

        private ConfirmPopup BuildConfirm(out Button confirm, out Button cancel)
        {
            var popup = AddPopup<ConfirmPopup>("Confirm");
            TMP_Text Label(string labelName) => new GameObject(labelName, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            Button MakeButton(string buttonName) => new GameObject(buttonName, typeof(RectTransform), typeof(Image)).AddComponent<Button>();

            confirm = MakeButton("Confirm");
            cancel = MakeButton("Cancel");
            foreach (var t in new Component[] { confirm, cancel }) t.transform.SetParent(popup.transform, false);

            var message = Label("Message");
            message.transform.SetParent(popup.transform, false);

            var so = new SerializedObject(popup);
            so.FindProperty("messageText").objectReferenceValue = message;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("cancelButton").objectReferenceValue = cancel;
            so.ApplyModifiedPropertiesWithoutUndo();
            Register(popup);
            return popup;
        }

        [Test]
        public void ConfirmPopup_ConfirmButton_AnswersTrue()
        {
            var popup = BuildConfirm(out var confirm, out _);

            var ask = popup.AskAsync("Sure?");
            Assert.AreEqual(UniTaskStatus.Pending, ask.Status);
            confirm.onClick.Invoke();

            Assert.AreEqual(UniTaskStatus.Succeeded, ask.Status);
            Assert.IsTrue(ask.GetAwaiter().GetResult());
            Assert.IsFalse(popup.IsOpen);
        }

        [Test]
        public void ConfirmPopup_CancelOrBack_AnswersFalse()
        {
            var popup = BuildConfirm(out _, out var cancel);

            var first = popup.AskAsync("Sure?");
            cancel.onClick.Invoke();
            Assert.IsFalse(first.GetAwaiter().GetResult());

            var second = popup.AskAsync("Really?");
            manager.HandleBack();
            Assert.IsFalse(second.GetAwaiter().GetResult());
        }

        [Test]
        public void ConfirmPopup_NullCancel_HidesCancelButton()
        {
            var popup = BuildConfirm(out var confirm, out var cancel);

            var ask = popup.AskAsync("Done!", cancel: null);

            Assert.IsFalse(cancel.gameObject.activeSelf);
            confirm.onClick.Invoke();
            Assert.IsTrue(ask.GetAwaiter().GetResult());
        }
    }
}
