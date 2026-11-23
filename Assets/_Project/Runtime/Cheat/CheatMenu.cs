#if !DISABLE_CHEATS
using System;
using System.Collections.Generic;

namespace GameKit.Cheat
{
    /// <summary>
    /// The list of cheats, grouped by category in the order they are added. Shown in game by <see cref="CheatPanel"/>
    /// and in the Editor by the Cheats window (Tools/GameKit/Cheats/Cheat Window), both reading <see cref="Current"/>.
    /// Cheat classes add their entries in a <c>Register(CheatMenu)</c> method (see <see cref="GameCheats"/>).
    /// </summary>
    public sealed class CheatMenu
    {
        /// <summary>The menu of the running game (null outside Play mode or before the cheats are installed).</summary>
        public static CheatMenu Current { get; internal set; }

        public enum Kind
        {
            Button,
            Toggle,
            Stepper,
            Number
        }

        public sealed class Item
        {
            public Kind Kind;
            public string Category;
            public string Label;
            public Action Click;
            public bool ClosePanel;
            public Func<bool> GetBool;
            public Action<bool> SetBool;
            public Func<float> GetFloat;
            public Action<float> SetFloat;
            public float Min;
            public float Max;
            public float Step;
            public string Format;
        }

        private readonly List<Item> items = new List<Item>();

        public IReadOnlyList<Item> Items => items;

        /// <summary>Runs <paramref name="click"/>. With <paramref name="closePanel"/> the panel closes first (popups, level end).</summary>
        public void Button(string category, string label, Action click, bool closePanel = false)
        {
            items.Add(new Item { Kind = Kind.Button, Category = category, Label = label, Click = click, ClosePanel = closePanel });
        }

        /// <summary>On / off switch.</summary>
        public void Toggle(string category, string label, Func<bool> get, Action<bool> set)
        {
            items.Add(new Item { Kind = Kind.Toggle, Category = category, Label = label, GetBool = get, SetBool = set });
        }

        /// <summary>Value changed with − / + buttons by <paramref name="step"/>, kept in [min, max].</summary>
        public void Stepper(string category, string label, float min, float max, float step, Func<float> get, Action<float> set,
            string format = "0.##")
        {
            items.Add(new Item
            {
                Kind = Kind.Stepper, Category = category, Label = label, Min = min, Max = max, Step = step, GetFloat = get,
                SetFloat = set, Format = format
            });
        }

        /// <summary>Whole number typed in a field and applied with "Go" (e.g. jump to a level).</summary>
        public void Number(string category, string label, int min, int max, Func<int> get, Action<int> apply)
        {
            items.Add(new Item
            {
                Kind = Kind.Number, Category = category, Label = label, Min = min, Max = max, Step = 1f,
                GetFloat = () => get(), SetFloat = v => apply((int)v), Format = "0"
            });
        }
    }
}
#endif
