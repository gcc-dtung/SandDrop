using GameKit.Core.Contracts;
using GameKit.Core.Foundation.Pool;
using NUnit.Framework;
using UnityEngine;

namespace GameKit.Tests.Core
{
    public class ComponentPoolTests
    {
        private class Counter : MonoBehaviour, IPoolable
        {
            public int Spawned;
            public int Despawned;

            public void OnSpawned() => Spawned++;
            public void OnDespawned() => Despawned++;
        }

        private GameObject root;
        private Counter template;

        [SetUp]
        public void CreateTemplate()
        {
            root = new GameObject("PoolRoot");
            var go = new GameObject("Template");
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            template = go.AddComponent<Counter>();
        }

        [TearDown]
        public void DestroyAll()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Get_ActivatesInstance_Release_DeactivatesAndReturnsUnderParent()
        {
            var pool = new ComponentPool<Counter>(template, root.transform);

            var item = pool.Get();
            Assert.IsTrue(item.gameObject.activeSelf);
            Assert.AreNotSame(template, item);

            item.transform.SetParent(null, false);
            pool.Release(item);

            Assert.IsFalse(item.gameObject.activeSelf);
            Assert.AreSame(root.transform, item.transform.parent);
            Assert.AreEqual(1, pool.CountInactive);
        }

        [Test]
        public void ReleasedInstance_IsReused()
        {
            var pool = new ComponentPool<Counter>(template, root.transform);

            var first = pool.Get();
            pool.Release(first);
            var second = pool.Get();

            Assert.AreSame(first, second);
        }

        [Test]
        public void PoolableCallbacks_AreCalledOnGetAndRelease()
        {
            var pool = new ComponentPool<Counter>(template, root.transform);

            var item = pool.Get();
            pool.Release(item);
            pool.Get();

            Assert.AreEqual(2, item.Spawned);
            Assert.AreEqual(1, item.Despawned);
        }

        [Test]
        public void Prewarm_CreatesInactiveInstances()
        {
            var pool = new ComponentPool<Counter>(template, root.transform);

            pool.Prewarm(5);

            Assert.AreEqual(5, pool.CountInactive);
            Assert.AreEqual(0, pool.CountActive);
        }

        [Test]
        public void Clear_WhenInstancesAlreadyDestroyed_DoesNotThrow()
        {
            var pool = new ComponentPool<Counter>(template, root.transform);
            pool.Prewarm(3);

            // Leaving Play mode destroys scene objects before the pool is cleared.
            foreach (Transform child in root.transform)
            {
                if (child != template.transform) Object.DestroyImmediate(child.gameObject);
            }

            Assert.DoesNotThrow(() => pool.Clear());
        }

        [Test]
        public void Release_DestroyedItem_IsIgnored()
        {
            var pool = new ComponentPool<Counter>(template, root.transform);
            var item = pool.Get();
            Object.DestroyImmediate(item.gameObject);

            Assert.DoesNotThrow(() => pool.Release(item));
        }
    }
}
