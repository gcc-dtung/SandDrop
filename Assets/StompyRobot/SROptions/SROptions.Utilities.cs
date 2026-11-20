using System.ComponentModel;
using UnityEngine;

// Project-specific SRDebugger options. Kept here (Assembly-CSharp, next to SROptions.cs)
// instead of SRDebugger/Scripts, which is a separate assembly and would declare SROptions twice (CS0436).
public partial class SROptions
{
    [Category("Utilities")] // Options will be grouped by category
    public void ClearPlayerPrefs()
    {
        Debug.Log("Clearing PlayerPrefs");
        PlayerPrefs.DeleteAll();
    }
}
