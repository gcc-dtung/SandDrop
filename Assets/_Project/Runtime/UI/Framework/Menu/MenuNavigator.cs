using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// Switches between <see cref="MenuPanel"/> tabs. Tab button i opens panel i.
    /// Listen to <see cref="SelectionChanged"/> to update tab visuals (highlight, icon size…).
    /// New tab: add a MenuPanel subclass, a panel entry and a matching tab button.
    /// </summary>
    public sealed class MenuNavigator : MonoBehaviour
    {
        [SerializeField] private MenuPanel[] panels = new MenuPanel[0];
        [Tooltip("Optional. Same order as Panels.")]
        [SerializeField] private Button[] tabButtons = new Button[0];
        [SerializeField, Min(0)] private int defaultIndex;

        private bool initialised;

        public int CurrentIndex { get; private set; } = -1;
        public MenuPanel Current => CurrentIndex >= 0 ? panels[CurrentIndex] : null;
        public int Count => panels.Length;

        public event Action<int> SelectionChanged;

        private void Awake()
        {
            Initialise();
        }

        /// <summary>Wire tab buttons and show the default tab. Called from Awake; safe to call again.</summary>
        public void Initialise()
        {
            if (initialised)
            {
                return;
            }
            initialised = true;

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                int index = i;
                tabButtons[i].onClick.AddListener(() => Select(index));
            }

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] != null) panels[i].SetStateImmediate(false);
            }

            if (panels.Length > 0)
            {
                Select(Mathf.Clamp(defaultIndex, 0, panels.Length - 1), instant: true);
            }
        }

        public void Select(int index, bool instant = false)
        {
            if (index < 0 || index >= panels.Length || index == CurrentIndex)
            {
                return;
            }

            if (Current != null)
            {
                Current.Hide(instant);
            }

            CurrentIndex = index;
            panels[index].Show(instant);
            SelectionChanged?.Invoke(index);
        }

        public bool Select<T>(bool instant = false) where T : MenuPanel
        {
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] is T)
                {
                    Select(i, instant);
                    return true;
                }
            }
            return false;
        }
    }
}
