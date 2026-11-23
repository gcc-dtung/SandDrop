#if !DISABLE_CHEATS
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameKit.Cheat
{
    /// <summary>
    /// Hidden way to open the <see cref="CheatPanel"/> on a device: 3 quick taps on the graphic this is added to, while
    /// <see cref="Condition"/> holds (e.g. the "Paused" title of the pause popup). Added at runtime by
    /// <see cref="GameCheats"/>; compiled out with the rest of the cheats.
    /// </summary>
    public sealed class CheatTapTrigger : MonoBehaviour, IPointerClickHandler
    {
        private const int TapsToOpen = 3;
        /// <summary>Most seconds between two taps of the sequence (real time: the game is paused).</summary>
        private const float MaxGap = 0.6f;

        private int taps;
        private float lastTap = -10f;

        /// <summary>Taps only count while this returns true (null = always).</summary>
        public Func<bool> Condition { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Condition != null && !Condition())
            {
                taps = 0;
                return;
            }

            float now = Time.unscaledTime;
            taps = now - lastTap <= MaxGap ? taps + 1 : 1;
            lastTap = now;
            if (taps >= TapsToOpen && CheatPanel.Instance != null)
            {
                taps = 0;
                CheatPanel.Instance.Open();
            }
        }
    }
}
#endif
