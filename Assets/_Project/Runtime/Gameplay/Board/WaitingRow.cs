using System.Collections.Generic;

namespace GameKit.Gameplay.Board
{
    /// <summary>Waiting slots for sand that has no matching tray. Slots keep their index; -1 = empty.</summary>
    public sealed class WaitingRow
    {
        private readonly List<int> slots = new List<int>();

        public WaitingRow(int capacity)
        {
            AddSlots(capacity);
        }

        public int Capacity => slots.Count;

        public int this[int index] => slots[index];

        public bool IsFull => FirstFree() < 0;

        public bool IsEmpty
        {
            get
            {
                foreach (int color in slots)
                {
                    if (color >= 0)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public void AddSlots(int count)
        {
            for (int i = 0; i < count; i++)
            {
                slots.Add(-1);
            }
        }

        public int FirstFree()
        {
            return slots.IndexOf(-1);
        }

        /// <summary>Puts a colour in the first free slot and returns its index, or -1 when full.</summary>
        public int Put(int color)
        {
            int index = FirstFree();
            if (index >= 0)
            {
                slots[index] = color;
            }
            return index;
        }

        public void Clear(int index)
        {
            slots[index] = -1;
        }
    }
}
