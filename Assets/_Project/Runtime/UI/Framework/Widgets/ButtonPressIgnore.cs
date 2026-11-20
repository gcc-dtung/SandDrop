using UnityEngine;

namespace GameKit.UI.Framework
{
    /// <summary>
    /// Marks a Button that must not get <see cref="ButtonPress"/> (full-screen dims, invisible hit areas…).
    /// The "Add ButtonPress To Buttons" tool skips buttons with this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonPressIgnore : MonoBehaviour
    {
    }
}
