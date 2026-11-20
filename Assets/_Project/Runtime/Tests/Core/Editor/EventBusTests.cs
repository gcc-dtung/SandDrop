using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameKit.Tests.Core
{
    public class EventBusTests
    {
        private struct TestEvent : IEvent
        {
            public int Value;
        }

        private struct OtherEvent : IEvent
        {
        }

        [SetUp]
        [TearDown]
        public void ResetBuses()
        {
            EventBus<TestEvent>.Clear();
            EventBus<OtherEvent>.Clear();
        }

        [Test]
        public void Raise_DeliversPayloadToEverySubscriber()
        {
            var received = new List<int>();
            EventBus<TestEvent>.Subscribe(e => received.Add(e.Value));
            EventBus<TestEvent>.Subscribe(e => received.Add(e.Value * 10));

            EventBus<TestEvent>.Raise(new TestEvent { Value = 2 });

            CollectionAssert.AreEqual(new[] { 2, 20 }, received);
        }

        [Test]
        public void Raise_DoesNotReachOtherEventTypes()
        {
            var called = false;
            EventBus<OtherEvent>.Subscribe(_ => called = true);

            EventBus<TestEvent>.Raise(new TestEvent());

            Assert.IsFalse(called);
        }

        [Test]
        public void DisposingBinding_Unsubscribes()
        {
            var count = 0;
            var binding = EventBus<TestEvent>.Subscribe(_ => count++);

            EventBus<TestEvent>.Raise(new TestEvent());
            binding.Dispose();
            EventBus<TestEvent>.Raise(new TestEvent());

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, EventBus<TestEvent>.ListenerCount);
        }

        [Test]
        public void DuplicateSubscribe_IsIgnored()
        {
            var count = 0;
            Action<TestEvent> handler = _ => count++;
            EventBus<TestEvent>.Subscribe(handler);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("already subscribed"));
            EventBus<TestEvent>.Subscribe(handler);
            EventBus<TestEvent>.Raise(new TestEvent());

            Assert.AreEqual(1, count);
        }

        [Test]
        public void UnsubscribeDuringRaise_IsSafe_AndAppliesFromNextRaise()
        {
            var secondCalls = 0;
            Action<TestEvent> second = _ => secondCalls++;
            EventBus<TestEvent>.Subscribe(_ => EventBus<TestEvent>.Unsubscribe(second));
            EventBus<TestEvent>.Subscribe(second);

            Assert.DoesNotThrow(() => EventBus<TestEvent>.Raise(new TestEvent()));
            EventBus<TestEvent>.Raise(new TestEvent());

            Assert.AreEqual(1, secondCalls);
        }

        [Test]
        public void SubscribeDuringRaise_AppliesFromNextRaise()
        {
            var lateCalls = 0;
            var subscribed = false;
            EventBus<TestEvent>.Subscribe(_ =>
            {
                if (subscribed) return;
                subscribed = true;
                EventBus<TestEvent>.Subscribe(__ => lateCalls++);
            });

            EventBus<TestEvent>.Raise(new TestEvent());
            Assert.AreEqual(0, lateCalls);

            EventBus<TestEvent>.Raise(new TestEvent());
            Assert.AreEqual(1, lateCalls);
        }

        [Test]
        public void ThrowingListener_DoesNotStopOthers()
        {
            var reached = false;
            EventBus<TestEvent>.Subscribe(_ => throw new InvalidOperationException("boom"));
            EventBus<TestEvent>.Subscribe(_ => reached = true);

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            EventBus<TestEvent>.Raise(new TestEvent());

            Assert.IsTrue(reached);
        }

        [Test]
        public void SubscribeWithToken_UnsubscribesOnCancel()
        {
            var count = 0;
            using (var cts = new CancellationTokenSource())
            {
                EventBus<TestEvent>.Subscribe(_ => count++, cts.Token);
                EventBus<TestEvent>.Raise(new TestEvent());

                cts.Cancel();
                EventBus<TestEvent>.Raise(new TestEvent());
            }

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, EventBus<TestEvent>.ListenerCount);
        }

        [Test]
        public void WaitNext_CompletesWithRaisedPayload_AndCleansUp()
        {
            var task = EventBus<TestEvent>.WaitNext();
            Assert.AreEqual(UniTaskStatus.Pending, task.Status);

            EventBus<TestEvent>.Raise(new TestEvent { Value = 7 });

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(7, task.GetAwaiter().GetResult().Value);
            Assert.AreEqual(0, EventBus<TestEvent>.ListenerCount);
        }

        [Test]
        public void WaitNext_Cancelled_ThrowsAndCleansUp()
        {
            using (var cts = new CancellationTokenSource())
            {
                var task = EventBus<TestEvent>.WaitNext(cts.Token);
                cts.Cancel();

                Assert.AreEqual(UniTaskStatus.Canceled, task.Status);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            }

            Assert.AreEqual(0, EventBus<TestEvent>.ListenerCount);
        }

        [Test]
        public void Registry_ListsUsedBuses_AndClearAllRemovesListeners()
        {
            EventBus<TestEvent>.Subscribe(_ => { });

            var found = false;
            foreach (var entry in EventBusRegistry.Entries)
            {
                if (entry.EventType == typeof(TestEvent)) found = entry.ListenerCount == 1;
            }
            Assert.IsTrue(found);

            EventBusRegistry.ClearAll();
            Assert.AreEqual(0, EventBus<TestEvent>.ListenerCount);
        }
    }
}
