using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameKit.Gameplay.UI
{
    /// <summary>
    /// In-level HUD: pause button, "Level N" title, a row of level cells (past / current / next),
    /// a vertical zoom slider and a button that puts the model back to its start view.
    /// </summary>
    public sealed class LevelHud : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private Button pauseButton;

        [Header("Level progress row (left to right)")]
        [SerializeField] private Image[] progressCells = new Image[5];
        [SerializeField] private TMP_Text[] progressLabels = new TMP_Text[5];
        [SerializeField] private Color doneColor = new Color(0.62f, 0.68f, 0.86f);
        [SerializeField] private Color currentColor = new Color(1f, 0.78f, 0.22f);
        [SerializeField] private Color nextColor = new Color(0.84f, 0.87f, 0.97f);
        [SerializeField] private float currentScale = 1.25f;

        [Header("View")]
        [SerializeField] private Slider zoomSlider;
        [SerializeField] private Button recenterButton;

        private bool silent;

        public event Action PauseClicked;
        public event Action RecenterClicked;
        /// <summary>Zoom factor from the slider (1 = level default).</summary>
        public event Action<float> ZoomChanged;

        private void Awake()
        {
            if (pauseButton != null) pauseButton.onClick.AddListener(() => PauseClicked?.Invoke());
            if (recenterButton != null) recenterButton.onClick.AddListener(() => RecenterClicked?.Invoke());
            if (zoomSlider != null)
            {
                zoomSlider.onValueChanged.AddListener(v =>
                {
                    if (!silent) ZoomChanged?.Invoke(v);
                });
            }
        }

        /// <summary>Shows the 1-based level number in the title and the progress row.</summary>
        public void SetLevel(int levelNumber)
        {
            if (levelText != null)
            {
                levelText.text = $"Level {levelNumber}";
            }

            int count = progressCells.Length;
            // Keep the current level in the middle when there are enough levels before it.
            int middle = count / 2;
            int first = Mathf.Max(1, levelNumber - middle);
            for (int i = 0; i < count; i++)
            {
                int number = first + i;
                if (progressLabels.Length > i && progressLabels[i] != null)
                {
                    progressLabels[i].text = number.ToString();
                }
                if (progressCells[i] == null)
                {
                    continue;
                }
                progressCells[i].color = number < levelNumber ? doneColor : number == levelNumber ? currentColor : nextColor;
                progressCells[i].transform.localScale = Vector3.one * (number == levelNumber ? currentScale : 1f);
            }
        }

        /// <summary>Moves the slider without raising <see cref="ZoomChanged"/>.</summary>
        public void SetZoomWithoutNotify(float zoom)
        {
            if (zoomSlider == null)
            {
                return;
            }
            silent = true;
            zoomSlider.value = zoom;
            silent = false;
        }
    }
}
