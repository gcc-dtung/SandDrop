using DG.Tweening;
using GameKit.Core.Contracts;
using UnityEngine;

namespace GameKit.Gameplay.Visuals
{
    /// <summary>One waiting slot: an empty plate, or a plate with a jar of sand waiting for its tray.</summary>
    public sealed class WaitingSlotView : MonoBehaviour, IPoolable
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer plate;
        [SerializeField] private JarView jar;
        [SerializeField] private Color plateColor = new Color(0.93f, 0.92f, 0.95f);
        [SerializeField] private Color dangerPlateColor = new Color(0.96f, 0.56f, 0.56f);

        private MaterialPropertyBlock block;
        private Vector3 jarScale = Vector3.one;
        private bool jarScaleKnown;

        public bool Occupied { get; private set; }
        public JarView Jar => jar;

        /// <summary>Puts an empty jar of this colour on the plate (sand is about to arrive).</summary>
        public JarView Reserve(Color sandColor)
        {
            Remember();
            Occupied = true;
            jar.ResetJar();
            jar.SetColor(sandColor);
            jar.gameObject.SetActive(true);
            jar.transform.DOKill();
            jar.transform.localScale = jarScale * 0.6f;
            jar.transform.DOScale(jarScale, 0.18f).SetEase(Ease.OutBack).SetLink(jar.gameObject);
            return jar;
        }

        /// <summary>Removes the jar (its sand has left for a tray).</summary>
        public void Clear()
        {
            Remember();
            Occupied = false;
            if (jar == null)
            {
                return;
            }
            jar.transform.DOKill();
            jar.transform.localScale = jarScale;
            jar.ResetJar();
            jar.gameObject.SetActive(false);
        }

        /// <summary>Tints the plate red when the waiting row is nearly full.</summary>
        public void SetDanger(bool danger)
        {
            if (plate == null)
            {
                return;
            }
            block ??= new MaterialPropertyBlock();
            block.SetColor(ColorId, danger ? dangerPlateColor : plateColor);
            plate.SetPropertyBlock(block);
        }

        private void Remember()
        {
            if (!jarScaleKnown && jar != null)
            {
                jarScale = jar.transform.localScale;
                jarScaleKnown = true;
            }
        }

        public void OnSpawned()
        {
            SetDanger(false);
            Clear();
        }

        public void OnDespawned()
        {
            Clear();
        }
    }
}
